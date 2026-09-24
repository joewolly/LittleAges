using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Validates the M14-only ownership extension against the canonical M13 entity state.</summary>
public static class MigrationValidation
{
    public static void Validate(SimulationPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SimulationRulesVersion != SimulationEngine.MigrationSimulationRulesVersion)
        {
            Require(snapshot.MigrationStateJson is null && snapshot.MigrationState is null,
                "Only M14 snapshots may carry migration state.");
            return;
        }

        Require(snapshot.MigrationStateJson is not null && snapshot.MigrationState is not null,
            "M14 snapshots require canonical migration state.");
        Require(MigrationWorldState.Parse(snapshot.MigrationStateJson!) .ToCanonicalJson() == snapshot.MigrationStateJson,
            "M14 migration state must be canonical.");
        var state = snapshot.MigrationState!.Validate();
        var world = snapshot.World ?? throw new ArgumentException("M14 snapshots require a world.");
        var settlement = snapshot.Settlement ?? throw new ArgumentException("M14 snapshots require the original settlement.");
        var living = LivingWorldCodec.Deserialize(snapshot.LivingStateJson ??
            throw new ArgumentException("M14 snapshots require Living state."));
        _ = MigrationSettlementStockState.From(settlement);

        if (state.DaughterSettlement is { } daughter)
        {
            daughter.Validate();
            Require(daughter.Site != world.StartingSite && daughter.Site.X >= 0 && daughter.Site.Y >= 0 &&
                daughter.Site.X < world.Width && daughter.Site.Y < world.Height &&
                world.GetTile(daughter.Site).Walkable, "Daughter settlement site must be a distinct walkable world tile.");
            var ownedStructures = snapshot.Structures.Where(x => Owner(state.StructureOwners, x.Id.Value) == MigrationDaughterSettlementState.SettlementId).ToArray();
            var capacity = checked(daughter.CommunalStock.BaseStorageCapacity +
                ownedStructures.Count(x => x.Type == StructureType.Stockpile && x.Status == StructureStatus.Complete) * CitizenSimulationRules.StockpileStorageBonus +
                ownedStructures.Count(x => x.Type == StructureType.Granary && x.Status == StructureStatus.Complete) * AgricultureRules.GranaryFoodCapacity);
            var stored = (long)daughter.CommunalStock.StorageUsed + daughter.LivingGoods.Sum(x => (long)x.Quantity);
            Require(stored <= capacity, "Daughter settlement stock exceeds its local storage capacity.");
        }

        var citizenOwners = ValidateResidences(state.CitizenResidences, snapshot.Citizens.Select(x => x.Id.Value), "citizens");
        var householdOwners = ValidateResidences(state.HouseholdResidences, snapshot.Households.Select(x => x.Id.Value), "households");
        var structureOwners = ValidateResidences(state.StructureOwners, snapshot.Structures.Select(x => x.Id.Value), "structures");
        var facilityOwners = ValidateResidences(state.FacilityOwners, living.Facilities.Select(x => x.Id), "facilities");
        var orderOwners = ValidateResidences(state.WorkOrderOwners, living.Orders.Select(x => x.Id), "work orders");

        var citizenById = snapshot.Citizens.ToDictionary(x => x.Id.Value);
        foreach (var pressure in state.FoundingPressure ?? Array.Empty<MigrationFoundingPressureState>())
            Require(householdOwners.ContainsKey(pressure.HouseholdId) && pressure.SinceMinute <= snapshot.WorldMinute.Value,
                "Migration founding pressure must reference an existing household and cannot start in the future.");
        foreach (var lastMove in state.LastRelocations ?? Array.Empty<MigrationHouseholdRelocationState>())
            Require(householdOwners.ContainsKey(lastMove.HouseholdId) && lastMove.LastCompletedMinute <= snapshot.WorldMinute.Value,
                "Migration relocation time must reference an existing household and cannot be in the future.");
        foreach (var citizen in snapshot.Citizens)
        {
            var residence = citizenOwners[citizen.Id.Value];
            if (citizen.HouseholdId is { } householdId)
            {
                Require(householdOwners.TryGetValue(householdId.Value, out var householdResidence) && householdResidence == residence,
                    "A citizen and household must have the same settlement residence.");
            }
            if (citizen.HomeStructureId is { } homeId)
                Require(structureOwners.TryGetValue(homeId.Value, out var structureResidence) && structureResidence == residence,
                    "A citizen and home structure must have the same settlement owner.");
        }

