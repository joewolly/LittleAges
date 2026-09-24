using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleAges.Domain;

/// <summary>Stable persisted historical event vocabulary. Values are compatibility data.</summary>
public enum HistoricalEventType : int
{
    WorldCreated = 1,
    SettlementFounded = 2,
    CitizenBorn = 3,
    CitizenDied = 4,
    PartnershipFormed = 5,
    FriendshipFormed = 6,
    RivalryFormed = 7,
    HouseholdCreated = 8,
    StructureStarted = 9,
    StructureCompleted = 10,
    PopulationMilestone = 11,
    ResourceShortageStarted = 12,
    ResourceShortageEnded = 13,
    CitizenSpecializationChanged = 14,
    SeasonStarted = 15,
    ExpeditionDeparted = 16,
    ExpeditionReturned = 17,
    ExpeditionLost = 18,
    DaughterSettlementFounded = 19,
    HouseholdRelocated = 20,
    FamilyVisitDeparted = 21,
    FamilyVisitReturned = 22
}

public enum HistoricalImportance : int
{
    Debug = 0,
    Routine = 1,
    Personal = 2,
    Notable = 3,
    Major = 4,
    Historic = 5
}

// Alias vocabulary retained for callers that use the longer schema name.
public enum HistoricalEventImportance : int
{
    Debug = 0,
    Routine = 1,
    Personal = 2,
    Notable = 3,
    Major = 4,
    Historic = 5
}

public enum HistoricalEventOrigin : int
{
    Live = 1,
    MigrationBackfill = 2
}

public enum HistoricalCitizenLinkRole : int
{
    Subject = 1,
    Parent = 2,
    Partner = 3,
    Founder = 4,
    Participant = 5,
    Member = 6,
    Contributor = 7
}

public enum HistoricalStructureLinkRole : int
{
    Subject = 1
}

public enum MemoryType : int
{
    ChildBorn = 1,
    PartnerDied = 2,
    PartnershipFormed = 3,
    FriendshipFormed = 4,
    RivalryFormed = 5,
    StructureCompleted = 6
}

public enum CitizenMemoryType : int
{
    ChildBorn = 1,
    PartnerDied = 2,
    PartnershipFormed = 3,
    FriendshipFormed = 4,
    RivalryFormed = 5,
    StructureCompleted = 6
}

/// <summary>An immutable, schema-versioned factual historical transition.</summary>
public sealed record HistoricalEvent
{
    public const int CurrentSchemaVersion = 1;

    public HistoricalEvent(
        HistoricalEventId id,
        long worldMinute,
        HistoricalEventType eventType,
        HistoricalImportance importance,
        HistoricalEventOrigin origin,
        TileCoordinate? location,
        string payloadJson,
        int schemaVersion = CurrentSchemaVersion)
    {
        Id = id;
        HistoricalEventId = id;
        WorldMinute = worldMinute;
        EventType = eventType;
        Importance = importance;
        Origin = origin;
        Location = location;
        PayloadJson = payloadJson ?? throw new ArgumentNullException(nameof(payloadJson));
        SchemaVersion = schemaVersion;
    }

    public HistoricalEvent(
        HistoricalEventId id,
        WorldMinute worldMinute,
        HistoricalEventType eventType,
        HistoricalImportance importance,
        HistoricalEventOrigin origin,
        TileCoordinate? location,
        string payloadJson,
        int schemaVersion = CurrentSchemaVersion)
        : this(id, worldMinute.Value, eventType, importance, origin, location, payloadJson, schemaVersion) { }

    public HistoricalEvent(
        HistoricalEventId id,
        long worldMinute,
        HistoricalEventType eventType,
        HistoricalEventImportance importance,
        HistoricalEventOrigin origin,
        TileCoordinate? location,
        string payloadJson,
        int schemaVersion = CurrentSchemaVersion)
        : this(id, worldMinute, eventType, (HistoricalImportance)(int)importance, origin, location, payloadJson, schemaVersion) { }

    public HistoricalEventId Id { get; }
    public HistoricalEventId HistoricalEventId { get; }
    public long WorldMinute { get; }
    public WorldMinute Minute => new(WorldMinute);
    public HistoricalEventType EventType { get; }
    public HistoricalImportance Importance { get; }
    public HistoricalEventOrigin Origin { get; }
    public TileCoordinate? Location { get; }
    public string PayloadJson { get; }
    public int SchemaVersion { get; }
    public int HistoricalEventSchemaVersion => SchemaVersion;

