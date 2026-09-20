using LittleAges.Domain;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class LivingTravelCostsTests
{
    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(12345UL)]
    public void CompactDerivedCostsExactlyMatchExistingPathfinder(ulong seed)
    {
        var world = new SimulationEngine(new WorldSeed(seed)).World;
        var starts = new[] { world.StartingSite, world.Tiles.First(x => x.Walkable).Coordinate, world.Tiles.Last(x => x.Walkable).Coordinate };
        foreach (var start in starts)
        {
            var expected = DeterministicPathfinder.ComputeTravelCosts(world, start);
            var actual = LivingTravelCosts.Compute(world, start);
            Assert.Equal(expected.Count, actual.Count);
            foreach (var tile in world.Tiles)
            {
                Assert.Equal(expected.TryGetValue(tile.Coordinate, out var expectedCost), actual.TryGetValue(tile.Coordinate, out var actualCost));
                Assert.Equal(expectedCost, actualCost);
            }
        }
    }
}
