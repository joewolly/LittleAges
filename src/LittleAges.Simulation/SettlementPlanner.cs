using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>The part of a planned settlement a building belongs to.</summary>
internal enum PlanDistrict
{
    Core = 0,
    Storage = 1,
    Homes = 2,
    Crafts = 3,
    Farmland = 4
}

/// <summary>
/// M16 settlement layout. Each settlement site gets an organic plan derived only from
/// immutable geography: fields face the most fertile ground, the storage yard sits between
/// the core and the fields on the side richer in wood and stone, crafts sit on the other
/// flank, and homes spread out behind the core. Plans are never checkpointed; the same
/// world and site always rebuild the same plan.
/// </summary>
internal sealed class SettlementPlan
{
    // Octagonal compass, clockwise from north; ties resolve to the earlier direction.
    private static readonly (int X, int Y)[] Compass = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];
    internal const int SurveyRadius = 10;
    internal const int CoreRadius = 2;
    private const int StorageReach = 3;
    private const int CraftsReach = 4;
    private const int HomesReach = 4;
    private const int FarmlandReach = 6;

    // How strongly each district claims nearby land (percent of travel cost; lower claims more).
    private static readonly int[] ClaimWeight = [0, 100, 85, 125, 110];

    private readonly WorldMap _world;
    private readonly TileCoordinate[] _anchors;
    private readonly IReadOnlyDictionary<TileCoordinate, long>[] _costs;
    private readonly byte[] _districts;

    private SettlementPlan(WorldMap world, TileCoordinate site, TileCoordinate[] anchors, IReadOnlyDictionary<TileCoordinate, long>[] costs, byte[] districts)
    {
        _world = world; Site = site; _anchors = anchors; _costs = costs; _districts = districts;
    }

    public TileCoordinate Site { get; }

    public TileCoordinate Anchor(PlanDistrict district) => _anchors[(int)district];

    /// <summary>Terrain travel cost from the district's anchor, or null where it cannot be reached.</summary>
    public long? CostFrom(PlanDistrict district, TileCoordinate tile) => _costs[(int)district].TryGetValue(tile, out var cost) ? cost : null;

    /// <summary>The district that claims a tile, or null for tiles the site cannot reach.</summary>
    public PlanDistrict? DistrictOf(TileCoordinate tile)
    {
        var value = _districts[tile.Y * _world.Width + tile.X];
        return value == byte.MaxValue ? null : (PlanDistrict)value;
    }

    public static SettlementPlan Create(WorldMap world, TileCoordinate site)
    {
        ArgumentNullException.ThrowIfNull(world);
        var siteCosts = LivingTravelCosts.Compute(world, site);
        var fertility = new long[Compass.Length];
        var materials = new long[Compass.Length];
        var resourceTiles = world.Resources.Where(x => x.Type is ResourceType.Wood or ResourceType.Stone).Select(x => x.Coordinate).ToHashSet();
        for (var y = Math.Max(0, site.Y - SurveyRadius); y <= Math.Min(world.Height - 1, site.Y + SurveyRadius); y++)
            for (var x = Math.Max(0, site.X - SurveyRadius); x <= Math.Min(world.Width - 1, site.X + SurveyRadius); x++)
            {
                var coordinate = new TileCoordinate(x, y);
                if (Chebyshev(coordinate, site) < CoreRadius || !siteCosts.ContainsKey(coordinate)) continue;
                var octant = Octant(x - site.X, y - site.Y);
                var tile = world.GetTile(coordinate);
                if (AgricultureRules.Suitable(tile)) fertility[octant] += AgricultureRules.PotentialYield(tile);
                if (resourceTiles.Contains(coordinate)) materials[octant]++;
            }

        var fields = 0;
        for (var index = 1; index < Compass.Length; index++)
            if (fertility[index] > fertility[fields]) fields = index;
        var clockwise = (fields + 1) % Compass.Length;
        var counter = (fields + Compass.Length - 1) % Compass.Length;
        var storage = materials[counter] > materials[clockwise] ? counter : clockwise;
        // Crafts take the flank away from the storage yard; homes spread out behind the core.
        var crafts = storage == clockwise ? (fields + Compass.Length - 2) % Compass.Length : (fields + 2) % Compass.Length;
        var homes = (fields + Compass.Length / 2) % Compass.Length;

        var anchors = new TileCoordinate[5];
        anchors[(int)PlanDistrict.Core] = site;
        anchors[(int)PlanDistrict.Storage] = Snap(world, siteCosts, site, storage, StorageReach);
        anchors[(int)PlanDistrict.Homes] = Snap(world, siteCosts, site, homes, HomesReach);
        anchors[(int)PlanDistrict.Crafts] = Snap(world, siteCosts, site, crafts, CraftsReach);
        anchors[(int)PlanDistrict.Farmland] = Snap(world, siteCosts, site, fields, FarmlandReach);

        var costs = new IReadOnlyDictionary<TileCoordinate, long>[5];
        costs[(int)PlanDistrict.Core] = siteCosts;
        for (var district = 1; district < costs.Length; district++)
            costs[district] = anchors[district] == site ? siteCosts : LivingTravelCosts.Compute(world, anchors[district]);

        var districts = new byte[checked(world.Width * world.Height)];
        Array.Fill(districts, byte.MaxValue);
        foreach (var (coordinate, _) in siteCosts)
        {
            var best = (int)PlanDistrict.Core;
            if (Chebyshev(coordinate, site) > CoreRadius)
            {
                var bestScore = long.MaxValue;
                for (var district = 1; district < costs.Length; district++)
                {
                    if (!costs[district].TryGetValue(coordinate, out var cost)) continue;
                    var score = checked(cost * ClaimWeight[district]);
                    if (score < bestScore) { bestScore = score; best = district; }
                }
                if (bestScore == long.MaxValue) best = (int)PlanDistrict.Homes;
            }
            districts[coordinate.Y * world.Width + coordinate.X] = (byte)best;
        }
        return new SettlementPlan(world, site, anchors, costs, districts);
    }

    internal static int Chebyshev(TileCoordinate first, TileCoordinate second) => Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    /// <summary>Integer octant of an offset: 22.5 degree sectors approximated by a 2:5 slope.</summary>
    private static int Octant(int dx, int dy)
    {
        var sx = Math.Sign(dx);
        var sy = Math.Sign(dy);
        if (5 * Math.Abs(dy) <= 2 * Math.Abs(dx)) sy = 0;
        else if (5 * Math.Abs(dx) <= 2 * Math.Abs(dy)) sx = 0;
        return Array.IndexOf(Compass, (sx, sy));
    }

    /// <summary>The reachable, buildable tile nearest the ideal point <paramref name="reach"/> steps out along a compass direction.</summary>
    private static TileCoordinate Snap(WorldMap world, LivingTravelCosts siteCosts, TileCoordinate site, int direction, int reach)
    {
        var (dx, dy) = Compass[direction];
        var idealX = site.X + dx * reach;
        var idealY = site.Y + dy * reach;
        TileCoordinate? best = null;
        var bestDistance = int.MaxValue;
        for (var y = Math.Max(0, idealY - SurveyRadius); y <= Math.Min(world.Height - 1, idealY + SurveyRadius); y++)
            for (var x = Math.Max(0, idealX - SurveyRadius); x <= Math.Min(world.Width - 1, idealX + SurveyRadius); x++)
            {
                var coordinate = new TileCoordinate(x, y);
                if (Chebyshev(coordinate, site) <= CoreRadius || !world.GetTile(coordinate).Buildable || !siteCosts.ContainsKey(coordinate)) continue;
                var distance = Math.Abs(x - idealX) + Math.Abs(y - idealY);
                if (distance < bestDistance) { bestDistance = distance; best = coordinate; }
            }
        return best ?? site;
    }
}

