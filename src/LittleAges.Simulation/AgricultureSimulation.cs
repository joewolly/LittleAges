using System.Security.Cryptography;
using System.Text;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private const int DaughterAnnualFoodDemandPerResident = 2333;
    private readonly SortedDictionary<long, FarmCrop> _farms = [];
    private readonly List<HarvestRecord> _harvests = [];
    private long _agricultureSeasonIndex;
    public static bool AgricultureSystemsEnabled(string rules) => rules is AgricultureSimulationRulesVersion or BarterSimulationRulesVersion or SpacedSimulationRulesVersion || UnifiedSimulationRulesEnabled(rules);
    public int GranaryCapacity => GranaryCapacityAt(1);
    private int GranaryCapacityAt(long settlementId) => StructuresAt(settlementId).Count(s => s.Type == StructureType.Granary && s.Status == StructureStatus.Complete) * AgricultureRules.GranaryFoodCapacity;
    public int WinterFoodTarget => checked(LivingPopulation * 810);
    public AgricultureState? CaptureAgriculture() => AgricultureSystemsEnabled(SimulationRulesVersion)
        ? new(1, _agricultureSeasonIndex, Array.AsReadOnly(_farms.Values.ToArray()), Array.AsReadOnly(_harvests.ToArray())) : null;
    public string ComputeAgricultureFingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CaptureAgriculture()?.ToCanonicalJson() ?? "null"))).ToLowerInvariant();

    private void LoadAgriculture(AgricultureState? state)
    {
        if (state is null) return;
        _agricultureSeasonIndex = state.SeasonIndex;
        foreach (var farm in state.Farms) _farms.Add(farm.StructureId, farm);
        _harvests.AddRange(state.Harvests);
    }

    private int AvailableStorage(ResourceType type)
        => AvailableStorage(type, 1);

    private int AvailableStorage(ResourceType type, long settlementId)
    {
        var settlement = SettlementFor(settlementId);
        var owned = EconomyEnabled ? OwnedStoredGoodsAt(settlementId) : new Goods();
        var totalFree = checked((int)Math.Max(0, StorageCapacityAt(settlementId) - settlement.StorageUsed - LivingStoredQuantityAt(settlementId) - owned.Total - (EconomyEnabled ? TradeCargoReservedAt(settlementId) : 0)));
        if (type == ResourceType.Food) return totalFree;

        var granaryCapacity = GranaryCapacityAt(settlementId);
        var additionalFoodReserve = MigrationSystemsEnabled(SimulationRulesVersion) &&
            settlementId == MigrationDaughterSettlementState.SettlementId
            ? Math.Max(0L, checked((long)PopulationAt(settlementId) * DaughterFoodReservePerResident) - granaryCapacity)
            : 0L;
        var nonFoodFree = Math.Max(0, checked((int)(StorageCapacityAt(settlementId) - granaryCapacity -
            settlement.WoodStored - settlement.StoneStored - owned.Wood - owned.Stone -
            (EconomyEnabled ? TradeNonFoodReservedAt(settlementId) : 0) - additionalFoodReserve)));
        return Math.Min(totalFree, nonFoodFree);
    }

    private void AdvanceAgricultureSeason()
    {
        var seasonIndex = CurrentMinute.Value / (WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay);
        if (seasonIndex == _agricultureSeasonIndex) return;
        _agricultureSeasonIndex = seasonIndex;
        var season = CurrentMinute.ToCalendar().Season;
        foreach (var farm in _farms.Values.ToArray())
        {
            var next = farm;
            if (season == WorldSeason.Spring)
                next = new(farm.StructureId, seasonIndex / 4, CropStage.Fallow, 0, 0, 0, 0, 0);
            else if (season == WorldSeason.Summer) next = farm with { Stage = CropStage.Growing };
            else if (season == WorldSeason.Autumn)
            {
                var tile = World.GetTile(_structures[farm.StructureId].Location);
                var yield = AgricultureRules.LaborYield(tile, farm.PlantingWork, farm.TendingWork);
                next = farm with { Stage = CropStage.Harvest, Yield = yield, Remaining = yield };
            }
            else
            {
                _harvests.Add(new(farm.StructureId, farm.Year, farm.PlantingWork, farm.TendingWork, farm.Yield, farm.Harvested, farm.Remaining));
                next = farm with { Stage = CropStage.Dormant, Remaining = 0 };
            }
            _farms[farm.StructureId] = next;
        }
    }

    private StructureType? SelectAgricultureDemand()
        => SelectAgricultureDemand(1);

    private StructureType? SelectAgricultureDemand(long settlementId)
    {
        var population = PopulationAt(settlementId);
        if (population == 0) return null;
        if (GranaryCapacityAt(settlementId) < checked(population * 810)) return StructureType.Granary;
        var expectedProduction = StructuresAt(settlementId).Where(s => s.Type == StructureType.Farm)
            .Sum(s => (long)AgricultureRules.PotentialYield(World.GetTile(s.Location)));
        // Preserve the established origin target while scaling daughter farms to observed annual consumption.
        var annualFoodTarget = MigrationSystemsEnabled(SimulationRulesVersion) &&
            settlementId == MigrationDaughterSettlementState.SettlementId
            ? population * (long)DaughterAnnualFoodDemandPerResident
            : population * 1080L;
        return expectedProduction < annualFoodTarget && SelectFarmSite(settlementId) is not null
            ? StructureType.Farm : null;
    }

    private TileCoordinate? SelectFarmSite()
        => SelectFarmSite(1);

    private TileCoordinate? SelectFarmSite(long settlementId)
    {
        var occupied = _structures.Values.Select(s => s.Location)
            .Concat(MigrationSystemsEnabled(SimulationRulesVersion) ? _living!.Facilities.Select(x => x.Location) : [])
            .Concat(MigrationSystemsEnabled(SimulationRulesVersion) ? _living!.Fields.Select(x => x.Location) : [])
            .ToHashSet();
        var site = SiteLocation(settlementId);
        var costs = GetTravelCostsCached(site);
        var candidates = World.Tiles.Where(t => AgricultureRules.Suitable(t) && t.Coordinate != site &&
                !occupied.Contains(t.Coordinate) && World.GetResources(t.Coordinate).Count == 0 && costs.ContainsKey(t.Coordinate) &&
                (!MigrationSystemsEnabled(SimulationRulesVersion) || SiteIdForLocation(t.Coordinate) == settlementId))
            .OrderBy(t => costs[t.Coordinate]).ThenByDescending(AgricultureRules.PotentialYield)
            .ThenBy(t => t.Coordinate.Y).ThenBy(t => t.Coordinate.X);
        if (SimulationRulesVersion != SpacedSimulationRulesVersion && !UnifiedSimulationRulesEnabled(SimulationRulesVersion)) return candidates.Select(t => (TileCoordinate?)t.Coordinate).FirstOrDefault();
        var excluded = MigrationSystemsEnabled(SimulationRulesVersion) ? SpacedConstructionExclusions(settlementId) : SpacedConstructionExclusions();
        return candidates.Where(t => !excluded.Contains(t.Coordinate)).Select(t => (TileCoordinate?)t.Coordinate).FirstOrDefault()
            ?? candidates.Select(t => (TileCoordinate?)t.Coordinate).FirstOrDefault();
    }

    private Structure? SelectFarmTarget(Citizen citizen, CitizenAction action)
    {
        if (!AgricultureSystemsEnabled(SimulationRulesVersion) || citizen.AgeYears(CurrentMinute) < 13) return null;
        var season = CurrentMinute.ToCalendar().Season;
        if (season == WorldSeason.Winter || action == CitizenAction.HaulHarvest && season != WorldSeason.Autumn || action == CitizenAction.WorkFarm && season == WorldSeason.Autumn) return null;
        var costs = GetTravelCostsCached(citizen.Location);
        var siteId = SiteIdForCitizen(citizen);
        var incoming = CitizensAt(siteId).Where(c => c.IsAlive && c.Id != citizen.Id).Sum(c =>
            (long)c.CarriedResourceQuantity + (c.CurrentAction == CitizenAction.HaulHarvest && c.CarriedResourceQuantity == 0 &&
                !(UnifiedSimulationRulesEnabled(SimulationRulesVersion) && _living!.Orders.Any(o => o.Kind == LivingWorkKind.Harvest && o.CitizenId == c.Id.Value && o.CargoInTransit))
                ? HarvestLoadCapacity(siteId) : 0));
        foreach (var farm in FarmsAt(siteId))
        {
            var structure = _structures[farm.StructureId];
            if (structure.Status != StructureStatus.Complete || !costs.ContainsKey(structure.Location)) continue;
            var workers = _citizens.Values.Count(c => c.IsAlive && (!MigrationSystemsEnabled(SimulationRulesVersion) || SiteIdForCitizen(c) == siteId) && c.CurrentAction == action && c.TargetStructureId == structure.Id && c.CarriedResourceQuantity == 0);
            if (action == CitizenAction.HaulHarvest)
            {
                var loadCapacity = HarvestLoadCapacity(siteId);
                if (farm.Remaining > workers * loadCapacity && AvailableStorage(ResourceType.Food, siteId) >= incoming + loadCapacity) return structure;
            }
            else if (season == WorldSeason.Spring && farm.PlantingWork + workers * AgricultureRules.WorkPerShift < AgricultureRules.PlantingWork ||
                season == WorldSeason.Summer && farm.PlantingWork > 0 && farm.TendingWork + workers * AgricultureRules.WorkPerShift < AgricultureRules.TendingWork) return structure;
        }
        return null;
    }

    private void CompleteFarmWork(Citizen citizen)
    {
        if (citizen.TargetStructureId is not { } id || !_farms.TryGetValue(id.Value, out var farm)) return;
        var season = CurrentMinute.ToCalendar().Season;
        if (citizen.CurrentAction == CitizenAction.WorkFarm)
        {
            var work = FarmWorkPerShift(citizen);
            if (season == WorldSeason.Spring) _farms[id.Value] = farm with { Stage = CropStage.Planted, PlantingWork = Math.Min(AgricultureRules.PlantingWork, farm.PlantingWork + work) };
            else if (season == WorldSeason.Summer && farm.PlantingWork > 0) _farms[id.Value] = farm with { Stage = CropStage.Growing, TendingWork = Math.Min(AgricultureRules.TendingWork, farm.TendingWork + work) };
            return;
        }
        if (season != WorldSeason.Autumn) return;
        var siteId = SiteIdForCitizen(citizen);
        var amount = Math.Min(farm.Remaining, Math.Min(HarvestLoadCapacity(siteId), AvailableStorage(ResourceType.Food, siteId)));
        var site = SiteLocation(siteId);
        if (amount <= 0 || FindPathCached(citizen.Location, site) is null) return;
        _farms[id.Value] = farm with { Remaining = farm.Remaining - amount, Harvested = checked(farm.Harvested + amount) };
        var grain = UnifiedSimulationRulesEnabled(SimulationRulesVersion)
            ? checked((int)((farm.Harvested + amount) / 10 - farm.Harvested / 10))
            : 0;
        var food = amount - grain;
        if (UnifiedSimulationRulesEnabled(SimulationRulesVersion))
        {
            if (grain > 0)
            {
                var now = CurrentMinute.Value;
                var grainOrder = new LivingWorkOrder
                {
                    Id = _living!.NextId++,
                    Kind = LivingWorkKind.Harvest,
                    Location = _structures[id.Value].Location,
                    SupplyLocation = citizen.Location,
                    SubjectId = id.Value,
                    CitizenId = citizen.Id.Value,
                    CreatedMinute = now,
                    ClaimedMinute = now,
                    Priority = 5000,
                    RequiredWork = LivingWorkDefinitions.Work(LivingWorkKind.Harvest),
                    WorkDone = LivingWorkDefinitions.Work(LivingWorkKind.Harvest),
                    Phase = LivingWorkPhase.Deliver,
                    Reserved = true,
                    Produced = true,
                    CargoInTransit = true,
                    Cargo = [new(LivingGood.Grain, grain)]
                };
                _living!.Orders.Add(grainOrder);
                RecordMigrationOwner(MigrationEntityKind.WorkOrder, grainOrder.Id, siteId);
            }
            _living!.FarmFoodHarvested = checked(_living.FarmFoodHarvested + food);
            _living.CommunalGrainHarvested = checked(_living.CommunalGrainHarvested + grain);
        }
        RecordProduction(citizen, ResourceType.Food, food);
        citizen.CarriedResourceType = ResourceType.Food;
        citizen.CarriedResourceQuantity = food;
        BeginTravel(citizen, CitizenAction.HaulHarvest, site, null, CitizenActionPhase.ReturnToStockpile, id);
    }

    private int FarmWorkPerShift(Citizen citizen)
    {
        if (!UnifiedSimulationRulesEnabled(SimulationRulesVersion)) return AgricultureRules.WorkPerShift;
        var weatherAdjustment = _living!.Weather switch
        {
            LivingWeatherKind.Rain => 20,
            LivingWeatherKind.Drought or LivingWeatherKind.ColdSpell => -20,
            _ => 0
        };
        var cultivationBonus = Knows(citizen, LivingTechnique.Cultivation) ? 20 : 0;
        return Math.Max(1, AgricultureRules.WorkPerShift + weatherAdjustment + cultivationBonus);
    }

    private int HarvestLoadCapacity(long settlementId) =>
        MigrationSystemsEnabled(SimulationRulesVersion) &&
        settlementId == MigrationDaughterSettlementState.SettlementId
            ? 120
            : AgricultureRules.HarvestPerShift;
}
