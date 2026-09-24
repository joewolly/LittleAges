using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleAges.Domain;

/// <summary>Settlement-2 stock state. Settlement 1 remains stored in the legacy M13 state.</summary>
public sealed record MigrationSettlementStockState(
    int FoodStored,
    int WoodStored,
    int StoneStored,
    int BaseStorageCapacity,
    long DemandUpdatedMinute,
    long ExposureConsequencesStartMinute)
{
    public int StorageUsed => checked(FoodStored + WoodStored + StoneStored);

    public MigrationSettlementStockState Validate()
    {
        if (FoodStored < 0 || WoodStored < 0 || StoneStored < 0 || BaseStorageCapacity < 0 ||
            DemandUpdatedMinute < 0 || ExposureConsequencesStartMinute < 0)
            throw new ArgumentException("Migration settlement stock cannot be negative.");
        _ = StorageUsed;
        return this;
    }

    public static MigrationSettlementStockState From(SettlementState settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        settlement.Validate();
        return new(settlement.FoodStored, settlement.WoodStored, settlement.StoneStored,
            settlement.BaseStorageCapacity, settlement.DemandUpdatedMinute, settlement.ExposureConsequencesStartMinute);
    }

    public SettlementState ToSettlementState() => new(FoodStored, WoodStored, StoneStored,
        BaseStorageCapacity, DemandUpdatedMinute, ExposureConsequencesStartMinute);
}

/// <summary>The optional daughter site and the only authoritative stocks stored for settlement 2.</summary>
public sealed class MigrationDaughterSettlementState
{
    public const long SettlementId = 2;

    public MigrationDaughterSettlementState(TileCoordinate site, MigrationSettlementStockState communalStock,
        IReadOnlyList<LivingStock> livingGoods)
    {
        ArgumentNullException.ThrowIfNull(communalStock);
        ArgumentNullException.ThrowIfNull(livingGoods);
        Site = site;
        CommunalStock = communalStock;
        LivingGoods = Array.AsReadOnly(livingGoods.ToArray());
    }

    public TileCoordinate Site { get; }
    public MigrationSettlementStockState CommunalStock { get; }
    public IReadOnlyList<LivingStock> LivingGoods { get; }

    public MigrationDaughterSettlementState Validate()
    {
        CommunalStock.Validate();
        if (LivingGoods is null || LivingGoods.Any(x => x is null) ||
            !LivingGoods.Select(x => x.Good).SequenceEqual(Enum.GetValues<LivingGood>()) ||
            LivingGoods.Any(x => !Enum.IsDefined(x.Good) || x.Quantity < 0))
            throw new ArgumentException("Daughter living stock must contain every good once in canonical order.");
        return this;
    }
}

/// <summary>
/// Settlement ownership/residence for one world-wide entity ID family. Living facility and work-order
/// IDs keep the existing shared Living namespace; IDs are stable across sites and never reset per settlement.
/// </summary>
public sealed record MigrationEntityResidence(long EntityId, long SettlementId);

public enum MigrationCargoGood
{
    Food = 1,
    Wood,
    Stone,
    Grain,
    Meal,
    PreservedFood,
    Fuel,
    Tool,
    Clothing,
    Medicine,
    Fiber,
    Hide
}

public enum MigrationCargoPurpose { Provisions = 1, Cargo = 2 }
public enum MigrationJourneyKind { Founding = 0, Relocation = 1, Visit = 2 }
public enum MigrationVisitPhase { Outbound = 1, Dwell = 2, Returning = 3 }

/// <summary>Continuous founding pressure recorded for one household.</summary>
public sealed record MigrationFoundingPressureState(long HouseholdId, long SinceMinute);

/// <summary>The exact world minute when a household last completed a physical relocation.</summary>
public sealed record MigrationHouseholdRelocationState(long HouseholdId, long LastCompletedMinute);

/// <summary>A uniquely identified cargo stack has one owner: its in-transit party.</summary>
public sealed record MigrationCargoStackState(long Id, MigrationCargoGood Good, long Quantity, MigrationCargoPurpose Purpose);

