using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private SettlementState? _daughterSettlementRuntime;
    private List<LivingStock>? _daughterLivingGoodsRuntime;

    /// <summary>Creates the stable, immutable M14 projection for the core observer.</summary>
    public MigrationReadSnapshot CreateMigrationReadSnapshot()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion))
            throw new InvalidOperationException("Migration read snapshots are available only for M14 worlds.");
        return MigrationValidation.CreateReadSnapshot(CreatePersistenceSnapshot());
    }

    private MigrationWorldState? CaptureMigrationState()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return null;
        if (_living is null) throw new InvalidDataException("M14 requires Living state.");

        SyncMigrationDaughterStocks();
        var prior = _migrationState;
        var priorCitizens = prior?.CitizenResidences.ToDictionary(x => x.EntityId, x => x.SettlementId) ?? new Dictionary<long, long>();
        var priorHouseholds = prior?.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId) ?? new Dictionary<long, long>();
        var priorStructures = prior?.StructureOwners.ToDictionary(x => x.EntityId, x => x.SettlementId) ?? new Dictionary<long, long>();
        var priorFacilities = prior?.FacilityOwners.ToDictionary(x => x.EntityId, x => x.SettlementId) ?? new Dictionary<long, long>();
        var priorOrders = prior?.WorkOrderOwners.ToDictionary(x => x.EntityId, x => x.SettlementId) ?? new Dictionary<long, long>();

        var citizenResidence = new List<MigrationEntityResidence>();
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value))
        {
            var residence = priorCitizens.TryGetValue(citizen.Id.Value, out var known) ? known :
                citizen.HouseholdId is { } household && priorHouseholds.TryGetValue(household.Value, out var householdSite) ? householdSite :
                citizen.ParentAId is { } parentA && priorCitizens.TryGetValue(parentA.Value, out var parentASite) ? parentASite :
                citizen.ParentBId is { } parentB && priorCitizens.TryGetValue(parentB.Value, out var parentBSite) ? parentBSite : 1;
            citizenResidence.Add(new MigrationEntityResidence(citizen.Id.Value, residence));
        }
        var citizenSites = citizenResidence.ToDictionary(x => x.EntityId, x => x.SettlementId);

        var householdResidence = _households.Values.OrderBy(x => x.Id.Value).Select(household =>
        {
            if (priorHouseholds.TryGetValue(household.Id.Value, out var known))
                return new MigrationEntityResidence(household.Id.Value, known);
            var memberSites = _citizens.Values.Where(x => x.HouseholdId == household.Id)
                .Select(x => citizenSites[x.Id.Value]).Distinct().Order().ToArray();
            return new MigrationEntityResidence(household.Id.Value, memberSites.Length == 1 ? memberSites[0] : 1);
        }).ToArray();
        var householdSites = householdResidence.ToDictionary(x => x.EntityId, x => x.SettlementId);

        var structureResidence = _structures.Values.OrderBy(x => x.Id.Value).Select(structure =>
        {
            if (priorStructures.TryGetValue(structure.Id.Value, out var known))
                return new MigrationEntityResidence(structure.Id.Value, known);
            var contributorSites = _structureContributions.Values.Where(x => x.StructureId == structure.Id)
                .Select(x => citizenSites.GetValueOrDefault(x.CitizenId.Value, 1)).Distinct().Order().ToArray();
            return new MigrationEntityResidence(structure.Id.Value, contributorSites.Length == 1 ? contributorSites[0] : 1);
        }).ToArray();
        var structureSites = structureResidence.ToDictionary(x => x.EntityId, x => x.SettlementId);

        var facilityResidence = _living.Facilities.OrderBy(x => x.Id).Select(facility =>
            new MigrationEntityResidence(facility.Id, priorFacilities.GetValueOrDefault(facility.Id, 1))).ToArray();
        var facilitySites = facilityResidence.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var workOrderResidence = _living.Orders.OrderBy(x => x.Id).Select(order =>
        {
            if (priorOrders.TryGetValue(order.Id, out var known)) return new MigrationEntityResidence(order.Id, known);
            if (order.CitizenId is { } citizenId && citizenSites.TryGetValue(citizenId, out var workerSite))
                return new MigrationEntityResidence(order.Id, workerSite);
            if (order.SubjectId is { } subjectId && facilitySites.TryGetValue(subjectId, out var facilitySite))
                return new MigrationEntityResidence(order.Id, facilitySite);
            if (order.SubjectId is { } structureId && structureSites.TryGetValue(structureId, out var structureSite))
                return new MigrationEntityResidence(order.Id, structureSite);
            return new MigrationEntityResidence(order.Id, 1);
        }).ToArray();

        _migrationState = new MigrationWorldState(1, citizenResidence, householdResidence, structureResidence,
            facilityResidence, workOrderResidence, prior?.DaughterSettlement, prior?.InTransitParties,
            prior?.FoundingPressure, prior?.LastRelocations, prior?.LastVisitAttemptYear);
        return _migrationState;
    }

    private void InitializeMigrationRuntime()
    {
        var daughter = _migrationState?.DaughterSettlement;
        _daughterSettlementRuntime = daughter?.CommunalStock.ToSettlementState();
        _daughterLivingGoodsRuntime = daughter?.LivingGoods.ToList();
    }

    private void SyncMigrationDaughterStocks()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) || _migrationState?.DaughterSettlement is not { } daughter) return;
        var stock = _daughterSettlementRuntime ?? daughter.CommunalStock.ToSettlementState();
        var goods = _daughterLivingGoodsRuntime ?? daughter.LivingGoods.ToList();
        _migrationState = new MigrationWorldState(_migrationState.Version, _migrationState.CitizenResidences,
            _migrationState.HouseholdResidences, _migrationState.StructureOwners, _migrationState.FacilityOwners,
            _migrationState.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughter.Site, MigrationSettlementStockState.From(stock), goods),
            _migrationState.InTransitParties, _migrationState.FoundingPressure, _migrationState.LastRelocations,
            _migrationState.LastVisitAttemptYear);
    }

    private long[] MigrationSettlementIds => !MigrationSystemsEnabled(SimulationRulesVersion) || _migrationState?.DaughterSettlement is null
        ? [1]
        : [1, MigrationDaughterSettlementState.SettlementId];

    private SettlementState SettlementFor(long settlementId)
    {
        if (settlementId == 1) return Settlement;
        if (MigrationSystemsEnabled(SimulationRulesVersion) && settlementId == MigrationDaughterSettlementState.SettlementId && _migrationState?.DaughterSettlement is { } daughter)
            return _daughterSettlementRuntime ??= daughter.CommunalStock.ToSettlementState();
        throw new ArgumentOutOfRangeException(nameof(settlementId), settlementId, "The settlement does not exist in this world.");
    }

    private SettlementState SettlementFor(Citizen citizen) => SettlementFor(SiteIdForCitizen(citizen));

    private Structure? ActiveConstructionProjectFor(long settlementId) => StructuresAt(settlementId)
        .Where(x => x.Status == StructureStatus.UnderConstruction).OrderBy(x => x.Id.Value).SingleOrDefault();

    private IReadOnlyList<LivingStock> LivingGoodsFor(long settlementId)
    {
        if (settlementId == 1) return _living is null ? Array.Empty<LivingStock>() : _living.Stock;
        if (MigrationSystemsEnabled(SimulationRulesVersion) && settlementId == MigrationDaughterSettlementState.SettlementId && _migrationState?.DaughterSettlement is { } daughter)
            return _daughterLivingGoodsRuntime ??= daughter.LivingGoods.ToList();
        throw new ArgumentOutOfRangeException(nameof(settlementId), settlementId, "The settlement does not exist in this world.");
    }

    private int GoodAt(long settlementId, LivingGood good) => LivingGoodsFor(settlementId).Single(x => x.Good == good).Quantity;

    private void ChangeGoodAt(long settlementId, LivingGood good, int quantity)
    {
        if (settlementId == 1)
        {
            if (_living is null) throw new InvalidOperationException("Living state is unavailable.");
            var index = _living.Stock.FindIndex(x => x.Good == good);
            var value = checked(_living.Stock[index].Quantity + quantity);
            if (value < 0) throw new InvalidOperationException("Living goods cannot become negative.");
            _living.Stock[index] = new(good, value);
            return;
        }

        var goods = _daughterLivingGoodsRuntime ?? LivingGoodsFor(settlementId).ToList();
        var stockIndex = goods.FindIndex(x => x.Good == good);
        var next = checked(goods[stockIndex].Quantity + quantity);
        if (next < 0) throw new InvalidOperationException("Living goods cannot become negative.");
        goods[stockIndex] = new(good, next);
        _daughterLivingGoodsRuntime = goods;
    }

    private long SiteIdForCitizen(Citizen citizen)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return 1;
        var known = ResidenceFor(_migrationState?.CitizenResidences, citizen.Id.Value);
        if (known is not null) return known.SettlementId;
        if (citizen.HouseholdId is { } household && ResidenceFor(_migrationState?.HouseholdResidences, household.Value) is { } householdResidence)
            return householdResidence.SettlementId;
        foreach (var parentId in new[] { citizen.ParentAId, citizen.ParentBId })
            if (parentId is { } parent && _citizens.TryGetValue(parent.Value, out var parentCitizen)) return SiteIdForCitizen(parentCitizen);
        return SiteIdForLocation(citizen.Location);
    }

    private long SiteIdForCitizen(long citizenId) => _citizens.TryGetValue(citizenId, out var citizen)
        ? SiteIdForCitizen(citizen)
        : ResidenceFor(_migrationState?.CitizenResidences, citizenId)?.SettlementId ?? 1;

    private long SiteIdForHousehold(long householdId)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return 1;
        var known = ResidenceFor(_migrationState?.HouseholdResidences, householdId);
        if (known is not null) return known.SettlementId;
        var memberSites = _citizens.Values.Where(x => x.HouseholdId?.Value == householdId)
            .Select(SiteIdForCitizen).Distinct().Order().ToArray();
        return memberSites.Length == 1 ? memberSites[0] : 1;
    }

    private long SiteIdForStructure(long structureId)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return 1;
        var known = ResidenceFor(_migrationState?.StructureOwners, structureId);
        if (known is not null) return known.SettlementId;
        var contributors = _structureContributions.Values.Where(x => x.StructureId.Value == structureId)
            .Select(x => SiteIdForCitizen(x.CitizenId.Value)).Distinct().Order().ToArray();
        if (contributors.Length == 1) return contributors[0];
        return _structures.TryGetValue(structureId, out var structure) ? SiteIdForLocation(structure.Location) : 1;
    }

    private long SiteIdForFacility(LivingFacility facility)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return 1;
        var known = ResidenceFor(_migrationState?.FacilityOwners, facility.Id);
        return known?.SettlementId ?? SiteIdForLocation(facility.Location);
    }

    private long SiteIdForOrder(LivingWorkOrder order)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return 1;
        var known = ResidenceFor(_migrationState?.WorkOrderOwners, order.Id);
        if (known is not null) return known.SettlementId;
        if (order.CitizenId is { } citizenId) return SiteIdForCitizen(citizenId);
        if (order.SubjectId is { } subjectId)
        {
            var facility = _living?.Facilities.FirstOrDefault(x => x.Id == subjectId);
            if (facility is not null) return SiteIdForFacility(facility);
            if (_structures.ContainsKey(subjectId)) return SiteIdForStructure(subjectId);
        }
        return SiteIdForLocation(order.Location);
    }

    private long SiteIdForLocation(TileCoordinate location)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) || _migrationState?.DaughterSettlement is not { } daughter) return 1;
        var firstCosts = GetTravelCostsCached(World.StartingSite);
        var secondCosts = GetTravelCostsCached(daughter.Site);
        var firstReachable = firstCosts.TryGetValue(location, out var firstCost);
        var secondReachable = secondCosts.TryGetValue(location, out var secondCost);
        if (!firstReachable) return secondReachable ? MigrationDaughterSettlementState.SettlementId : NearestByManhattan();
        if (!secondReachable) return 1;
        return firstCost <= secondCost ? 1 : MigrationDaughterSettlementState.SettlementId;

        long NearestByManhattan() => Distance(location, World.StartingSite) <= Distance(location, daughter.Site)
            ? 1 : MigrationDaughterSettlementState.SettlementId;
    }

    private long SiteIdForResource(ResourceNode node) => SiteIdForLocation(node.Coordinate);

    private TileCoordinate SiteLocation(long settlementId) => settlementId == 1
        ? World.StartingSite
        : _migrationState?.DaughterSettlement is { } daughter && settlementId == MigrationDaughterSettlementState.SettlementId
            ? daughter.Site
            : throw new ArgumentOutOfRangeException(nameof(settlementId));

    private IEnumerable<Citizen> CitizensAt(long settlementId) => _citizens.Values.Where(x => SiteIdForCitizen(x) == settlementId);
    private IEnumerable<Household> HouseholdsAt(long settlementId) => _households.Values.Where(x => SiteIdForHousehold(x.Id.Value) == settlementId);
    private IEnumerable<Structure> StructuresAt(long settlementId) => _structures.Values.Where(x => SiteIdForStructure(x.Id.Value) == settlementId);
    private IEnumerable<LivingFacility> FacilitiesAt(long settlementId) => (_living?.Facilities ?? []).Where(x => SiteIdForFacility(x) == settlementId);
    private IEnumerable<LivingWorkOrder> OrdersAt(long settlementId) => (_living?.Orders ?? []).Where(x => SiteIdForOrder(x) == settlementId);
    private IEnumerable<FarmCrop> FarmsAt(long settlementId) => _farms.Values.Where(x => SiteIdForStructure(x.StructureId) == settlementId);

    private int PopulationAt(long settlementId) => CitizensAt(settlementId).Count(x => x.IsAlive);
    private int StorageCapacityAt(long settlementId)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return StorageCapacity;
        var stockpiles = 0;
        var granaries = 0;
        foreach (var structure in StructuresAt(settlementId))
        {
            if (structure.Status != StructureStatus.Complete) continue;
            if (structure.Type == StructureType.Stockpile) stockpiles++;
            else if (structure.Type == StructureType.Granary) granaries++;
        }
        return checked(SettlementFor(settlementId).BaseStorageCapacity +
            stockpiles * CitizenSimulationRules.StockpileStorageBonus +
            granaries * AgricultureRules.GranaryFoodCapacity);
    }

    private int ShelterCapacityAt(long settlementId) => MigrationSystemsEnabled(SimulationRulesVersion)
        ? checked(StructuresAt(settlementId).Count(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete) * CitizenSimulationRules.ShelterCapacityPerBuilding)
        : ShelterCapacity;

    private void RecordMigrationOwner(MigrationEntityKind kind, long id, long settlementId)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return;
        if (settlementId is not (1 or MigrationDaughterSettlementState.SettlementId)) throw new ArgumentOutOfRangeException(nameof(settlementId));
        if (settlementId == MigrationDaughterSettlementState.SettlementId && _migrationState?.DaughterSettlement is null)
            throw new InvalidOperationException("Settlement 2 ownership requires a daughter site.");
        SyncMigrationDaughterStocks();
        var state = _migrationState ?? throw new InvalidDataException("M14 migration state is unavailable.");
        var residences = kind switch
        {
            MigrationEntityKind.Citizen => state.CitizenResidences,
            MigrationEntityKind.Household => state.HouseholdResidences,
            MigrationEntityKind.Structure => state.StructureOwners,
            MigrationEntityKind.Facility => state.FacilityOwners,
            MigrationEntityKind.WorkOrder => state.WorkOrderOwners,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var updated = residences.Where(x => x.EntityId != id).Append(new MigrationEntityResidence(id, settlementId)).OrderBy(x => x.EntityId).ToArray();
        _migrationState = new MigrationWorldState(state.Version,
            kind == MigrationEntityKind.Citizen ? updated : state.CitizenResidences,
            kind == MigrationEntityKind.Household ? updated : state.HouseholdResidences,
            kind == MigrationEntityKind.Structure ? updated : state.StructureOwners,
            kind == MigrationEntityKind.Facility ? updated : state.FacilityOwners,
            kind == MigrationEntityKind.WorkOrder ? updated : state.WorkOrderOwners,
            state.DaughterSettlement, state.InTransitParties, state.FoundingPressure, state.LastRelocations,
            state.LastVisitAttemptYear);
    }

    private static MigrationEntityResidence? ResidenceFor(IReadOnlyList<MigrationEntityResidence>? residences, long entityId)
    {
        if (residences is null) return null;
        var low = 0;
        var high = residences.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var residence = residences[middle];
            var comparison = residence.EntityId.CompareTo(entityId);
            if (comparison == 0) return residence;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private enum MigrationEntityKind { Citizen, Household, Structure, Facility, WorkOrder }
}
