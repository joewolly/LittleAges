using System.Text.Json;
using LittleAges.Domain;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class WorldRulesUpgradeTests
{
    [Fact]
    public void EveryDefaultHasARegisteredContinuationPath()
    {
        Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, WorldRulesUpgrades.SupportedRules[^1]);
        foreach (var rules in WorldRulesUpgrades.SupportedRules)
        {
            var snapshot = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules).CreatePersistenceSnapshot();
            Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, WorldRulesUpgrades.Plan(snapshot).TargetRules);
        }
    }

    [Theory]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion)]
    [InlineData(SimulationEngine.RoadsSimulationRulesVersion)]
    [InlineData(SimulationEngine.PlannedSimulationRulesVersion)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion)]
    [InlineData(SimulationEngine.NewcomersRulesVersion)]
    public void ConversionPreservesCivilizationAndContinuesAcrossChunks(string rules)
    {
        var original = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
        original.AdvanceUntil(new WorldMinute(10000));
        var source = original.CreatePersistenceSnapshot();
        var sourceHash = WorldRulesUpgrades.Fingerprint(source);
        var plan = WorldRulesUpgrades.Plan(source);
        Assert.Equal(sourceHash, WorldRulesUpgrades.Fingerprint(source));
        Assert.Equal(source.WorldMinute, plan.Snapshot.WorldMinute);
        Assert.Equal(source.World!.Fingerprint, plan.Snapshot.World!.Fingerprint);
        Assert.Equal(source.Counters, plan.Snapshot.Counters);
        Assert.Equal(JsonSerializer.Serialize(source.Citizens), JsonSerializer.Serialize(plan.Snapshot.Citizens));
        Assert.Equal(JsonSerializer.Serialize(source.Structures), JsonSerializer.Serialize(plan.Snapshot.Structures));
        Assert.Equal(JsonSerializer.Serialize(source.Households), JsonSerializer.Serialize(plan.Snapshot.Households));
        Assert.Equal(source.ResourceStates, plan.Snapshot.ResourceStates);
        Assert.Equal(source.Settlement, plan.Snapshot.Settlement);
        Assert.Equal(source.Relationships, plan.Snapshot.Relationships);
        Assert.Equal(JsonSerializer.Serialize(source.StructureContributions), JsonSerializer.Serialize(plan.Snapshot.StructureContributions));
        Assert.Equal(source.Agriculture!.ToCanonicalJson(), plan.Snapshot.Agriculture!.ToCanonicalJson());
        Assert.Equal(source.Economy!.ToUnifiedCanonicalJson(), plan.Snapshot.Economy!.ToUnifiedCanonicalJson());
        Assert.Equal(JsonSerializer.Serialize(source.HistoricalEvents), JsonSerializer.Serialize(plan.Snapshot.HistoricalEvents));
        Assert.Equal(source.HistoricalEventCitizens, plan.Snapshot.HistoricalEventCitizens);
        Assert.Equal(source.HistoricalEventStructures, plan.Snapshot.HistoricalEventStructures);
        Assert.Equal(source.StatisticsSamples, plan.Snapshot.StatisticsSamples);
        Assert.Equal(source.Memories, plan.Snapshot.Memories);
        var originalLiving = LivingWorldCodec.Deserialize(source.LivingStateJson!);
        var upgradedLiving = LivingWorldCodec.Deserialize(plan.Snapshot.LivingStateJson!);
        upgradedLiving.Festivals = originalLiving.Festivals;
        upgradedLiving.Newcomers = originalLiving.Newcomers;
        Assert.Equal(LivingWorldCodec.Serialize(originalLiving), LivingWorldCodec.Serialize(upgradedLiving));
        if (SimulationEngine.RoadSystemsEnabled(rules))
            Assert.Equal(JsonSerializer.Serialize(source.MigrationState!.Roads), JsonSerializer.Serialize(plan.Snapshot.MigrationState!.Roads));
        Assert.Equal(source.ScheduledEvents, plan.Snapshot.ScheduledEvents);
        var a = SimulationEngine.FromPersistenceSnapshot(plan.Snapshot);
        var b = SimulationEngine.FromPersistenceSnapshot(WorldRulesUpgrades.Plan(source).Snapshot);
        a.AdvanceUntil(new WorldMinute(20000));
        for (var minute = 11000; minute <= 20000; minute += 1000) b.AdvanceUntil(new WorldMinute(minute));
        Assert.Equal(WorldRulesUpgrades.Fingerprint(a.CreatePersistenceSnapshot()), WorldRulesUpgrades.Fingerprint(b.CreatePersistenceSnapshot()));
        Assert.Empty(WorldRulesUpgrades.Plan(plan.Snapshot).Converters);
    }

    [Fact]
    public void RoadsStartWithoutInventedWearAndFreezeOldRemainingRoutes()
    {
        var engine = new SimulationEngine(new WorldSeed(17), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(1000));
        var source = engine.CreatePersistenceSnapshot();
        var converted = WorldRulesUpgrades.Plan(source, SimulationEngine.RoadsSimulationRulesVersion).Snapshot;
        Assert.Empty(converted.MigrationState!.Roads!.Tiles);
        foreach (var route in converted.MigrationState.Roads.ActiveRoutes)
        {
            var person = source.Citizens.Single(c => c.Id.Value == route.CitizenId);
            Assert.Equal(DeterministicPathfinder.Find(source.World!, person.Location, person.ActionTarget!.Value), route.Route);
        }
        Assert.NotEmpty(converted.MigrationState.Roads.ActiveRoutes);
    }

    [Fact]
    public void FutureSchedulesDoNotReplayElapsedOccurrences()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PlannedSimulationRulesVersion);
        engine.AdvanceUntil(new WorldCalendarDate(0, 9, 1, 13, 0).ToWorldMinute());
        var upgraded = WorldRulesUpgrades.Plan(engine.CreatePersistenceSnapshot()).Snapshot;
        var living = LivingWorldCodec.Deserialize(upgraded.LivingStateJson!);
        Assert.All(living.Festivals!, f => Assert.True(f.StartMinute > upgraded.WorldMinute.Value));
        Assert.Equal(0, living.Newcomers!.LastAttemptYear);
        Assert.Empty(living.Newcomers.Visitors);
    }

    [Fact]
    public void LegacyRulesAndBackwardConversionRemainUnsupported()
    {
        var legacy = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion).CreatePersistenceSnapshot();
        Assert.Throws<NotSupportedException>(() => WorldRulesUpgrades.Plan(legacy));
        var current = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        Assert.Throws<NotSupportedException>(() => WorldRulesUpgrades.Plan(current, SimulationEngine.MigrationSimulationRulesVersion));
    }

    [Fact]
    public void TwoSettlementsRetainResidentsStocksAndOwnership()
    {
        var source = M14MigrationFoundationTests.CreateDaughterFixture(new WorldSeed(42)).Snapshot;
        var converted = WorldRulesUpgrades.Plan(source).Snapshot;
        Assert.Equal(source.MigrationState!.DaughterSettlement!.Site, converted.MigrationState!.DaughterSettlement!.Site);
        Assert.Equal(source.MigrationState.DaughterSettlement.CommunalStock, converted.MigrationState.DaughterSettlement.CommunalStock);
        Assert.Equal(source.MigrationState.CitizenResidences, converted.MigrationState.CitizenResidences);
        Assert.Equal(source.MigrationState.HouseholdResidences, converted.MigrationState.HouseholdResidences);
        Assert.Equal(source.MigrationState.StructureOwners, converted.MigrationState.StructureOwners);
        Assert.Equal(2, LivingWorldCodec.Deserialize(converted.LivingStateJson!).Festivals!.Count);
    }

    [Fact]
    public void ActiveFestivalEscrowAttendanceAndHistoryAreRetained()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.FestivalsSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0) + 120));
        var source = engine.CreatePersistenceSnapshot();
        var festival = LivingWorldCodec.Deserialize(source.LivingStateJson!).Festivals!;
        Assert.True(festival[0].Started);
        var upgraded = WorldRulesUpgrades.Plan(source).Snapshot;
        Assert.Equal(JsonSerializer.Serialize(festival), JsonSerializer.Serialize(LivingWorldCodec.Deserialize(upgraded.LivingStateJson!).Festivals));
        Assert.Equal(source.HistoricalEvents, upgraded.HistoricalEvents);
    }

    [Theory]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion, 1L, 2L)]
    [InlineData(SimulationEngine.RoadsSimulationRulesVersion, 2L, 1L)]
    public void MigratingHouseholdsKeepPhysicalJourneysCargoAndScheduledIdentity(string rules, long origin, long destination)
    {
        var fixture = M14MigrationFoundationTests.CreateRelocationEngine(new WorldSeed(918 + (ulong)origin), origin, destination, rules);
        typeof(SimulationEngine).GetMethod("EvaluateMigrationRelocation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fixture.Engine, null);
        var source = fixture.Engine.CreatePersistenceSnapshot();
        var party = Assert.Single(source.MigrationState!.InTransitParties);
        Assert.Equal(MigrationJourneyKind.Relocation, party.JourneyKind);
        Assert.NotEmpty(party.Cargo);
        var upgraded = WorldRulesUpgrades.Plan(source).Snapshot;
        Assert.Equal(JsonSerializer.Serialize(source.MigrationState.InTransitParties), JsonSerializer.Serialize(upgraded.MigrationState!.InTransitParties));
        Assert.Equal(JsonSerializer.Serialize(source.Citizens), JsonSerializer.Serialize(upgraded.Citizens));
        Assert.Equal(source.ScheduledEvents, upgraded.ScheduledEvents);
        Assert.Equal(source.HistoricalEvents, upgraded.HistoricalEvents);
        var reference = new SimulationEngine(upgraded);
        var resumed = new SimulationEngine(upgraded);
        var end = source.WorldMinute.Add(WorldCalendar.MinutesPerDay);
        reference.AdvanceUntil(end);
        for (var minute = source.WorldMinute.Value + 360; minute <= end.Value; minute += 360)
            resumed.AdvanceUntil(new WorldMinute(minute));
        Assert.Equal(WorldRulesUpgrades.Fingerprint(reference.CreatePersistenceSnapshot()), WorldRulesUpgrades.Fingerprint(resumed.CreatePersistenceSnapshot()));
    }

    [Fact]
    public void ExtinctWorldIsPreservedWithoutFoundersOrVisitors()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var citizens = (Dictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
        var kill = typeof(SimulationEngine).GetMethod("KillNatural", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var citizen in citizens.Values.ToArray()) kill.Invoke(engine, [citizen]);
        typeof(SimulationEngine).GetMethod("SynchronizeLivingPeople", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(engine, null);
        var upgraded = WorldRulesUpgrades.Plan(engine.CreatePersistenceSnapshot()).Snapshot;
        Assert.All(upgraded.Citizens, citizen => Assert.False(citizen.IsAlive));
        Assert.Empty(LivingWorldCodec.Deserialize(upgraded.LivingStateJson!).Newcomers!.Visitors);
        Assert.Equal(engine.CounterSnapshot, upgraded.Counters);
    }
}
