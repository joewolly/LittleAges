using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class CitizenSimulationTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(42UL)]
    [InlineData(ulong.MaxValue)]
    public void FoundersAreDeterministicCompleteAndValid(ulong seed)
    {
        var engine = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var citizens = engine.Citizens;
        Assert.Equal(20, citizens.Count);
        Assert.Equal(Enumerable.Range(0, 20), citizens.Select(c => c.FounderOrdinal));
        Assert.Equal(Enumerable.Range(1, 20), citizens.Select(c => checked((int)c.Id.Value)));
        Assert.Equal(20, citizens.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, citizens.Select(c => c.Location).Distinct().Count());
        Assert.All(citizens, citizen =>
        {
            Assert.True(citizen.BirthMinute < 0);
            Assert.InRange(citizen.GetAgeYears(engine.CurrentMinute), 18, 45);
            Assert.Equal(10000, citizen.Health);
            Assert.InRange(citizen.Needs.Hunger, 0, 10000);
            Assert.InRange(citizen.Needs.Rest, 0, 10000);
            Assert.InRange(citizen.Traits.Industriousness, 0, 10000);
            Assert.True(citizen.Skills.Foraging >= 0);
            Assert.True(engine.World.GetTile(citizen.Location).Walkable);
        });
    }

    [Theory]
    [InlineData(0UL, "8550f3dcd5a30b84b78eedc51b623aa42abec73fc5da6884c66a0ddf701f24ac")]
    [InlineData(42UL, "6c310f306fd58939d596959acc3147371e680faea4b8e64bb5f95ffa0e84ad55")]
    [InlineData(ulong.MaxValue, "eaae4f6399cb5819de832012ec0c0a2ce7a3cd74b8f17265d576e2765e4efe2e")]
    public void FounderRosterFingerprintIsLocked(ulong seed, string expected)
    {
        var engine = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        Assert.Equal(expected, CitizenGenerator.Fingerprint(new WorldSeed(seed), engine.Citizens));
    }

    [Fact]
    public void NeedProjectionIsPureAndUsesIntegerRates()
    {
        var citizen = new Citizen(new CitizenId(1), 0, "A", "B", -100, new TileCoordinate(0, 0), new CitizenTraits(1, 2, 3, 4, 5, 6), new CitizenSkills(1, 2, 3, 4, 5, 6), new CitizenNeeds(9000, 1000, 5000, 7000));
        var before = citizen.Needs;
        Assert.Equal(new CitizenNeeds(9020, 1030, 5010, 7010), citizen.GetProjectedNeeds(new WorldMinute(10)));
        Assert.Equal(before, citizen.Needs);
    }

    [Fact]
    public void NeedProjectionSaturatesForMaximumWorldMinute()
    {
        var needs = new CitizenNeeds(1, 2, 3, 4);
        Assert.Equal(new CitizenNeeds(10000, 10000, 10000, 10000), NeedsProjection.Project(needs, 0, new WorldMinute(long.MaxValue)));
    }

    [Fact]
    public void SubstantialM2RunLeavesWorldResourcesHealthAndSkillsUnchanged()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var fingerprint = engine.World.Fingerprint;
        var resources = engine.World.Resources.ToArray();
        var skills = engine.Citizens.ToDictionary(c => c.Id.Value, c => c.Skills);
        engine.AdvanceUntil(new WorldMinute(5_000));
        Assert.Equal(fingerprint, engine.World.Fingerprint);
        Assert.Equal(resources, engine.World.Resources);
        Assert.All(engine.Citizens, citizen =>
        {
            Assert.Equal(10000, citizen.Health);
            Assert.Null(citizen.DeathMinute);
            Assert.Equal(skills[citizen.Id.Value], citizen.Skills);
            Assert.InRange((int)citizen.CurrentAction, 0, 4);
            var projected = citizen.GetProjectedNeeds(engine.CurrentMinute);
            Assert.InRange(projected.Hunger, 0, 10000);
            Assert.InRange(projected.Shelter, 0, 10000);
            Assert.InRange(projected.Social, 0, 10000);
        });
    }

    [Fact]
    public void DecisionsAdvanceLocalSequenceAndAreRepeatable()
    {
        var first = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var second = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        first.ProcessNextEvent();
        second.ProcessNextEvent();
        var a = first.CreateReadSnapshot().Citizens[0];
        var b = second.CreateReadSnapshot().Citizens[0];
        Assert.Equal(a, b);
        Assert.Equal(1, a.ActionSequence);
        Assert.NotEqual(CitizenAction.None, a.CurrentAction);
    }

    [Theory]
    [InlineData(CitizenAction.Rest, CitizenAction.Explore, CitizenAction.Rest)]
    [InlineData(CitizenAction.Explore, CitizenAction.Wander, CitizenAction.Explore)]
    [InlineData(CitizenAction.Wander, CitizenAction.Idle, CitizenAction.Wander)]
    public void ExactDecisionTiesUseTheDocumentedActionRank(CitizenAction first, CitizenAction second, CitizenAction expected)
    {
        var evaluations = new[] { Evaluation(first), Evaluation(second) };

        Assert.Equal(expected, SimulationEngine.SelectDecision(evaluations));
    }

    [Fact]
    public void FourWayDecisionTieSelectsRest()
    {
        var evaluations = new[]
        {
            Evaluation(CitizenAction.Idle),
            Evaluation(CitizenAction.Wander),
            Evaluation(CitizenAction.Explore),
            Evaluation(CitizenAction.Rest)
        };

        Assert.Equal(CitizenAction.Rest, SimulationEngine.SelectDecision(evaluations));
    }

    [Fact]
    public void PayloadRoundTripsAndReadSnapshotsDoNotExposeMutableState()
    {
        var payload = "{\"citizenId\":\"7\",\"actionSequence\":3}";
        var scheduled = new ScheduledEventSnapshot(new ScheduledEventId(7), new ScheduledEventOrder(WorldMinute.Zero, CitizenEventNames.DecisionPriority, 7, 7), CitizenEventNames.Decision, payload).Validate();
        Assert.Equal(payload, scheduled.EventPayloadJson);
        var engine = new SimulationEngine(new WorldSeed(1), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var read = engine.CreateReadSnapshot();
        Assert.Equal(20, read.Citizens.Count);
        Assert.NotSame(engine.GetCitizen(new CitizenId(1)), engine.GetCitizen(new CitizenId(1)));
    }

    [Fact]
    public void WeightedPathIsDeterministicAndNeverUsesBlockedTiles()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var start = engine.World.StartingSite;
        var destination = engine.World.Tiles.First(tile => tile.Walkable && tile.Coordinate != start).Coordinate;
        var path = DeterministicPathfinder.FindPath(engine.World, start, destination);
        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(destination, path[^1]);
        Assert.All(path, coordinate => Assert.True(engine.World.GetTile(coordinate).Walkable));
        Assert.Equal(path, DeterministicPathfinder.FindPath(engine.World, start, destination));
        Assert.Null(DeterministicPathfinder.FindPath(engine.World, start, engine.World.Tiles.First(tile => !tile.Walkable).Coordinate));
    }

    [Fact]
    public void WeightedPathGoldenLocksCrossPlatformTieBreaks()
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        var path = DeterministicPathfinder.FindPath(world, world.StartingSite, new TileCoordinate(127, 126));
        Assert.Equal(
            new[] { new TileCoordinate(131, 130), new TileCoordinate(130, 130), new TileCoordinate(129, 130), new TileCoordinate(128, 130), new TileCoordinate(127, 129), new TileCoordinate(127, 128), new TileCoordinate(127, 127), new TileCoordinate(127, 126) },
            path);
    }

    [Fact]
    public void SyntheticMapAllowsLegalDiagonalMovement()
    {
        var world = SyntheticMap();
        var path = DeterministicPathfinder.FindPath(world, new TileCoordinate(1, 1), new TileCoordinate(2, 2));
        Assert.Equal(new[] { new TileCoordinate(1, 1), new TileCoordinate(2, 2) }, path);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SyntheticMapRejectsCornerCutting(bool blockEast, bool blockSouth)
    {
        var world = SyntheticMap((blockEast, blockSouth));
        var path = DeterministicPathfinder.FindPath(world, new TileCoordinate(1, 1), new TileCoordinate(2, 2));
        Assert.NotNull(path);
        Assert.NotEqual(new TileCoordinate(2, 2), path![1]);
        Assert.All(path, coordinate => Assert.True(world.GetTile(coordinate).Walkable));
    }

    [Fact]
    public void SyntheticMapChoosesLowerWeightedRouteAndRejectsBlockedDestination()
    {
        var world = SyntheticMap((false, false), highCost: [new TileCoordinate(2, 1), new TileCoordinate(3, 1)]);
        var path = DeterministicPathfinder.FindPath(world, new TileCoordinate(1, 1), new TileCoordinate(4, 1));
        Assert.NotNull(path);
        Assert.DoesNotContain(new TileCoordinate(2, 1), path!);
        Assert.All(path!, coordinate => Assert.True(world.GetTile(coordinate).Walkable));
        var blocked = SyntheticMap((false, false), blocked: [new TileCoordinate(2, 2)]);
        Assert.Null(DeterministicPathfinder.FindPath(blocked, new TileCoordinate(1, 1), new TileCoordinate(2, 2)));
    }

    [Fact]
    public void MixedCostMovementKeepsTheMathematicalArrivalMinute()
    {
        var engine = CreateMovementEngine(
            new TileCoordinate(4, 2),
            58,
            (new TileCoordinate(2, 1), TerrainType.Grassland),
            (new TileCoordinate(3, 2), TerrainType.Forest),
            (new TileCoordinate(4, 2), TerrainType.Forest));

        Assert.Equal(new WorldMinute(10), CitizenEvent(engine).Order.DueWorldMinute);
        Assert.Equal(new WorldMinute(58), engine.GetCitizen(new CitizenId(1))!.ActionCompletesMinute);

        Assert.True(engine.ProcessNextEvent());
        Assert.Equal(new WorldMinute(10), engine.CurrentMinute);
        Assert.Equal(new WorldMinute(38), CitizenEvent(engine).Order.DueWorldMinute);
        Assert.Equal(new WorldMinute(58), engine.GetCitizen(new CitizenId(1))!.ActionCompletesMinute);

        Assert.True(engine.ProcessNextEvent());
        Assert.Equal(new WorldMinute(38), engine.CurrentMinute);
        Assert.Equal(new WorldMinute(58), CitizenEvent(engine).Order.DueWorldMinute);
        Assert.Equal(new WorldMinute(58), engine.GetCitizen(new CitizenId(1))!.ActionCompletesMinute);

        Assert.True(engine.ProcessNextEvent());
        var arrived = engine.GetCitizen(new CitizenId(1))!;
        Assert.Equal(new WorldMinute(58), engine.CurrentMinute);
        Assert.Equal(new TileCoordinate(4, 2), arrived.Location);
        Assert.Null(arrived.ActionCompletesMinute);
        Assert.Equal(3, arrived.LifetimeMovementSteps);
        Assert.Equal(58, arrived.LifetimeMovementCost);
    }

    [Fact]
    public void OrthogonalAndDiagonalMovementUseDestinationTerrainCosts()
    {
        var engine = CreateMovementEngine(
            new TileCoordinate(3, 2),
            34,
            (new TileCoordinate(2, 1), TerrainType.Forest),
            (new TileCoordinate(3, 2), TerrainType.Grassland));

        Assert.Equal(new WorldMinute(20), CitizenEvent(engine).Order.DueWorldMinute);
        Assert.True(engine.ProcessNextEvent());
        Assert.Equal(new WorldMinute(20), engine.CurrentMinute);
        Assert.Equal(new WorldMinute(34), CitizenEvent(engine).Order.DueWorldMinute);
        Assert.Equal(new WorldMinute(34), engine.GetCitizen(new CitizenId(1))!.ActionCompletesMinute);

        Assert.True(engine.ProcessNextEvent());
        Assert.Equal(new WorldMinute(34), engine.CurrentMinute);
        Assert.Equal(new TileCoordinate(3, 2), engine.GetCitizen(new CitizenId(1))!.Location);
    }

    [Fact]
    public void MixedCostMovementReloadPreservesCorrectRemainingTiming()
    {
        var source = CreateMovementEngine(
            new TileCoordinate(4, 2),
            58,
            (new TileCoordinate(2, 1), TerrainType.Grassland),
            (new TileCoordinate(3, 2), TerrainType.Forest),
            (new TileCoordinate(4, 2), TerrainType.Forest));
        Assert.True(source.ProcessNextEvent());

        var restored = SimulationEngine.FromPersistenceSnapshot(source.CreatePersistenceSnapshot());
        Assert.Equal(new WorldMinute(38), CitizenEvent(source).Order.DueWorldMinute);
        Assert.Equal(new WorldMinute(38), CitizenEvent(restored).Order.DueWorldMinute);
        Assert.Equal(new WorldMinute(58), source.GetCitizen(new CitizenId(1))!.ActionCompletesMinute);
        Assert.Equal(new WorldMinute(58), restored.GetCitizen(new CitizenId(1))!.ActionCompletesMinute);

        Assert.True(source.ProcessNextEvent());
        Assert.True(source.ProcessNextEvent());
        Assert.True(restored.ProcessNextEvent());
        Assert.True(restored.ProcessNextEvent());

        var uninterruptedCitizen = source.GetCitizen(new CitizenId(1))!;
        var restoredCitizen = restored.GetCitizen(new CitizenId(1))!;
        Assert.Equal(new WorldMinute(58), source.CurrentMinute);
        Assert.Equal(source.CurrentMinute, restored.CurrentMinute);
        Assert.Equal(3, uninterruptedCitizen.LifetimeMovementSteps);
        Assert.Equal(58, uninterruptedCitizen.LifetimeMovementCost);
        Assert.Equal(uninterruptedCitizen.LifetimeMovementSteps, restoredCitizen.LifetimeMovementSteps);
        Assert.Equal(uninterruptedCitizen.LifetimeMovementCost, restoredCitizen.LifetimeMovementCost);
        Assert.Equal(source.CreatePersistenceSnapshot().ScheduledEvents, restored.CreatePersistenceSnapshot().ScheduledEvents);
    }

    [Fact]
    public void PersistenceAndTimeChunksPreserveCitizenState()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(180));
        var restored = SimulationEngine.FromPersistenceSnapshot(source.CreatePersistenceSnapshot());
        Assert.Equal(source.CreateReadSnapshot().Citizens, restored.CreateReadSnapshot().Citizens);

        var chunked = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        chunked.AdvanceUntil(new WorldMinute(60));
        chunked.AdvanceUntil(new WorldMinute(180));
        Assert.Equal(source.CreateReadSnapshot().Citizens, chunked.CreateReadSnapshot().Citizens);
    }

    [Fact]
    public void ReadSnapshotPollingDoesNotAlterCanonicalHistory()
    {
        var unobserved = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var observed = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);

        unobserved.AdvanceUntil(new WorldMinute(500));
        for (var minute = 1; minute <= 500; minute++)
        {
            observed.AdvanceUntil(new WorldMinute(minute));
            _ = observed.CreateReadSnapshot();
        }

        var expected = unobserved.CreatePersistenceSnapshot();
        var actual = observed.CreatePersistenceSnapshot();
        Assert.Equal(expected.WorldMinute, actual.WorldMinute);
        Assert.Equal(expected.Counters, actual.Counters);
        Assert.Equal(expected.ScheduledEvents, actual.ScheduledEvents);
        Assert.Equal(unobserved.CreateReadSnapshot().Citizens, observed.CreateReadSnapshot().Citizens);
        Assert.Equal(expected.World!.Fingerprint, actual.World!.Fingerprint);
    }

    [Fact]
    public void ReversedCitizenInputOrderCanonicalizesToTheSameSnapshot()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var snapshot = source.CreatePersistenceSnapshot();
        var reversed = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents, snapshot.World, snapshot.Citizens.Reverse().ToArray(), snapshot.CitizenGenerationVersion);
        Assert.Equal(snapshot.Citizens, reversed.Citizens);
        Assert.Equal(snapshot.ScheduledEvents, reversed.ScheduledEvents);
    }

    [Fact]
    public void MidActionReloadContinuesAtTheSameMinuteAndState()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        while (source.GetCitizen(new CitizenId(1))!.CurrentAction == CitizenAction.None) Assert.True(source.ProcessNextEvent());
        var checkpoint = source.CreatePersistenceSnapshot();
        var restored = SimulationEngine.FromPersistenceSnapshot(checkpoint);
        var until = source.GetCitizen(new CitizenId(1))!.ActionCompletesMinute;
        Assert.NotNull(until);
        source.AdvanceUntil(until!.Value);
        restored.AdvanceUntil(until.Value);
        Assert.Equal(source.CurrentMinute, restored.CurrentMinute);
        Assert.Equal(source.CreateReadSnapshot().Citizens, restored.CreateReadSnapshot().Citizens);
        Assert.Equal(source.CounterSnapshot, restored.CounterSnapshot);
    }

    private static WorldMap SyntheticMap((bool East, bool South)? cornerBlocks = null, IReadOnlyCollection<TileCoordinate>? blocked = null, IReadOnlyCollection<TileCoordinate>? highCost = null)
    {
        var config = new WorldGenerationConfiguration { Width = 8, Height = 8, StartSiteRadius = 1, MinimumNearbyFood = 0, MinimumNearbyWood = 0, MinimumNearbyStone = 0, MinimumNearbyFreshwater = 0, MinimumWalkableCount = 1 }.Validate();
        var blockedSet = blocked?.ToHashSet() ?? [];
        var highCostSet = highCost?.ToHashSet() ?? [];
        if (cornerBlocks is { East: true }) blockedSet.Add(new TileCoordinate(2, 1));
        if (cornerBlocks is { South: true }) blockedSet.Add(new TileCoordinate(1, 2));
        var weighted = new HashSet<TileCoordinate>(blockedSet);
        var tiles = Enumerable.Range(0, config.Width * config.Height).Select(index =>
        {
            var coordinate = TileCoordinate.FromIndex(index, config.Width);
            if (weighted.Contains(coordinate)) return new WorldTile(coordinate, TerrainType.RockyGround, 9000, 0, 0, false, 0);
            if (highCostSet.Contains(coordinate)) return new WorldTile(coordinate, TerrainType.Forest, 6000, 6000, 0, true, 2);
            return new WorldTile(coordinate, TerrainType.Grassland, 5000, 6000, 0, true, 1);
        });
        return new WorldMap(new WorldSeed(9), 1, 0, config, tiles, [], new TileCoordinate(3, 3));
    }

    private static CitizenDecisionEvaluation Evaluation(CitizenAction action) => new(action, 0, 0, 0, 0, 100);

    private static ScheduledEventSnapshot CitizenEvent(SimulationEngine engine) =>
        engine.CreatePersistenceSnapshot().ScheduledEvents.Single(item => item.Order.EntitySortKey == 1);

    private static SimulationEngine CreateMovementEngine(
        TileCoordinate target,
        long expectedArrivalMinute,
        params (TileCoordinate Coordinate, TerrainType Terrain)[] routeTiles)
    {
        var start = new TileCoordinate(1, 1);
        var supportTiles = new[] { new TileCoordinate(2, 2), new TileCoordinate(3, 1) };
        var terrainByCoordinate = routeTiles.ToDictionary(item => item.Coordinate, item => item.Terrain);
        terrainByCoordinate[start] = TerrainType.Grassland;
        foreach (var support in supportTiles) terrainByCoordinate.TryAdd(support, TerrainType.DenseWilderness);

        var config = new WorldGenerationConfiguration
        {
            Width = 8,
            Height = 8,
            StartSiteRadius = 1,
            MinimumNearbyFood = 0,
            MinimumNearbyWood = 0,
            MinimumNearbyStone = 0,
            MinimumNearbyFreshwater = 0,
            MinimumWalkableCount = 1
        }.Validate();
        var tiles = Enumerable.Range(0, config.Width * config.Height).Select(index =>
        {
            var coordinate = TileCoordinate.FromIndex(index, config.Width);
            if (!terrainByCoordinate.TryGetValue(coordinate, out var terrain))
                return new WorldTile(coordinate, TerrainType.RockyGround, 9000, 0, 0, false, 0);
            var movementCost = terrain switch
            {
                TerrainType.Grassland => 1,
                TerrainType.Forest => 2,
                TerrainType.DenseWilderness => 3,
                _ => throw new InvalidOperationException("Movement fixtures use only walkable terrain.")
            };
            return new WorldTile(coordinate, terrain, 5000, 6000, 0, true, movementCost);
        });
        var world = new WorldMap(new WorldSeed(9), 1, 0, config, tiles, [], start);
        var citizens = Enumerable.Range(1, CitizenSimulationRules.FounderCount).Select(id =>
        {
            var citizen = new Citizen(
                new CitizenId(id),
                id - 1,
                $"Citizen{id}",
                "Fixture",
                -10_000_000,
                start,
                new CitizenTraits(0, 0, 0, 0, 0, 0),
                new CitizenSkills(0, 0, 0, 0, 0, 0));
            if (id == 1)
            {
                citizen.CurrentAction = CitizenAction.Wander;
                citizen.ActionSequence = 1;
                citizen.ActionStartedMinute = WorldMinute.Zero;
                citizen.ActionCompletesMinute = new WorldMinute(expectedArrivalMinute);
                citizen.ActionTarget = target;
            }
            else
            {
                citizen.CurrentAction = CitizenAction.Idle;
                citizen.ActionStartedMinute = WorldMinute.Zero;
                citizen.ActionCompletesMinute = new WorldMinute(1_000);
            }
            return citizen;
        }).ToArray();
        var firstStep = routeTiles[0].Coordinate;
        var firstStepCost = (firstStep.X == start.X || firstStep.Y == start.Y ? 10 : 14) * world.GetTile(firstStep).MovementCost;
        var events = citizens.Select(citizen =>
        {
            var sequence = citizen.Id.Value;
            var moving = citizen.Id.Value == 1;
            return new ScheduledEventSnapshot(
                new ScheduledEventId(sequence),
                new ScheduledEventOrder(moving ? new WorldMinute(firstStepCost) : new WorldMinute(1_000), moving ? CitizenEventNames.MovementPriority : CitizenEventNames.CompletionPriority, citizen.Id.Value, sequence),
                moving ? CitizenEventNames.MoveStep : CitizenEventNames.ActionComplete,
                $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}");
        }).ToArray();
        var snapshot = new SimulationPersistenceSnapshot(
            new WorldSeed(9),
            WorldMinute.Zero,
            SimulationEngine.CurrentWorldSchemaVersion,
            SimulationEngine.PreviousSimulationRulesVersion,
            "test",
            config.CanonicalJson,
            new DeterministicCountersSnapshot(21, 1, 21),
            events,
            world,
            citizens,
            SimulationEngine.CitizenGenerationVersion);
        return SimulationEngine.FromPersistenceSnapshot(snapshot);
    }
}
