using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Pure deterministic M1 geography/resource generator.</summary>
public sealed class WorldGenerator
{
    public const int GenerationVersion = WorldGenerationConfiguration.CurrentVersion;
    private readonly int _generationVersion = GenerationVersion;

    public WorldMap Generate(WorldSeed seed, WorldGenerationConfiguration? configuration = null)
    {
        var config = (configuration ?? WorldGenerationConfiguration.Default).Validate();
        ArgumentException? lastFailure = null;
        for (var attempt = 0; attempt < config.MaximumAttempts; attempt++)
        {
            try
            {
                var map = GenerateAttempt(seed, config, attempt, _generationVersion);
                map.Validate();
                return map;
            }
            catch (ArgumentException exception)
            {
                lastFailure = exception;
            }
        }
        throw new WorldGenerationException($"Unable to generate a viable world after {config.MaximumAttempts} deterministic attempts for seed {seed.Value}.", lastFailure);
    }

    private static WorldMap GenerateAttempt(WorldSeed originalSeed, WorldGenerationConfiguration config, int attempt, int generationVersion)
    {
        var attemptSeed = new WorldSeed(new DeterministicRandom(originalSeed).NextUInt64(RandomDomain.WorldGeneration, (ulong)generationVersion, (ulong)attempt));
        var random = new DeterministicRandom(attemptSeed);
        var tiles = new WorldTile[checked(config.Width * config.Height)];
        var resources = new List<ResourceNode>();
        var elevations = new int[tiles.Length];
        var baseFertilities = new int[tiles.Length];
        var terrains = new TerrainType[tiles.Length];
        for (var index = 0; index < tiles.Length; index++)
        {
            var coordinate = TileCoordinate.FromIndex(index, config.Width);
            var elevation = Field(random, coordinate, 1);
            var baseFertility = Field(random, coordinate, 2);
            var terrain = elevation < config.TerrainWaterThreshold ? TerrainType.Freshwater :
                elevation >= config.TerrainRockThreshold ? TerrainType.RockyGround :
                baseFertility >= config.TerrainDenseFertilityThreshold ? TerrainType.DenseWilderness :
                baseFertility >= config.TerrainForestFertilityThreshold ? TerrainType.Forest : TerrainType.Grassland;
            elevations[index] = elevation;
            baseFertilities[index] = baseFertility;
            terrains[index] = terrain;
        }

        var waterAccess = ComputeWaterAccess(terrains, config.Width, config.Height);
        for (var index = 0; index < tiles.Length; index++)
        {
            var coordinate = TileCoordinate.FromIndex(index, config.Width);
            var terrain = terrains[index];
            var fertility = DeriveFertility(baseFertilities[index], waterAccess[index], terrain);
            var walkable = terrain != TerrainType.Freshwater && terrain != TerrainType.RockyGround;
            var movement = walkable ? terrain == TerrainType.DenseWilderness ? 3 : terrain == TerrainType.Forest ? 2 : 1 : 0;
            tiles[index] = new WorldTile(coordinate, terrain, elevations[index], fertility, waterAccess[index], walkable, movement);
        }
        for (var index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            if (tile.Terrain != TerrainType.Freshwater)
            {
                AddResource(resources, random, tile.Coordinate, index, ResourceType.Food, tile.Fertility >= config.FoodPlacementThreshold && tile.WaterAccess >= 2600 && HasFreshwaterNeighbor(tiles, tile.Coordinate, config.Width, config.Height), tile.Fertility, 1);
                AddResource(resources, random, tile.Coordinate, index, ResourceType.Wood, (tile.Terrain is TerrainType.Forest or TerrainType.DenseWilderness) && tile.Fertility >= config.WoodPlacementThreshold, tile.Fertility, 2);
                AddResource(resources, random, tile.Coordinate, index, ResourceType.Stone, tile.Terrain == TerrainType.RockyGround || tile.Elevation >= config.StonePlacementThreshold, tile.Elevation, 3);
            }
        }
        var start = ChooseStartingSite(config, tiles, resources);
        return new WorldMap(originalSeed, generationVersion, attempt, config, tiles, resources, start);
    }

    private static int[] ComputeWaterAccess(TerrainType[] terrains, int width, int height)
    {
        var distances = Enumerable.Repeat(int.MaxValue, terrains.Length).ToArray();
        var queue = new int[terrains.Length];
        var head = 0;
        var tail = 0;
        for (var index = 0; index < terrains.Length; index++)
        {
            if (terrains[index] != TerrainType.Freshwater) continue;
            distances[index] = 0;
            queue[tail++] = index;
        }

        while (head < tail)
        {
            var index = queue[head++];
            var coordinate = TileCoordinate.FromIndex(index, width);
            for (var y = Math.Max(0, coordinate.Y - 1); y <= Math.Min(height - 1, coordinate.Y + 1); y++)
            for (var x = Math.Max(0, coordinate.X - 1); x <= Math.Min(width - 1, coordinate.X + 1); x++)
            {
                var neighborIndex = (y * width) + x;
                if (distances[neighborIndex] != int.MaxValue) continue;
                distances[neighborIndex] = distances[index] + 1;
                queue[tail++] = neighborIndex;
            }
        }

        return distances.Select(static distance => distance == int.MaxValue ? 0 : 10000 / (distance + 1)).ToArray();
    }

