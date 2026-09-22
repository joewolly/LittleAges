using System.Security.Cryptography;
using System.Text;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private readonly SortedDictionary<long, FarmCrop> _farms = [];
    private readonly List<HarvestRecord> _harvests = [];
    private long _agricultureSeasonIndex;
    public static bool AgricultureSystemsEnabled(string rules) => rules is AgricultureSimulationRulesVersion or BarterSimulationRulesVersion or SpacedSimulationRulesVersion;
    public int GranaryCapacity => _structures.Values.Count(s => s.Type == StructureType.Granary && s.Status == StructureStatus.Complete) * AgricultureRules.GranaryFoodCapacity;
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
    {
        var owned = EconomyEnabled ? OwnedStoredGoods : new Goods();
        var totalFree = checked((int)Math.Max(0, StorageCapacity - Settlement.StorageUsed - LivingStoredQuantity - owned.Total - (EconomyEnabled ? TradeCargoReserved : 0)));
        return type == ResourceType.Food ? totalFree : Math.Min(totalFree,
            Math.Max(0, checked((int)(StorageCapacity - GranaryCapacity - Settlement.WoodStored - Settlement.StoneStored - owned.Wood - owned.Stone - (EconomyEnabled ? TradeNonFoodReserved : 0)))));
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
    {
        if (LivingPopulation == 0) return null;
        if (GranaryCapacity < WinterFoodTarget) return StructureType.Granary;
        var expectedProduction = _structures.Values.Where(s => s.Type == StructureType.Farm)
            .Sum(s => (long)AgricultureRules.PotentialYield(World.GetTile(s.Location)));
        // Target half the estimated annual consumption from crops; gathering remains useful.
        return expectedProduction < LivingPopulation * 1080L && SelectFarmSite() is not null ? StructureType.Farm : null;
    }

    private TileCoordinate? SelectFarmSite()
    {
        var occupied = _structures.Values.Select(s => s.Location).ToHashSet();
        var costs = GetTravelCostsCached(World.StartingSite);
        var candidates = World.Tiles.Where(t => AgricultureRules.Suitable(t) && t.Coordinate != World.StartingSite &&
                !occupied.Contains(t.Coordinate) && World.GetResources(t.Coordinate).Count == 0 && costs.ContainsKey(t.Coordinate))
            .OrderBy(t => costs[t.Coordinate]).ThenByDescending(AgricultureRules.PotentialYield)
            .ThenBy(t => t.Coordinate.Y).ThenBy(t => t.Coordinate.X);
        if (SimulationRulesVersion != SpacedSimulationRulesVersion) return candidates.Select(t => (TileCoordinate?)t.Coordinate).FirstOrDefault();
        var excluded = SpacedConstructionExclusions();
        return candidates.Where(t => !excluded.Contains(t.Coordinate)).Select(t => (TileCoordinate?)t.Coordinate).FirstOrDefault()
            ?? candidates.Select(t => (TileCoordinate?)t.Coordinate).FirstOrDefault();
    }

    private Structure? SelectFarmTarget(Citizen citizen, CitizenAction action)
    {
        if (!AgricultureSystemsEnabled(SimulationRulesVersion) || citizen.AgeYears(CurrentMinute) < 13) return null;
        var season = CurrentMinute.ToCalendar().Season;
        if (season == WorldSeason.Winter || action == CitizenAction.HaulHarvest && season != WorldSeason.Autumn || action == CitizenAction.WorkFarm && season == WorldSeason.Autumn) return null;
        var costs = GetTravelCostsCached(citizen.Location);
        var incoming = _citizens.Values.Where(c => c.IsAlive && c.Id != citizen.Id).Sum(c =>
            (long)c.CarriedResourceQuantity + (c.CurrentAction == CitizenAction.HaulHarvest && c.CarriedResourceQuantity == 0 ? AgricultureRules.HarvestPerShift : 0));
        foreach (var farm in _farms.Values)
        {
            var structure = _structures[farm.StructureId];
            if (structure.Status != StructureStatus.Complete || !costs.ContainsKey(structure.Location)) continue;
            var workers = _citizens.Values.Count(c => c.IsAlive && c.CurrentAction == action && c.TargetStructureId == structure.Id && c.CarriedResourceQuantity == 0);
            if (action == CitizenAction.HaulHarvest)
            {
                if (farm.Remaining > workers * AgricultureRules.HarvestPerShift && AvailableStorage(ResourceType.Food) >= incoming + AgricultureRules.HarvestPerShift) return structure;
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
            if (season == WorldSeason.Spring) _farms[id.Value] = farm with { Stage = CropStage.Planted, PlantingWork = Math.Min(AgricultureRules.PlantingWork, farm.PlantingWork + AgricultureRules.WorkPerShift) };
            else if (season == WorldSeason.Summer && farm.PlantingWork > 0) _farms[id.Value] = farm with { Stage = CropStage.Growing, TendingWork = Math.Min(AgricultureRules.TendingWork, farm.TendingWork + AgricultureRules.WorkPerShift) };
            return;
        }
        if (season != WorldSeason.Autumn) return;
        var amount = Math.Min(farm.Remaining, Math.Min(AgricultureRules.HarvestPerShift, AvailableStorage(ResourceType.Food)));
        if (amount <= 0 || FindPathCached(citizen.Location, World.StartingSite) is null) return;
        _farms[id.Value] = farm with { Remaining = farm.Remaining - amount, Harvested = checked(farm.Harvested + amount) };
        RecordProduction(citizen, ResourceType.Food, amount);
        citizen.CarriedResourceType = ResourceType.Food;
        citizen.CarriedResourceQuantity = amount;
        BeginTravel(citizen, CitizenAction.HaulHarvest, World.StartingSite, null, CitizenActionPhase.ReturnToStockpile, id);
    }
}