/// <summary>Chooses where a planned settlement puts each new building.</summary>
internal static class SettlementPlanner
{
    /// <summary>What kind of neighbour a building is for spacing: storage and fields may pack edge to edge with their own kind.</summary>
    internal enum Footprint { Solitary, Storage, Field }

    internal readonly record struct Placed(TileCoordinate Location, Footprint Footprint);

    public static PlanDistrict DistrictFor(StructureType type) => type switch
    {
        StructureType.Shelter => PlanDistrict.Homes,
        StructureType.Stockpile or StructureType.Storehouse or StructureType.Granary => PlanDistrict.Storage,
        StructureType.Workshop => PlanDistrict.Crafts,
        StructureType.Marketplace => PlanDistrict.Core,
        StructureType.Farm => PlanDistrict.Farmland,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static Footprint FootprintFor(StructureType type) =>
        type is StructureType.Stockpile or StructureType.Storehouse or StructureType.Granary ? Footprint.Storage : Footprint.Solitary;

    /// <summary>
    /// The best free tile for a building of <paramref name="footprint"/> in <paramref name="district"/>.
    /// Candidates are tried in widening passes: inside the district with full spacing and off worn paths,
    /// then in neighbouring districts (never the plaza), then with spacing anywhere, then anywhere at all,
    /// so a crowded site still grows.
    /// </summary>
    public static TileCoordinate? Choose(SettlementPlan plan, PlanDistrict district, Footprint footprint,
        IEnumerable<WorldTile> candidates, IReadOnlyCollection<Placed> placed, Func<TileCoordinate, bool> onPath,
        Func<WorldTile, int>? preference = null)
    {
        var blocked = new Dictionary<TileCoordinate, Footprint>();
        void Block(TileCoordinate center, Footprint kind)
        {
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    // Buildings on the map's top or left edge have no neighbours beyond it.
                    if (center.X + dx < 0 || center.Y + dy < 0) continue;
                    var tile = new TileCoordinate(center.X + dx, center.Y + dy);
                    // A tile beside two different kinds of building stays closed to both.
                    blocked[tile] = blocked.TryGetValue(tile, out var existing) && existing != kind ? Footprint.Solitary : kind;
                }
        }
        Block(plan.Site, Footprint.Solitary);
        foreach (var building in placed) Block(building.Location, building.Footprint);

        var ranked = candidates
            .Select(tile => (Tile: tile, Cost: plan.CostFrom(district, tile.Coordinate)))
            .Where(x => x.Cost is not null)
            .Select(x => (x.Tile, Cost: x.Cost!.Value, Own: plan.DistrictOf(x.Tile.Coordinate) == district,
                // Overflow may spill into a neighbouring district, but only the core's own buildings use the plaza.
                Open: district == PlanDistrict.Core || plan.DistrictOf(x.Tile.Coordinate) != PlanDistrict.Core,
                Spaced: !blocked.TryGetValue(x.Tile.Coordinate, out var near) || footprint != Footprint.Solitary && near == footprint,
                Clear: !onPath(x.Tile.Coordinate)))
            .OrderBy(x => x.Cost - (preference?.Invoke(x.Tile) ?? 0))
            .ThenBy(x => Math.Abs(x.Tile.Coordinate.X - plan.Site.X) + Math.Abs(x.Tile.Coordinate.Y - plan.Site.Y))
            .ThenBy(x => x.Tile.Coordinate.Y).ThenBy(x => x.Tile.Coordinate.X)
            .ToArray();
        return FirstOrNull(ranked.Where(x => x.Own && x.Spaced && x.Clear))
            ?? FirstOrNull(ranked.Where(x => x.Open && x.Spaced && x.Clear))
            ?? FirstOrNull(ranked.Where(x => x.Open && x.Spaced))
            ?? FirstOrNull(ranked.Where(x => x.Spaced))
            ?? FirstOrNull(ranked);

        static TileCoordinate? FirstOrNull(IEnumerable<(WorldTile Tile, long Cost, bool Own, bool Open, bool Spaced, bool Clear)> source)
        {
            foreach (var item in source) return item.Tile.Coordinate;
            return null;
        }
    }
}
