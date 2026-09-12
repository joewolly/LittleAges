using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class WorldCoreTests
{
    [Fact]
    public void CoordinatesRoundTripAndOrderRowMajor()
    {
        Assert.Equal(13L, new TileCoordinate(3, 2).ToIndex(5));
        Assert.Equal(new TileCoordinate(3, 2), TileCoordinate.FromIndex(13, 5));
        Assert.True(new TileCoordinate(0, 1) > new TileCoordinate(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileCoordinate(5, 0).ToIndex(5));
    }

    [Fact]
    public void ConfigurationIsCanonicalAndStrictlyParsed()
    {
        var configuration = WorldGenerationConfiguration.Default;
        var json = configuration.ToCanonicalJson();
        Assert.Equal(json, WorldGenerationConfiguration.Parse(json).ToCanonicalJson());
        var reordered = json.Replace("{\"version\":1,\"width\":160,\"height\":160,", "{\"height\":160,\"width\":160,\"version\":1,", StringComparison.Ordinal);
        Assert.NotEqual(json, reordered);
        Assert.Equal(json, WorldGenerationConfiguration.Parse(reordered).ToCanonicalJson());
        Assert.Throws<FormatException>(() => WorldGenerationConfiguration.Parse(json[..^1] + ",\"unknown\":1}"));
        Assert.Throws<FormatException>(() => WorldGenerationConfiguration.Parse(json.Replace("\"width\":160,", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public void PersistedEnumValuesAreExplicit()
    {
        Assert.Equal(1, (int)TerrainType.Freshwater);
        Assert.Equal(5, (int)TerrainType.DenseWilderness);
        Assert.Equal(1, (int)ResourceType.Food);
        Assert.Equal(3, (int)ResourceType.Stone);
    }

    [Fact]
    public void MapRejectsIncompatibleResourceRowsAndTerrainSemantics()
    {
        var configuration = SmallConfiguration();
        var tiles = CreateGrasslandTiles(configuration);
        var invalidId = new ResourceNode(new ResourceNodeId(99), new TileCoordinate(0, 0), ResourceType.Food, 1, 1);
        Assert.Throws<ArgumentException>(() => new WorldMap(new WorldSeed(1), 1, 0, configuration, tiles, [invalidId], new TileCoordinate(0, 0)));

        var invalidFoodEcology = new ResourceNode(new ResourceNodeId(1), new TileCoordinate(0, 0), ResourceType.Food, 1, 1);
        var dryTiles = CreateGrasslandTiles(configuration, fertility: 0, waterAccess: 0);
        Assert.Throws<ArgumentException>(() => new WorldMap(new WorldSeed(1), 1, 0, configuration, dryTiles, [invalidFoodEcology], new TileCoordinate(0, 0)));

        var invalidMovementTiles = CreateGrasslandTiles(configuration);
        invalidMovementTiles[0] = new WorldTile(new TileCoordinate(0, 0), TerrainType.Grassland, 5000, 5000, 3000, true, 2);
        Assert.Throws<ArgumentException>(() => new WorldMap(new WorldSeed(1), 1, 0, configuration, invalidMovementTiles, [], new TileCoordinate(0, 0)));
    }

    private static WorldGenerationConfiguration SmallConfiguration() => new()
    {
        Width = 8,
        Height = 8,
        MinimumNearbyFood = 0,
        MinimumNearbyWood = 0,
        MinimumNearbyStone = 0,
        MinimumNearbyFreshwater = 0,
        MinimumWalkableCount = 1,
        MaximumAttempts = 1
    };

    private static WorldTile[] CreateGrasslandTiles(WorldGenerationConfiguration configuration, int fertility = 5000, int waterAccess = 3000) =>
        Enumerable.Range(0, configuration.Width * configuration.Height)
            .Select(index => new WorldTile(TileCoordinate.FromIndex(index, configuration.Width), TerrainType.Grassland, 5000, fertility, waterAccess, true, 1))
            .ToArray();
}