    public HistoricalEvent Validate(long currentMinute = long.MaxValue)
    {
        WorldIdValidation.RequirePositive(Id.Value, nameof(Id));
        if (WorldMinute < 0 || WorldMinute > currentMinute) throw new ArgumentException("Historical event minute is outside the world timeline.");
        if (!Enum.IsDefined(EventType) || EventType is < HistoricalEventType.WorldCreated or > HistoricalEventType.FamilyVisitReturned) throw new ArgumentException("Historical event type is unsupported.");
        if (!Enum.IsDefined(Importance) || Importance is < HistoricalImportance.Debug or > HistoricalImportance.Historic) throw new ArgumentException("Historical event importance is unsupported.");
        if (!Enum.IsDefined(Origin) || Origin is not (HistoricalEventOrigin.Live or HistoricalEventOrigin.MigrationBackfill)) throw new ArgumentException("Historical event origin is unsupported.");
        if (SchemaVersion != CurrentSchemaVersion) throw new NotSupportedException($"Historical event schema version '{SchemaVersion}' is not supported.");
        HistoricalEventPayloads.Validate(EventType, PayloadJson);
        return this;
    }

    /// <summary>Checks the event-specific link roles before an event can enter the append-only stream.</summary>
    public void ValidateLinks(IEnumerable<HistoricalEventCitizenLink> citizenLinks, IEnumerable<HistoricalEventStructureLink> structureLinks)
    {
        ArgumentNullException.ThrowIfNull(citizenLinks);
        ArgumentNullException.ThrowIfNull(structureLinks);
        var citizens = citizenLinks.ToArray();
        var structures = structureLinks.ToArray();
        if (citizens.Any(x => x.HistoricalEventId != Id) || structures.Any(x => x.HistoricalEventId != Id)) throw new ArgumentException("Historical links must belong to their event.");
        foreach (var link in citizens) link.Validate();
        foreach (var link in structures) link.Validate();
        if (citizens.Select(x => (x.CitizenId.Value, x.Role)).Distinct().Count() != citizens.Length || structures.Select(x => (x.StructureId.Value, x.Role)).Distinct().Count() != structures.Length) throw new ArgumentException("Historical links must be unique.");
        static void RequireRoles(IReadOnlyList<HistoricalEventCitizenLink> links, string role, int minimum, int maximum)
        {
            var count = links.Count(x => string.Equals(x.Role, role, StringComparison.Ordinal));
            if (count < minimum || count > maximum || links.Any(x => !string.Equals(x.Role, role, StringComparison.Ordinal))) throw new ArgumentException("Historical citizen link roles are not canonical.");
        }
        static void RequireStructure(IReadOnlyList<HistoricalEventStructureLink> links)
        {
            if (links.Count != 1 || links[0].Role != "subject") throw new ArgumentException("Historical structure links are not canonical.");
        }
        switch (EventType)
        {
            case HistoricalEventType.WorldCreated:
            case HistoricalEventType.PopulationMilestone:
            case HistoricalEventType.ResourceShortageStarted:
            case HistoricalEventType.ResourceShortageEnded:
            case HistoricalEventType.SeasonStarted:
                if (citizens.Length != 0 || structures.Length != 0) throw new ArgumentException("This historical event cannot have entity links.");
                break;
            case HistoricalEventType.ExpeditionDeparted:
            case HistoricalEventType.ExpeditionReturned:
            case HistoricalEventType.ExpeditionLost:
                RequireRoles(citizens, "participant", 1, int.MaxValue);
                if (structures.Length != 0) throw new ArgumentException("Expedition events cannot have structure links.");
                break;
            case HistoricalEventType.DaughterSettlementFounded:
                RequireRoles(citizens, "founder", 1, int.MaxValue);
                if (structures.Length != 0) throw new ArgumentException("DaughterSettlementFounded cannot have structure links.");
                if (citizens.Length != CanonicalPayloadCount("founderCount")) throw new ArgumentException("DaughterSettlementFounded founder links must match founderCount.");
                break;
            case HistoricalEventType.HouseholdRelocated:
                RequireRoles(citizens, "member", 1, int.MaxValue);
                if (structures.Length != 0) throw new ArgumentException("HouseholdRelocated cannot have structure links.");
                if (citizens.Length != CanonicalPayloadCount("travelerCount")) throw new ArgumentException("HouseholdRelocated member links must match travelerCount.");
                break;
            case HistoricalEventType.FamilyVisitDeparted:
            case HistoricalEventType.FamilyVisitReturned:
                if (citizens.Length != 2 || citizens.Count(x => x.Role == "subject") != 1 || citizens.Count(x => x.Role == "participant") != 1 ||
                    citizens.Any(x => x.Role is not ("subject" or "participant")) ||
                    citizens[0].CitizenId == citizens[1].CitizenId)
                    throw new ArgumentException("Family visit citizen links must identify one distinct subject and participant.");
                if (structures.Length != 0) throw new ArgumentException("Family visit events cannot have structure links.");
                break;
            case HistoricalEventType.SettlementFounded:
                RequireRoles(citizens, "founder", 1, int.MaxValue);
                if (structures.Length != 0) throw new ArgumentException("SettlementFounded cannot have structure links.");
                break;
            case HistoricalEventType.CitizenBorn:
                if (citizens.Count(x => x.Role == "subject") != 1 || citizens.Count(x => x.Role == "parent") != 2 || citizens.Any(x => x.Role is not ("subject" or "parent"))) throw new ArgumentException("CitizenBorn links are not canonical.");
                if (structures.Length != 0) throw new ArgumentException("CitizenBorn cannot have structure links.");
                break;
            case HistoricalEventType.CitizenDied:
                RequireRoles(citizens, "subject", 1, 1);
                if (structures.Length != 0) throw new ArgumentException("CitizenDied cannot have structure links.");
                break;
            case HistoricalEventType.PartnershipFormed:
                RequireRoles(citizens, "partner", 2, 2);
                if (structures.Length != 0) throw new ArgumentException("PartnershipFormed cannot have structure links.");
                break;
            case HistoricalEventType.FriendshipFormed:
            case HistoricalEventType.RivalryFormed:
                RequireRoles(citizens, "participant", 2, 2);
                if (structures.Length != 0) throw new ArgumentException("Relationship events cannot have structure links.");
                break;
            case HistoricalEventType.HouseholdCreated:
                RequireRoles(citizens, "member", 2, 2);
                if (structures.Length != 0) throw new ArgumentException("HouseholdCreated cannot have structure links.");
                break;
            case HistoricalEventType.StructureStarted:
                if (citizens.Length != 0) throw new ArgumentException("StructureStarted cannot have citizen links.");
                RequireStructure(structures);
                break;
            case HistoricalEventType.StructureCompleted:
                if (citizens.Length == 0 || citizens.Any(x => x.Role != "contributor")) throw new ArgumentException("StructureCompleted citizen links are not canonical.");
                RequireStructure(structures);
                break;
            case HistoricalEventType.CitizenSpecializationChanged:
                RequireRoles(citizens, "subject", 1, 1);
                if (structures.Length != 0) throw new ArgumentException("Specialization events cannot have structure links.");
                break;
            default:
                throw new ArgumentException("Historical event type is unsupported.");
        }
    }