    private static int DeriveFertility(int baseFertility, int waterAccess, TerrainType terrain)
    {
        var terrainFactor = terrain switch
        {
            TerrainType.Freshwater => 4000,
            TerrainType.Grassland => 6000,
            TerrainType.Forest => 8000,
            TerrainType.RockyGround => 1500,
            TerrainType.DenseWilderness => 8500,
            _ => 0
        };
        return Math.Clamp(((baseFertility * 5) + (waterAccess * 3) + (terrainFactor * 2)) / 10, 0, 10000);
    }

    private static int Field(DeterministicRandom random, TileCoordinate coordinate, ulong layer)
    {
        // Four stable samples form a smooth coarse field without mutable/random iteration state.
        var coarseX = coordinate.X / 8;
        var coarseY = coordinate.Y / 8;
        var fx = coordinate.X % 8;
        var fy = coordinate.Y % 8;
        var a = Sample(random, coarseX, coarseY, layer);
        var b = Sample(random, coarseX + 1, coarseY, layer);
        var c = Sample(random, coarseX, coarseY + 1, layer);
        var d = Sample(random, coarseX + 1, coarseY + 1, layer);
        var top = a + ((b - a) * fx / 8);
        var bottom = c + ((d - c) * fx / 8);
        return Math.Clamp(top + ((bottom - top) * fy / 8), 0, 10000);
    }

    private static int Sample(DeterministicRandom random, int x, int y, ulong layer) => (int)(random.NextUInt64(RandomDomain.WorldGeneration, layer, unchecked((ulong)(uint)x), unchecked((ulong)(uint)y)) % 10001);

    private static bool HasFreshwaterNeighbor(WorldTile[] tiles, TileCoordinate coordinate, int width, int height)
    {
        for (var y = Math.Max(0, coordinate.Y - 1); y <= Math.Min(height - 1, coordinate.Y + 1); y++)
        for (var x = Math.Max(0, coordinate.X - 1); x <= Math.Min(width - 1, coordinate.X + 1); x++)
            if (tiles[(y * width) + x].Terrain == TerrainType.Freshwater) return true;
        return false;
    }

    private static void AddResource(List<ResourceNode> resources, DeterministicRandom random, TileCoordinate coordinate, int index, ResourceType type, bool suitable, int ecology, long typeCode)
    {
        if (!suitable || random.NextUInt64(RandomDomain.WorldGeneration, 100, (ulong)index, (ulong)type) % 100 >= 24) return;
        var id = checked(((long)index * 4) + typeCode);
        var quantity = 40 + (int)(random.NextUInt64(RandomDomain.WorldGeneration, 101, (ulong)index, (ulong)type) % 161);
        resources.Add(new ResourceNode(new ResourceNodeId(id), coordinate, type, quantity, quantity, Math.Clamp(ecology, 0, 10000)));
    }

    private static TileCoordinate ChooseStartingSite(WorldGenerationConfiguration config, WorldTile[] tiles, List<ResourceNode> resources)
    {
        var radius = config.StartSiteRadius;
        var candidates = new List<(long Score, int Index)>();
        var resourcesByIndex = resources
            .GroupBy(node => checked((int)node.Coordinate.ToIndex(config.Width)))
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        foreach (var tile in tiles)
        {
            if (!tile.Buildable) continue;
            var minX = Math.Max(0, tile.Coordinate.X - radius);
            var maxX = Math.Min(config.Width - 1, tile.Coordinate.X + radius);
            var minY = Math.Max(0, tile.Coordinate.Y - radius);
            var maxY = Math.Min(config.Height - 1, tile.Coordinate.Y + radius);
            var food = 0;
            var wood = 0;
            var stone = 0;
            var water = 0;
            var walkable = 0;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var nearby = tiles[(y * config.Width) + x];
                if (nearby.Terrain == TerrainType.Freshwater) water++;
                if (nearby.Walkable) walkable++;
                if (!resourcesByIndex.TryGetValue((y * config.Width) + x, out var nodes)) continue;
                foreach (var node in nodes)
                {
                    if (node.Type == ResourceType.Food) food += node.MaximumQuantity;
                    else if (node.Type == ResourceType.Wood) wood += node.MaximumQuantity;
                    else stone += node.MaximumQuantity;
                }
            }
            if (food < config.MinimumNearbyFood || wood < config.MinimumNearbyWood || stone < config.MinimumNearbyStone || water < config.MinimumNearbyFreshwater || walkable < config.MinimumWalkableCount) continue;
            var score = checked(((long)food * 100000) + ((long)wood * 10000) + ((long)stone * 1000) + ((long)water * 100) + walkable + tile.Fertility);
            candidates.Add((score, checked((int)tile.Coordinate.ToIndex(config.Width))));
        }
        if (candidates.Count == 0) return new TileCoordinate(0, 0);
        return TileCoordinate.FromIndex(candidates.OrderByDescending(static item => item.Score).ThenBy(static item => item.Index).First().Index, config.Width);
    }
}
