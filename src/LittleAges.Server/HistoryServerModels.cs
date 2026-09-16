using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Server;

public sealed record ServerHistoricalCitizenLinkSnapshot(string EventId, string CitizenId, string Role);
public sealed record ServerHistoricalStructureLinkSnapshot(string EventId, string StructureId, string Role);
public sealed record ServerStatisticsSampleSnapshot(
    long WorldMinute, long PeriodStartMinute, int Population, long BirthsPeriod, long DeathsPeriod,
    int FoodStored, long FoodProducedPeriod, long FoodConsumedPeriod, int WoodStored, int StoneStored,
    int ShelterCapacity, int AverageHealth, int AverageHunger)
{
    public ServerStatisticsSampleSnapshot(StatisticsSample sample)
        : this(sample.WorldMinute, sample.PeriodStartMinute, sample.Population, sample.BirthsPeriod, sample.DeathsPeriod, sample.FoodStored, sample.FoodProducedPeriod, sample.FoodConsumedPeriod, sample.WoodStored, sample.StoneStored, sample.ShelterCapacity, sample.AverageHealth, sample.AverageHunger) { }
}

public sealed record ServerCitizenMemorySnapshot(
    string CitizenId, string EventId, MemoryType MemoryType, HistoricalImportance Importance, int EmotionalValence, long CreatedMinute)
{
    public ServerCitizenMemorySnapshot(CitizenMemory memory)
        : this(memory.CitizenId.Value.ToString(CultureInfo.InvariantCulture), memory.HistoricalEventId.Value.ToString(CultureInfo.InvariantCulture), memory.MemoryType, memory.Importance, memory.EmotionalValence, memory.CreatedMinute) { }
}

public sealed record ServerHistoricalEventSnapshot
{
    public ServerHistoricalEventSnapshot(HistoricalEvent item, IEnumerable<HistoricalEventCitizenLink> citizenLinks, IEnumerable<HistoricalEventStructureLink> structureLinks,
        IReadOnlyDictionary<long, string>? citizenNames = null, IReadOnlyDictionary<long, StructureType>? structureTypes = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        EventId = item.Id.Value.ToString(CultureInfo.InvariantCulture);
        HistoricalEventId = EventId;
        WorldMinute = item.WorldMinute;
        EventType = item.EventType;
        Importance = item.Importance;
        Origin = item.Origin;
        Location = item.Location;
        PayloadJson = item.PayloadJson;
        SchemaVersion = item.SchemaVersion;
        var citizenLinkArray = (citizenLinks ?? Array.Empty<HistoricalEventCitizenLink>()).ToArray();
        var structureLinkArray = (structureLinks ?? Array.Empty<HistoricalEventStructureLink>()).ToArray();
        CitizenLinks = Array.AsReadOnly(citizenLinkArray.OrderBy(x => x.CitizenId.Value).ThenBy(x => x.Role, StringComparer.Ordinal).Select(x => new ServerHistoricalCitizenLinkSnapshot(EventId, x.CitizenId.Value.ToString(CultureInfo.InvariantCulture), x.Role)).ToArray());
        StructureLinks = Array.AsReadOnly(structureLinkArray.OrderBy(x => x.StructureId.Value).ThenBy(x => x.Role, StringComparer.Ordinal).Select(x => new ServerHistoricalStructureLinkSnapshot(EventId, x.StructureId.Value.ToString(CultureInfo.InvariantCulture), x.Role)).ToArray());
        Summary = HistoricalEventSummary.Render(item, citizenLinkArray, structureLinkArray, citizenNames, structureTypes);
    }
    public string EventId { get; }
    public string HistoricalEventId { get; }
    public long WorldMinute { get; }
    public HistoricalEventType EventType { get; }
    public HistoricalImportance Importance { get; }
    public HistoricalEventOrigin Origin { get; }
    public TileCoordinate? Location { get; }
    public string PayloadJson { get; }
    public int SchemaVersion { get; }
    /// <summary>Deterministic factual presentation text derived from the event, payload, and links.</summary>
    public string Summary { get; }
    public IReadOnlyList<ServerHistoricalCitizenLinkSnapshot> CitizenLinks { get; }
    public IReadOnlyList<ServerHistoricalStructureLinkSnapshot> StructureLinks { get; }
}

public sealed record ServerHistoryStateSnapshot(
    long HistoryStartMinute, string HistoryStartEventId, long PeriodStartMinute, long BirthsSinceSample, long DeathsSinceSample,
    long FoodProducedSinceSample, long FoodConsumedSinceSample, bool ActiveFoodShortage, int PopulationMilestoneWatermark)
{
    public ServerHistoryStateSnapshot(HistoryState state)
        : this(state.HistoryStartMinute, state.HistoryStartEventId.ToString(CultureInfo.InvariantCulture), state.PeriodStartMinute, state.BirthsSinceSample, state.DeathsSinceSample, state.FoodProducedSinceSample, state.FoodConsumedSinceSample, state.ActiveFoodShortage, state.PopulationMilestoneWatermark) { }
}