    private int CanonicalPayloadCount(string propertyName)
    {
        HistoricalEventPayloads.Validate(EventType, PayloadJson);
        using var document = JsonDocument.Parse(PayloadJson);
        return document.RootElement.GetProperty(propertyName).GetInt32();
    }
}

public sealed record HistoricalEventCitizenLink
{
    public HistoricalEventCitizenLink(HistoricalEventId historicalEventId, CitizenId citizenId, string role)
    {
        HistoricalEventId = historicalEventId;
        CitizenId = citizenId;
        Role = role ?? throw new ArgumentNullException(nameof(role));
    }

    public HistoricalEventCitizenLink(HistoricalEventId historicalEventId, CitizenId citizenId, HistoricalCitizenLinkRole role)
        : this(historicalEventId, citizenId, RoleName(role)) { }

    public HistoricalEventId HistoricalEventId { get; }
    public HistoricalEventId EventId => HistoricalEventId;
    public CitizenId CitizenId { get; }
    public string Role { get; }

    public void Validate()
    {
        WorldIdValidation.RequirePositive(HistoricalEventId.Value, nameof(HistoricalEventId));
        WorldIdValidation.RequirePositive(CitizenId.Value, nameof(CitizenId));
        if (!HistoricalLinkRoles.Contains(Role)) throw new ArgumentException("Historical citizen link role is unsupported.");
    }

    public static string RoleName(HistoricalCitizenLinkRole role) => role switch
    {
        HistoricalCitizenLinkRole.Subject => "subject",
        HistoricalCitizenLinkRole.Parent => "parent",
        HistoricalCitizenLinkRole.Partner => "partner",
        HistoricalCitizenLinkRole.Founder => "founder",
        HistoricalCitizenLinkRole.Participant => "participant",
        HistoricalCitizenLinkRole.Member => "member",
        HistoricalCitizenLinkRole.Contributor => "contributor",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static readonly IReadOnlySet<string> HistoricalLinkRoles = new HashSet<string>(StringComparer.Ordinal)
    { "subject", "parent", "partner", "founder", "participant", "member", "contributor" };
}

public sealed record HistoricalEventStructureLink
{
    public HistoricalEventStructureLink(HistoricalEventId historicalEventId, StructureId structureId, string role = "subject")
    {
        HistoricalEventId = historicalEventId;
        StructureId = structureId;
        Role = role ?? throw new ArgumentNullException(nameof(role));
    }

    public HistoricalEventId HistoricalEventId { get; }
    public HistoricalEventId EventId => HistoricalEventId;
    public StructureId StructureId { get; }
    public string Role { get; }

