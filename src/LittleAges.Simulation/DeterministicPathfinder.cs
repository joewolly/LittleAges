using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Pure, integer weighted A* over the immutable M1 map.</summary>
public sealed class DeterministicPathfinder
{
    private static readonly (int X, int Y)[] Directions =
    [ (0,-1), (1,-1), (1,0), (1,1), (0,1), (-1,1), (-1,0), (-1,-1) ];

    public static IReadOnlyList<TileCoordinate>? FindPath(WorldMap world, TileCoordinate start, TileCoordinate destination, RoadGradeMap? roads = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var startTile = world.GetTile(start);
        var goalTile = world.GetTile(destination);
        if (!startTile.Walkable || !goalTile.Walkable) return null;
        if (start == destination) return Array.AsReadOnly(new[] { start });

        var width = world.Width;
        var goalIndex = destination.ToIndex(width);
        var startIndex = start.ToIndex(width);
        var g = new Dictionary<long, long> { [startIndex] = 0 };
        var parent = new Dictionary<long, long>();
        var startNode = new Node(startIndex, 0, Heuristic(start, destination, roads), 0);
        var queue = new PriorityQueue<Node, NodePriority>();
        queue.Enqueue(startNode, NodePriority.From(startNode));
        long localSequence = 0;
        while (queue.TryDequeue(out var current, out _))
        {
            if (current.Index == goalIndex) return Reconstruct(parent, current.Index, width);
            var coordinate = TileCoordinate.FromIndex(current.Index, width);
            for (var direction = 0; direction < Directions.Length; direction++)
            {
                var (dx, dy) = Directions[direction];
                var nx = coordinate.X + dx; var ny = coordinate.Y + dy;
                if (nx < 0 || nx >= world.Width || ny < 0 || ny >= world.Height) continue;
                if (dx != 0 && dy != 0 && (!world.GetTile(nx, coordinate.Y).Walkable || !world.GetTile(coordinate.X, ny).Walkable)) continue;
                var next = world.GetTile(nx, ny);
                if (!next.Walkable) continue;
                var nextIndex = next.Coordinate.ToIndex(width);
                var step = TravelCost.Step(dx == 0 || dy == 0, next, roads);
                var nextG = checked(current.G + step);
                if (g.TryGetValue(nextIndex, out var oldG) && nextG >= oldG) continue;
                g[nextIndex] = nextG; parent[nextIndex] = current.Index;
                var candidate = new Node(nextIndex, nextG, Heuristic(next.Coordinate, destination, roads), localSequence++);
                queue.Enqueue(candidate, NodePriority.From(candidate));
            }
        }
        return null;
    }

    public static IReadOnlyList<TileCoordinate>? Find(WorldMap world, TileCoordinate start, TileCoordinate destination, RoadGradeMap? roads = null) => FindPath(world, start, destination, roads);

    /// <summary>Computes exact weighted travel costs from one tile to every reachable tile.</summary>
    public static IReadOnlyDictionary<TileCoordinate, long> ComputeTravelCosts(WorldMap world, TileCoordinate start)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.GetTile(start).Walkable) return new Dictionary<TileCoordinate, long>();
        var costs = new Dictionary<TileCoordinate, long> { [start] = 0 };
        var queue = new PriorityQueue<(TileCoordinate Coordinate, long Cost), (long Cost, long Sequence)>();
        long sequence = 1; queue.Enqueue((start, 0), (0, 0));
        while (queue.TryDequeue(out var current, out _))
        {
            if (!costs.TryGetValue(current.Coordinate, out var known) || known != current.Cost) continue;
            for (var direction = 0; direction < Directions.Length; direction++)
            {
                var (dx, dy) = Directions[direction]; var nx = current.Coordinate.X + dx; var ny = current.Coordinate.Y + dy;
                if (nx < 0 || nx >= world.Width || ny < 0 || ny >= world.Height) continue;
                if (dx != 0 && dy != 0 && (!world.GetTile(nx, current.Coordinate.Y).Walkable || !world.GetTile(current.Coordinate.X, ny).Walkable)) continue;
                var next = world.GetTile(nx, ny); if (!next.Walkable) continue;
                var nextCost = checked(current.Cost + TravelCost.Step(dx == 0 || dy == 0, next, null));
                if (costs.TryGetValue(next.Coordinate, out var old) && nextCost >= old) continue;
                costs[next.Coordinate] = nextCost; queue.Enqueue((next.Coordinate, nextCost), (nextCost, sequence++));
            }
        }
        return costs;
    }

    private static long Heuristic(TileCoordinate from, TileCoordinate to, RoadGradeMap? roads)
    {
        var dx = Math.Abs(from.X - to.X); var dy = Math.Abs(from.Y - to.Y);
        // Road steps can cost less than the M1 terrain minimum, so M15 bounds by the cheapest road step.
        if (roads is not null)
            return (long)TravelCost.MinimumOrthogonalStep * Math.Max(dx, dy) +
                   (long)(TravelCost.MinimumDiagonalStep - TravelCost.MinimumOrthogonalStep) * Math.Min(dx, dy);
        return 10L * Math.Max(dx, dy) + 4L * Math.Min(dx, dy);
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<TileCoordinate> Reconstruct(Dictionary<long, long> parent, long index, int width)
    {
        var result = new List<TileCoordinate> { TileCoordinate.FromIndex(index, width) };
        while (parent.TryGetValue(index, out index)) result.Add(TileCoordinate.FromIndex(index, width));
        result.Reverse(); return result.AsReadOnly();
    }
    private readonly record struct Node(long Index, long G, long H, long LocalSequence) { public long F => checked(G + H); }
    private readonly record struct NodePriority(long F, long H, long Index, long LocalSequence) : IComparable<NodePriority>
    {
        public static NodePriority From(Node node) => new(node.F, node.H, node.Index, node.LocalSequence);

        public int CompareTo(NodePriority other)
        {
            var result = F.CompareTo(other.F); if (result != 0) return result;
            result = H.CompareTo(other.H); if (result != 0) return result;
            result = Index.CompareTo(other.Index); return result != 0 ? result : LocalSequence.CompareTo(other.LocalSequence);
        }
    }
}
