using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Simulation.Tests;

[Trait("Category", "Long")]
public sealed class M14NoSiteLongRunTests(ITestOutputHelper output)
{
    private const long FoundingTravelCost = 32;
    private static readonly (int X, int Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    [Fact]
    public void Valid160WorldWithoutReachableFoundingSiteRemainsSingleSiteForOneHundredYears()
    {
        var seed = new WorldSeed(42);
        var initial = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var initialSnapshot = initial.CreatePersistenceSnapshot();
        var constrained = CreateNoSiteSnapshot(initial, initialSnapshot);
        var engine = new SimulationEngine(constrained);

        Assert.Equal(160, engine.World.Width);
        Assert.Equal(160, engine.World.Height);
        Assert.Equal(seed, engine.World.OriginalSeed);
        AssertNoReachableFoundingSite(engine.World);
        AssertSingleSiteState(engine);

        SimulationEngine? uninterruptedAtCheckpoint = null;
        for (var year = 1; year <= 100; year++)
        {
            var target = new WorldMinute((long)year * WorldCalendar.MinutesPerYear);
            engine.AdvanceUntil(target);
            AssertSingleSiteState(engine);

            if (uninterruptedAtCheckpoint is not null)
            {
                uninterruptedAtCheckpoint.AdvanceUntil(target);
                AssertSingleSiteState(uninterruptedAtCheckpoint);
                AssertEquivalentCheckpoint(uninterruptedAtCheckpoint.CreatePersistenceSnapshot(), engine.CreatePersistenceSnapshot());
                uninterruptedAtCheckpoint = null;
            }

            if (year == 50)
            {
                // This synthesized terrain is not seed-replayable, so this verifies an in-memory restore only.
                var checkpoint = engine.CreatePersistenceSnapshot();
                var resumed = new SimulationEngine(checkpoint);
                AssertEquivalentCheckpoint(checkpoint, resumed.CreatePersistenceSnapshot());
                uninterruptedAtCheckpoint = engine;
                engine = resumed;
            }

            output.WriteLine($"year={year} minute={engine.CurrentMinute.Value} living={engine.LivingPopulation} total={engine.TotalCitizenCount} fingerprint={engine.World.Fingerprint}");
        }
    }

    private static SimulationPersistenceSnapshot CreateNoSiteSnapshot(SimulationEngine initial, SimulationPersistenceSnapshot snapshot)
    {
        var source = initial.World;
        var start = source.StartingSite;
        var living = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!);
        var protectedLocations = snapshot.Citizens.Select(x => x.Location)
            .Concat(living.Animals.Select(x => x.Location))
            .Concat(snapshot.Structures.Select(x => x.Location))
            .ToHashSet();
        var resources = source.Resources.ToList();
        var resourceCoordinates = resources.Select(x => x.Coordinate).ToHashSet();
        var constrainedCoordinates = source.Tiles.Where(x => OctileCost(start, x.Coordinate) < FoundingTravelCost)
            .Select(x => x.Coordinate).ToHashSet();
        var tiles = source.Tiles.ToDictionary(x => x.Coordinate);
        var deliberatelyBlocked = new HashSet<TileCoordinate>();

        foreach (var tile in source.Tiles)
        {
            var coordinate = tile.Coordinate;
            if (resourceCoordinates.Contains(coordinate) || tile.Terrain == TerrainType.Freshwater)
                continue;

            if (constrainedCoordinates.Contains(coordinate))
            {
                tiles[coordinate] = Grassland(tile);
                continue;
            }

            if (protectedLocations.Contains(coordinate))
            {
                AddMarkerWoodNode(tiles, resources, resourceCoordinates, source.Configuration, coordinate);
                continue;
            }

            tiles[coordinate] = RockyGround(tile);
        }

        EnsureStartingSiteWalkability(tiles, source, constrainedCoordinates, protectedLocations, resourceCoordinates, deliberatelyBlocked);
        WorldMap world;
        while (true)
        {
            world = CreateWorld(source, tiles, resources);
            var costs = ComputeTravelCosts(world, start);
            var tooFar = world.Tiles.Where(x => x.Walkable && x.Buildable && x.Terrain != TerrainType.Freshwater &&
                    x.Coordinate != start && !resourceCoordinates.Contains(x.Coordinate) &&
                    costs.TryGetValue(x.Coordinate, out var cost) && cost >= FoundingTravelCost)
                .Select(x => x.Coordinate).ToArray();
            if (tooFar.Length == 0) break;

            foreach (var coordinate in tooFar)
            {
                if (protectedLocations.Contains(coordinate))
                    AddMarkerWoodNode(tiles, resources, resourceCoordinates, source.Configuration, coordinate);
                else
                {
                    tiles[coordinate] = RockyGround(tiles[coordinate]);
                    deliberatelyBlocked.Add(coordinate);
                }
            }
            EnsureStartingSiteWalkability(tiles, source, constrainedCoordinates, protectedLocations, resourceCoordinates, deliberatelyBlocked);
        }

        world.Validate();
        AssertNoReachableFoundingSite(world);
        foreach (var location in protectedLocations) Assert.True(world.GetTile(location).Walkable, $"Occupied tile {location} must remain walkable.");

        var addedResourceStates = resources.Where(node => snapshot.ResourceStates.All(state => state.ResourceNodeId != node.Id))
            .Select(node => new ResourceState(node.Id, node.InitialQuantity));
        var resourceStates = snapshot.ResourceStates.Concat(addedResourceStates).OrderBy(x => x.ResourceNodeId.Value).ToArray();

        return new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion,
            snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters,
            snapshot.ScheduledEvents, world, snapshot.Citizens, snapshot.CitizenGenerationVersion, resourceStates,
            snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion, snapshot.Structures,
            snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households,
            snapshot.HistoryVersion, snapshot.HistoryState, snapshot.HistoricalEvents, snapshot.HistoricalEventCitizens,
            snapshot.HistoricalEventStructures, snapshot.StatisticsSamples, snapshot.Memories, snapshot.Agriculture,
            snapshot.Economy, snapshot.LivingStateJson, snapshot.MigrationState!.ToCanonicalJson());
    }

    private static void EnsureStartingSiteWalkability(Dictionary<TileCoordinate, WorldTile> tiles, WorldMap source,
        HashSet<TileCoordinate> constrainedCoordinates, HashSet<TileCoordinate> protectedLocations,
        HashSet<TileCoordinate> resourceCoordinates, HashSet<TileCoordinate> deliberatelyBlocked)
    {
        var start = source.StartingSite;
        var radius = source.Configuration.StartSiteRadius;
        int WalkableNearStart() => tiles.Values.Count(tile => tile.Walkable &&
            Math.Abs(tile.Coordinate.X - start.X) <= radius && Math.Abs(tile.Coordinate.Y - start.Y) <= radius);

        var candidates = tiles.Values.Where(tile => !tile.Walkable && tile.Terrain != TerrainType.Freshwater &&
                !constrainedCoordinates.Contains(tile.Coordinate) && !protectedLocations.Contains(tile.Coordinate) &&
                !resourceCoordinates.Contains(tile.Coordinate) && !deliberatelyBlocked.Contains(tile.Coordinate) &&
                Math.Abs(tile.Coordinate.X - start.X) <= radius && Math.Abs(tile.Coordinate.Y - start.Y) <= radius &&
                Math.Abs(tile.Coordinate.X - start.X) + Math.Abs(tile.Coordinate.Y - start.Y) >= 6)
            .OrderByDescending(tile => Math.Abs(tile.Coordinate.X - start.X) + Math.Abs(tile.Coordinate.Y - start.Y))
            .ThenBy(tile => tile.Coordinate.Y).ThenBy(tile => tile.Coordinate.X);

        foreach (var candidate in candidates)
        {
            if (WalkableNearStart() >= source.Configuration.MinimumWalkableCount) return;
            tiles[candidate.Coordinate] = Grassland(candidate);
        }

        if (WalkableNearStart() < source.Configuration.MinimumWalkableCount)
            throw new InvalidOperationException("The constrained 160x160 map cannot preserve the configured starting-site walkability requirement.");
    }

    private static void AddMarkerWoodNode(Dictionary<TileCoordinate, WorldTile> tiles, List<ResourceNode> resources,
        HashSet<TileCoordinate> resourceCoordinates, WorldGenerationConfiguration configuration, TileCoordinate coordinate)
    {
        if (!resourceCoordinates.Add(coordinate)) return;
        var tile = tiles[coordinate];
        var forest = new WorldTile(coordinate, TerrainType.Forest, tile.Elevation,
            Math.Max(tile.Fertility, configuration.WoodPlacementThreshold), tile.WaterAccess, walkable: true, movementCost: 2);
        tiles[coordinate] = forest;
        var id = checked((coordinate.ToIndex(configuration.Width) * 4) + (int)ResourceType.Wood);
        resources.Add(new ResourceNode(new ResourceNodeId(id), coordinate, ResourceType.Wood, 1, 1));
    }

    private static WorldMap CreateWorld(WorldMap source, Dictionary<TileCoordinate, WorldTile> tiles, List<ResourceNode> resources) =>
        new(source.OriginalSeed, source.GenerationVersion, source.GenerationAttempt, source.Configuration,
            tiles.Values, resources, source.StartingSite);

    private static WorldTile Grassland(WorldTile tile) =>
        new(tile.Coordinate, TerrainType.Grassland, tile.Elevation, tile.Fertility, tile.WaterAccess, walkable: true, movementCost: 1);

    private static WorldTile RockyGround(WorldTile tile) =>
        new(tile.Coordinate, TerrainType.RockyGround, tile.Elevation, tile.Fertility, tile.WaterAccess, walkable: false, movementCost: 0);

    private static long OctileCost(TileCoordinate from, TileCoordinate to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);
        var diagonal = Math.Min(dx, dy);
        return (14L * diagonal) + (10L * (Math.Max(dx, dy) - diagonal));
    }

    private static Dictionary<TileCoordinate, long> ComputeTravelCosts(WorldMap world, TileCoordinate start)
    {
        var costs = new Dictionary<TileCoordinate, long> { [start] = 0 };
        var queue = new PriorityQueue<TileCoordinate, long>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var current, out var currentCost))
        {
            if (costs[current] != currentCost) continue;
            foreach (var (dx, dy) in Directions)
            {
                var x = current.X + dx;
                var y = current.Y + dy;
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
                if (dx != 0 && dy != 0 && (!world.GetTile(x, current.Y).Walkable || !world.GetTile(current.X, y).Walkable)) continue;
                var next = world.GetTile(x, y);
                if (!next.Walkable) continue;
                var step = (dx == 0 || dy == 0 ? 10L : 14L) * next.MovementCost;
                var candidate = checked(currentCost + step);
                if (costs.TryGetValue(next.Coordinate, out var existing) && existing <= candidate) continue;
                costs[next.Coordinate] = candidate;
                queue.Enqueue(next.Coordinate, candidate);
            }
        }
        return costs;
    }

    private static void AssertNoReachableFoundingSite(WorldMap world)
    {
        var costs = ComputeTravelCosts(world, world.StartingSite);
        var resourceCoordinates = world.Resources.Select(x => x.Coordinate).ToHashSet();
        var candidate = world.Tiles.FirstOrDefault(tile => tile.Walkable && tile.Buildable &&
            tile.Terrain != TerrainType.Freshwater && tile.Coordinate != world.StartingSite &&
            !resourceCoordinates.Contains(tile.Coordinate) && costs.TryGetValue(tile.Coordinate, out var cost) &&
            cost >= FoundingTravelCost);
        Assert.Null(candidate);
    }

    private static void AssertSingleSiteState(SimulationEngine engine)
    {
        var snapshot = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(snapshot);
        var migration = Assert.IsType<MigrationWorldState>(snapshot.MigrationState);
        Assert.Null(migration.DaughterSettlement);
        Assert.Empty(migration.InTransitParties);
        Assert.All(migration.CitizenResidences, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(migration.HouseholdResidences, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(migration.StructureOwners, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(migration.FacilityOwners, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(migration.WorkOrderOwners, residence => Assert.Equal(1, residence.SettlementId));
        Assert.Equal(migration.ToCanonicalJson(), snapshot.MigrationStateJson);
        AssertNoReachableFoundingSite(engine.World);

        var resourceDefinitions = engine.World.Resources.ToDictionary(x => x.Id);
        Assert.Equal(resourceDefinitions.Count, snapshot.ResourceStates.Count);
        Assert.All(snapshot.ResourceStates, state =>
            Assert.InRange(state.CurrentQuantity, 0, resourceDefinitions[state.ResourceNodeId].MaximumQuantity));
    }

    private static void AssertEquivalentCheckpoint(SimulationPersistenceSnapshot expected, SimulationPersistenceSnapshot actual)
    {
        Assert.Equal(expected.WorldMinute, actual.WorldMinute);
        Assert.Equal(expected.World!.Fingerprint, actual.World!.Fingerprint);
        Assert.Equal(expected.Counters, actual.Counters);
        Assert.Equal(expected.ScheduledEvents, actual.ScheduledEvents);
        Assert.True(expected.Citizens.SequenceEqual(actual.Citizens));
        Assert.Equal(expected.ResourceStates, actual.ResourceStates);
        Assert.Equal(expected.LivingStateJson, actual.LivingStateJson);
        Assert.Equal(expected.MigrationStateJson, actual.MigrationStateJson);
        Assert.Equal(expected.Settlement, actual.Settlement);
    }
}