    public void Validate()
    {
        WorldIdValidation.RequirePositive(HistoricalEventId.Value, nameof(HistoricalEventId));
        WorldIdValidation.RequirePositive(StructureId.Value, nameof(StructureId));
        if (!string.Equals(Role, "subject", StringComparison.Ordinal)) throw new ArgumentException("Historical structure link role is unsupported.");
    }
}

/// <summary>The canonical singleton state used by the history and statistics scheduler.</summary>
public sealed record HistoryState
{
    public HistoryState(long historyStartMinute = 0, long historyStartEventId = 1, long periodStartMinute = 0,
        long birthsSinceSample = 0, long deathsSinceSample = 0, long foodProducedSinceSample = 0,
        long foodConsumedSinceSample = 0, bool activeFoodShortage = false, int populationMilestoneWatermark = 0)
    {
        HistoryStartMinute = historyStartMinute;
        HistoryStartEventId = historyStartEventId;
        PeriodStartMinute = periodStartMinute;
        BirthsSinceSample = birthsSinceSample;
        DeathsSinceSample = deathsSinceSample;
        FoodProducedSinceSample = foodProducedSinceSample;
        FoodConsumedSinceSample = foodConsumedSinceSample;
        ActiveFoodShortage = activeFoodShortage;
        PopulationMilestoneWatermark = populationMilestoneWatermark;
    }

    public long HistoryStartMinute { get; set; }
    public long HistoryStartEventId { get; set; }
    public long PeriodStartMinute { get; set; }
    public long BirthsSinceSample { get; set; }
    public long DeathsSinceSample { get; set; }
    public long FoodProducedSinceSample { get; set; }
    public long FoodConsumedSinceSample { get; set; }
    public bool ActiveFoodShortage { get; set; }
    public int PopulationMilestoneWatermark { get; set; }
    public bool ActiveShortage { get => ActiveFoodShortage; set => ActiveFoodShortage = value; }