        foreach (var household in snapshot.Households)
        {
            var residence = householdOwners[household.Id.Value];
            Require(snapshot.Citizens.Where(x => x.HouseholdId == household.Id).All(x => citizenOwners[x.Id.Value] == residence),
                "All members of a household must share its settlement residence.");
        }

        foreach (var order in living.Orders)
        {
            if (order.CitizenId is { } citizenId)
                Require(citizenOwners.TryGetValue(citizenId, out var residence) && orderOwners[order.Id] == residence,
                    "A work order and its worker must belong to the same settlement.");
            if (order.Kind is (LivingWorkKind.Cook or LivingWorkKind.Preserve) && order.SubjectId is { } facilityId)
                Require(facilityOwners.TryGetValue(facilityId, out var facilityResidence) && orderOwners[order.Id] == facilityResidence,
                    "A work order and its facility must belong to the same settlement.");
            if (order.Kind == LivingWorkKind.Harvest && order.SubjectId is { } farmStructureId)
                Require(structureOwners.TryGetValue(farmStructureId, out var farmResidence) && orderOwners[order.Id] == farmResidence,
                    "A farm work order and its structure must belong to the same settlement.");
        }

        var sharedIds = snapshot.Citizens.Select(x => x.Id.Value)
            .Concat(snapshot.Households.Select(x => x.Id.Value))
            .Concat(snapshot.Structures.Select(x => x.Id.Value))
            .Concat(state.InTransitParties.Select(x => x.Id))
            .Concat(state.InTransitParties.SelectMany(x => x.Cargo).Select(x => x.Id)).ToArray();
        Require(sharedIds.Distinct().Count() == sharedIds.Length && sharedIds.All(x => x < snapshot.Counters.NextEntityId),
            "M14 world-wide entity IDs must be unique and below the next entity ID.");
        foreach (var party in state.InTransitParties)
        {
            party.Validate();
            Require(party.DepartedMinute <= snapshot.WorldMinute.Value &&
                party.Location.X >= 0 && party.Location.Y >= 0 && party.Location.X < world.Width && party.Location.Y < world.Height && world.GetTile(party.Location).Walkable &&
                party.DestinationSite.X >= 0 && party.DestinationSite.Y >= 0 && party.DestinationSite.X < world.Width && party.DestinationSite.Y < world.Height && world.GetTile(party.DestinationSite).Walkable,
                "Migration party location or timing is invalid.");
            Require(householdOwners.TryGetValue(party.HouseholdId, out var householdResidence) && householdResidence == party.OriginSettlementId,
                "A migration party must belong to its household's origin settlement.");
            foreach (var citizenId in party.CitizenIds)
                Require(citizenById.TryGetValue(citizenId, out var citizen) && citizen.HouseholdId?.Value == party.HouseholdId &&
                    citizenOwners[citizenId] == party.OriginSettlementId,
                    "Migration party members must be residents of its originating household.");
            var representative = party.CitizenIds.Select(id => citizenById[id]).FirstOrDefault(x => x.IsAlive);
            if (representative is not null)
                Require(party.Location == representative.Location, "A migration party must track its lowest-ID living member.");
        }

