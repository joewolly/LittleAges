using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M16FestivalTests
{
    private static readonly Lazy<SimulationPersistenceSnapshot> BeforeOpening = new(() =>
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.FestivalsSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0) - 30));
        return engine.CreatePersistenceSnapshot();
    });

    [Fact]
    public void FestivalsInheritM15ButDoNotChangeEarlierWorlds()
    {
        Assert.Equal(SimulationEngine.FestivalsSimulationRulesVersion, SimulationEngine.CurrentSimulationRulesVersion);
        Assert.True(SimulationEngine.RoadSystemsEnabled(SimulationEngine.FestivalsSimulationRulesVersion));
        Assert.True(SimulationEngine.MigrationSystemsEnabled(SimulationEngine.FestivalsSimulationRulesVersion));
        var old = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion);
        old.AdvanceUntil(new WorldMinute(1440));
        Assert.Null(LivingWorldCodec.Deserialize(old.LivingStateJson!).Festivals);
        Assert.DoesNotContain("festivals", old.LivingStateJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(old.CreatePersistenceSnapshot().HistoricalEvents, x => x.EventType >= HistoricalEventType.FestivalStarted);
        Assert.Equal(29, (int)HistoricalEventType.FestivalStarted);
        Assert.Equal(30, (int)HistoricalEventType.FestivalEnded);
        Assert.Equal(31, (int)HistoricalEventType.FestivalAttended);
        Assert.Equal(7, (int)MemoryType.FestivalAttended);
    }

    [Fact]
    public void LeanYearStillOpensAndEndsWithoutSpendingFeastFood()
    {
        var engine = new SimulationEngine(BeforeOpening.Value);
        engine.Settlement.FoodStored = 0;
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0)));
        var festival = Assert.Single(LivingWorldCodec.Deserialize(engine.LivingStateJson!).Festivals!);
        Assert.True(festival.Started);
        Assert.Equal(FestivalMode.Gathering, festival.Mode);
        Assert.Equal(0, festival.InitialFood);
        engine.AdvanceUntil(new WorldMinute(festival.EndMinute));
        festival = Assert.Single(LivingWorldCodec.Deserialize(engine.LivingStateJson!).Festivals!);
        Assert.True(festival.Finished);
        Assert.Equal(0, festival.ConsumedFood);
        Assert.DoesNotContain(LivingWorldCodec.Deserialize(engine.LivingStateJson!).Orders, x => x.Kind == LivingWorkKind.AttendFestival);
    }

    [Fact]
    public void ActualAttendanceConsumesOnePortionAndCreatesOneFactualMemory()
    {
        var engine = new SimulationEngine(BeforeOpening.Value);
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0) + 180));
        var snapshot = engine.CreatePersistenceSnapshot();
        var festival = Assert.Single(LivingWorldCodec.Deserialize(snapshot.LivingStateJson!).Festivals!);
        Assert.Equal(FestivalMode.Feast, festival.Mode);
        Assert.Contains(festival.Attendance, x => x.BenefitsGranted);
        Assert.Equal(festival.InitialFood, festival.ReservedFood + festival.ConsumedFood);
        foreach (var attendee in festival.Attendance.Where(x => x.BenefitsGranted))
        {
            var history = Assert.Single(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.FestivalAttended && snapshot.HistoricalEventCitizens.Any(link => link.HistoricalEventId == x.Id && link.CitizenId.Value == attendee.CitizenId));
            Assert.Contains(snapshot.Memories, x => x.HistoricalEventId == history.Id && x.MemoryType == MemoryType.FestivalAttended);
            Assert.Single(LivingWorldCodec.Deserialize(snapshot.LivingStateJson!).People.Single(x => x.CitizenId == attendee.CitizenId).Experiences, x => x.Kind == LivingExperienceKind.Festival);
        }
        engine.AdvanceUntil(new WorldMinute(festival.EndMinute));
        var completed = Assert.Single(LivingWorldCodec.Deserialize(engine.LivingStateJson!).Festivals!);
        Assert.Equal(completed.Attendance.Count(x => x.PortionConsumed) * 10, completed.ConsumedFood);
        Assert.Equal(0, completed.ReservedFood);
        LivingValidation.Validate(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void InterruptedAttendanceRetainsProgressWithoutToolOrWorkSkillChanges()
    {
        var engine = new SimulationEngine(BeforeOpening.Value);
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0) + 120));
        var living = Field<LivingWorldState>(engine, "_living");
        var order = living.Orders.First(x => x.Kind == LivingWorkKind.AttendFestival && x.CitizenId is not null && x.Phase == LivingWorkPhase.Work);
        var citizen = Field<Dictionary<long, Citizen>>(engine, "_citizens")[order.CitizenId!.Value];
        var person = living.People.Single(x => x.CitizenId == citizen.Id.Value);
        var tool = person.ToolCondition;
        var skill = citizen.Skills.Domestic;
        citizen.Needs = citizen.GetProjectedNeeds(engine.CurrentMinute) with { Rest = 5000 };
        citizen.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        engine.AdvanceUntil(citizen.ActionCompletesMinute!.Value);
        Assert.Null(order.CitizenId);
        Assert.Equal(tool, person.ToolCondition);
        Assert.Equal(skill, citizen.Skills.Domestic);
        Assert.True(living.Festivals![0].Attendance.Single(x => x.CitizenId == citizen.Id.Value).Minutes > 0);
    }

    [Fact]
    public void ChunkedAndRestoredFestivalContinueIdenticallyThroughClosing()
    {
        var reference = new SimulationEngine(BeforeOpening.Value);
        var chunked = new SimulationEngine(BeforeOpening.Value);
        var checkpoint = FestivalRules.Start(1, 0) + 90;
        reference.AdvanceUntil(new WorldMinute(checkpoint));
        for (var minute = BeforeOpening.Value.WorldMinute.Value + 7; minute < checkpoint; minute += 7) chunked.AdvanceUntil(new WorldMinute(minute));
        chunked.AdvanceUntil(new WorldMinute(checkpoint));
        Assert.Equal(reference.LivingStateJson, chunked.LivingStateJson);
        var reopened = new SimulationEngine(chunked.CreatePersistenceSnapshot());
        var end = new WorldMinute(FestivalRules.Start(1, 0) + FestivalRules.DurationMinutes + 120);
        reference.AdvanceUntil(end);
        reopened.AdvanceUntil(end);
        Assert.Equal(reference.LivingStateJson, reopened.LivingStateJson);
        Assert.Equal(reference.HistoryFingerprint, reopened.HistoryFingerprint);
        Assert.Equal(reference.SettlementFingerprint, reopened.SettlementFingerprint);
        Assert.Equal(reference.CreatePersistenceSnapshot().MigrationStateJson, reopened.CreatePersistenceSnapshot().MigrationStateJson);
    }

    [Fact]
    public void CorruptEscrowAndAttendanceAreRejected()
    {
        var engine = new SimulationEngine(BeforeOpening.Value);
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0) + 90));
        var state = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
        state.Festivals![0].ReservedFood++;
        Assert.Throws<ArgumentException>(() => FestivalValidation.Validate(engine.CreatePersistenceSnapshot(), state));
        state.Festivals[0].ReservedFood--;
        state.Festivals[0].Attendance = null!;
        Assert.Throws<ArgumentException>(() => FestivalValidation.Validate(engine.CreatePersistenceSnapshot(), state));
        state = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
        state.Festivals![0].Attendance.Add(new FestivalAttendance { CitizenId = long.MaxValue, Minutes = 60, BenefitsGranted = true });
        Assert.Throws<ArgumentException>(() => FestivalValidation.Validate(engine.CreatePersistenceSnapshot(), state));
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
