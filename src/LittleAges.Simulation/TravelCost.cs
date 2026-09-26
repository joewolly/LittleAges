using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>
/// The single step-cost rule. Without a road overlay it is the unchanged M1 terrain cost;
/// M15 scales that cost by the destination tile's road grade, rounding up.
/// </summary>
public static class TravelCost
{
    public const int MinimumOrthogonalStep = 5;
    public const int MinimumDiagonalStep = 7;

    public static long Step(WorldMap world, TileCoordinate from, TileCoordinate to, RoadGradeMap? roads = null) =>
        Step(from.X == to.X || from.Y == to.Y, world.GetTile(to), roads);

    public static long Step(bool orthogonal, WorldTile destination, RoadGradeMap? roads)
    {
        var terrainCost = checked((orthogonal ? 10L : 14L) * destination.MovementCost);
        if (roads is null) return terrainCost;
        var percent = GradeCostPercent(roads.GradeAt(destination.Coordinate));
        return checked((terrainCost * percent + 99) / 100);
    }

    public static long Path(IReadOnlyList<TileCoordinate> path, WorldMap world, RoadGradeMap? roads = null)
    {
        var cost = 0L;
        for (var index = 1; index < path.Count; index++) cost = checked(cost + Step(world, path[index - 1], path[index], roads));
        return cost;
    }

    public static int GradeCostPercent(RoadGrade grade) => grade switch
    {
        RoadGrade.None => 100,
        RoadGrade.Track => 90,
        RoadGrade.Trail => 75,
        RoadGrade.Road => 50,
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "Unknown road grade.")
    };
}
