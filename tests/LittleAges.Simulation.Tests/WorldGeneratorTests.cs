using LittleAges.Domain;
using LittleAges.Simulation;
using System.Diagnostics;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class WorldGeneratorTests
{
    [Fact]
    public void FixedSeedsGenerateEqualValidatedMapsAndDifferentFingerprints()
    {
        var generator = new WorldGenerator();
        var seeds = new[] { 0UL, 1UL, 2UL, 42UL, 123456789UL, ulong.MaxValue };
        var maps = seeds.Select(seed => generator.Generate(new WorldSeed(seed))).ToArray();

        for (var index = 0; index < maps.Length; index++)
        {
            var map = maps[index];
            var repeated = generator.Generate(new WorldSeed(seeds[index]));
            Assert.Equal(map.Fingerprint, repeated.Fingerprint);
            Assert.Equal(160 * 160, map.Tiles.Count);
            Assert.Equal(map.Tiles, map.EnumerateTilesRowMajor());
            Assert.Equal(map.Resources.OrderBy(node => node.Id.Value), map.Resources);
            map.Validate();
        }

        Assert.Equal("97eeea22c01791cef8957c6f461c7428cb59f1bb38a66d8b27c65ce8c93923bc", maps[0].Fingerprint);
        Assert.Equal("a0568524bd2257a3126b91dadd57825b06b15d20d1f09376c62180d48b54dd81", maps[3].Fingerprint);
        Assert.Equal("b9cbba5088e6b0968928f91ddedaf4eedb7a270840888e350d42fa0f31c1bd64", maps[^1].Fingerprint);
        Assert.Equal(seeds.Length, maps.Select(map => map.Fingerprint).Distinct(StringComparer.Ordinal).Count());
        Assert.False(maps[0].Tiles.Select(tile => tile.Terrain).SequenceEqual(maps[1].Tiles.Select(tile => tile.Terrain)));
        Assert.False(maps[0].Resources.Select(resource => resource.Id).SequenceEqual(maps[1].Resources.Select(resource => resource.Id)));
        Assert.NotEqual(maps[0].StartingSite, maps[1].StartingSite);
    }

    [Fact]
    public void EngineRestoresThePersistedWorldWithoutChangingFingerprint()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);

        Assert.NotNull(snapshot.World);
        Assert.Equal(source.World.Fingerprint, restored.World.Fingerprint);
        Assert.Equal(source.World.GenerationAttempt, restored.World.GenerationAttempt);
    }

    [Fact]
    public void ExhaustedViabilityAttemptsUseWorldGenerationException()
    {
        var configuration = new WorldGenerationConfiguration
        {
            Width = 8,
            Height = 8,
            MinimumNearbyFood = 100_000_000,
            MaximumAttempts = 2
        };

        var exception = Assert.Throws<WorldGenerationException>(() => new WorldGenerator().Generate(new WorldSeed(7), configuration));
        Assert.Contains("2 deterministic attempts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WaterAccessIsDerivedFromFreshwaterAndFertilityTracksIt()
    {
        var map = new WorldGenerator().Generate(new WorldSeed(0));
        var freshwater = map.Tiles.Where(tile => tile.Terrain == TerrainType.Freshwater).ToArray();
        var adjacent = map.Tiles.Where(tile => tile.Terrain != TerrainType.Freshwater && freshwater.Any(water => Math.Abs(water.Coordinate.X - tile.Coordinate.X) <= 1 && Math.Abs(water.Coordinate.Y - tile.Coordinate.Y) <= 1)).ToArray();

        Assert.NotEmpty(freshwater);
        Assert.All(freshwater, tile => Assert.Equal(10000, tile.WaterAccess));
        Assert.NotEmpty(adjacent);
        Assert.All(adjacent, tile => Assert.InRange(tile.WaterAccess, 5000, 10000));
        Assert.All(map.Tiles.Where(tile => tile.Terrain != TerrainType.Freshwater), tile => Assert.InRange(tile.WaterAccess, 0, 5000));

        var wetFertility = map.Tiles.Where(tile => tile.WaterAccess >= 5000).Average(tile => tile.Fertility);
        var dryFertility = map.Tiles.Where(tile => tile.WaterAccess <= 1000).Average(tile => tile.Fertility);
        Assert.True(wetFertility > dryFertility);
    }

    [Fact]
    public void DefaultGenerationCompletesWithinTenSeconds()
    {
        var stopwatch = Stopwatch.StartNew();
        var map = new WorldGenerator().Generate(new WorldSeed(ulong.MaxValue));
        stopwatch.Stop();

        Assert.Equal(160 * 160, map.Tiles.Count);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }
}
