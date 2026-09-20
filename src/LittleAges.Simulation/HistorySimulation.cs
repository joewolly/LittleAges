using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Immutable history observation published with a simulation read snapshot.</summary>
public sealed record HistoryReadSnapshot
{
    public HistoryReadSnapshot(int version, HistoryState state, IEnumerable<HistoricalEvent> events,
        IEnumerable<HistoricalEventCitizenLink> citizenLinks, IEnumerable<HistoricalEventStructureLink> structureLinks,
        IEnumerable<StatisticsSample> statistics, IEnumerable<CitizenMemory> memories)
    {
        Version = version;
        State = CloneState(state);
        var eventArray = (events ?? throw new ArgumentNullException(nameof(events))).ToArray();
        var citizenLinkArray = (citizenLinks ?? throw new ArgumentNullException(nameof(citizenLinks))).ToArray();
        var structureLinkArray = (structureLinks ?? throw new ArgumentNullException(nameof(structureLinks))).ToArray();
        var statisticsArray = (statistics ?? throw new ArgumentNullException(nameof(statistics))).ToArray();
        var memoryArray = (memories ?? throw new ArgumentNullException(nameof(memories))).ToArray();
        RequireOrder(eventArray, eventArray.OrderBy(x => x.Id.Value), "historical events");
        RequireOrder(citizenLinkArray, citizenLinkArray.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.CitizenId.Value).ThenBy(x => x.Role, StringComparer.Ordinal), "historical citizen links");
        RequireOrder(structureLinkArray, structureLinkArray.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.StructureId.Value).ThenBy(x => x.Role, StringComparer.Ordinal), "historical structure links");
        RequireOrder(statisticsArray, statisticsArray.OrderBy(x => x.WorldMinute), "statistics samples");
        RequireOrder(memoryArray, memoryArray.OrderBy(x => x.CitizenId.Value).ThenBy(x => x.HistoricalEventId.Value).ThenBy(x => x.MemoryType), "memories");
        Events = Array.AsReadOnly(eventArray);
        CitizenLinks = Array.AsReadOnly(citizenLinkArray);
        StructureLinks = Array.AsReadOnly(structureLinkArray);
        Statistics = Array.AsReadOnly(statisticsArray);
        Memories = Array.AsReadOnly(memoryArray);
    }

    public int Version { get; }
    public int HistoryVersion => Version;
    public HistoryState State { get; }
    public HistoryState HistoryState => State;
    public IReadOnlyList<HistoricalEvent> Events { get; }
    public IReadOnlyList<HistoricalEvent> HistoricalEvents => Events;
    public IReadOnlyList<HistoricalEventCitizenLink> CitizenLinks { get; }
    public IReadOnlyList<HistoricalEventCitizenLink> HistoricalEventCitizens => CitizenLinks;
    public IReadOnlyList<HistoricalEventStructureLink> StructureLinks { get; }
    public IReadOnlyList<HistoricalEventStructureLink> HistoricalEventStructures => StructureLinks;
    public IReadOnlyList<StatisticsSample> Statistics { get; }
    public IReadOnlyList<StatisticsSample> StatisticsSamples => Statistics;
    public IReadOnlyList<CitizenMemory> Memories { get; }

    private static HistoryState CloneState(HistoryState value) => new(value.HistoryStartMinute, value.HistoryStartEventId, value.PeriodStartMinute, value.BirthsSinceSample, value.DeathsSinceSample, value.FoodProducedSinceSample, value.FoodConsumedSinceSample, value.ActiveFoodShortage, value.PopulationMilestoneWatermark);

    private static void RequireOrder<T>(IReadOnlyList<T> actual, IEnumerable<T> ordered, string name)
    {
        var expected = ordered.ToArray();
        if (actual.Count != expected.Length || actual.Zip(expected).Any(pair => !EqualityComparer<T>.Default.Equals(pair.First, pair.Second))) throw new ArgumentException($"{name} must be in canonical order.", name);
    }
}

