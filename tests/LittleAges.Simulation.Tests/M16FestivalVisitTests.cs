using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M16FestivalVisitTests
{
    [Fact]
    public void FamilyVisitWaitsForFestivalAndKeepsItsDeadlineThroughMealAndReturn()
    {
        var fixture = MigrationVisitSimulationTests.CreateFixture(new WorldSeed(1401), SimulationEngine.FestivalsSimulationRulesVersion);
        var engine = fixture.Engine;
        var start = FestivalRules.Start(2, 0);
        // Exercise the visit coordinator's clock decisions without aging the tiny
        // artificial daughter settlement used by this fixture.
        SetClock(engine, FestivalRules.Start(1, 0) - 14L * WorldCalendar.MinutesPerDay);
        var citizens = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var visitor = citizens[fixture.VisitorId];
        var relative = citizens[fixture.RelativeId];
        Assert.True(visitor.IsAlive && relative.IsAlive);
        var oldVisitorParent = visitor.ParentAId;
        var oldRelativeParent = relative.ParentAId;
        visitor.ParentAId = new CitizenId(fixture.ParentId);
        relative.ParentAId = new CitizenId(fixture.ParentId);
        Invoke(engine, "EvaluateMigrationVisits");
        var plan = Field<LivingWorldState>(engine, "_living").FestivalVisit;
        Assert.NotNull(plan);
        Assert.Null(plan.PartyId);
        Assert.Empty(Field<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        SetClock(engine, plan.DepartMinute);
        Invoke(engine, "EvaluateMigrationVisits");
        Assert.NotNull(plan);
        Assert.NotNull(plan.PartyId);
        Assert.True(plan.DepartMinute < start - WorldCalendar.MinutesPerDay);
        Assert.True(engine.CurrentMinute.Value >= plan.DepartMinute);
        visitor.ParentAId = oldVisitorParent;
        relative.ParentAId = oldRelativeParent;
        var party = Field<MigrationWorldState>(engine, "_migrationState").InTransitParties.Single(x => x.Id == plan.PartyId);
        Assert.Equal(MigrationJourneyKind.Visit, party.JourneyKind);
        Assert.Equal(0, Field<MigrationWorldState>(engine, "_migrationState").LastVisitAttemptYear);

        SetClock(engine, start - WorldCalendar.MinutesPerDay);
        visitor.Location = fixture.DaughterSite;
        Invoke(engine, "HandleFoundingPartyArrival", visitor);
        party = Field<MigrationWorldState>(engine, "_migrationState").InTransitParties.Single(x => x.Id == plan.PartyId);
        Assert.Equal(MigrationVisitPhase.Dwell, party.VisitPhase);
        Assert.Equal(start + FestivalRules.DurationMinutes, party.VisitDwellEndsMinute);
        visitor.Needs = new CitizenNeeds(hunger: 3500);
        visitor.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        Assert.True((bool)Invoke(engine, "TryPauseMigrationVisitForMeal", visitor)!);
        Assert.Equal(CitizenAction.Eat, visitor.CurrentAction);
        Assert.Equal(start + FestivalRules.DurationMinutes, party.VisitDwellEndsMinute);
        // Resuming the same trip keeps the deadline and excludes the meal interval
        // from attendance. Arrival after closing uses the normal one-hour visit.
        SetClock(engine, start + FestivalRules.DurationMinutes);
        Invoke(engine, "ResumeMigrationVisit", visitor, party);
        var returning = Field<MigrationWorldState>(engine, "_migrationState").InTransitParties.Single(x => x.Id == plan.PartyId);
        Assert.Equal(MigrationVisitPhase.Returning, returning.VisitPhase);
        Invoke(engine, "EvaluateMigrationVisits");
        Assert.Single(Field<MigrationWorldState>(engine, "_migrationState").InTransitParties);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void LateVisitorCountsFinalIntervalBeforeReturning(int arrivalOffset, bool qualifies)
    {
        var fixture = MigrationVisitSimulationTests.CreateFixture(new WorldSeed(1401), SimulationEngine.FestivalsSimulationRulesVersion);
        var engine = fixture.Engine;
        var start = FestivalRules.Start(2, 0);
        var citizens = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var visitor = citizens[fixture.VisitorId];
        var relative = citizens[fixture.RelativeId];
        visitor.ParentAId = relative.ParentAId = new CitizenId(fixture.ParentId);
        SetClock(engine, FestivalRules.Start(1, 0) - 14L * WorldCalendar.MinutesPerDay);
        Invoke(engine, "EvaluateMigrationVisits");
        var living = Field<LivingWorldState>(engine, "_living");
        var plan = living.FestivalVisit!;
        SetClock(engine, plan.DepartMinute);
        Invoke(engine, "EvaluateMigrationVisits");
        Assert.NotNull(plan.PartyId);
        SetClock(engine, start);
        Invoke(engine, "AdvanceFestivals");
        SetClock(engine, start + arrivalOffset);
        visitor.Location = fixture.DaughterSite;
        visitor.Needs = new CitizenNeeds();
        visitor.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        Invoke(engine, "HandleFoundingPartyArrival", visitor);
        SetClock(engine, start + 330);
        Invoke(engine, "AdvanceFestivals");
        SetClock(engine, start + FestivalRules.DurationMinutes);
        // A 17:00 arrival completes before the closing pulse. A later arrival
        // retains the normal one-hour dwell, but festival time still stops at 18:00.
        Assert.True((bool)Invoke(engine, "TryCompleteMigrationVisitDwell", visitor)!);
        Assert.Equal(qualifies ? MigrationVisitPhase.Returning : MigrationVisitPhase.Dwell,
            Field<MigrationWorldState>(engine, "_migrationState").InTransitParties.Single(x => x.Id == plan.PartyId).VisitPhase);
        Invoke(engine, "AdvanceFestivals");
        if (!qualifies)
        {
            SetClock(engine, start + arrivalOffset + WorldCalendar.MinutesPerHour);
            Assert.True((bool)Invoke(engine, "TryCompleteMigrationVisitDwell", visitor)!);
        }
        var attendance = Assert.Single(living.Festivals!.Single(x => x.SettlementId == 2).Attendance);
        Assert.Equal(FestivalRules.DurationMinutes - arrivalOffset, attendance.Minutes);
        Assert.Equal(qualifies, attendance.BenefitsGranted);
        Assert.Equal(qualifies ? 1 : 0, engine.HistoricalEvents.Count(x => x.EventType == HistoricalEventType.FestivalAttended));
        Assert.False((bool)Invoke(engine, "TryCompleteMigrationVisitDwell", visitor)!);
        Invoke(engine, "AdvanceFestivals");
        Assert.Equal(FestivalRules.DurationMinutes - arrivalOffset, attendance.Minutes);
        Assert.Equal(MigrationVisitPhase.Returning, Field<MigrationWorldState>(engine, "_migrationState").InTransitParties.Single(x => x.Id == plan.PartyId).VisitPhase);
    }

    private static void SetClock(SimulationEngine engine, long minute) => typeof(SimulationEngine).GetProperty("CurrentMinute")!.SetValue(engine, new WorldMinute(minute));

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Invoke(object instance, string name, params object?[] args) => instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(x => x.Name == name && x.GetParameters().Length == args.Length).Invoke(instance, args);
}