/// <summary>Canonical state for one in-transit founding party. Goods are held only by its stacks.</summary>
public sealed class MigrationTransitPartyState
{
    public MigrationTransitPartyState(long id, long householdId, long originSettlementId,
        long? destinationSettlementId, TileCoordinate location, TileCoordinate destinationSite,
        IReadOnlyList<long> citizenIds, IReadOnlyList<MigrationCargoStackState> cargo,
        int remainingPathCost, long departedMinute, bool returning = false, bool foundingAdultArrived = false,
        MigrationJourneyKind journeyKind = MigrationJourneyKind.Founding, long? visitRelativeId = null,
        MigrationVisitPhase? visitPhase = null, long? visitDwellEndsMinute = null)
    {
        ArgumentNullException.ThrowIfNull(citizenIds);
        ArgumentNullException.ThrowIfNull(cargo);
        Id = id;
        HouseholdId = householdId;
        OriginSettlementId = originSettlementId;
        DestinationSettlementId = destinationSettlementId;
        Location = location;
        DestinationSite = destinationSite;
        CitizenIds = Array.AsReadOnly(citizenIds.Order().ToArray());
        Cargo = Array.AsReadOnly(cargo.OrderBy(x => x.Id).ToArray());
        RemainingPathCost = remainingPathCost;
        DepartedMinute = departedMinute;
        Returning = returning;
        FoundingAdultArrived = foundingAdultArrived;
        JourneyKind = journeyKind;
        VisitRelativeId = visitRelativeId;
        VisitPhase = visitPhase;
        VisitDwellEndsMinute = visitDwellEndsMinute;
    }