    public HistoryState Validate(long currentMinute = long.MaxValue)
    {
        if (HistoryStartMinute < 0 || HistoryStartMinute > currentMinute || HistoryStartEventId <= 0 || PeriodStartMinute < HistoryStartMinute || PeriodStartMinute > currentMinute || BirthsSinceSample < 0 || DeathsSinceSample < 0 || FoodProducedSinceSample < 0 || FoodConsumedSinceSample < 0 || PopulationMilestoneWatermark < 0) throw new ArgumentException("History state is not canonical.");
        return this;
    }
}

public sealed record StatisticsSample
{
    public StatisticsSample(long worldMinute, long periodStartMinute, int population, long birthsPeriod, long deathsPeriod,
        int foodStored, long foodProducedPeriod, long foodConsumedPeriod, int woodStored, int stoneStored,
        int shelterCapacity, int averageHealth, int averageHunger)
    {
        WorldMinute = worldMinute; PeriodStartMinute = periodStartMinute; Population = population; BirthsPeriod = birthsPeriod; DeathsPeriod = deathsPeriod;
        FoodStored = foodStored; FoodProducedPeriod = foodProducedPeriod; FoodConsumedPeriod = foodConsumedPeriod; WoodStored = woodStored; StoneStored = stoneStored;
        ShelterCapacity = shelterCapacity; AverageHealth = averageHealth; AverageHunger = averageHunger;
    }
    public long WorldMinute { get; }
    public long PeriodStartMinute { get; }
    public int Population { get; }
    public long BirthsPeriod { get; }
    public long DeathsPeriod { get; }
    public int FoodStored { get; }
    public long FoodProducedPeriod { get; }
    public long FoodConsumedPeriod { get; }
    public int WoodStored { get; }
    public int StoneStored { get; }
    public int ShelterCapacity { get; }
    public int AverageHealth { get; }
    public int AverageHunger { get; }
    public void Validate(long currentMinute = long.MaxValue)
    {
        if (WorldMinute < 0 || WorldMinute > currentMinute || PeriodStartMinute < 0 || PeriodStartMinute > WorldMinute || Population < 0 || BirthsPeriod < 0 || DeathsPeriod < 0 || FoodStored < 0 || FoodProducedPeriod < 0 || FoodConsumedPeriod < 0 || WoodStored < 0 || StoneStored < 0 || ShelterCapacity < 0 || AverageHealth is < 0 or > 10000 || AverageHunger is < 0 or > 10000) throw new ArgumentException("Statistics sample is not canonical.");
    }
}

public sealed record CitizenMemory
{
    public CitizenMemory(CitizenId citizenId, HistoricalEventId historicalEventId, MemoryType memoryType, HistoricalImportance importance, int emotionalValence, long createdMinute)
    {
        CitizenId = citizenId; HistoricalEventId = historicalEventId; MemoryType = memoryType; Importance = importance; EmotionalValence = emotionalValence; CreatedMinute = createdMinute;
    }
    public CitizenMemory(CitizenId citizenId, HistoricalEventId historicalEventId, CitizenMemoryType memoryType, HistoricalImportance importance, int emotionalValence, long createdMinute)
        : this(citizenId, historicalEventId, (MemoryType)(int)memoryType, importance, emotionalValence, createdMinute) { }
    public CitizenId CitizenId { get; }
    public HistoricalEventId HistoricalEventId { get; }
    public HistoricalEventId EventId => HistoricalEventId;
    public MemoryType MemoryType { get; }
    public HistoricalImportance Importance { get; }
    public int EmotionalValence { get; }
    public long CreatedMinute { get; }
    public void Validate(long currentMinute = long.MaxValue)
    {
        WorldIdValidation.RequirePositive(CitizenId.Value, nameof(CitizenId)); WorldIdValidation.RequirePositive(HistoricalEventId.Value, nameof(HistoricalEventId));
        if (!Enum.IsDefined(MemoryType) || !Enum.IsDefined(Importance) || CreatedMinute < 0 || CreatedMinute > currentMinute || EmotionalValence is < -10000 or > 10000) throw new ArgumentException("Citizen memory is not canonical.");
    }
}

/// <summary>Fixed-order canonical payloads for historical events.</summary>
public static class HistoricalEventPayloads
{
    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
    public static string WorldCreated(WorldSeed seed) => $"{{\"seed\":\"{seed.Value.ToString(CultureInfo.InvariantCulture)}\"}}";
    public static string SettlementFounded(int founderCount) => $"{{\"founderCount\":{founderCount.ToString(CultureInfo.InvariantCulture)}}}";
    public static string CitizenBorn(HouseholdId? householdId) => householdId is null ? "{}" : $"{{\"householdId\":\"{Id(householdId.Value.Value)}\"}}";
    public static string CitizenDied(string cause, int ageYears) => $"{{\"cause\":{JsonSerializer.Serialize(cause)},\"ageYears\":{ageYears.ToString(CultureInfo.InvariantCulture)}}}";
    public static string PartnershipFormed(HouseholdId householdId) => $"{{\"householdId\":\"{Id(householdId.Value)}\"}}";
    public static string HouseholdCreated(HouseholdId householdId) => PartnershipFormed(householdId);
    public static string Relationship(int familiarity, int affinity, int trust, int conflict) => $"{{\"familiarity\":{familiarity.ToString(CultureInfo.InvariantCulture)},\"affinity\":{affinity.ToString(CultureInfo.InvariantCulture)},\"trust\":{trust.ToString(CultureInfo.InvariantCulture)},\"conflict\":{conflict.ToString(CultureInfo.InvariantCulture)}}}";
    public static string StructureStarted(StructureType type, int requiredWood, int requiredStone, int requiredWork) => $"{{\"structureType\":{JsonSerializer.Serialize(type.ToString())},\"requiredWood\":{requiredWood.ToString(CultureInfo.InvariantCulture)},\"requiredStone\":{requiredStone.ToString(CultureInfo.InvariantCulture)},\"requiredWork\":{requiredWork.ToString(CultureInfo.InvariantCulture)}}}";
    public static string StructureCompleted(StructureType type) => $"{{\"structureType\":{JsonSerializer.Serialize(type.ToString())}}}";
    public static string PopulationMilestone(int population) => $"{{\"population\":{population.ToString(CultureInfo.InvariantCulture)}}}";
    public static string ResourceShortage(ResourceType resourceType, int quantity, int livingPopulation, bool preexistingAtHistoryStart = false) => $"{{\"resourceType\":{JsonSerializer.Serialize(resourceType.ToString())},\"quantity\":{quantity.ToString(CultureInfo.InvariantCulture)},\"livingPopulation\":{livingPopulation.ToString(CultureInfo.InvariantCulture)},\"preexistingAtHistoryStart\":{(preexistingAtHistoryStart ? "true" : "false")}}}";
    public static string SpecializationChanged(string from, string to) => $"{{\"from\":{JsonSerializer.Serialize(from)},\"to\":{JsonSerializer.Serialize(to)}}}";
    public static string SeasonStarted(WorldSeason season, long year) => $"{{\"season\":{JsonSerializer.Serialize(season.ToString())},\"year\":{year.ToString(CultureInfo.InvariantCulture)}}}";
    public static string ExpeditionDeparted(long partyId, long householdId, long originSettlementId, int destinationX, int destinationY, int travelerCount) =>
        MigrationExpedition(partyId, householdId, originSettlementId, destinationX, destinationY, travelerCount);
    public static string ExpeditionReturned(long partyId, long householdId, long originSettlementId, int destinationX, int destinationY, int travelerCount) =>
        MigrationExpedition(partyId, householdId, originSettlementId, destinationX, destinationY, travelerCount);
    public static string ExpeditionLost(long partyId, long householdId, long originSettlementId, int destinationX, int destinationY, int travelerCount) =>
        MigrationExpedition(partyId, householdId, originSettlementId, destinationX, destinationY, travelerCount);
    public static string DaughterSettlementFounded(long settlementId, long partyId, long householdId, int founderCount) =>
        $"{{\"settlementId\":\"{Id(RequirePositiveId(settlementId, nameof(settlementId)))}\",\"partyId\":\"{Id(RequirePositiveId(partyId, nameof(partyId)))}\",\"householdId\":\"{Id(RequirePositiveId(householdId, nameof(householdId)))}\",\"founderCount\":{RequirePositiveInt(founderCount, nameof(founderCount)).ToString(CultureInfo.InvariantCulture)}}}";
    public static string HouseholdRelocated(long householdId, long originSettlementId, long destinationSettlementId, int travelerCount) =>
        $"{{\"householdId\":\"{Id(RequirePositiveId(householdId, nameof(householdId)))}\",\"originSettlementId\":\"{Id(RequireDistinctSettlementIds(originSettlementId, destinationSettlementId))}\",\"destinationSettlementId\":\"{Id(RequirePositiveId(destinationSettlementId, nameof(destinationSettlementId)))}\",\"travelerCount\":{RequirePositiveInt(travelerCount, nameof(travelerCount)).ToString(CultureInfo.InvariantCulture)}}}";
    public static string FamilyVisitDeparted(long visitorId, long relativeId, long originSettlementId, long destinationSettlementId) =>
        FamilyVisit(visitorId, relativeId, originSettlementId, destinationSettlementId);
    public static string FamilyVisitReturned(long visitorId, long relativeId, long originSettlementId, long destinationSettlementId) =>
        FamilyVisit(visitorId, relativeId, originSettlementId, destinationSettlementId);

