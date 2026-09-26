using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    internal const int RoadTrackWear = 24;
    internal const int RoadTrackFadeWear = 12;
    internal const int RoadTrailWear = 72;
    internal const int RoadTrailFadeWear = 36;

    private int[]? _roadWear;
    private long? _trailConnectedMinute;
    private long? _roadConnectedMinute;
    private readonly SortedDictionary<long, int> _roadTilesBuiltThisSeason = [];
    private readonly Dictionary<TileCoordinate, IReadOnlyDictionary<TileCoordinate, long>> _siteTerrainCosts = new();

    /// <summary>The current graded road tiles, for observers. Raw wear is not exposed.</summary>
    public IReadOnlyList<(TileCoordinate Coordinate, RoadGrade Grade)> RoadGrades
    {
        get
        {
            if (Roads is not { } roads) return [];
            var result = new List<(TileCoordinate, RoadGrade)>();
            for (var y = 0; y < World.Height; y++)
                for (var x = 0; x < World.Width; x++)
                {
                    var coordinate = new TileCoordinate(x, y);
                    var grade = roads.GradeAt(coordinate);
                    if (grade != RoadGrade.None) result.Add((coordinate, grade));
                }
            return result;
        }
    }

    private int[] RoadWear => _roadWear ??= new int[checked(World.Width * World.Height)];

    private void InitializeRoadRuntime(RoadNetworkState? state)
    {
        if (!RoadSystemsEnabled(SimulationRulesVersion) || state is null) return;
        var grades = Roads!;
        var wear = RoadWear;
        foreach (var tile in state.Tiles)
        {
            wear[tile.Y * World.Width + tile.X] = tile.Wear;
            grades.SetGrade(tile.Coordinate, tile.Grade);
        }
        foreach (var route in state.ActiveRoutes)
            _activePaths[(route.CitizenId, route.ActionSequence)] = route.Route;
        _trailConnectedMinute = state.TrailConnectedMinute;
        _roadConnectedMinute = state.RoadConnectedMinute;
        foreach (var work in state.SeasonWork ?? []) _roadTilesBuiltThisSeason[work.SettlementId] = work.TilesBuilt;
    }

    private RoadNetworkState CaptureRoadState()
    {
        var grades = Roads!;
        var wear = RoadWear;
        var tiles = new List<RoadTileState>();
        for (var y = 0; y < World.Height; y++)
            for (var x = 0; x < World.Width; x++)
            {
                var tileWear = wear[y * World.Width + x];
                var grade = grades.GradeAt(new TileCoordinate(x, y));
                if (tileWear != 0 || grade != RoadGrade.None) tiles.Add(new RoadTileState(x, y, tileWear, grade));
            }
        var routes = _citizens.Values.Where(x => x.IsAlive).OrderBy(x => x.Id.Value)
            .Select(x => _activePaths.TryGetValue((x.Id.Value, x.ActionSequence), out var route)
                ? new RoadActiveRouteState(x.Id.Value, x.ActionSequence, route)
                : null)
            .OfType<RoadActiveRouteState>().ToArray();
        return new RoadNetworkState(1, tiles, routes, _trailConnectedMinute, _roadConnectedMinute,
            _roadTilesBuiltThisSeason.Select(x => new RoadSeasonWorkState(x.Key, x.Value)).ToArray());
    }

    private void AccrueRoadWear(TileCoordinate tile)
    {
        var wear = RoadWear;
        var index = tile.Y * World.Width + tile.X;
        wear[index] = checked(wear[index] + 1);
    }

    private void EvaluateRoadSeason()
    {
        if (CurrentMinute.Value == 0 || CurrentMinute.Value % MinutesPerMigrationSeason != 0) return;
        RecordRoadWorkSeason();
        GradeRoadsForSeason();
        RecordRouteConnections();
        foreach (var settlementId in MigrationSettlementIds) PlanRoadWork(settlementId);
    }

    /// <summary>One history entry per settlement for the season that just ended, and only if it paved anything.</summary>
    private void RecordRoadWorkSeason()
    {
        var ended = new WorldMinute(CurrentMinute.Value - 1).ToCalendar();
        foreach (var (settlementId, tilesBuilt) in _roadTilesBuiltThisSeason)
            EmitHistory(HistoricalEventType.RoadWorkSeason, HistoricalImportance.Routine, SiteLocation(settlementId),
                HistoricalEventPayloads.RoadWorkSeason(settlementId, tilesBuilt, ended.Season, ended.Year));
        _roadTilesBuiltThisSeason.Clear();
    }

    /// <summary>Records the first time a trail, and later a road, runs unbroken between the two sites.</summary>
    private void RecordRouteConnections()
    {
        if (_trailConnectedMinute is null && SitesConnectedAt(RoadGrade.Trail))
        {
            _trailConnectedMinute = CurrentMinute.Value;
            EmitHistory(HistoricalEventType.RouteConnected, HistoricalImportance.Notable, World.StartingSite,
                HistoricalEventPayloads.RouteConnected(RoadGrade.Trail));
        }
        if (_trailConnectedMinute is not null && _roadConnectedMinute is null && SitesConnectedAt(RoadGrade.Road))
        {
            _roadConnectedMinute = CurrentMinute.Value;
            EmitHistory(HistoricalEventType.RouteConnected, HistoricalImportance.Notable, World.StartingSite,
                HistoricalEventPayloads.RouteConnected(RoadGrade.Road));
        }
    }

    internal const int RoadOrdersPerSeason = 4;
    internal const int RoadWorkPriority = 1800;
    internal static int RoadStoneReserve(int population) => checked(10 + 2 * population);

    /// <summary>Requests paving of the settlement's own trails: the route between the sites first, then by wear.</summary>
    private void PlanRoadWork(long settlementId)
    {
        var population = PopulationAt(settlementId);
        if (population == 0 || !SettlementKnows(settlementId, LivingTechnique.Toolmaking) ||
            SettlementFor(settlementId).StoneStored < RoadStoneReserve(population)) return;
        var pending = OrdersAt(settlementId).Where(x => x.Kind == LivingWorkKind.BuildRoad).Select(x => x.Location).ToHashSet();
        var capacity = RoadOrdersPerSeason - pending.Count;
        if (capacity <= 0) return;
        var grades = Roads!;
        var wear = RoadWear;
        var route = IntersiteRoute();
        var candidates = new List<(TileCoordinate Coordinate, bool OnRoute, int Wear)>();
        for (var y = 0; y < World.Height; y++)
            for (var x = 0; x < World.Width; x++)
            {
                var coordinate = new TileCoordinate(x, y);
                if (grades.GradeAt(coordinate) != RoadGrade.Trail || pending.Contains(coordinate) ||
                    SiteIdForLocation(coordinate) != settlementId) continue;
                candidates.Add((coordinate, route.Contains(coordinate), wear[y * World.Width + x]));
            }
        foreach (var tile in candidates.OrderByDescending(x => x.OnRoute).ThenByDescending(x => x.Wear).ThenBy(x => x.Coordinate).Take(capacity))
            RequestLiving(LivingWorkKind.BuildRoad, tile.Coordinate, RoadWorkPriority, settlementId: settlementId,
                ingredients: [.. LivingWorkDefinitions.Ingredients(LivingWorkKind.BuildRoad)]);
    }

    private HashSet<TileCoordinate> IntersiteRoute() =>
        _migrationState?.DaughterSettlement is { } daughter && FindPathCached(World.StartingSite, daughter.Site) is { } path
            ? path.ToHashSet()
            : [];

    private void CompleteRoadTile(LivingWorkOrder order)
    {
        var grades = Roads ?? throw new InvalidOperationException("Road work requires M15 rules.");
        if (grades.GradeAt(order.Location) != RoadGrade.Trail) return;
        grades.SetGrade(order.Location, RoadGrade.Road);
        OnRoadNetworkChanged();
        var settlementId = SiteIdForOrder(order);
        _roadTilesBuiltThisSeason[settlementId] = checked(_roadTilesBuiltThisSeason.GetValueOrDefault(settlementId) + 1);
        RecordRouteConnections();
    }

    /// <summary>The seasonal pass: regrade from wear with hysteresis, then fade wear by one eighth.</summary>
    private void GradeRoadsForSeason()
    {
        var grades = Roads!;
        var wear = RoadWear;
        var changed = false;
        for (var y = 0; y < World.Height; y++)
            for (var x = 0; x < World.Width; x++)
            {
                var index = y * World.Width + x;
                var tileWear = wear[index];
                var coordinate = new TileCoordinate(x, y);
                var grade = grades.GradeAt(coordinate);
                var next = NextRoadGrade(grade, tileWear);
                if (next != grade)
                {
                    grades.SetGrade(coordinate, next);
                    changed = true;
                }
                wear[index] = tileWear - (tileWear + 7) / 8;
            }
        if (changed) OnRoadNetworkChanged();
    }

    internal static RoadGrade NextRoadGrade(RoadGrade grade, int wear) => grade switch
    {
        RoadGrade.Road => RoadGrade.Road,
        RoadGrade.Trail => wear >= RoadTrailFadeWear ? RoadGrade.Trail : wear >= RoadTrackFadeWear ? RoadGrade.Track : RoadGrade.None,
        RoadGrade.Track => wear >= RoadTrailWear ? RoadGrade.Trail : wear >= RoadTrackFadeWear ? RoadGrade.Track : RoadGrade.None,
        _ => wear >= RoadTrailWear ? RoadGrade.Trail : wear >= RoadTrackWear ? RoadGrade.Track : RoadGrade.None
    };

    /// <summary>
    /// Grade changes alter every derived cost. In-flight routes are kept: they are checkpointed, so a
    /// reopened world continues them exactly as an uninterrupted run does.
    /// </summary>
    private void OnRoadNetworkChanged()
    {
        _pathCache.Clear();
        _pathCacheInsertionOrder.Clear();
        _travelCostCache.Clear();
        _travelCostCacheInsertionOrder.Clear();
        _livingTravelCostNodes.Clear();
        _livingTravelCostRecency.Clear();
    }

    /// <summary>Site ownership of a tile uses terrain costs only, so building roads never moves a border.</summary>
    private IReadOnlyDictionary<TileCoordinate, long> SiteOwnershipCosts(TileCoordinate site)
    {
        if (!RoadSystemsEnabled(SimulationRulesVersion)) return GetTravelCostsCached(site);
        if (!_siteTerrainCosts.TryGetValue(site, out var costs))
            _siteTerrainCosts[site] = costs = LivingTravelCosts.Compute(World, site);
        return costs;
    }

    /// <summary>True when a continuous run of tiles at or above the grade joins both settlement sites.</summary>
    private bool SitesConnectedAt(RoadGrade minimum)
    {
        if (Roads is not { } grades || _migrationState?.DaughterSettlement is not { } daughter) return false;
        var start = World.StartingSite;
        var goal = daughter.Site;
        if (grades.GradeAt(start) < minimum || grades.GradeAt(goal) < minimum) return false;
        var seen = new bool[World.Width * World.Height];
        var queue = new Queue<TileCoordinate>();
        queue.Enqueue(start);
        seen[start.Y * World.Width + start.X] = true;
        while (queue.TryDequeue(out var current))
        {
            if (current == goal) return true;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var x = current.X + dx;
                    var y = current.Y + dy;
                    if (x < 0 || y < 0 || x >= World.Width || y >= World.Height) continue;
                    if (dx != 0 && dy != 0 && (!World.GetTile(x, current.Y).Walkable || !World.GetTile(current.X, y).Walkable)) continue;
                    var index = y * World.Width + x;
                    var next = new TileCoordinate(x, y);
                    if (seen[index] || grades.GradeAt(next) < minimum) continue;
                    seen[index] = true;
                    queue.Enqueue(next);
                }
        }
        return false;
    }
}