public sealed partial class SimulationEngine
{
    private const long StatisticsPeriodMinutes = 30L * WorldCalendar.MinutesPerDay;
    private static readonly int[] PopulationMilestones = [25, 50, 100, 250, 500, 1000, 2000];
    private readonly List<HistoricalEvent> _historicalEvents = [];
    private readonly List<HistoricalEventCitizenLink> _historicalEventCitizens = [];
    private readonly List<HistoricalEventStructureLink> _historicalEventStructures = [];
    private readonly List<StatisticsSample> _statisticsSamples = [];
    private readonly List<CitizenMemory> _memories = [];
    private readonly Dictionary<long, string> _knownOccupations = new();
    private readonly HashSet<(HistoricalEventType Type, long CitizenId)> _subjectHistoryKeys = [];
    private readonly HashSet<(HistoricalEventType Type, long First, long Second)> _pairHistoryKeys = [];
    private readonly HashSet<(HistoricalEventType Type, long StructureId)> _structureHistoryKeys = [];
    private HistoryState? _historyState;

    public HistoryState? HistoryState => _historyState is null ? null : CloneHistoryState(_historyState);
    public IReadOnlyList<HistoricalEvent> HistoricalEvents => Array.AsReadOnly(_historicalEvents.OrderBy(x => x.Id.Value).ToArray());
    public IReadOnlyList<HistoricalEventCitizenLink> HistoricalEventCitizens => Array.AsReadOnly(_historicalEventCitizens.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.CitizenId.Value).ThenBy(x => x.Role, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<HistoricalEventStructureLink> HistoricalEventStructures => Array.AsReadOnly(_historicalEventStructures.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.StructureId.Value).ThenBy(x => x.Role, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<StatisticsSample> StatisticsSamples => Array.AsReadOnly(_statisticsSamples.OrderBy(x => x.WorldMinute).ToArray());
    public IReadOnlyList<CitizenMemory> Memories => Array.AsReadOnly(_memories.OrderBy(x => x.CitizenId.Value).ThenBy(x => x.HistoricalEventId.Value).ThenBy(x => x.MemoryType).ToArray());

    private void InitializeHistory()
    {
        if (!HistorySystemsEnabled(SimulationRulesVersion)) return;
        if (_historyState is not null) return;
        _historyState = new HistoryState(0, _counters.Snapshot.NextHistoricalEventId, 0);
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value)) _knownOccupations[citizen.Id.Value] = citizen.Occupation;
        EmitHistory(HistoricalEventType.WorldCreated, HistoricalImportance.Historic, World.StartingSite, HistoricalEventPayloads.WorldCreated(Seed));
        var founders = _citizens.Values.Where(x => x.FounderOrdinal is not null).OrderBy(x => x.Id.Value).ToArray();
        EmitHistory(HistoricalEventType.SettlementFounded, HistoricalImportance.Historic, World.StartingSite, HistoricalEventPayloads.SettlementFounded(founders.Length), founders.Select(x => (x.Id, "founder")));
        EmitHistory(HistoricalEventType.SeasonStarted, HistoricalImportance.Routine, null, HistoricalEventPayloads.SeasonStarted(WorldSeason.Spring, 0));
        ScheduleStatisticsSample();
        ReevaluateFoodShortage(preexisting: true);
    }