    /// <summary>Validates the event-specific canonical JSON representation.</summary>
    public static void Validate(HistoricalEventType eventType, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Canonical payload must be a JSON object.", nameof(payloadJson));

            switch (eventType)
            {
                case HistoricalEventType.WorldCreated:
                    RequireCanonical(payloadJson, root, ["seed"], () =>
                    {
                        var value = RequiredString(root, "seed");
                        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) || seed.ToString(CultureInfo.InvariantCulture) != value) throw new ArgumentException("WorldCreated seed must be a canonical UInt64 decimal string.", nameof(payloadJson));
                        return WorldCreated(new WorldSeed(seed));
                    });
                    break;
                case HistoricalEventType.SettlementFounded:
                    RequireCanonical(payloadJson, root, ["founderCount"], () => SettlementFounded(RequiredNonNegativeInt(root, "founderCount")));
                    break;
                case HistoricalEventType.CitizenBorn:
                    if (payloadJson == "{}") break;
                    RequireCanonical(payloadJson, root, ["householdId"], () => CitizenBorn(new HouseholdId(RequiredPositiveId(root, "householdId"))));
                    break;
                case HistoricalEventType.CitizenDied:
                    RequireCanonical(payloadJson, root, ["cause", "ageYears"], () =>
                    {
                        var cause = RequiredString(root, "cause");
                        if (cause is not ("starvation" or "exhaustion" or "exposure" or "deprivation" or "natural")) throw new ArgumentException("CitizenDied cause is not canonical.", nameof(payloadJson));
                        return CitizenDied(cause, RequiredNonNegativeInt(root, "ageYears"));
                    });
                    break;
                case HistoricalEventType.PartnershipFormed:
                case HistoricalEventType.HouseholdCreated:
                    RequireCanonical(payloadJson, root, ["householdId"], () => PartnershipFormed(new HouseholdId(RequiredPositiveId(root, "householdId"))));
                    break;
                case HistoricalEventType.FriendshipFormed:
                case HistoricalEventType.RivalryFormed:
                    RequireCanonical(payloadJson, root, ["familiarity", "affinity", "trust", "conflict"], () => Relationship(
                        RequiredRange(root, "familiarity", 0, 10000), RequiredRange(root, "affinity", -10000, 10000),
                        RequiredRange(root, "trust", 0, 10000), RequiredRange(root, "conflict", 0, 10000)));
                    break;
                case HistoricalEventType.StructureStarted:
                    RequireCanonical(payloadJson, root, ["structureType", "requiredWood", "requiredStone", "requiredWork"], () =>
                    {
                        var type = RequiredStructureType(root, "structureType");
                        var wood = RequiredNonNegativeInt(root, "requiredWood");
                        var stone = RequiredNonNegativeInt(root, "requiredStone");
                        var work = RequiredPositiveInt(root, "requiredWork");
                        if (!StructureDefinitions.HasCanonicalRequirements(type, wood, stone, work)) throw new ArgumentException("StructureStarted requirements are not canonical.", nameof(payloadJson));
                        return StructureStarted(type, wood, stone, work);
                    });
                    break;
                case HistoricalEventType.StructureCompleted:
                    RequireCanonical(payloadJson, root, ["structureType"], () => StructureCompleted(RequiredStructureType(root, "structureType")));
                    break;
                case HistoricalEventType.PopulationMilestone:
                    RequireCanonical(payloadJson, root, ["population"], () => PopulationMilestone(RequiredPositiveInt(root, "population")));
                    break;
                case HistoricalEventType.ResourceShortageStarted:
                case HistoricalEventType.ResourceShortageEnded:
                    RequireCanonical(payloadJson, root, ["resourceType", "quantity", "livingPopulation", "preexistingAtHistoryStart"], () => ResourceShortage(
                        RequiredResourceType(root, "resourceType"), RequiredNonNegativeInt(root, "quantity"),
                        RequiredNonNegativeInt(root, "livingPopulation"), RequiredBoolean(root, "preexistingAtHistoryStart")));
                    break;
                case HistoricalEventType.CitizenSpecializationChanged:
                    RequireCanonical(payloadJson, root, ["from", "to"], () =>
                    {
                        var from = RequiredOccupation(root, "from");
                        var to = RequiredOccupation(root, "to");
                        if (string.Equals(from, to, StringComparison.Ordinal)) throw new ArgumentException("Specialization change must change the occupation label.", nameof(payloadJson));
                        return SpecializationChanged(from, to);
                    });
                    break;
                case HistoricalEventType.SeasonStarted:
                    RequireCanonical(payloadJson, root, ["season", "year"], () =>
                    {
                        var season = RequiredSeason(root, "season");
                        return SeasonStarted(season, RequiredNonNegativeLong(root, "year"));
                    });
                    break;
                case HistoricalEventType.ExpeditionDeparted:
                case HistoricalEventType.ExpeditionReturned:
                case HistoricalEventType.ExpeditionLost:
                    RequireCanonical(payloadJson, root, ["partyId", "householdId", "originSettlementId", "destinationX", "destinationY", "travelerCount"], () =>
                        MigrationExpedition(RequiredPositiveId(root, "partyId"), RequiredPositiveId(root, "householdId"),
                            RequiredPositiveId(root, "originSettlementId"), RequiredNonNegativeInt(root, "destinationX"),
                            RequiredNonNegativeInt(root, "destinationY"), RequiredPositiveInt(root, "travelerCount")));
                    break;
                case HistoricalEventType.DaughterSettlementFounded:
                    RequireCanonical(payloadJson, root, ["settlementId", "partyId", "householdId", "founderCount"], () =>
                        DaughterSettlementFounded(RequiredPositiveId(root, "settlementId"), RequiredPositiveId(root, "partyId"),
                            RequiredPositiveId(root, "householdId"), RequiredPositiveInt(root, "founderCount")));
                    break;
                case HistoricalEventType.HouseholdRelocated:
                    RequireCanonical(payloadJson, root, ["householdId", "originSettlementId", "destinationSettlementId", "travelerCount"], () =>
                        HouseholdRelocated(RequiredPositiveId(root, "householdId"), RequiredPositiveId(root, "originSettlementId"),
                            RequiredPositiveId(root, "destinationSettlementId"), RequiredPositiveInt(root, "travelerCount")));
                    break;
                case HistoricalEventType.FamilyVisitDeparted:
                case HistoricalEventType.FamilyVisitReturned:
                    RequireCanonical(payloadJson, root, ["visitorId", "relativeId", "originSettlementId", "destinationSettlementId"], () =>
                        FamilyVisit(RequiredPositiveId(root, "visitorId"), RequiredPositiveId(root, "relativeId"),
                            RequiredPositiveId(root, "originSettlementId"), RequiredPositiveId(root, "destinationSettlementId")));
                    break;
                default:
                    throw new ArgumentException("Historical event type is unsupported.", nameof(eventType));
            }
        }
        catch (JsonException exception) { throw new ArgumentException("Canonical payload must be valid JSON.", nameof(payloadJson), exception); }
    }

    public static bool IsCanonical(HistoricalEventType eventType, string payloadJson)
    {
        try { Validate(eventType, payloadJson); return true; }
        catch (ArgumentException) { return false; }
    }

    private static void RequireCanonical(string payload, JsonElement root, IReadOnlyList<string> names, Func<string> builder)
    {
        var actual = root.EnumerateObject().Select(x => x.Name).ToArray();
        if (!actual.SequenceEqual(names, StringComparer.Ordinal) || !string.Equals(payload, builder(), StringComparison.Ordinal)) throw new ArgumentException("Historical event payload is not canonical.", nameof(payload));
    }
    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length == 0) throw new ArgumentException($"Payload field '{name}' is required.");
        return text;
    }
    private static long RequiredPositiveId(JsonElement root, string name)
    {
        var text = RequiredString(root, name);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 || value.ToString(CultureInfo.InvariantCulture) != text) throw new ArgumentException($"Payload field '{name}' must be a canonical positive decimal ID.");
        return value;
    }
    private static int RequiredNonNegativeInt(JsonElement root, string name) => RequiredRange(root, name, 0, int.MaxValue);
    private static int RequiredPositiveInt(JsonElement root, string name) => RequiredRange(root, name, 1, int.MaxValue);
    private static string MigrationExpedition(long partyId, long householdId, long originSettlementId, int destinationX, int destinationY, int travelerCount) =>
        $"{{\"partyId\":\"{Id(RequirePositiveId(partyId, nameof(partyId)))}\",\"householdId\":\"{Id(RequirePositiveId(householdId, nameof(householdId)))}\",\"originSettlementId\":\"{Id(RequirePositiveId(originSettlementId, nameof(originSettlementId)))}\",\"destinationX\":{RequireNonNegativeInt(destinationX, nameof(destinationX)).ToString(CultureInfo.InvariantCulture)},\"destinationY\":{RequireNonNegativeInt(destinationY, nameof(destinationY)).ToString(CultureInfo.InvariantCulture)},\"travelerCount\":{RequirePositiveInt(travelerCount, nameof(travelerCount)).ToString(CultureInfo.InvariantCulture)}}}";
    private static string FamilyVisit(long visitorId, long relativeId, long originSettlementId, long destinationSettlementId) =>
        $"{{\"visitorId\":\"{Id(RequirePositiveId(visitorId, nameof(visitorId)))}\",\"relativeId\":\"{Id(RequirePositiveId(relativeId, nameof(relativeId)))}\",\"originSettlementId\":\"{Id(RequireDistinctSettlementIds(originSettlementId, destinationSettlementId))}\",\"destinationSettlementId\":\"{Id(RequirePositiveId(destinationSettlementId, nameof(destinationSettlementId)))}\"}}";
    private static long RequireDistinctSettlementIds(long originSettlementId, long destinationSettlementId)
    {
        RequirePositiveId(originSettlementId, nameof(originSettlementId));
        RequirePositiveId(destinationSettlementId, nameof(destinationSettlementId));
        if (originSettlementId == destinationSettlementId) throw new ArgumentException("Origin and destination settlements must be distinct.");
        return originSettlementId;
    }
    private static long RequirePositiveId(long value, string parameterName) => value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName, "ID must be positive.");
    private static int RequireNonNegativeInt(int value, string parameterName) => value >= 0 ? value : throw new ArgumentOutOfRangeException(parameterName, "Coordinate must be non-negative.");
    private static int RequirePositiveInt(int value, string parameterName) => value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName, "Count must be positive.");
    private static long RequiredNonNegativeLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result) || result < 0 || value.GetRawText() != result.ToString(CultureInfo.InvariantCulture)) throw new ArgumentException($"Payload field '{name}' must be a canonical non-negative integer.");
        return result;
    }
    private static int RequiredRange(JsonElement root, string name, int minimum, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result < minimum || result > maximum || value.GetRawText() != result.ToString(CultureInfo.InvariantCulture)) throw new ArgumentException($"Payload field '{name}' is outside its canonical range.");
        return result;
    }
    private static bool RequiredBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)) throw new ArgumentException($"Payload field '{name}' must be a boolean.");
        return value.GetBoolean();
    }
    private static StructureType RequiredStructureType(JsonElement root, string name) => Enum.TryParse<StructureType>(RequiredString(root, name), ignoreCase: false, out var value) && Enum.IsDefined(value) ? value : throw new ArgumentException($"Payload field '{name}' is not a canonical structure type.");
    private static ResourceType RequiredResourceType(JsonElement root, string name) => Enum.TryParse<ResourceType>(RequiredString(root, name), ignoreCase: false, out var value) && Enum.IsDefined(value) ? value : throw new ArgumentException($"Payload field '{name}' is not a canonical resource type.");
    private static WorldSeason RequiredSeason(JsonElement root, string name) => Enum.TryParse<WorldSeason>(RequiredString(root, name), ignoreCase: false, out var value) && Enum.IsDefined(value) ? value : throw new ArgumentException($"Payload field '{name}' is not a canonical season.");
    private static string RequiredOccupation(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        if (value is not (CitizenOccupation.Generalist or "Forager" or "Lumberjack" or "Stoneworker" or "Builder" or "Hauler")) throw new ArgumentException($"Payload field '{name}' is not a canonical occupation.");
        return value;
    }
}

internal static class CanonicalJson
{
    public static void RequireObject(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Canonical payload must be a JSON object.");
        }
        catch (JsonException exception) { throw new ArgumentException("Canonical payload must be valid JSON.", exception); }
    }
}
