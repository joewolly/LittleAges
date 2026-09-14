using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Pure, integer weighted A* over the immutable M1 map.</summary>
public sealed class DeterministicPathfinder
{
    private static readonly (int X, int Y)[] Directions =
    [ (0,-1), (1,-1), (1,0), (1,1), (0,1), (-1,1), (-1,0), (-1,-1) ];

    public static IReadOnlyList<TileCoordinate>? FindPath(WorldMap world, TileCoordinate start, TileCoordinate destination)
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
        var queue = new List<Node> { new(startIndex, 0, Heuristic(start, destination), 0) };
        long localSequence = 0;
        while (queue.Count > 0)
        {
            queue.Sort(NodeComparer.Instance);
            var current = queue[0]; queue.RemoveAt(0);
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
                var step = checked((dx == 0 || dy == 0 ? 10L : 14L) * next.MovementCost);
                var nextG = checked(current.G + step);
                if (g.TryGetValue(nextIndex, out var oldG) && nextG >= oldG) continue;
                g[nextIndex] = nextG; parent[nextIndex] = current.Index;
                queue.Add(new Node(nextIndex, nextG, Heuristic(next.Coordinate, destination), localSequence++));
            }
        }
        return null;
    }

    public static IReadOnlyList<TileCoordinate>? Find(WorldMap world, TileCoordinate start, TileCoordinate destination) => FindPath(world, start, destination);

    private static long Heuristic(TileCoordinate from, TileCoordinate to)
    {
        var dx = Math.Abs(from.X - to.X); var dy = Math.Abs(from.Y - to.Y);
        return 10L * Math.Max(dx, dy) + 4L * Math.Min(dx, dy);
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<TileCoordinate> Reconstruct(Dictionary<long, long> parent, long index, int width)
    {
        var result = new List<TileCoordinate> { TileCoordinate.FromIndex(index, width) };
        while (parent.TryGetValue(index, out index)) result.Add(TileCoordinate.FromIndex(index, width));
        result.Reverse(); return result.AsReadOnly();
    }
    private readonly record struct Node(long Index, long G, long H, long LocalSequence) { public long F => checked(G + H); }
    private sealed class NodeComparer : IComparer<Node>
    {
        public static readonly NodeComparer Instance = new();
        public int Compare(Node a, Node b)
        {
            var result = a.F.CompareTo(b.F); if (result != 0) return result;
            result = a.H.CompareTo(b.H); if (result != 0) return result;
            result = a.Index.CompareTo(b.Index); return result != 0 ? result : a.LocalSequence.CompareTo(b.LocalSequence);
        }
    }
}