public sealed record ServerHistorySnapshot
{
    public ServerHistorySnapshot(HistoryReadSnapshot snapshot, IReadOnlyList<ServerCitizenSnapshot>? citizens = null, IReadOnlyList<ServerStructureSnapshot>? structures = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var citizenNames = (citizens ?? Array.Empty<ServerCitizenSnapshot>()).ToDictionary(x => long.Parse(x.CitizenId, CultureInfo.InvariantCulture), x => x.Name);
        var structureTypes = (structures ?? Array.Empty<ServerStructureSnapshot>()).ToDictionary(x => long.Parse(x.StructureId, CultureInfo.InvariantCulture), x => x.Type);
        HistoryVersion = snapshot.Version;
        State = new ServerHistoryStateSnapshot(snapshot.State);
        Events = Array.AsReadOnly(snapshot.Events.Select(item => new ServerHistoricalEventSnapshot(item, snapshot.CitizenLinks.Where(x => x.HistoricalEventId == item.Id), snapshot.StructureLinks.Where(x => x.HistoricalEventId == item.Id), citizenNames, structureTypes)).ToArray());
        Statistics = Array.AsReadOnly(snapshot.Statistics.Select(static item => new ServerStatisticsSampleSnapshot(item)).ToArray());
        Memories = Array.AsReadOnly(snapshot.Memories.Select(static item => new ServerCitizenMemorySnapshot(item)).ToArray());
    }
    public int HistoryVersion { get; }
    public ServerHistoryStateSnapshot State { get; }
    public IReadOnlyList<ServerHistoricalEventSnapshot> Events { get; }
    public IReadOnlyList<ServerStatisticsSampleSnapshot> Statistics { get; }
    public IReadOnlyList<ServerCitizenMemorySnapshot> Memories { get; }
}

public static class HistoricalEventSummary
{
    public static string Render(HistoricalEvent item, IEnumerable<HistoricalEventCitizenLink>? citizenLinks = null,
        IEnumerable<HistoricalEventStructureLink>? structureLinks = null, IReadOnlyDictionary<long, string>? citizenNames = null,
        IReadOnlyDictionary<long, StructureType>? structureTypes = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var citizens = (citizenLinks ?? Array.Empty<HistoricalEventCitizenLink>()).ToArray();
        var structures = (structureLinks ?? Array.Empty<HistoricalEventStructureLink>()).ToArray();
        var name = (long id) => citizenNames is not null && citizenNames.TryGetValue(id, out var value) ? value : id.ToString(CultureInfo.InvariantCulture);
        var subjectId = citizens.FirstOrDefault(x => x.Role == "subject")?.CitizenId.Value;
        var subjectName = subjectId is { } subject ? name(subject) : "Citizen";
        var pair = citizens.Where(x => x.Role is "partner" or "participant" or "member").OrderBy(x => x.CitizenId.Value).Select(x => name(x.CitizenId.Value)).ToArray();
        var parentNames = citizens.Where(x => x.Role == "parent").OrderBy(x => x.CitizenId.Value).Select(x => name(x.CitizenId.Value)).ToArray();
        using var document = JsonDocument.Parse(item.PayloadJson);
        var payload = document.RootElement;
        return item.EventType switch
        {
            HistoricalEventType.WorldCreated => $"The world was created with seed {payload.GetProperty("seed").GetString()}.",
            HistoricalEventType.SettlementFounded => $"The settlement was founded by {payload.GetProperty("founderCount").GetInt32().ToString(CultureInfo.InvariantCulture)} founders.",
            HistoricalEventType.CitizenBorn => parentNames.Length == 0 ? $"{subjectName} was born." : $"{subjectName} was born to {string.Join(" and ", parentNames)}.",
            HistoricalEventType.CitizenDied => $"{subjectName} died ({payload.GetProperty("cause").GetString()}).",
            HistoricalEventType.PartnershipFormed => $"{string.Join(" and ", pair)} formed a partnership.",
            HistoricalEventType.FriendshipFormed => $"{string.Join(" and ", pair)} became friends.",
            HistoricalEventType.RivalryFormed => $"{string.Join(" and ", pair)} became rivals.",
            HistoricalEventType.HouseholdCreated => pair.Length == 0 ? $"Household {payload.GetProperty("householdId").GetString()} was created." : $"Household {payload.GetProperty("householdId").GetString()} was created for {string.Join(" and ", pair)}.",
            HistoricalEventType.StructureStarted => $"{StructureLabel(structures, payload, structureTypes)} was started.",
            HistoricalEventType.StructureCompleted => $"{StructureLabel(structures, payload, structureTypes)} was completed.",
            HistoricalEventType.PopulationMilestone => $"The living population reached {payload.GetProperty("population").GetInt32().ToString(CultureInfo.InvariantCulture)}.",
            HistoricalEventType.ResourceShortageStarted => payload.GetProperty("preexistingAtHistoryStart").GetBoolean() ? "A food shortage was already present when historical tracking began." : "A food shortage began.",
            HistoricalEventType.ResourceShortageEnded => "The food shortage ended.",
            HistoricalEventType.CitizenSpecializationChanged => $"{subjectName} became a {payload.GetProperty("to").GetString()}.",
            HistoricalEventType.SeasonStarted => $"{payload.GetProperty("season").GetString()} began in Year {payload.GetProperty("year").GetInt64().ToString(CultureInfo.InvariantCulture)}.",
            _ => item.EventType.ToString()
        };
    }

    private static string StructureLabel(IReadOnlyList<HistoricalEventStructureLink> links, JsonElement payload, IReadOnlyDictionary<long, StructureType>? structureTypes)
    {
        var structure = links.OrderBy(x => x.StructureId.Value).FirstOrDefault();
        var id = structure?.StructureId.Value.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var type = payload.TryGetProperty("structureType", out var typeValue) ? typeValue.GetString() : structure is not null && structureTypes is not null && structureTypes.TryGetValue(structure.StructureId.Value, out var known) ? known.ToString() : "structure";
        return $"{type} {id}";
    }
}

public sealed record ServerCitizenBiographySnapshot(
    ServerCitizenSnapshot Citizen,
    IReadOnlyList<ServerHistoricalEventSnapshot> Events,
    IReadOnlyList<ServerCitizenMemorySnapshot> Memories,
    IReadOnlyList<string> ParentIds,
    string? PartnerId,
    IReadOnlyList<string> ChildrenIds,
    long? BirthMinute,
    long? DeathMinute,
    string? DeathCause);
