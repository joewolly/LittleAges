using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M17HousingDemandTests
{
    [Theory]
    [InlineData(SimulationEngine.PlannedSimulationRulesVersion, false)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion, false)]
    [InlineData(SimulationEngine.NewcomersRulesVersion, true)]
    public void FragmentedSpareSlotsDemandShelterOnlyUnderM17(string rules, bool corrected)
    {
        var engine = new SimulationEngine(new WorldSeed(7), simulationRulesVersion: rules);
        var citizens = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.OrderBy(c => c.Id.Value).ToArray();
        var household = Field<Dictionary<long, Household>>(engine, "_households").Values.OrderBy(h => h.Id.Value).First();
        foreach (var citizen in citizens) citizen.HomeStructureId = null;
        citizens[0].HouseholdId = household.Id;
        citizens[1].HouseholdId = household.Id;
        household.DwellingStructureId = null;
        foreach (var other in Field<Dictionary<long, Household>>(engine, "_households").Values.Where(h => !citizens.Any(c => c.HouseholdId == h.Id)))
        { other.DissolvedMinute = 0; other.DwellingStructureId = null; }
        var tiles = engine.World.Tiles.Where(t => t.Buildable && t.Coordinate != engine.World.StartingSite).Take(8).ToArray();
        for (var index = 0; index < 6; index++)
        {
            Invoke(engine, "CreateStructure", StructureType.Shelter, tiles[index].Coordinate);
            var shelter = Field<Dictionary<long, Structure>>(engine, "_structures").Values.Single(s => s.Status == StructureStatus.UnderConstruction);
            Complete(shelter);
            foreach (var citizen in citizens.Skip(2 + index * 3).Take(3))
            {
                citizen.HomeStructureId = shelter.Id;
                Field<Dictionary<long, Household>>(engine, "_households")[citizen.HouseholdId!.Value.Value].DwellingStructureId = shelter.Id;
            }
        }
        Invoke(engine, "ReconcileMigrationHouseholdsAndHousing");
        Assert.Equal(24, engine.ShelterCapacity);
        Assert.Equal(20, engine.LivingPopulation);
        Assert.Equal(2, engine.Citizens.Count(c => c.IsAlive && c.HomeStructureId is null));
        var demand = Invoke(engine, "SelectSettlementDemand", 1L);
        if (!corrected) { Assert.NotEqual(StructureType.Shelter, demand); return; }
        Assert.Equal(StructureType.Shelter, demand);
        // Existing storage work is allowed to finish; exposure correction does not preempt it.
        Invoke(engine, "CreateStructure", StructureType.Storehouse, tiles[6].Coordinate);
        var storehouse = Field<Dictionary<long, Structure>>(engine, "_structures").Values.Single(s => s.Status == StructureStatus.UnderConstruction);
        Invoke(engine, "EvaluateSettlementDemand");
        Assert.Equal(storehouse.Id, Assert.Single(engine.Structures, s => s.Status == StructureStatus.UnderConstruction).Id);
        Complete(storehouse);
        Assert.Equal(StructureType.Shelter, Invoke(engine, "SelectSettlementDemand", 1L));
        Invoke(engine, "CreateStructure", StructureType.Shelter, tiles[7].Coordinate);
        var newHome = Field<Dictionary<long, Structure>>(engine, "_structures").Values.Single(s => s.Status == StructureStatus.UnderConstruction);
        // An existing shelter project also blocks duplicate orders at the demand boundary.
        Invoke(engine, "EvaluateSettlementDemand");
        Assert.Equal(newHome.Id, Assert.Single(engine.Structures, s => s.Status == StructureStatus.UnderConstruction).Id);
        Complete(newHome);
        Invoke(engine, "ReconcileMigrationHouseholdsAndHousing");
        Assert.All(engine.Citizens, c => Assert.NotNull(c.HomeStructureId));
        Assert.Equal(newHome.Id, household.DwellingStructureId);
        Assert.Equal(citizens[0].HomeStructureId, citizens[1].HomeStructureId);
        Assert.NotEqual(StructureType.Shelter, Invoke(engine, "SelectSettlementDemand", 1L));
    }

    private static void Complete(Structure shelter)
    {
        shelter.DeliveredWood = shelter.RequiredWood;
        shelter.DeliveredStone = shelter.RequiredStone;
        shelter.CompletedWork = shelter.RequiredWork;
        shelter.CompletedMinute = 0;
        shelter.Status = StructureStatus.Complete;
    }

    private static T Field<T>(SimulationEngine engine, string name) =>
        (T)typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;

    private static object? Invoke(SimulationEngine engine, string name, params object[] args) =>
        typeof(SimulationEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(engine, args);
}
