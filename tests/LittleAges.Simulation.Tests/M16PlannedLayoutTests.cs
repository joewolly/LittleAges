using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M16PlannedLayoutTests
{
    private const string Planned = SimulationEngine.PlannedSimulationRulesVersion;

    [Fact]
    public void M16IncludesEveryM15SystemAndIsTheNewWorldDefault()
    {
        Assert.Equal(Planned, SimulationEngine.CurrentSimulationRulesVersion);
        Assert.True(SimulationEngine.RoadSystemsEnabled(Planned));
        Assert.True(SimulationEngine.MigrationSystemsEnabled(Planned));
        Assert.True(SimulationEngine.UnifiedSimulationRulesEnabled(Planned));
        Assert.True(SimulationEngine.LivingSystemsEnabled(Planned));
        Assert.True(SimulationEngine.SocialSystemsEnabled(Planned));
        Assert.True(SimulationEngine.HistorySystemsEnabled(Planned));
        Assert.True(SimulationEngine.GrowthSystemsEnabled(Planned));
        Assert.True(SimulationEngine.AgricultureSystemsEnabled(Planned));
        Assert.True(SimulationEngine.EconomySystemsEnabled(Planned));
        Assert.Equal(20, SimulationEngine.FoodShortageRecoveryMultiplier(Planned));

        Assert.True(SimulationEngine.PlannedLayoutEnabled(Planned));
        Assert.False(SimulationEngine.PlannedLayoutEnabled(SimulationEngine.RoadsSimulationRulesVersion));
        Assert.False(SimulationEngine.PlannedLayoutEnabled(SimulationEngine.MigrationSimulationRulesVersion));
    }

    [Fact]
    public void PlanIsDerivedFromGeographyAlone()
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        var first = SettlementPlan.Create(world, world.StartingSite);
        var second = SettlementPlan.Create(world, world.StartingSite);
        var districts = Enum.GetValues<PlanDistrict>();

        Assert.Equal(districts.Select(first.Anchor), districts.Select(second.Anchor));
        Assert.Equal(world.StartingSite, first.Anchor(PlanDistrict.Core));
        Assert.Equal(districts.Length, districts.Select(first.Anchor).Distinct().Count());
        foreach (var district in districts.Where(x => x != PlanDistrict.Core))
        {
            var anchor = first.Anchor(district);
            Assert.True(world.GetTile(anchor).Buildable);
            Assert.InRange(SettlementPlan.Chebyshev(anchor, world.StartingSite), SettlementPlan.CoreRadius + 1, SettlementPlan.SurveyRadius);
            Assert.Equal(district, first.DistrictOf(anchor));
        }
        foreach (var tile in world.Tiles.Where(x => first.DistrictOf(x.Coordinate) is not null))
            Assert.Equal(first.DistrictOf(tile.Coordinate), second.DistrictOf(tile.Coordinate));
        Assert.Equal(PlanDistrict.Core, first.DistrictOf(world.StartingSite));
    }

    [Fact]
    public void StoragePacksIntoOneYardWhileOtherBuildingsKeepTheirDistance()
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        var plan = SettlementPlan.Create(world, world.StartingSite);
        var candidates = world.Tiles.Where(x => x.Buildable && plan.DistrictOf(x.Coordinate) is not null).ToArray();
        var placed = new List<SettlementPlanner.Placed>();
        TileCoordinate Place(StructureType type)
        {
            var site = Assert.IsType<TileCoordinate>(SettlementPlanner.Choose(plan, SettlementPlanner.DistrictFor(type), SettlementPlanner.FootprintFor(type),
                candidates.Where(x => placed.All(p => p.Location != x.Coordinate)), placed, _ => false));
            placed.Add(new(site, SettlementPlanner.FootprintFor(type)));
            return site;
        }

        var storage = Enumerable.Range(0, 6).Select(_ => Place(StructureType.Stockpile)).ToArray();
        var homes = Enumerable.Range(0, 6).Select(_ => Place(StructureType.Shelter)).ToArray();

        // Stockpiles fill neighbouring plots in the storage district, so the yard stays compact.
        Assert.All(storage, x => Assert.Equal(PlanDistrict.Storage, plan.DistrictOf(x)));
        Assert.All(storage, x => Assert.Contains(storage, y => y != x && SettlementPlan.Chebyshev(x, y) == 1));
        Assert.True(storage.Max(x => storage.Max(y => SettlementPlan.Chebyshev(x, y))) <= 3);
        // Homes gather in their own district, never touch the yard, and keep a walkable gap between them.
        Assert.All(homes, x => Assert.Equal(PlanDistrict.Homes, plan.DistrictOf(x)));
        Assert.All(homes, x => Assert.All(storage, y => Assert.True(SettlementPlan.Chebyshev(x, y) >= 2)));
        Assert.All(homes, x => Assert.All(homes.Where(y => y != x), y => Assert.True(SettlementPlan.Chebyshev(x, y) >= 2)));
        Assert.All(storage.Concat(homes), x => Assert.True(SettlementPlan.Chebyshev(x, world.StartingSite) >= 2));
    }

    [Fact]
    public void NewBuildingsLeaveWornPathsOpenWhenTheyCan()
    {
        var world = new WorldGenerator().Generate(new WorldSeed(42));
        var plan = SettlementPlan.Create(world, world.StartingSite);
        var candidates = world.Tiles.Where(x => x.Buildable && plan.DistrictOf(x.Coordinate) is not null).ToArray();
        var unconstrained = Assert.IsType<TileCoordinate>(SettlementPlanner.Choose(plan, PlanDistrict.Homes, SettlementPlanner.Footprint.Solitary, candidates, [], _ => false));
        var avoiding = Assert.IsType<TileCoordinate>(SettlementPlanner.Choose(plan, PlanDistrict.Homes, SettlementPlanner.Footprint.Solitary, candidates, [], x => x == unconstrained));
        Assert.NotEqual(unconstrained, avoiding);
        // With nowhere else to go, a path tile is still better than no building at all.
        var only = candidates.Single(x => x.Coordinate == unconstrained);
        Assert.Equal(unconstrained, SettlementPlanner.Choose(plan, PlanDistrict.Homes, SettlementPlanner.Footprint.Solitary, [only], [], _ => true));
    }

    [Theory]
    [InlineData(4, 0, 800, 1000, StructureType.Stockpile)]
    [InlineData(7, 1, 850, 1000, StructureType.Stockpile)]
    [InlineData(8, 0, 850, 1000, StructureType.Storehouse)]
    [InlineData(3, 2, 850, 1000, StructureType.Storehouse)]
    [InlineData(30, 0, 940, 1000, StructureType.Storehouse)]
    [InlineData(30, 0, 950, 1000, StructureType.Stockpile)]
    [InlineData(30, 3, 990, 1000, StructureType.Stockpile)]
    [InlineData(30, 4, 990, 1000, StructureType.Storehouse)]
    [InlineData(3, 5, 960, 1000, StructureType.Storehouse)]
    public void StorageGrowsThroughStorehousesSupplementedByQuickStockpiles(int population, int stockpiles, long used, long capacity, StructureType expected) =>
        Assert.Equal(expected, SimulationEngine.PlannedStorageExpansion(population, stockpiles, used, capacity));

    [Fact]
    public void AStorehouseHoldsThreeStockpilesForTheMaterialsOfTwo()
    {
        Structure Complete(StructureType type, int wood, int stone, int work) =>
            new(new StructureId(1), type, new TileCoordinate(0, 0), 0, wood, stone, work) { Status = StructureStatus.Complete, CompletedMinute = 1, DeliveredWood = wood, DeliveredStone = stone, CompletedWork = work };
        var stockpile = Complete(StructureType.Stockpile, StructureDefinitions.StockpileRequiredWood, StructureDefinitions.StockpileRequiredStone, StructureDefinitions.StockpileRequiredWork).Validate();
        var storehouse = Complete(StructureType.Storehouse, StructureDefinitions.StorehouseRequiredWood, StructureDefinitions.StorehouseRequiredStone, StructureDefinitions.StorehouseRequiredWork).Validate();

        Assert.Equal(3 * CitizenSimulationRules.GeneralStorageBonus(stockpile), CitizenSimulationRules.GeneralStorageBonus(storehouse));
        Assert.Equal(2 * stockpile.RequiredWood, storehouse.RequiredWood);
        Assert.Equal(2 * stockpile.RequiredStone, storehouse.RequiredStone);
        Assert.Equal(2 * stockpile.RequiredWork, storehouse.RequiredWork);
        Assert.Equal(CitizenSimulationRules.StockpileStorageBonus + CitizenSimulationRules.StorehouseStorageBonus, CitizenSimulationRules.GeneralStorageOf([stockpile, storehouse]));
        storehouse.Status = StructureStatus.UnderConstruction;
        Assert.Equal(0, CitizenSimulationRules.GeneralStorageBonus(storehouse));
    }

    [Fact]
    public void PlannedWorldsRoundTripAndOlderRulesStillPlaceTheOldWay()
    {
        var planned = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: Planned);
        var roads = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion);
        planned.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay * 30));
        roads.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay * 30));

        var reloaded = new SimulationEngine(planned.CreatePersistenceSnapshot());
        Assert.Equal(Planned, reloaded.SimulationRulesVersion);
        Assert.Equal(planned.SettlementFingerprint, reloaded.SettlementFingerprint);
        // The first building already differs: m15 builds on the nearest free tile, m16 in the homes district.
        var plannedFirst = planned.Structures.OrderBy(x => x.Id.Value).First();
        var roadsFirst = roads.Structures.OrderBy(x => x.Id.Value).First();
        Assert.Equal(StructureType.Shelter, plannedFirst.Type);
        Assert.Equal(PlanDistrict.Homes, planned.PlanFor(1).DistrictOf(plannedFirst.Location));
        Assert.NotEqual(roadsFirst.Location, plannedFirst.Location);
    }
}
