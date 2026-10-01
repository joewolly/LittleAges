using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>M16 planned layout: routes every new building through its settlement's plan.</summary>
public sealed partial class SimulationEngine
{
    // Derived from immutable geography and the (fixed) site location; never checkpointed.
    private readonly Dictionary<TileCoordinate, SettlementPlan> _settlementPlans = new();

    internal SettlementPlan PlanFor(long settlementId)
    {
        var site = SiteLocation(settlementId);
        if (!_settlementPlans.TryGetValue(site, out var plan)) _settlementPlans[site] = plan = SettlementPlan.Create(World, site);
        return plan;
    }

    private List<SettlementPlanner.Placed> PlacedFootprints()
    {
        var placed = _structures.Values.Select(x => new SettlementPlanner.Placed(x.Location, SettlementPlanner.FootprintFor(x.Type))).ToList();
        if (_living is not { } living) return placed;
        placed.AddRange(living.Facilities.Select(x => new SettlementPlanner.Placed(x.Location, SettlementPlanner.Footprint.Solitary)));
        placed.AddRange(living.Fields.Select(x => new SettlementPlanner.Placed(x.Location, SettlementPlanner.Footprint.Field)));
        foreach (var order in living.Orders)
            if (order.Kind == LivingWorkKind.EstablishField) placed.Add(new(order.Location, SettlementPlanner.Footprint.Field));
            else if (order.Kind is LivingWorkKind.BuildHearth or LivingWorkKind.BuildLoom or LivingWorkKind.BuildCareHouse) placed.Add(new(order.Location, SettlementPlanner.Footprint.Solitary));
        return placed;
    }

    // Worn trails and roads are the village's streets; new buildings leave them open when they can.
    private bool OnWornPath(TileCoordinate tile) => Roads is { } roads && roads.GradeAt(tile) >= RoadGrade.Trail;

    private TileCoordinate? SelectPlannedSite(long settlementId, PlanDistrict district, SettlementPlanner.Footprint footprint,
        IEnumerable<WorldTile> candidates, Func<WorldTile, int>? preference = null) =>
        SettlementPlanner.Choose(PlanFor(settlementId), district, footprint, candidates, PlacedFootprints(), OnWornPath, preference);

    /// <summary>The first hearth warms the core; later ones join the homes they cook for.</summary>
    private PlanDistrict PlannedHearthDistrict(long settlementId)
    {
        var plan = PlanFor(settlementId);
        var hearths = FacilitiesAt(settlementId).Where(x => x.Kind == LivingFacilityKind.Hearth).Select(x => x.Location)
            .Concat(OrdersAt(settlementId).Where(x => x.Kind == LivingWorkKind.BuildHearth).Select(x => x.Location));
        return hearths.Any(x => plan.DistrictOf(x) == PlanDistrict.Core) ? PlanDistrict.Homes : PlanDistrict.Core;
    }

    /// <summary>
    /// Storage expansion for planned settlements: a new village starts with quick stockpiles, then grows
    /// through storehouses. A nearly full store may add a quick stockpile to supplement, up to a small limit,
    /// so a settlement that is always full does not fill up with stockpiles again.
    /// </summary>
    internal static StructureType PlannedStorageExpansion(int population, int stockpiles, long used, long capacity)
    {
        var established = population >= CitizenSimulationRules.StorehouseMinimumPopulation || stockpiles >= CitizenSimulationRules.StorehouseAfterStockpiles;
        if (!established) return StructureType.Stockpile;
        var urgent = used * 100L >= capacity * CitizenSimulationRules.StorehouseUrgentFillPercent;
        return urgent && stockpiles < CitizenSimulationRules.SupplementaryStockpileLimit ? StructureType.Stockpile : StructureType.Storehouse;
    }
}
