using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M15TradePersistenceTests
{
    [Fact]
    public async Task TradeJourneysReplayAcrossSqliteReopenInEveryPhaseAndAfterLoss()
    {
        var start = CreateDepartedTrade(new WorldSeed(1501));
        var end = new WorldMinute(start.WorldMinute.Value + WorldCalendar.MinutesPerDay);
        var reference = SimulationEngine.FromPersistenceSnapshot(start);
        var checkpoints = new List<(string Name, SimulationPersistenceSnapshot Snapshot, SimulationEngine Expected)>();
        var phases = new HashSet<MigrationVisitPhase>();
        for (var minute = start.WorldMinute.Value + 5; minute < end.Value && phases.Count < 3; minute += 5)
        {
            reference.AdvanceUntil(new WorldMinute(minute));
            if (TradeParty(reference.CreatePersistenceSnapshot()) is { VisitPhase: { } phase } && phases.Add(phase))
                checkpoints.Add((phase.ToString(), reference.CreatePersistenceSnapshot(), reference));
        }
        Assert.Equal(3, phases.Count);

        var lost = SimulationEngine.FromPersistenceSnapshot(start);
        lost.AdvanceUntil(new WorldMinute(start.WorldMinute.Value + 5));
        var trader = ((Dictionary<long, Citizen>)Field(lost, "_citizens"))[TradeParty(lost.CreatePersistenceSnapshot())!.CitizenIds[0]];
        Invoke(lost, "KillNatural", trader);
        Invoke(lost, "SynchronizeLivingPeople");
        var lostCheckpoint = lost.CreatePersistenceSnapshot();
        Assert.Contains(lostCheckpoint.HistoricalEvents, x => x.EventType == HistoricalEventType.TradeLost);
        checkpoints.Add(("Lost", lostCheckpoint, lost));

        reference.AdvanceUntil(end);
        lost.AdvanceUntil(end);
        Assert.Contains(reference.CreatePersistenceSnapshot().HistoricalEvents, x => x.EventType == HistoricalEventType.TradeReturned);

        var root = Path.Combine(Path.GetTempPath(), "littleages-m15-trade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (name, checkpoint, expected) in checkpoints)
            {
                var path = Path.Combine(root, name + ".db");
                await using (var database = await WorldDatabase.OpenAsync(path))
                    await database.CreateCheckpointStore().CheckpointAsync(checkpoint);
                await using (var database = await WorldDatabase.OpenAsync(path))
                {
                    var loaded = await database.CreateCheckpointStore().LoadAsync();
                    MigrationValidation.Validate(loaded);
                    Assert.Equal(checkpoint.MigrationStateJson, loaded.MigrationStateJson);
                    Assert.Equal(checkpoint.HistoricalEvents.Select(x => x.EventType), loaded.HistoricalEvents.Select(x => x.EventType));
                    var reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                    reopened.AdvanceUntil(end);
                    var left = expected.CreatePersistenceSnapshot();
                    var right = reopened.CreatePersistenceSnapshot();
                    Assert.Equal(left.MigrationStateJson, right.MigrationStateJson);
                    Assert.Equal(left.LivingStateJson, right.LivingStateJson);
                    Assert.Equal(left.Citizens, right.Citizens);
                    Assert.Equal(expected.HistoryFingerprint, reopened.HistoryFingerprint);
                    Assert.Equal(expected.SettlementFingerprint, reopened.SettlementFingerprint);
                    await database.CreateCheckpointStore().CheckpointAsync(right);
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static MigrationTransitPartyState? TradeParty(SimulationPersistenceSnapshot snapshot) =>
        snapshot.MigrationState!.InTransitParties.SingleOrDefault(x => x.JourneyKind == MigrationJourneyKind.Trade && x.OriginSettlementId == 1);

    /// <summary>
    /// A new M15 world with eight households at a daughter site. Site 1 has spare fuel and the daughter spare
    /// medicine, and site 1's trader has just set out.
    /// </summary>
    private static SimulationPersistenceSnapshot CreateDepartedTrade(WorldSeed seed)
    {
        var baseline = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion).CreatePersistenceSnapshot();
        var daughterHouseholds = baseline.Households.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .Take(8).Select(x => x.Id.Value).ToHashSet();
        var world = baseline.World!;
        var daughterSite = world.Tiles.Where(x => x.Walkable &&
                Math.Abs(x.Coordinate.X - world.StartingSite.X) + Math.Abs(x.Coordinate.Y - world.StartingSite.Y) == 6)
            .OrderBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .First(x => DeterministicPathfinder.Find(world, world.StartingSite, x.Coordinate) is { Count: >= 2 }).Coordinate;
        long Site(long? householdId) => householdId is { } id && daughterHouseholds.Contains(id) ? 2 : 1;
        var citizens = baseline.Citizens.Select(citizen =>
        {
            if (Site(citizen.HouseholdId?.Value) == 2)
            {
                citizen.Location = daughterSite;
                citizen.HomeStructureId = null;
            }
            return citizen;
        }).ToArray();
        var current = baseline.MigrationState!;
        var migration = new MigrationWorldState(current.Version,
            citizens.Select(x => new MigrationEntityResidence(x.Id.Value, Site(x.HouseholdId?.Value))).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value, Site(x.Id.Value))).ToArray(),
            current.StructureOwners, current.FacilityOwners, current.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                    baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, x == LivingGood.Medicine ? 40 : 0)).ToArray()),
            roads: current.Roads);
        var engine = SimulationEngine.FromPersistenceSnapshot(new SimulationPersistenceSnapshot(baseline.Seed,
            baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion,
            baseline.WorldConfiguration, baseline.Counters, baseline.ScheduledEvents, baseline.World, citizens,
            baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion,
            baseline.SettlementVersion, baseline.Structures, baseline.StructureContributions, baseline.SocialVersion,
            baseline.Relationships, baseline.Households, baseline.HistoryVersion, baseline.HistoryState,
            baseline.HistoricalEvents, baseline.HistoricalEventCitizens, baseline.HistoricalEventStructures,
            baseline.StatisticsSamples, baseline.Memories, baseline.Agriculture, baseline.Economy,
            baseline.LivingStateJson, migration.ToCanonicalJson()));
        Invoke(engine, "ChangeGoodAt", 1L, LivingGood.Fuel, 150);
        Invoke(engine, "EvaluateMigrationTrade");
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.NotNull(TradeParty(snapshot));
        return snapshot;
    }

    private static object Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static object? Invoke(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(x => x.Name == name && x.GetParameters().Length == arguments.Length).Invoke(instance, arguments);
}
