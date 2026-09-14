using System.Globalization;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Simulation.Tests;

/// <summary>Deterministic, autonomous settlement evidence at the M4 sixty-day horizon.</summary>
public sealed class M4LongHorizonAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public M4LongHorizonAcceptanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Seed42AutonomousSettlementEvolvesThroughConstructionWithoutImmediateExposureCollapse()
    {
        var engine = new SimulationEngine(new WorldSeed(42));
        var start = Snapshot(engine);
        Assert.Empty(engine.Structures);

        var observations = new List<DayObservation>();
        for (var day = 1; day <= 60; day++)
        {
            engine.AdvanceUntil(new WorldMinute(day * WorldCalendar.MinutesPerDay));
            observations.Add(new DayObservation(day, Snapshot(engine)));
        }

        var end = Snapshot(engine);
        var firstProjectDay = observations.FirstOrDefault(observation => observation.State.StructuresCreated > 0)?.Day ?? 0;
        var firstCompletedShelterDay = observations.FirstOrDefault(observation => observation.State.CompletedShelters > 0)?.Day ?? 0;
        var firstCompletedShelterMinute = engine.Structures.Where(structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Shelter)
            .Select(structure => structure.CompletedMinute).OfType<long>().DefaultIfEmpty(-1).Min();
        var firstAdditionalConstructionMinute = engine.Structures.Where(structure => structure.ConstructionStartedMinute > firstCompletedShelterMinute)
            .Select(structure => structure.ConstructionStartedMinute).DefaultIfEmpty(-1).Min();
        var afterExposureGrace = observations.First(observation => observation.Day >= 8);
        var deathCauses = engine.Citizens.Where(citizen => citizen.DeathCause is not null)
            .GroupBy(citizen => citizen.DeathCause!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Count()}");
        var occupations = engine.Citizens.Select(citizen => citizen.Occupation)
            .GroupBy(value => value, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Count()}");
        var active = engine.ActiveConstructionProject;

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"seed=42 days=60 startLiving={start.LivingPopulation} endLiving={end.LivingPopulation} deaths=[{string.Join(',', deathCauses)}]"));
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"structures created={end.StructuresCreated} completedShelter={end.CompletedShelters} completedStockpile={end.CompletedStockpiles} completedWorkshop={end.CompletedWorkshops} shelterCapacity={end.ShelterCapacity} housed={end.HousedPopulation} unhoused={end.UnhousedPopulation}"));
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"storage capacity={end.StorageCapacity} used={end.StorageUsed} food={end.Food} wood={end.Wood} stone={end.Stone} deliveredWood={end.DeliveredWood} deliveredStone={end.DeliveredStone} constructionSkillGain={end.ConstructionSkill - start.ConstructionSkill} haulingSkillGain={end.HaulingSkill - start.HaulingSkill}"));
        _output.WriteLine($"occupations=[{string.Join(',', occupations)}] exposure day8Living={afterExposureGrace.State.LivingPopulation} day8MinHealth={afterExposureGrace.State.MinimumLivingHealth} day8CriticalShelter={afterExposureGrace.State.CriticalShelterCount} finalMinHealth={end.MinimumLivingHealth} finalCriticalShelter={end.CriticalShelterCount}");
        _output.WriteLine($"milestones firstProjectDay={firstProjectDay} firstCompletedShelterDay={firstCompletedShelterDay} firstCompletedShelterMinute={firstCompletedShelterMinute} firstAdditionalConstructionMinute={firstAdditionalConstructionMinute} activeProject={(active is null ? "none" : $"{active.Id.Value}:{active.Type}:{active.Status}:{active.DeliveredWood}/{active.RequiredWood}w,{active.DeliveredStone}/{active.RequiredStone}s,{active.CompletedWork}/{active.RequiredWork}work")}");

        Assert.True(firstProjectDay > 0, "Normal settlement demand must create a construction site.");
        Assert.True(firstCompletedShelterDay > 0, "Autonomous construction must complete a Shelter within sixty days.");
        Assert.True(firstAdditionalConstructionMinute > firstCompletedShelterMinute, "Normal settlement demand must create an additional structure after the first completed Shelter.");
        Assert.True(end.CompletedShelters > 0, "The sixty-day settlement must contain a completed Shelter.");
        Assert.True(end.StructuresCreated > 1, "The sixty-day settlement must include additional normal-demand construction.");
        Assert.True(end.DeliveredWood + end.DeliveredStone > 0, "Construction material hauling must have delivered material.");
        Assert.True(end.ConstructionSkill > start.ConstructionSkill, "Autonomous builders must gain Construction skill.");
        Assert.True(end.HaulingSkill > start.HaulingSkill, "Autonomous haulers must gain Hauling skill.");
        Assert.True(afterExposureGrace.State.LivingPopulation > 0, "The default settlement must not collapse immediately from exposure after its grace period.");
        Assert.True(end.LivingPopulation > 0, "The seed-42 settlement must retain living citizens at day sixty.");
    }

    private static SettlementObservation Snapshot(SimulationEngine engine)
    {
        var citizens = engine.Citizens;
        var structures = engine.Structures;
        var living = citizens.Where(citizen => citizen.IsAlive).ToArray();
        return new SettlementObservation(
            engine.LivingPopulation,
            structures.Count,
            structures.Count(structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Shelter),
            structures.Count(structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Stockpile),
            structures.Count(structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Workshop),
            engine.ShelterCapacity,
            living.Count(citizen => citizen.HomeStructureId is not null),
            living.Count(citizen => citizen.HomeStructureId is null),
            engine.StorageCapacity,
            engine.Settlement.FoodStored + engine.Settlement.WoodStored + engine.Settlement.StoneStored,
            engine.Settlement.FoodStored,
            engine.Settlement.WoodStored,
            engine.Settlement.StoneStored,
            engine.StructureContributions.Sum(contribution => contribution.WoodDelivered),
            engine.StructureContributions.Sum(contribution => contribution.StoneDelivered),
            citizens.Sum(citizen => citizen.Skills.Construction),
            citizens.Sum(citizen => citizen.Skills.Hauling),
            living.Length == 0 ? 0 : living.Min(citizen => citizen.Health),
            living.Count(citizen => citizen.Needs.Shelter >= CitizenSimulationRules.ShelterCriticalThreshold));
    }

    private sealed record DayObservation(int Day, SettlementObservation State);

    private sealed record SettlementObservation(
        int LivingPopulation,
        int StructuresCreated,
        int CompletedShelters,
        int CompletedStockpiles,
        int CompletedWorkshops,
        int ShelterCapacity,
        int HousedPopulation,
        int UnhousedPopulation,
        int StorageCapacity,
        int StorageUsed,
        int Food,
        int Wood,
        int Stone,
        int DeliveredWood,
        int DeliveredStone,
        int ConstructionSkill,
        int HaulingSkill,
        int MinimumLivingHealth,
        int CriticalShelterCount);
}