    public long Id { get; }
    public long HouseholdId { get; }
    public long OriginSettlementId { get; }
    public long? DestinationSettlementId { get; }
    public TileCoordinate Location { get; }
    public TileCoordinate DestinationSite { get; }
    public IReadOnlyList<long> CitizenIds { get; }
    public IReadOnlyList<MigrationCargoStackState> Cargo { get; }
    public int RemainingPathCost { get; }
    public long DepartedMinute { get; }
    public bool Returning { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FoundingAdultArrived { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MigrationJourneyKind JourneyKind { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? VisitRelativeId { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MigrationVisitPhase? VisitPhase { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? VisitDwellEndsMinute { get; }

    public MigrationTransitPartyState Validate()
    {
        if (Id <= 0 || HouseholdId <= 0 || OriginSettlementId is not (1 or 2) ||
            DestinationSettlementId is not (null or 1 or 2) || RemainingPathCost < 0 || DepartedMinute < 0 ||
            CitizenIds.Count == 0 || CitizenIds.Any(x => x <= 0) || CitizenIds.Distinct().Count() != CitizenIds.Count ||
            !CitizenIds.SequenceEqual(CitizenIds.Order()) || Cargo.Any(x => x is null || x.Id <= 0 ||
                !Enum.IsDefined(x.Good) || !Enum.IsDefined(x.Purpose) || x.Quantity <= 0) ||
            Cargo.Select(x => x.Id).Distinct().Count() != Cargo.Count ||
            !Cargo.SequenceEqual(Cargo.OrderBy(x => x.Id)))
            throw new ArgumentException("Migration transit party or cargo is invalid.");
        if (!Enum.IsDefined(JourneyKind) ||
            (JourneyKind == MigrationJourneyKind.Founding
                ? (Returning && DestinationSettlementId != OriginSettlementId) || (!Returning && DestinationSettlementId is not null)
                : DestinationSettlementId is null || (!Returning && DestinationSettlementId == OriginSettlementId) ||
                  (Returning && DestinationSettlementId != OriginSettlementId)))
            throw new ArgumentException("Migration founding and relocation destinations are inconsistent.");
        if (JourneyKind == MigrationJourneyKind.Visit
            ? CitizenIds.Count != 1 || VisitRelativeId is not > 0 || CitizenIds.Contains(VisitRelativeId.Value) ||
              VisitPhase is null || !Enum.IsDefined(VisitPhase.Value) || FoundingAdultArrived ||
              Cargo.Any(x => x.Good != MigrationCargoGood.Food || x.Purpose != MigrationCargoPurpose.Provisions) ||
              VisitPhase switch
              {
                  MigrationVisitPhase.Outbound => Returning || VisitDwellEndsMinute is not null,
                  MigrationVisitPhase.Dwell => Returning || Location != DestinationSite || VisitDwellEndsMinute is not > 0,
                  MigrationVisitPhase.Returning => !Returning || VisitDwellEndsMinute is not null,
                  _ => true
              }
            : VisitRelativeId is not null || VisitPhase is not null || VisitDwellEndsMinute is not null)
            throw new ArgumentException("Migration visit party state is inconsistent.");
        return this;
    }
}

/// <summary>
/// M14-only canonical extension. Site 1's inventory is deliberately absent: it remains authoritative
/// in SettlementState and LivingWorldState.Stock. This extension stores site 2 and world-wide ownership.
/// The entity ID families retain their existing M13 allocation model; settlement ownership never allocates,
/// resets, or reuses an entity ID.
/// </summary>
public sealed class MigrationWorldState
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public MigrationWorldState(int version,
        IReadOnlyList<MigrationEntityResidence> citizenResidences,
        IReadOnlyList<MigrationEntityResidence> householdResidences,
        IReadOnlyList<MigrationEntityResidence> structureOwners,
        IReadOnlyList<MigrationEntityResidence> facilityOwners,
        IReadOnlyList<MigrationEntityResidence> workOrderOwners,
        MigrationDaughterSettlementState? daughterSettlement = null,
        IReadOnlyList<MigrationTransitPartyState>? inTransitParties = null,
        IReadOnlyList<MigrationFoundingPressureState>? foundingPressure = null,
        IReadOnlyList<MigrationHouseholdRelocationState>? lastRelocations = null,
        long? lastVisitAttemptYear = null)
    {
        ArgumentNullException.ThrowIfNull(citizenResidences);
        ArgumentNullException.ThrowIfNull(householdResidences);
        ArgumentNullException.ThrowIfNull(structureOwners);
        ArgumentNullException.ThrowIfNull(facilityOwners);
        ArgumentNullException.ThrowIfNull(workOrderOwners);
        Version = version;
        CitizenResidences = CopyOrdered(citizenResidences);
        HouseholdResidences = CopyOrdered(householdResidences);
        StructureOwners = CopyOrdered(structureOwners);
        FacilityOwners = CopyOrdered(facilityOwners);
        WorkOrderOwners = CopyOrdered(workOrderOwners);
        DaughterSettlement = daughterSettlement is null ? null :
            new MigrationDaughterSettlementState(daughterSettlement.Site, daughterSettlement.CommunalStock,
                daughterSettlement.LivingGoods);
        InTransitParties = Array.AsReadOnly((inTransitParties ?? Array.Empty<MigrationTransitPartyState>())
            .Select(CloneParty).OrderBy(x => x.Id).ToArray());
        FoundingPressure = foundingPressure is null || foundingPressure.Count == 0
            ? null
            : Array.AsReadOnly(foundingPressure.OrderBy(x => x.HouseholdId).ToArray());
        LastRelocations = lastRelocations is null || lastRelocations.Count == 0
            ? null
            : Array.AsReadOnly(lastRelocations.OrderBy(x => x.HouseholdId).ToArray());
        LastVisitAttemptYear = lastVisitAttemptYear;
    }

    public int Version { get; }
    public IReadOnlyList<MigrationEntityResidence> CitizenResidences { get; }
    public IReadOnlyList<MigrationEntityResidence> HouseholdResidences { get; }
    public IReadOnlyList<MigrationEntityResidence> StructureOwners { get; }
    public IReadOnlyList<MigrationEntityResidence> FacilityOwners { get; }
    public IReadOnlyList<MigrationEntityResidence> WorkOrderOwners { get; }
    public MigrationDaughterSettlementState? DaughterSettlement { get; }
    public IReadOnlyList<MigrationTransitPartyState> InTransitParties { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MigrationFoundingPressureState>? FoundingPressure { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MigrationHouseholdRelocationState>? LastRelocations { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastVisitAttemptYear { get; }

    public MigrationWorldState Validate()
    {
        if (Version != 1 || CitizenResidences is null || HouseholdResidences is null || StructureOwners is null ||
            FacilityOwners is null || WorkOrderOwners is null || InTransitParties is null ||
            LastVisitAttemptYear is < 0 || DaughterSettlement is null && LastVisitAttemptYear is not null)
            throw new ArgumentException("Unsupported or incomplete migration state.");
        ValidateResidences(CitizenResidences, nameof(CitizenResidences));
        ValidateResidences(HouseholdResidences, nameof(HouseholdResidences));
        ValidateResidences(StructureOwners, nameof(StructureOwners));
        ValidateResidences(FacilityOwners, nameof(FacilityOwners));
        ValidateResidences(WorkOrderOwners, nameof(WorkOrderOwners));
        DaughterSettlement?.Validate();
        if (DaughterSettlement is null && AllResidences().Any(x => x.SettlementId != 1))
            throw new ArgumentException("Settlement 2 ownership requires a daughter site.");
        if (InTransitParties.Any(x => x is null) || !InTransitParties.SequenceEqual(InTransitParties.OrderBy(x => x.Id)) ||
            InTransitParties.Select(x => x.Id).Distinct().Count() != InTransitParties.Count)
            throw new ArgumentException("Migration transit parties must be unique and ordered.");
        foreach (var party in InTransitParties) party.Validate();
        if (FoundingPressure is not null && (FoundingPressure.Any(x => x is null || x.HouseholdId <= 0 || x.SinceMinute < 0) ||
            FoundingPressure.Select(x => x.HouseholdId).Distinct().Count() != FoundingPressure.Count ||
            !FoundingPressure.SequenceEqual(FoundingPressure.OrderBy(x => x.HouseholdId))))
            throw new ArgumentException("Migration founding pressure must be unique and ordered by household.");
        if (LastRelocations is not null && (LastRelocations.Any(x => x is null || x.HouseholdId <= 0 || x.LastCompletedMinute < 0) ||
            LastRelocations.Select(x => x.HouseholdId).Distinct().Count() != LastRelocations.Count ||
            !LastRelocations.SequenceEqual(LastRelocations.OrderBy(x => x.HouseholdId))))
            throw new ArgumentException("Migration relocation times must be unique and ordered by household.");
        var cargoIds = InTransitParties.SelectMany(x => x.Cargo).Select(x => x.Id).ToArray();
        if (cargoIds.Distinct().Count() != cargoIds.Length)
            throw new ArgumentException("Each migration cargo stack must have exactly one location.");
        var travelers = InTransitParties.SelectMany(x => x.CitizenIds).ToArray();
        if (travelers.Distinct().Count() != travelers.Length)
            throw new ArgumentException("A citizen cannot be in more than one migration party.");
        return this;
    }

    public string ToCanonicalJson()
    {
        Validate();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static MigrationWorldState Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var value = JsonSerializer.Deserialize<MigrationWorldState>(json, JsonOptions)
            ?? throw new InvalidDataException("Missing migration state.");
        value.Validate();
        if (!string.Equals(value.ToCanonicalJson(), json, StringComparison.Ordinal))
            throw new InvalidDataException("Migration state must use canonical JSON.");
        return value;
    }

    private IEnumerable<MigrationEntityResidence> AllResidences() => CitizenResidences
        .Concat(HouseholdResidences).Concat(StructureOwners).Concat(FacilityOwners).Concat(WorkOrderOwners);

    private static void ValidateResidences(IReadOnlyList<MigrationEntityResidence> values, string name)
    {
        if (values.Any(x => x is null || x.EntityId <= 0 || x.SettlementId is not (1 or 2)) ||
            values.Select(x => x.EntityId).Distinct().Count() != values.Count ||
            !values.SequenceEqual(values.OrderBy(x => x.EntityId)))
            throw new ArgumentException("Migration residences must be positive, unique, and ordered.", name);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<MigrationEntityResidence> CopyOrdered(IReadOnlyList<MigrationEntityResidence> values) =>
        Array.AsReadOnly(values.OrderBy(x => x.EntityId).ToArray());

    private static MigrationTransitPartyState CloneParty(MigrationTransitPartyState value) =>
        new(value.Id, value.HouseholdId, value.OriginSettlementId, value.DestinationSettlementId, value.Location,
            value.DestinationSite, value.CitizenIds, value.Cargo, value.RemainingPathCost, value.DepartedMinute,
            value.Returning, value.FoundingAdultArrived, value.JourneyKind, value.VisitRelativeId, value.VisitPhase,
            value.VisitDwellEndsMinute);

    internal static MigrationTransitPartyState ClonePartyForRead(MigrationTransitPartyState value) => CloneParty(value);
}

/// <summary>Read-only per-site projection of M14 canonical state.</summary>
public sealed class MigrationSettlementReadSnapshot
{
    public MigrationSettlementReadSnapshot(long id, TileCoordinate site, MigrationSettlementStockState communalStock,
        IReadOnlyList<LivingStock> livingGoods, int storageCapacity,
        IReadOnlyList<long> citizenIds, IReadOnlyList<long> householdIds, IReadOnlyList<long> structureIds,
        IReadOnlyList<long> farmStructureIds, IReadOnlyList<long> facilityIds, IReadOnlyList<long> workOrderIds)
    {
        ArgumentNullException.ThrowIfNull(communalStock);
        ArgumentNullException.ThrowIfNull(livingGoods);
        ArgumentNullException.ThrowIfNull(citizenIds);
        ArgumentNullException.ThrowIfNull(householdIds);
        ArgumentNullException.ThrowIfNull(structureIds);
        ArgumentNullException.ThrowIfNull(farmStructureIds);
        ArgumentNullException.ThrowIfNull(facilityIds);
        ArgumentNullException.ThrowIfNull(workOrderIds);
        Id = id;
        Site = site;
        CommunalStock = communalStock;
        LivingGoods = Array.AsReadOnly(livingGoods.ToArray());
        StorageCapacity = storageCapacity;
        CitizenIds = Array.AsReadOnly(citizenIds.Order().ToArray());
        HouseholdIds = Array.AsReadOnly(householdIds.Order().ToArray());
        StructureIds = Array.AsReadOnly(structureIds.Order().ToArray());
        FarmStructureIds = Array.AsReadOnly(farmStructureIds.Order().ToArray());
        FacilityIds = Array.AsReadOnly(facilityIds.Order().ToArray());
        WorkOrderIds = Array.AsReadOnly(workOrderIds.Order().ToArray());
    }

    public long Id { get; }
    public TileCoordinate Site { get; }
    public MigrationSettlementStockState CommunalStock { get; }
    public IReadOnlyList<LivingStock> LivingGoods { get; }
    public int StorageCapacity { get; }
    public IReadOnlyList<long> CitizenIds { get; }
    public IReadOnlyList<long> HouseholdIds { get; }
    public IReadOnlyList<long> StructureIds { get; }
    public IReadOnlyList<long> FarmStructureIds { get; }
    public IReadOnlyList<long> FacilityIds { get; }
    public IReadOnlyList<long> WorkOrderIds { get; }
}

/// <summary>Stable M14 read projection, including the site-1 stocks joined from the legacy canonical state.</summary>
public sealed class MigrationReadSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public MigrationReadSnapshot(string rulesVersion, string seed, long worldMinute, string worldFingerprint,
        IReadOnlyList<MigrationSettlementReadSnapshot> settlements,
        IReadOnlyList<MigrationTransitPartyState> inTransitParties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldFingerprint);
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(inTransitParties);
        RulesVersion = rulesVersion;
        Seed = seed;
        WorldMinute = worldMinute;
        WorldFingerprint = worldFingerprint;
        Settlements = Array.AsReadOnly(settlements.OrderBy(x => x.Id).ToArray());
        InTransitParties = Array.AsReadOnly(inTransitParties.OrderBy(x => x.Id).Select(MigrationWorldState.ClonePartyForRead).ToArray());
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalJson()))).ToLowerInvariant();
    }

    public string RulesVersion { get; }
    public string Seed { get; }
    public long WorldMinute { get; }
    public string WorldFingerprint { get; }
    public IReadOnlyList<MigrationSettlementReadSnapshot> Settlements { get; }
    public IReadOnlyList<MigrationTransitPartyState> InTransitParties { get; }
    public string Fingerprint { get; }

    public string ToCanonicalJson() => JsonSerializer.Serialize(new
    {
        RulesVersion, Seed, WorldMinute, WorldFingerprint, Settlements, InTransitParties
    }, JsonOptions);
}
