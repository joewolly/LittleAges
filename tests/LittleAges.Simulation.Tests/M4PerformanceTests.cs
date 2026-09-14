using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M4PerformanceTests
{
    [Fact]
    public void ImmutableMapPathAndTravelCachesEvictDeterministicallyWithoutChangingSettlementState()
    {
        var cold = new SimulationEngine(new WorldSeed(42));
        var warmed = new SimulationEngine(new WorldSeed(42));
        var findPath = GetMethod("FindPathCached");
        var travelCosts = GetMethod("GetTravelCostsCached");
        var regenerate = GetMethod("RegenerateResources");
        var destination = warmed.World.StartingSite;
        var starts = warmed.World.Tiles.Where(tile => tile.Walkable && tile.Coordinate != destination)
            .OrderBy(tile => Math.Abs(tile.Coordinate.X - destination.X) + Math.Abs(tile.Coordinate.Y - destination.Y))
            .ThenBy(tile => tile.Coordinate.Y).ThenBy(tile => tile.Coordinate.X).Select(tile => tile.Coordinate).Take(513).ToArray();

        Assert.Equal(513, starts.Length);
        foreach (var start in starts) _ = findPath.Invoke(warmed, [start, destination]);
        foreach (var start in starts.Take(129)) _ = travelCosts.Invoke(warmed, [start]);

        Assert.InRange(CacheCount(warmed, "_pathCache"), 1, 512);
        Assert.InRange(CacheCount(warmed, "_travelCostCache"), 1, 128);

        regenerate.Invoke(cold, null);
        regenerate.Invoke(warmed, null);

        Assert.Equal(cold.ComputeSettlementFingerprint(), warmed.ComputeSettlementFingerprint());
        Assert.InRange(CacheCount(warmed, "_pathCache"), 1, 512);
        Assert.InRange(CacheCount(warmed, "_travelCostCache"), 1, 128);
    }

    [Fact]
    public void ReloadStartsWithEmptyCachesAndRetainsCanonicalSettlementEvolution()
    {
        var uninterrupted = new SimulationEngine(new WorldSeed(0));
        var checkpointSource = new SimulationEngine(new WorldSeed(0));
        var findPath = GetMethod("FindPathCached");
        _ = findPath.Invoke(checkpointSource, [checkpointSource.World.StartingSite, checkpointSource.World.Resources[0].Coordinate]);
        var checkpoint = checkpointSource.CreatePersistenceSnapshot();
        var reloaded = SimulationEngine.FromPersistenceSnapshot(checkpoint);

        Assert.Equal(0, CacheCount(reloaded, "_pathCache"));
        Assert.Equal(0, CacheCount(reloaded, "_travelCostCache"));

        Assert.Equal(uninterrupted.ComputeSettlementFingerprint(), reloaded.ComputeSettlementFingerprint());
    }

    private static MethodInfo GetMethod(string name) => typeof(SimulationEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Private cache method '{name}' was not found.");

    private static int CacheCount(SimulationEngine engine, string fieldName)
    {
        var cache = typeof(SimulationEngine).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(engine)
            ?? throw new InvalidOperationException($"Cache field '{fieldName}' was not found.");
        return (int)(cache.GetType().GetProperty("Count")?.GetValue(cache)
            ?? throw new InvalidOperationException($"Cache field '{fieldName}' has no count."));
    }
}