        _ = CreateReadSnapshot(snapshot, state, living, citizenOwners, householdOwners, structureOwners, facilityOwners, orderOwners);
    }

    public static MigrationReadSnapshot CreateReadSnapshot(SimulationPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);
        var state = snapshot.MigrationState ?? throw new ArgumentException("M14 migration state is required.", nameof(snapshot));
        var living = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!);
        var citizenOwners = state.CitizenResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var householdOwners = state.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var structureOwners = state.StructureOwners.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var facilityOwners = state.FacilityOwners.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var orderOwners = state.WorkOrderOwners.ToDictionary(x => x.EntityId, x => x.SettlementId);
        return CreateReadSnapshot(snapshot, state, living, citizenOwners, householdOwners, structureOwners, facilityOwners, orderOwners);
    }

    /// <summary>Counts living citizens at each site using the validated residence projection.</summary>
    public static IReadOnlyDictionary<long, int> GetLivingResidentCountsBySettlement(
        SimulationPersistenceSnapshot snapshot, MigrationReadSnapshot migration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(migration);
        var livingCitizenIds = snapshot.Citizens.Where(citizen => citizen.IsAlive)
            .Select(citizen => citizen.Id.Value).ToHashSet();
        return migration.Settlements.ToDictionary(settlement => settlement.Id,
            settlement => settlement.CitizenIds.Count(livingCitizenIds.Contains));
    }

    private static MigrationReadSnapshot CreateReadSnapshot(SimulationPersistenceSnapshot snapshot, MigrationWorldState state,
        LivingWorldState living, IReadOnlyDictionary<long, long> citizenOwners,
        IReadOnlyDictionary<long, long> householdOwners, IReadOnlyDictionary<long, long> structureOwners,
        IReadOnlyDictionary<long, long> facilityOwners, IReadOnlyDictionary<long, long> orderOwners)
    {
        var settlements = new List<MigrationSettlementReadSnapshot>
        {
            CreateSettlementReadSnapshot(1, snapshot.World!.StartingSite, MigrationSettlementStockState.From(snapshot.Settlement!),
                living.Stock, snapshot.Settlement!.BaseStorageCapacity, snapshot, citizenOwners, householdOwners,
                structureOwners, facilityOwners, orderOwners)
        };
        if (state.DaughterSettlement is { } daughter)
            settlements.Add(CreateSettlementReadSnapshot(MigrationDaughterSettlementState.SettlementId, daughter.Site,
                daughter.CommunalStock, daughter.LivingGoods, daughter.CommunalStock.BaseStorageCapacity,
                snapshot, citizenOwners, householdOwners, structureOwners, facilityOwners, orderOwners));
        return new MigrationReadSnapshot(snapshot.SimulationRulesVersion, snapshot.Seed.ToString(), snapshot.WorldMinute.Value,
            snapshot.World!.Fingerprint, settlements, state.InTransitParties);
    }

    private static MigrationSettlementReadSnapshot CreateSettlementReadSnapshot(long settlementId, TileCoordinate site,
        MigrationSettlementStockState communalStock, IReadOnlyList<LivingStock> livingGoods, int baseCapacity,
        SimulationPersistenceSnapshot snapshot, IReadOnlyDictionary<long, long> citizenOwners,
        IReadOnlyDictionary<long, long> householdOwners, IReadOnlyDictionary<long, long> structureOwners,
        IReadOnlyDictionary<long, long> facilityOwners, IReadOnlyDictionary<long, long> orderOwners)
    {
        var structures = snapshot.Structures.Where(x => structureOwners[x.Id.Value] == settlementId).ToArray();
        var storageCapacity = checked(baseCapacity +
            structures.Count(x => x.Type == StructureType.Stockpile && x.Status == StructureStatus.Complete) * CitizenSimulationRules.StockpileStorageBonus +
            structures.Count(x => x.Type == StructureType.Granary && x.Status == StructureStatus.Complete) * AgricultureRules.GranaryFoodCapacity);
        var farmIds = (snapshot.Agriculture?.Farms ?? Array.Empty<FarmCrop>()).Select(x => x.StructureId)
            .Where(id => structureOwners.GetValueOrDefault(id) == settlementId).ToArray();
        return new MigrationSettlementReadSnapshot(settlementId, site, communalStock, livingGoods, storageCapacity,
            citizenOwners.Where(x => x.Value == settlementId).Select(x => x.Key).ToArray(),
            householdOwners.Where(x => x.Value == settlementId).Select(x => x.Key).ToArray(),
            structures.Select(x => x.Id.Value).ToArray(), farmIds,
            facilityOwners.Where(x => x.Value == settlementId).Select(x => x.Key).ToArray(),
            orderOwners.Where(x => x.Value == settlementId).Select(x => x.Key).ToArray());
    }

    private static Dictionary<long, long> ValidateResidences(IReadOnlyList<MigrationEntityResidence> actual,
        IEnumerable<long> expectedIds, string kind)
    {
        var expected = expectedIds.Order().ToArray();
        Require(actual.Select(x => x.EntityId).SequenceEqual(expected), $"Every {kind} must have exactly one settlement residence.");
        return actual.ToDictionary(x => x.EntityId, x => x.SettlementId);
    }

    private static long Owner(IReadOnlyList<MigrationEntityResidence> owners, long entityId) =>
        owners.SingleOrDefault(x => x.EntityId == entityId)?.SettlementId ?? 1;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
