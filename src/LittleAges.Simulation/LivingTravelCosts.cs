using System.Collections;
using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Compact, derived distances over immutable geography. Never checkpointed.</summary>
internal sealed class LivingTravelCosts : IReadOnlyDictionary<TileCoordinate, long>
{
    private readonly long[] _costs;
    private readonly int _width;
    private readonly int _height;
    private static readonly (int X, int Y)[] Directions = [(0,-1), (1,-1), (1,0), (1,1), (0,1), (-1,1), (-1,0), (-1,-1)];

    private LivingTravelCosts(WorldMap world)
    {
        _width = world.Width;
        _height = world.Height;
        _costs = new long[checked(_width * _height)];
        Array.Fill(_costs, long.MaxValue);
    }

    public static LivingTravelCosts Compute(WorldMap world, TileCoordinate start)
    {
        var result = new LivingTravelCosts(world);
        if (!world.GetTile(start).Walkable) return result;
        var startIndex = checked(start.Y * world.Width + start.X);
        result._costs[startIndex] = 0;
        result.Count = 1;
        // Equal-cost expansion order cannot affect shortest distances. Actual routes
        // still use the compatibility pathfinder and its explicit tie ordering.
        var queue = new PriorityQueue<int, long>();
        queue.Enqueue(startIndex, 0);
        while (queue.TryDequeue(out var currentIndex, out var currentCost))
        {
            if (result._costs[currentIndex] != currentCost) continue;
            var cx = currentIndex % world.Width;
            var cy = currentIndex / world.Width;
            foreach (var (dx, dy) in Directions)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
                if (dx != 0 && dy != 0 && (!world.GetTile(x, cy).Walkable || !world.GetTile(cx, y).Walkable)) continue;
                var tile = world.GetTile(x, y);
                if (!tile.Walkable) continue;
                var index = y * world.Width + x;
                var cost = checked(currentCost + (dx == 0 || dy == 0 ? 10L : 14L) * tile.MovementCost);
                if (cost >= result._costs[index]) continue;
                if (result._costs[index] == long.MaxValue) result.Count++;
                result._costs[index] = cost;
                queue.Enqueue(index, cost);
            }
        }
        return result;
    }

    public int Count { get; private set; }
    public IEnumerable<TileCoordinate> Keys => this.Select(x => x.Key);
    public IEnumerable<long> Values => this.Select(x => x.Value);
    public long this[TileCoordinate key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
    public bool ContainsKey(TileCoordinate key) => TryGetValue(key, out _);
    public bool TryGetValue(TileCoordinate key, out long value)
    {
        if (key.X >= 0 && key.X < _width && key.Y >= 0 && key.Y < _height && _costs[key.Y * _width + key.X] != long.MaxValue)
        { value = _costs[key.Y * _width + key.X]; return true; }
        value = 0;
        return false;
    }
    public IEnumerator<KeyValuePair<TileCoordinate, long>> GetEnumerator()
    {
        for (var i = 0; i < _costs.Length; i++)
            if (_costs[i] != long.MaxValue) yield return new(new TileCoordinate(i % _width, i / _width), _costs[i]);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
