using System.Reflection;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M17ObservationPurityTests
{
    [Fact]
    public void CompletedNativeOrdersRetireTheirOwnersWithoutAnObserver()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        engine.AdvanceUntil(new(30L * WorldCalendar.MinutesPerDay));
        var living = Field<LivingWorldState>(engine, "_living");
        Assert.True(living.CompletedOrders > 0);
        Assert.Equal(living.Orders.Select(x => x.Id).Order(), State(engine).WorkOrderOwners.Select(x => x.EntityId));
        var before = State(engine);
        _ = engine.CreatePersistenceSnapshot();
        Assert.Same(before, State(engine));
    }

    [Theory]
    [InlineData(SimulationEngine.NewcomersRulesVersion, true)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion, false)]
    public void CapturingInferredOwnershipIsPureOnlyForM17(string rules, bool pure)
    {
        var engine = Create(rules);
        var household = DaughterResident(engine).HouseholdId!.Value.Value;
        RemoveHouseholdOwner(engine, household);
        var before = State(engine);
        var checkpoint = engine.CreatePersistenceSnapshot();
        Assert.Equal(2, checkpoint.MigrationState!.HouseholdResidences.Single(x => x.EntityId == household).SettlementId);
        if (pure) Assert.Same(before, State(engine));
        else Assert.NotSame(before, State(engine));
        Assert.Equal(JsonSerializer.Serialize(checkpoint), JsonSerializer.Serialize(new SimulationEngine(checkpoint).CreatePersistenceSnapshot()));
    }

    [Fact]
    public void IndependentDaughterHouseholdGetsOwnershipAtTheActualTransition()
    {
        var engine = Create(SimulationEngine.NewcomersRulesVersion);
        var resident = DaughterResident(engine);
        resident.HouseholdId = null;
        Call(engine, "EnsureIndependentHouseholds");
        var owner = Assert.Single(State(engine).HouseholdResidences, x => x.EntityId == resident.HouseholdId!.Value.Value);
        Assert.Equal(2, owner.SettlementId);
        var before = State(engine);
        _ = engine.CreatePersistenceSnapshot();
        Assert.Same(before, State(engine));
    }

    [Fact]
    public void FrequentPublicObservationsAndReopenDoNotChangeContinuation()
    {
        var initial = Create(SimulationEngine.NewcomersRulesVersion).CreatePersistenceSnapshot();
        var unseen = new SimulationEngine(initial);
        var observed = new SimulationEngine(initial);
        var household = DaughterResident(observed).HouseholdId!.Value.Value;
        RemoveHouseholdOwner(unseen, household);
        RemoveHouseholdOwner(observed, household);
        var restored = new SimulationEngine(observed.CreatePersistenceSnapshot());
        var end = initial.WorldMinute.Add(1440);
        unseen.AdvanceUntil(end);
        for (var minute = initial.WorldMinute.Value + 37; minute < end.Value; minute += 37)
        {
            observed.AdvanceUntil(new(minute));
            _ = observed.CreatePersistenceSnapshot();
            _ = observed.CreateMigrationReadSnapshot();
            _ = observed.CreateLivingObservation();
            restored.AdvanceUntil(new(minute));
        }
        observed.AdvanceUntil(end);
        restored.AdvanceUntil(end);
        var expected = JsonSerializer.Serialize(unseen.CreatePersistenceSnapshot());
        Assert.Equal(expected, JsonSerializer.Serialize(observed.CreatePersistenceSnapshot()));
        Assert.Equal(expected, JsonSerializer.Serialize(restored.CreatePersistenceSnapshot()));
    }

    private static SimulationEngine Create(string rules)
    {
        var helper = new M14MigrationFoundationTests();
        var fixture = Call(helper, "CreateRelocationEngine", new WorldSeed(920), 2L, 1L, rules)!;
        return (SimulationEngine)fixture.GetType().GetProperty("Engine")!.GetValue(fixture)!;
    }

    private static Citizen DaughterResident(SimulationEngine engine) =>
        Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive &&
            x.PartnerId is null && x.HouseholdId is not null && x.AgeYears(engine.CurrentMinute) >= 18 &&
            State(engine).CitizenResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 2);

    private static void RemoveHouseholdOwner(SimulationEngine engine, long household)
    {
        var state = State(engine);
        typeof(SimulationEngine).GetField("_migrationState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine,
            new MigrationWorldState(state.Version, state.CitizenResidences,
                state.HouseholdResidences.Where(x => x.EntityId != household).ToArray(), state.StructureOwners,
                state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement, state.InTransitParties,
                state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear, state.Roads));
    }

    private static MigrationWorldState State(SimulationEngine engine) => Field<MigrationWorldState>(engine, "_migrationState");
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object? Call(object value, string name, params object?[] arguments) => value.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
        .Single(x => x.Name == name && x.GetParameters().Length == arguments.Length)
        .Invoke(value, arguments);
}
