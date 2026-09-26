using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M15RoadRulesTests
{
    private const string Roads = SimulationEngine.RoadsSimulationRulesVersion;

    [Fact]
    public void M15IncludesEveryM14SystemAndStaysOptInForNewWorlds()
    {
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, SimulationEngine.CurrentSimulationRulesVersion);
        Assert.True(SimulationEngine.MigrationSystemsEnabled(Roads));
        Assert.True(SimulationEngine.UnifiedSimulationRulesEnabled(Roads));
        Assert.True(SimulationEngine.LivingSystemsEnabled(Roads));
        Assert.True(SimulationEngine.SocialSystemsEnabled(Roads));
        Assert.True(SimulationEngine.HistorySystemsEnabled(Roads));
        Assert.True(SimulationEngine.GrowthSystemsEnabled(Roads));
        Assert.True(SimulationEngine.AgricultureSystemsEnabled(Roads));
        Assert.True(SimulationEngine.EconomySystemsEnabled(Roads));
        Assert.Equal(20, SimulationEngine.FoodShortageRecoveryMultiplier(Roads));

        Assert.True(SimulationEngine.RoadSystemsEnabled(Roads));
        Assert.False(SimulationEngine.RoadSystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.False(SimulationEngine.RoadSystemsEnabled(SimulationEngine.UnifiedSimulationRulesVersion));
    }

    [Fact]
    public void StepCostWithoutRoadsIsTheUnchangedTerrainCost()
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        foreach (var tile in world.Tiles.Where(x => x.Walkable))
        {
            Assert.Equal(10L * tile.MovementCost, TravelCost.Step(true, tile, null));
            Assert.Equal(14L * tile.MovementCost, TravelCost.Step(false, tile, null));
        }
    }

    [Theory]
    [InlineData(TerrainType.Grassland, RoadGrade.None, 10, 14)]
    [InlineData(TerrainType.Grassland, RoadGrade.Track, 9, 13)]
    [InlineData(TerrainType.Grassland, RoadGrade.Trail, 8, 11)]
    [InlineData(TerrainType.Grassland, RoadGrade.Road, 5, 7)]
    [InlineData(TerrainType.Forest, RoadGrade.Trail, 15, 21)]
    [InlineData(TerrainType.DenseWilderness, RoadGrade.Road, 15, 21)]
    public void RoadGradesScaleTerrainCostRoundingUp(TerrainType terrain, RoadGrade grade, long orthogonal, long diagonal)
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        var tile = world.Tiles.First(x => x.Terrain == terrain);
        var roads = new RoadGradeMap(world.Width, world.Height);
        roads.SetGrade(tile.Coordinate, grade);

        Assert.Equal(orthogonal, TravelCost.Step(true, tile, roads));
        Assert.Equal(diagonal, TravelCost.Step(false, tile, roads));
        Assert.True(orthogonal >= TravelCost.MinimumOrthogonalStep);
        Assert.True(diagonal >= TravelCost.MinimumDiagonalStep);
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(17UL)]
    public void RoadAwarePathsAreOptimalAndAgreeWithDerivedTravelCosts(ulong seed)
    {
        var world = new WorldGenerator().Generate(new WorldSeed(seed));
        var roads = new RoadGradeMap(world.Width, world.Height);
        foreach (var tile in world.Tiles.Where(x => x.Walkable))
            roads.SetGrade(tile.Coordinate, (RoadGrade)((tile.Coordinate.X * 7 + tile.Coordinate.Y * 3) % 4));

        var start = world.StartingSite;
        var legacyCosts = LivingTravelCosts.Compute(world, start);
        var roadCosts = LivingTravelCosts.Compute(world, start, roads);
        var destinations = legacyCosts.Where(x => x.Value > 0).OrderBy(x => x.Key).Where((_, index) => index % 97 == 0).Select(x => x.Key).ToArray();
        Assert.NotEmpty(destinations);

        foreach (var destination in destinations)
        {
            var legacyPath = DeterministicPathfinder.FindPath(world, start, destination)!;
            Assert.Equal(legacyCosts[destination], TravelCost.Path(legacyPath, world));

            var roadPath = DeterministicPathfinder.FindPath(world, start, destination, roads)!;
            Assert.Equal(roadCosts[destination], TravelCost.Path(roadPath, world, roads));
            Assert.True(roadCosts[destination] <= TravelCost.Path(legacyPath, world, roads));
        }
    }

    [Fact]
    public void EmptyRoadOverlayKeepsTerrainOptimalCosts()
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        var roads = new RoadGradeMap(world.Width, world.Height);
        var legacy = LivingTravelCosts.Compute(world, world.StartingSite);
        var empty = LivingTravelCosts.Compute(world, world.StartingSite, roads);

        Assert.Equal(legacy.Count, empty.Count);
        foreach (var (coordinate, cost) in legacy) Assert.Equal(cost, empty[coordinate]);
    }

    [Fact]
    public void M15RunsAreChunkIndependentAndRestoreFromSnapshots()
    {
        var seed = new WorldSeed(42);
        var target = new WorldMinute(20L * WorldCalendar.MinutesPerDay);
        var single = new SimulationEngine(seed, simulationRulesVersion: Roads);
        single.AdvanceUntil(target);

        var chunked = new SimulationEngine(seed, simulationRulesVersion: Roads);
        for (var day = 1; day <= 10; day++) chunked.AdvanceUntil(new WorldMinute(day * (long)WorldCalendar.MinutesPerDay));
        var restored = new SimulationEngine(chunked.CreatePersistenceSnapshot());
        Assert.Equal(Roads, restored.SimulationRulesVersion);
        for (var day = 11; day <= 20; day++) restored.AdvanceUntil(new WorldMinute(day * (long)WorldCalendar.MinutesPerDay));

        var expected = single.CreatePersistenceSnapshot();
        var actual = restored.CreatePersistenceSnapshot();
        Assert.Equal(single.SettlementFingerprint, restored.SettlementFingerprint);
        Assert.Equal(single.SocialFingerprint, restored.SocialFingerprint);
        Assert.Equal(expected.LivingStateJson, actual.LivingStateJson);
        Assert.Equal(expected.MigrationStateJson, actual.MigrationStateJson);
    }
}
