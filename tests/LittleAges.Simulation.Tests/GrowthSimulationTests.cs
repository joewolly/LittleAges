using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class GrowthSimulationTests
{
    [Fact]
    public void GrowthWorldHasIndependentFounderHouseholdsAndRoundTrips()
    {
        var engine = NewGrowth();
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Equal(20, snapshot.Households.Count);
        Assert.All(snapshot.Citizens, c => Assert.NotNull(c.HouseholdId));
        Assert.All(engine.CreateGrowthObservation().Households, h => Assert.DoesNotContain("Birth spacing", h.Blockers));
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(engine.ComputeSocialFingerprint(), restored.ComputeSocialFingerprint());
        Assert.Equal(SimulationEngine.GrowthSimulationRulesVersion, restored.SimulationRulesVersion);
        Assert.Empty(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion).Households);
    }

    [Fact]
    public void CloseKinBoundaryRejectsFirstCousinsButAllowsSecondCousins()
    {
        var citizens = Enumerable.Range(1, 12).Select(i => Person(i)).ToDictionary(c => c.Id.Value);
        Parent(3, 1); Parent(4, 1); Parent(5, 3); Parent(6, 4); Parent(7, 5); Parent(8, 6);
        Assert.True(GrowthKinship.AreCloseKin(new CitizenId(5), new CitizenId(6), citizens));
        Assert.False(GrowthKinship.AreCloseKin(new CitizenId(7), new CitizenId(8), citizens));
        Assert.True(GrowthKinship.AreCloseKin(new CitizenId(1), new CitizenId(7), citizens));
        Assert.True(GrowthKinship.AreCloseKin(new CitizenId(7), new CitizenId(7), citizens));
        Assert.False(GrowthKinship.AreCloseKin(new CitizenId(7), new CitizenId(12), citizens));
        void Parent(long child, long parent) => citizens[child].ParentAId = new CitizenId(parent);
    }

    [Fact]
    public void ObservationAndChunkingDoNotChangeGrowthTrajectory()
    {
        var direct = NewGrowth();
        var sampled = NewGrowth();
        var end = new WorldMinute(10L * WorldCalendar.MinutesPerDay);
        direct.AdvanceUntil(end);
        for (var day = 1; day <= 10; day++)
        {
            sampled.AdvanceUntil(new WorldMinute((long)day * WorldCalendar.MinutesPerDay));
            _ = sampled.CreateGrowthObservation();
            sampled = SimulationEngine.FromPersistenceSnapshot(sampled.CreatePersistenceSnapshot());
        }
        Assert.Equal(direct.ComputeSocialFingerprint(), sampled.ComputeSocialFingerprint());
        Assert.Equal(direct.ComputeHistoryFingerprint(), sampled.ComputeHistoryFingerprint());
    }

    [Fact]
    public void FoodGatheringReservesSpaceForWorkersAlreadyOutbound()
    {
        var engine = NewGrowth();
        engine.Settlement.FoodStored = 790;
        foreach (var citizen in engine.Citizens)
            Assert.DoesNotContain(engine.EvaluateDecision(citizen.Id), e => e.Action == CitizenAction.GatherFood);
        engine.Settlement.FoodStored = 700;
        Assert.Contains(engine.EvaluateDecision(engine.Citizens[0].Id), e => e.Action == CitizenAction.GatherFood);
    }

    private static SimulationEngine NewGrowth() => new(new WorldSeed(42), simulationRulesVersion: SimulationEngine.GrowthSimulationRulesVersion);
    private static Citizen Person(int id) => new(new CitizenId(id), null, $"Person{id}", "Family", 0,
        new TileCoordinate(0, 0), new CitizenTraits(0, 0, 0, 0, 0, 0), new CitizenSkills(0, 0, 0, 0, 0, 0));
}
