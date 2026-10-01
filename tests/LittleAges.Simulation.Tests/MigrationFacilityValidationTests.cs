using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class MigrationFacilityValidationTests
{
    [Theory]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion)]
    [InlineData(SimulationEngine.RoadsSimulationRulesVersion)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion)]
    public void BothSettlementsCanKeepTheirOwnFacilityAllowanceAndReload(string rules)
    {
        var engine = MigrationVisitSimulationTests.CreateFixture(new WorldSeed(1617), rules).Engine;
        AddFacilities(engine, 1, 18);
        AddFacilities(engine, 2, 18);

        var snapshot = engine.CreatePersistenceSnapshot();
        LivingValidation.Validate(snapshot);
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(snapshot.LivingStateJson, restored.LivingStateJson);
        Assert.Equal(snapshot.MigrationStateJson, restored.CreatePersistenceSnapshot().MigrationStateJson);

        AddFacilities(engine, 1, 1);
        var error = Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        Assert.Contains("Facilities are invalid", error.Message);
    }

    [Fact]
    public void SingleSettlementLivingRulesKeepTheOriginalEighteenFacilityLimit()
    {
        var engine = new SimulationEngine(new WorldSeed(1617), simulationRulesVersion: SimulationEngine.Living2SimulationRulesVersion);
        AddFacilities(engine, 1, 18);
        LivingValidation.Validate(engine.CreatePersistenceSnapshot());

        AddFacilities(engine, 1, 1);
        var error = Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        Assert.Contains("Facilities are invalid", error.Message);
    }

    private static void AddFacilities(SimulationEngine engine, long siteId, int count)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var state = (LivingWorldState)typeof(SimulationEngine).GetField("_living", flags)!.GetValue(engine)!;
        var locationSite = typeof(SimulationEngine).GetMethod("SiteIdForLocation", flags)!;
        var recordOwner = typeof(SimulationEngine).GetMethod("RecordMigrationOwner", flags)!;
        var facilityOwnerKind = Enum.Parse(recordOwner.GetParameters()[0].ParameterType, "Facility");
        var used = state.Fields.Select(x => x.Location).Concat(state.Facilities.Select(x => x.Location))
            .Concat(engine.Structures.Select(x => x.Location)).ToHashSet();
        var locations = engine.World.Tiles.Where(x => x.Buildable && x.Walkable && !used.Contains(x.Coordinate) &&
            (long)locationSite.Invoke(engine, [x.Coordinate])! == siteId).Take(count).ToArray();
        Assert.Equal(count, locations.Length);
        foreach (var tile in locations)
        {
            var kind = state.Facilities.Count(x => (long)locationSite.Invoke(engine, [x.Location])! == siteId) switch
            {
                16 => LivingFacilityKind.Loom,
                17 => LivingFacilityKind.CareHouse,
                _ => LivingFacilityKind.Hearth
            };
            var facility = new LivingFacility(state.NextId++, kind, tile.Coordinate, engine.CurrentMinute.Value);
            state.Facilities.Add(facility);
            recordOwner.Invoke(engine, [facilityOwnerKind, facility.Id, siteId]);
        }
    }
}
