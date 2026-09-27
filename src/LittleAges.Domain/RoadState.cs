namespace LittleAges.Domain;

public enum RoadGrade : byte { None = 0, Track = 1, Trail = 2, Road = 3 }

/// <summary>Per-tile M15 road grades over the immutable map. Derived from canonical road state; never checkpointed itself.</summary>
public sealed class RoadGradeMap
{
    private readonly RoadGrade[] _grades;

    public RoadGradeMap(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _grades = new RoadGrade[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public RoadGrade GradeAt(TileCoordinate coordinate) => _grades[Index(coordinate)];

    public void SetGrade(TileCoordinate coordinate, RoadGrade grade)
    {
        if (!Enum.IsDefined(grade)) throw new ArgumentOutOfRangeException(nameof(grade));
        _grades[Index(coordinate)] = grade;
    }

    private int Index(TileCoordinate coordinate)
    {
        if (coordinate.X < 0 || coordinate.X >= Width || coordinate.Y < 0 || coordinate.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return coordinate.Y * Width + coordinate.X;
    }
}

/// <summary>One tile with road wear or a grade. Tiles with neither are omitted.</summary>
public sealed record RoadTileState(int X, int Y, int Wear, RoadGrade Grade)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public TileCoordinate Coordinate => new(X, Y);
}

/// <summary>
/// A traveler's planned route for one action sequence. M15 checkpoints routes so a reopened world
/// continues along exactly the route an uninterrupted run would, even after road grades change.
/// </summary>
public sealed class RoadActiveRouteState
{
    public RoadActiveRouteState(long citizenId, long actionSequence, IReadOnlyList<TileCoordinate> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        CitizenId = citizenId;
        ActionSequence = actionSequence;
        Route = Array.AsReadOnly(route.ToArray());
    }

    public long CitizenId { get; }
    public long ActionSequence { get; }
    public IReadOnlyList<TileCoordinate> Route { get; }
}

/// <summary>Road tiles a settlement has paved since the last season boundary, reported in its season history.</summary>
public sealed record RoadSeasonWorkState(long SettlementId, int TilesBuilt);

/// <summary>M15 canonical road network: wear and grades over the immutable map, plus in-flight routes.</summary>
public sealed class RoadNetworkState
{
    public RoadNetworkState(int version, IReadOnlyList<RoadTileState> tiles, IReadOnlyList<RoadActiveRouteState> activeRoutes,
        long? trailConnectedMinute = null, long? roadConnectedMinute = null, IReadOnlyList<RoadSeasonWorkState>? seasonWork = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(activeRoutes);
        Version = version;
        Tiles = Array.AsReadOnly(tiles.ToArray());
        ActiveRoutes = Array.AsReadOnly(activeRoutes.ToArray());
        TrailConnectedMinute = trailConnectedMinute;
        RoadConnectedMinute = roadConnectedMinute;
        SeasonWork = seasonWork is null || seasonWork.Count == 0 ? null : Array.AsReadOnly(seasonWork.ToArray());
    }

    public int Version { get; }
    public IReadOnlyList<RoadTileState> Tiles { get; }
    public IReadOnlyList<RoadActiveRouteState> ActiveRoutes { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? TrailConnectedMinute { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? RoadConnectedMinute { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RoadSeasonWorkState>? SeasonWork { get; }

    public RoadNetworkState Validate()
    {
        if (Version != 1 || Tiles is null || ActiveRoutes is null || TrailConnectedMinute is < 0 || RoadConnectedMinute is < 0 ||
            RoadConnectedMinute is not null && TrailConnectedMinute is null)
            throw new ArgumentException("Unsupported or incomplete road state.");
        if (Tiles.Any(x => x is null || x.X < 0 || x.Y < 0 || x.Wear < 0 || !Enum.IsDefined(x.Grade) ||
                (x.Wear == 0 && x.Grade == RoadGrade.None)) ||
            !Tiles.Select(x => x.Coordinate).SequenceEqual(Tiles.Select(x => x.Coordinate).Order()) ||
            Tiles.Select(x => x.Coordinate).Distinct().Count() != Tiles.Count)
            throw new ArgumentException("Road tiles must be unique, ordered by coordinate, and carry wear or a grade.");
        if (ActiveRoutes.Any(x => x is null || x.CitizenId <= 0 || x.ActionSequence < 0 || x.Route.Count == 0) ||
            ActiveRoutes.Select(x => x.CitizenId).Distinct().Count() != ActiveRoutes.Count ||
            !ActiveRoutes.Select(x => x.CitizenId).SequenceEqual(ActiveRoutes.Select(x => x.CitizenId).Order()))
            throw new ArgumentException("Active routes must be unique per citizen and ordered by citizen.");
        foreach (var route in ActiveRoutes)
            for (var index = 1; index < route.Route.Count; index++)
                if (Math.Max(Math.Abs(route.Route[index].X - route.Route[index - 1].X), Math.Abs(route.Route[index].Y - route.Route[index - 1].Y)) != 1)
                    throw new ArgumentException("Active routes must move one tile per step.");
        if (SeasonWork is not null && (SeasonWork.Any(x => x is null || x.SettlementId is not (1 or 2) || x.TilesBuilt <= 0) ||
                !SeasonWork.Select(x => x.SettlementId).SequenceEqual(SeasonWork.Select(x => x.SettlementId).Distinct().Order())))
            throw new ArgumentException("Seasonal road work must be positive, unique, and ordered by settlement.");
        return this;
    }
}