    private void LoadHistory(SimulationPersistenceSnapshot snapshot)
    {
        if (!HistorySystemsEnabled(SimulationRulesVersion)) return;
        _historyState = CloneHistoryState(snapshot.HistoryState ?? throw new InvalidDataException("M6 snapshot has no history state."));
        _historicalEvents.AddRange(snapshot.HistoricalEvents);
        _historicalEventCitizens.AddRange(snapshot.HistoricalEventCitizens);
        _historicalEventStructures.AddRange(snapshot.HistoricalEventStructures);
        _statisticsSamples.AddRange(snapshot.StatisticsSamples);
        _memories.AddRange(snapshot.Memories);
        foreach (var historicalEvent in _historicalEvents)
        {
            foreach (var link in _historicalEventCitizens.Where(x => x.HistoricalEventId == historicalEvent.Id))
            {
                if (link.Role == "subject") _subjectHistoryKeys.Add((historicalEvent.EventType, link.CitizenId.Value));
            }
            var pair = _historicalEventCitizens.Where(x => x.HistoricalEventId == historicalEvent.Id && x.Role is "partner" or "participant").Select(x => x.CitizenId.Value).OrderBy(x => x).ToArray();
            if (pair.Length == 2) _pairHistoryKeys.Add((historicalEvent.EventType, pair[0], pair[1]));
        }
        foreach (var link in _historicalEventStructures) _structureHistoryKeys.Add((_historicalEvents.Single(x => x.Id == link.HistoricalEventId).EventType, link.StructureId.Value));
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value)) _knownOccupations[citizen.Id.Value] = citizen.Occupation;
    }

    public HistoryReadSnapshot? CreateHistoryReadSnapshot() => !HistorySystemsEnabled(SimulationRulesVersion) || _historyState is null
        ? null
        : new HistoryReadSnapshot(HistoryVersion, _historyState, _historicalEvents, _historicalEventCitizens, _historicalEventStructures, _statisticsSamples, _memories);

    public string ComputeHistoryFingerprint()
    {
        if (!HistorySystemsEnabled(SimulationRulesVersion) || _historyState is null) return string.Empty;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static void Add(IncrementalHash hash, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":"));
            hash.AppendData(bytes);
        }
        static string I<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        Add(hash, "social=" + ComputeSocialFingerprint());
        Add(hash, "history-version=" + I(HistoryVersion));
        Add(hash, "history-start-minute=" + I(_historyState.HistoryStartMinute));
        Add(hash, "history-start-event=" + I(_historyState.HistoryStartEventId));
        Add(hash, "period-start=" + I(_historyState.PeriodStartMinute));
        Add(hash, "births=" + I(_historyState.BirthsSinceSample));
        Add(hash, "deaths=" + I(_historyState.DeathsSinceSample));
        Add(hash, "food-produced=" + I(_historyState.FoodProducedSinceSample));
        Add(hash, "food-consumed=" + I(_historyState.FoodConsumedSinceSample));
        Add(hash, "shortage=" + (_historyState.ActiveFoodShortage ? "1" : "0"));
        Add(hash, "milestone=" + I(_historyState.PopulationMilestoneWatermark));
        foreach (var item in _historicalEvents.OrderBy(x => x.Id.Value)) Add(hash, $"event={I(item.Id.Value)}:{I(item.WorldMinute)}:{I((int)item.EventType)}:{I((int)item.Importance)}:{I((int)item.Origin)}:{(item.Location is { } p ? $"{I(p.X)},{I(p.Y)}" : "null")}:{item.PayloadJson}:{I(item.SchemaVersion)}");
        foreach (var link in _historicalEventCitizens.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.CitizenId.Value).ThenBy(x => x.Role, StringComparer.Ordinal)) Add(hash, $"citizen-link={I(link.HistoricalEventId.Value)}:{I(link.CitizenId.Value)}:{link.Role}");
        foreach (var link in _historicalEventStructures.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.StructureId.Value).ThenBy(x => x.Role, StringComparer.Ordinal)) Add(hash, $"structure-link={I(link.HistoricalEventId.Value)}:{I(link.StructureId.Value)}:{link.Role}");
        foreach (var sample in _statisticsSamples.OrderBy(x => x.WorldMinute)) Add(hash, $"sample={I(sample.WorldMinute)}:{I(sample.PeriodStartMinute)}:{I(sample.Population)}:{I(sample.BirthsPeriod)}:{I(sample.DeathsPeriod)}:{I(sample.FoodStored)}:{I(sample.FoodProducedPeriod)}:{I(sample.FoodConsumedPeriod)}:{I(sample.WoodStored)}:{I(sample.StoneStored)}:{I(sample.ShelterCapacity)}:{I(sample.AverageHealth)}:{I(sample.AverageHunger)}");
        foreach (var memory in _memories.OrderBy(x => x.CitizenId.Value).ThenBy(x => x.HistoricalEventId.Value).ThenBy(x => x.MemoryType)) Add(hash, $"memory={I(memory.CitizenId.Value)}:{I(memory.HistoricalEventId.Value)}:{I((int)memory.MemoryType)}:{I((int)memory.Importance)}:{I(memory.EmotionalValence)}:{I(memory.CreatedMinute)}");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public string HistoryFingerprint => ComputeHistoryFingerprint();

    // History is emitted at the canonical transition site.  In particular, do
    // not scan the complete world (or the historical stream) after every queued
    // event: routine movement, meals, gathering and decisions must remain cheap
    // and must not acquire retroactive meaning.
    private void RecordHistoryTransitions(int foodBefore)
    {
        if (_historyState is null) return;
        var foodDelta = Settlement.FoodStored - foodBefore;
        if (foodDelta > 0) _historyState.FoodProducedSinceSample = checked(_historyState.FoodProducedSinceSample + foodDelta);
        else if (foodDelta < 0) _historyState.FoodConsumedSinceSample = checked(_historyState.FoodConsumedSinceSample - foodDelta);
        // Population transitions are handled at the individual birth/death
        // transition sites below. Keep this whole-event pass for food deltas
        // only, so a multi-transition event cannot observe its final population.
        if (foodDelta != 0) ReevaluateFoodShortage(false);
    }

    private void RecordBirthHistory(Citizen child)
    {
        if (_historyState is null || child.ParentAId is not { } parentA || child.ParentBId is not { } parentB) return;
        if (child.HouseholdId is not { } householdId || !_households.ContainsKey(householdId.Value)) throw new InvalidDataException("A live birth must reference its canonical household.");
        if (HasSubjectEvent(HistoricalEventType.CitizenBorn, child.Id.Value)) return;
        EmitHistory(HistoricalEventType.CitizenBorn, HistoricalImportance.Notable, child.Location,
            HistoricalEventPayloads.CitizenBorn(householdId), [(child.Id, "subject"), (parentA, "parent"), (parentB, "parent")]);
        _historyState.BirthsSinceSample = checked(_historyState.BirthsSinceSample + 1);
        RecordPopulationMilestones();
        ReevaluateFoodShortage(false);
    }

    private void RecordDeathHistory(Citizen citizen)
    {
        if (_historyState is null || citizen.DeathMinute is not { } deathMinute || citizen.DeathCause is null) return;
        if (HasSubjectEvent(HistoricalEventType.CitizenDied, citizen.Id.Value)) return;
        var historicalEventMinute = new WorldMinute(deathMinute);
        EmitHistory(HistoricalEventType.CitizenDied, HistoricalImportance.Notable, citizen.Location,
            HistoricalEventPayloads.CitizenDied(citizen.DeathCause, citizen.AgeYears(historicalEventMinute)), [(citizen.Id, "subject")]);
        _historyState.DeathsSinceSample = checked(_historyState.DeathsSinceSample + 1);
        ReevaluateFoodShortage(false);
    }

    private void RecordSocialInteractionHistory(RelationshipState? previous, RelationshipState next)
    {
        if (_historyState is null) return;
        var previousLabel = RelationshipLabels.Derive(previous, isPartner: false, isFamily: false);
        var nextLabel = RelationshipLabels.Derive(next, isPartner: false, isFamily: false);
        var links = new[] { (next.CitizenAId, "participant"), (next.CitizenBId, "participant") };
        var payload = HistoricalEventPayloads.Relationship(next.Familiarity, next.Affinity, next.Trust, next.Conflict);
        if (previousLabel is not (RelationshipLabels.Friend or RelationshipLabels.CloseFriend) && nextLabel is RelationshipLabels.Friend or RelationshipLabels.CloseFriend && !HasPairEvent(HistoricalEventType.FriendshipFormed, next.CitizenAId.Value, next.CitizenBId.Value))
            EmitHistory(HistoricalEventType.FriendshipFormed, HistoricalImportance.Personal, null, payload, links);
        if (previousLabel != RelationshipLabels.Rival && nextLabel == RelationshipLabels.Rival && !HasPairEvent(HistoricalEventType.RivalryFormed, next.CitizenAId.Value, next.CitizenBId.Value))
            EmitHistory(HistoricalEventType.RivalryFormed, HistoricalImportance.Personal, null, payload, links);
    }

    private void RecordPartnershipHistory(Citizen first, Citizen second)
    {
        if (_historyState is null || first.PartnerId != second.Id || second.PartnerId != first.Id || first.HouseholdId is not { } householdId || second.HouseholdId != householdId) return;
        if (HasPairEvent(HistoricalEventType.PartnershipFormed, first.Id.Value, second.Id.Value)) return;
        var pair = RelationshipState.Normalize(first.Id, second.Id);
        EmitHistory(HistoricalEventType.PartnershipFormed, HistoricalImportance.Notable, null,
            HistoricalEventPayloads.PartnershipFormed(householdId), [(pair.A, "partner"), (pair.B, "partner")]);
        EmitHistory(HistoricalEventType.HouseholdCreated, HistoricalImportance.Personal, null,
            HistoricalEventPayloads.HouseholdCreated(householdId), [(pair.A, "member"), (pair.B, "member")]);
    }

    private void RecordStructureStartedHistory(Structure structure)
    {
        if (_historyState is null || HasStructureEvent(HistoricalEventType.StructureStarted, structure.Id.Value)) return;
        EmitHistory(HistoricalEventType.StructureStarted, HistoricalImportance.Personal, structure.Location,
            HistoricalEventPayloads.StructureStarted(structure.Type, structure.RequiredWood, structure.RequiredStone, structure.RequiredWork),
            [], [(structure.Id, "subject")]);
    }

    private void RecordStructureCompletedHistory(Structure structure)
    {
        if (_historyState is null || structure.Status != StructureStatus.Complete || HasStructureEvent(HistoricalEventType.StructureCompleted, structure.Id.Value)) return;
        var contributors = _structureContributions.Values
            .Where(x => x.StructureId == structure.Id && (x.ConstructionWork > 0 || x.WoodDelivered > 0 || x.StoneDelivered > 0))
            .OrderBy(x => x.CitizenId.Value).Select(x => (x.CitizenId, "contributor")).ToArray();
        EmitHistory(HistoricalEventType.StructureCompleted, HistoricalImportance.Notable, structure.Location,
            HistoricalEventPayloads.StructureCompleted(structure.Type), contributors, [(structure.Id, "subject")]);
    }

    private void RecordSpecializationHistory(Citizen citizen, string previousOccupation)
    {
        if (_historyState is null || string.Equals(previousOccupation, citizen.Occupation, StringComparison.Ordinal)) return;
        EmitHistory(HistoricalEventType.CitizenSpecializationChanged, HistoricalImportance.Personal, null,
            HistoricalEventPayloads.SpecializationChanged(previousOccupation, citizen.Occupation), [(citizen.Id, "subject")]);
    }

    private void RecordPopulationMilestones()
    {
        if (_historyState is null) return;
        foreach (var milestone in PopulationMilestones)
        {
            if (LivingPopulation < milestone || _historyState.PopulationMilestoneWatermark >= milestone) continue;
            EmitHistory(HistoricalEventType.PopulationMilestone, HistoricalImportance.Major, null, HistoricalEventPayloads.PopulationMilestone(milestone));
            _historyState.PopulationMilestoneWatermark = milestone;
        }
    }

    private void ReevaluateFoodShortage(bool preexisting)
        => ReevaluateFoodShortageCore(preexisting, sampledRecovery: false);

    private void ReevaluateFoodShortageCore(bool preexisting, bool sampledRecovery)
    {
        if (_historyState is null) return;
        var living = LivingPopulation;
        var shouldBeActive = !_historyState.ActiveFoodShortage
            ? living > 0 && Settlement.FoodStored < checked(living * 10)
            : living > 0 && (SimulationRulesVersion == M6SimulationRulesVersion
                ? Settlement.FoodStored < checked(living * FoodShortageRecoveryMultiplier(SimulationRulesVersion))
                : !sampledRecovery || Settlement.FoodStored < checked(living * FoodShortageRecoveryMultiplier(SimulationRulesVersion)));
        if (shouldBeActive == _historyState.ActiveFoodShortage) return;
        var quantity = Settlement.FoodStored;
        var type = shouldBeActive ? HistoricalEventType.ResourceShortageStarted : HistoricalEventType.ResourceShortageEnded;
        EmitHistory(type, HistoricalImportance.Notable, World.StartingSite, HistoricalEventPayloads.ResourceShortage(ResourceType.Food, quantity, living, preexisting && _historicalEvents.Count > 0));
        _historyState.ActiveFoodShortage = shouldBeActive;
    }

    private void ProcessStatisticsSample()
    {
        if (_historyState is null) return;
        var living = _citizens.Values.Where(x => x.IsAlive).OrderBy(x => x.Id.Value).ToArray();
        var averageHealth = living.Length == 0 ? 0 : living.Sum(x => (long)x.Health) / living.Length;
        var averageHunger = living.Length == 0 ? 0 : living.Sum(x => (long)x.GetProjectedNeeds(CurrentMinute).Hunger) / living.Length;
        if (CurrentMinute.Value % (NeedsProjection.MinutesPerSeason) == 0)
        {
            var date = CurrentMinute.ToCalendar();
            EmitHistory(HistoricalEventType.SeasonStarted, HistoricalImportance.Routine, null, HistoricalEventPayloads.SeasonStarted(date.Season, date.Year));
        }
        _statisticsSamples.Add(new StatisticsSample(CurrentMinute.Value, _historyState.PeriodStartMinute, living.Length, _historyState.BirthsSinceSample, _historyState.DeathsSinceSample, Settlement.FoodStored, _historyState.FoodProducedSinceSample, _historyState.FoodConsumedSinceSample, Settlement.WoodStored, Settlement.StoneStored, ShelterCapacity, checked((int)averageHealth), checked((int)averageHunger)));
        if (UsesSampledShortageRecovery(SimulationRulesVersion)) ReevaluateFoodShortageAtStatisticsSample();
        _historyState.PeriodStartMinute = CurrentMinute.Value;
        _historyState.BirthsSinceSample = 0;
        _historyState.DeathsSinceSample = 0;
        _historyState.FoodProducedSinceSample = 0;
        _historyState.FoodConsumedSinceSample = 0;
        ScheduleStatisticsSample();
    }

    private void ReevaluateFoodShortageAtStatisticsSample() => ReevaluateFoodShortageCore(preexisting: false, sampledRecovery: true);

    private void ScheduleStatisticsSample()
    {
        var due = new WorldMinute(checked(((CurrentMinute.Value / StatisticsPeriodMinutes) + 1) * StatisticsPeriodMinutes));
        var sequence = _counters.AllocateScheduledEventSequence();
        AddPending(new ScheduledEventId(sequence), new ScheduledEventOrder(due, CitizenEventNames.StatisticsSamplePriority, 0, sequence), CitizenEventNames.StatisticsSample, "{\"version\":1}");
    }

    private void EmitHistory(HistoricalEventType type, HistoricalImportance importance, TileCoordinate? location, string payload,
        IEnumerable<(CitizenId CitizenId, string Role)>? citizens = null, IEnumerable<(StructureId StructureId, string Role)>? structures = null)
    {
        if (_historyState is null) return;
        var citizenLinks = (citizens ?? Array.Empty<(CitizenId CitizenId, string Role)>()).Select(link => new HistoricalEventCitizenLink(new HistoricalEventId(1), link.CitizenId, link.Role)).ToArray();
        var structureLinks = (structures ?? Array.Empty<(StructureId StructureId, string Role)>()).Select(link => new HistoricalEventStructureLink(new HistoricalEventId(1), link.StructureId, link.Role)).ToArray();
        if (citizenLinks.Any(link => !_citizens.ContainsKey(link.CitizenId.Value)) || structureLinks.Any(link => !_structures.ContainsKey(link.StructureId.Value))) throw new InvalidDataException("Historical event link references an unknown live entity.");
        if (citizenLinks.Select(link => (link.CitizenId.Value, link.Role)).Distinct().Count() != citizenLinks.Length || structureLinks.Select(link => (link.StructureId.Value, link.Role)).Distinct().Count() != structureLinks.Length) throw new InvalidDataException("Historical event links must be unique.");
        var id = _counters.Snapshot.NextHistoricalEventId;
        var historicalId = new HistoricalEventId(id);
        var item = new HistoricalEvent(historicalId, CurrentMinute.Value, type, importance, HistoricalEventOrigin.Live, location, payload);
        item.Validate(CurrentMinute.Value);
        citizenLinks = citizenLinks.Select(link => new HistoricalEventCitizenLink(historicalId, link.CitizenId, link.Role)).ToArray();
        structureLinks = structureLinks.Select(link => new HistoricalEventStructureLink(historicalId, link.StructureId, link.Role)).ToArray();
        item.ValidateLinks(citizenLinks, structureLinks);
        _counters.AllocateHistoricalEventId();
        _historicalEvents.Add(item);
        _historicalEventCitizens.AddRange(citizenLinks);
        _historicalEventStructures.AddRange(structureLinks);
        _historicalEventCitizens.Sort(static (left, right) =>
        {
            var result = left.HistoricalEventId.Value.CompareTo(right.HistoricalEventId.Value);
            if (result != 0) return result;
            result = left.CitizenId.Value.CompareTo(right.CitizenId.Value);
            return result != 0 ? result : string.CompareOrdinal(left.Role, right.Role);
        });
        _historicalEventStructures.Sort(static (left, right) =>
        {
            var result = left.HistoricalEventId.Value.CompareTo(right.HistoricalEventId.Value);
            if (result != 0) return result;
            result = left.StructureId.Value.CompareTo(right.StructureId.Value);
            return result != 0 ? result : string.CompareOrdinal(left.Role, right.Role);
        });
        foreach (var link in citizenLinks)
        {
            if (link.Role == "subject") _subjectHistoryKeys.Add((type, link.CitizenId.Value));
        }
        var pair = citizenLinks.Where(x => x.Role is "partner" or "participant").Select(x => x.CitizenId.Value).OrderBy(x => x).ToArray();
        if (pair.Length == 2) _pairHistoryKeys.Add((type, pair[0], pair[1]));
        foreach (var link in structureLinks) _structureHistoryKeys.Add((type, link.StructureId.Value));
        AddMemories(item);
        _memories.Sort(static (left, right) =>
        {
            var result = left.CitizenId.Value.CompareTo(right.CitizenId.Value);
            if (result != 0) return result;
            result = left.HistoricalEventId.Value.CompareTo(right.HistoricalEventId.Value);
            return result != 0 ? result : left.MemoryType.CompareTo(right.MemoryType);
        });
    }

    private void AddMemories(HistoricalEvent item)
    {
        var valence = item.EventType switch { HistoricalEventType.CitizenBorn => 8000, HistoricalEventType.CitizenDied => -9000, HistoricalEventType.PartnershipFormed => 8000, HistoricalEventType.FriendshipFormed => 6000, HistoricalEventType.RivalryFormed => -7000, HistoricalEventType.StructureCompleted => 4000, _ => 0 };
        var memoryType = item.EventType switch { HistoricalEventType.CitizenBorn => MemoryType.ChildBorn, HistoricalEventType.CitizenDied => MemoryType.PartnerDied, HistoricalEventType.PartnershipFormed => MemoryType.PartnershipFormed, HistoricalEventType.FriendshipFormed => MemoryType.FriendshipFormed, HistoricalEventType.RivalryFormed => MemoryType.RivalryFormed, HistoricalEventType.StructureCompleted => MemoryType.StructureCompleted, _ => (MemoryType?)null };
        if (memoryType is null) return;
        var links = _historicalEventCitizens.Where(x => x.HistoricalEventId == item.Id).OrderBy(x => x.CitizenId.Value).ToArray();
        if (item.EventType == HistoricalEventType.CitizenDied)
        {
            var deceasedId = links.SingleOrDefault(x => x.Role == "subject")?.CitizenId;
            if (deceasedId is { } id && _citizens.TryGetValue(id.Value, out var deceased) && deceased.PartnerId is { } partner && _citizens.TryGetValue(partner.Value, out var surviving) && IsAliveAt(surviving, item.WorldMinute))
            {
                links = [new HistoricalEventCitizenLink(item.Id, surviving.Id, "partner")];
            }
            else links = [];
        }
        foreach (var link in links.Where(link => item.EventType switch
        {
            HistoricalEventType.CitizenBorn => link.Role == "parent",
            HistoricalEventType.CitizenDied => link.Role == "partner",
            HistoricalEventType.PartnershipFormed or HistoricalEventType.FriendshipFormed or HistoricalEventType.RivalryFormed => link.Role is "partner" or "participant",
            HistoricalEventType.StructureCompleted => link.Role == "contributor",
            _ => false
        }))
        {
            if (_memories.Any(x => x.CitizenId == link.CitizenId && x.HistoricalEventId == item.Id && x.MemoryType == memoryType.Value)) continue;
            _memories.Add(new CitizenMemory(link.CitizenId, item.Id, memoryType.Value, item.Importance, valence, item.WorldMinute));
        }
        foreach (var citizenId in links.Select(x => x.CitizenId).Distinct().ToArray())
        {
            var lower = _memories.Where(x => x.CitizenId == citizenId && !IsCriticalMemory(x.MemoryType)).OrderByDescending(x => x.Importance).ThenByDescending(x => x.CreatedMinute).ThenByDescending(x => x.HistoricalEventId.Value).ToArray();
            foreach (var remove in lower.Skip(64)) _memories.Remove(remove);
        }
    }

    private static bool IsAliveAt(Citizen citizen, long minute) => citizen.BirthMinute <= minute && (citizen.DeathMinute is null || citizen.DeathMinute.Value > minute);

    private static bool IsCriticalMemory(MemoryType type) => type is MemoryType.ChildBorn or MemoryType.PartnerDied or MemoryType.PartnershipFormed;
    private bool HasSubjectEvent(HistoricalEventType type, long citizenId) => _subjectHistoryKeys.Contains((type, citizenId));
    private bool HasPairEvent(HistoricalEventType type, long first, long second) => _pairHistoryKeys.Contains((type, Math.Min(first, second), Math.Max(first, second)));
    private bool HasStructureEvent(HistoricalEventType type, long structureId) => _structureHistoryKeys.Contains((type, structureId));

    private static bool AreFamily(Citizen first, Citizen second, IReadOnlyList<Citizen> citizens)
    {
        static HashSet<long> Ancestors(Citizen start, IReadOnlyList<Citizen> all)
        {
            var result = new HashSet<long>();
            var pending = new Stack<long>();
            pending.Push(start.Id.Value);
            while (pending.TryPop(out var id) && result.Add(id))
            {
                var citizen = all.SingleOrDefault(x => x.Id.Value == id);
                if (citizen?.ParentAId is { } parentA) pending.Push(parentA.Value);
                if (citizen?.ParentBId is { } parentB) pending.Push(parentB.Value);
            }
            return result;
        }
        return Ancestors(first, citizens).Overlaps(Ancestors(second, citizens));
    }

    private static HistoryState CloneHistoryState(HistoryState value) => new(value.HistoryStartMinute, value.HistoryStartEventId, value.PeriodStartMinute, value.BirthsSinceSample, value.DeathsSinceSample, value.FoodProducedSinceSample, value.FoodConsumedSinceSample, value.ActiveFoodShortage, value.PopulationMilestoneWatermark);
}
