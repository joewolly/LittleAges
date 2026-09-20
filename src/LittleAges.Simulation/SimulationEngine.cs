using LittleAges.Domain;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LittleAges.Simulation;

public static class CitizenEventNames
{
    public const string Decision = "citizen.decision.v1";
    public const string MoveStep = "citizen.move-step.v1";
    public const string ActionComplete = "citizen.action-complete.v1";
    public const string SurvivalCheck = "citizen.survival-check.v1";
    public const string ResourceRegenerate = "resource.regenerate.v1";
    public const string SettlementEvaluateDemand = "settlement.evaluate-demand.v1";
    public const string FamilyCheck = "social.family-check.v1";
    public const string LifecycleCheck = "population.lifecycle-check.v1";
    public const int MovementPriority = 10;
    public const int CompletionPriority = 15;
    public const int DecisionPriority = 20;
    public const int SurvivalPriority = 18;
    public const int RegenerationPriority = 5;
    public const int SettlementDemandPriority = 7;
    public const int FamilyCheckPriority = 8;
    public const int LifecycleCheckPriority = 17;
    public const string StatisticsSample = "history.statistics-sample.v1";
    public const int StatisticsSamplePriority = 19;
}

public sealed record ScheduledEventSnapshot(ScheduledEventId Id, ScheduledEventOrder Order, string Name, string PayloadJson = "{}")
{
    public string EventPayloadJson => PayloadJson;
    public ScheduledEventSnapshot Validate()
    {
        WorldIdValidation.RequirePositive(Id.Value, nameof(Id));
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("A scheduled event must have a stable name.", nameof(Name));
        if (Order.Sequence <= 0 || Id.Value != Order.Sequence) throw new ArgumentException("Scheduled event identity must equal its sequence.", nameof(Id));
        if (string.IsNullOrWhiteSpace(PayloadJson)) throw new ArgumentException("Event payload is required.", nameof(PayloadJson));
        try
        {
            using var document = JsonDocument.Parse(PayloadJson);
            if (Name == CitizenEventNames.SurvivalCheck)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("citizenId", out var id) || id.ValueKind != JsonValueKind.String || !long.TryParse(id.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0 || parsedId.ToString(System.Globalization.CultureInfo.InvariantCulture) != id.GetString() || Order.EntitySortKey != parsedId || PayloadJson != $"{{\"citizenId\":\"{parsedId}\"}}") throw new ArgumentException("Survival event payload is not canonical.", nameof(PayloadJson));
            }
            else if (Name == CitizenEventNames.ResourceRegenerate)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var parsedVersion) || parsedVersion != 1 || Order.EntitySortKey != 0 || PayloadJson != "{\"version\":1}") throw new ArgumentException("Resource regeneration event payload is not canonical.", nameof(PayloadJson));
            }
            else if (Name is CitizenEventNames.SettlementEvaluateDemand or CitizenEventNames.FamilyCheck or CitizenEventNames.LifecycleCheck)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var parsedVersion) || parsedVersion != 1 || Order.EntitySortKey != 0 || PayloadJson != "{\"version\":1}") throw new ArgumentException("Settlement demand event payload is not canonical.", nameof(PayloadJson));
            }
            else if (Name == CitizenEventNames.StatisticsSample)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var parsedVersion) || parsedVersion != 1 || Order.EntitySortKey != 0 || Order.Priority != CitizenEventNames.StatisticsSamplePriority || PayloadJson != "{\"version\":1}") throw new ArgumentException("Statistics sample event payload is not canonical.", nameof(PayloadJson));
            }
            else if (Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 || !root.TryGetProperty("citizenId", out var id) || id.ValueKind != JsonValueKind.String || !long.TryParse(id.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0 || parsedId.ToString(System.Globalization.CultureInfo.InvariantCulture) != id.GetString() || !root.TryGetProperty("actionSequence", out var sequence) || !sequence.TryGetInt64(out var parsedSequence) || parsedSequence < 0 || Order.EntitySortKey != parsedId || PayloadJson != $"{{\"citizenId\":\"{parsedId}\",\"actionSequence\":{parsedSequence}}}") throw new ArgumentException("Citizen event payload is not canonical.", nameof(PayloadJson));
            }
        }
        catch (JsonException exception) { throw new ArgumentException("Event payload must be valid JSON.", nameof(PayloadJson), exception); }
        return this;
    }
}
public sealed record SyntheticEventExecution(ScheduledEventId Id, ScheduledEventOrder Order, string Name, string PayloadJson = "{}");
// Opt-in diagnostic output for investigating daily M5 reproduction checks.  It is
// deliberately neither persisted nor included in fingerprints or read snapshots.
[Flags]
public enum FamilyCheckBlocker
{
    None = 0,
    Partnership = 1,
    Age = 2,
    HealthOrProjectedHunger = 4,
    Food = 8,
    Population = 16,
    HouseholdSize = 32,
    Cooldown = 64,
    Relationship = 128,
    ActualDwelling = 256
}

public sealed record FamilyCheckOpportunityDiagnostic(HouseholdId HouseholdId, long Day, CitizenId? ParentAId, CitizenId? ParentBId, int? Chance, int? Draw, FamilyCheckBlocker Blockers, int FoodRequired, int FoodActual, StructureId? DwellingStructureId, int DwellingOccupancy, int? ParentAAge, int? ParentBAge, int? ParentAHealth, int? ParentBHealth, int? ParentAProjectedHunger, int? ParentBProjectedHunger, bool ParentAHasAccessibleNonemptyFoodNode, bool ParentBHasAccessibleNonemptyFoodNode);

public sealed record FamilyCheckDiagnostic(WorldMinute Minute, int ActiveHouseholds, int ReadyIgnoringHousing, int BlockedActualDwelling, int HomeChanges, int ReadyWithHousingAfterRelocation, int ReadyUnlockedByRelocation, int BirthRandomOpportunities, int BirthRandomHits, int FoodBlocked, int HungerOrHealthBlocked, int CooldownBlocked, int AgesBlocked, int DwellingBlocked, IReadOnlyList<FamilyCheckOpportunityDiagnostic> Opportunities);
internal readonly record struct SocialTargetCandidate(CitizenId CitizenId, int Score, int Distance);
public sealed record CitizenMovementWaypointSnapshot(TileCoordinate Location, WorldMinute ArriveMinute);
public sealed record CitizenMovementPlanSnapshot
{
    public CitizenMovementPlanSnapshot(long actionSequence, WorldMinute observedMinute, IReadOnlyList<CitizenMovementWaypointSnapshot> waypoints, WorldMinute? segmentStartedMinute = null)
    {
        ActionSequence = actionSequence;
        ObservedMinute = observedMinute;
        SegmentStartedMinute = segmentStartedMinute ?? observedMinute;
        Waypoints = Array.AsReadOnly(waypoints.ToArray());
    }

    public long ActionSequence { get; }
    public WorldMinute ObservedMinute { get; }
    public WorldMinute SegmentStartedMinute { get; }
    public IReadOnlyList<CitizenMovementWaypointSnapshot> Waypoints { get; }

    public bool Equals(CitizenMovementPlanSnapshot? other) =>
        other is not null &&
        ActionSequence == other.ActionSequence &&
        ObservedMinute == other.ObservedMinute &&
        SegmentStartedMinute == other.SegmentStartedMinute &&
        Waypoints.SequenceEqual(other.Waypoints);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActionSequence);
        hash.Add(ObservedMinute);
        hash.Add(SegmentStartedMinute);
        foreach (var waypoint in Waypoints) hash.Add(waypoint);
        return hash.ToHashCode();
    }
}
public sealed record CitizenReadSnapshot(string CitizenId, int? FounderOrdinal, string GivenName, string FamilyName, string Name, int Age, string LifeStage, TileCoordinate Location, int Health, CitizenNeeds ProjectedNeeds, CitizenTraits Traits, CitizenSkills Skills, CitizenAction CurrentAction, WorldMinute? ActionStartedMinute, WorldMinute? ActionCompletesMinute, TileCoordinate? Target, long ActionSequence, bool IsAlive = true, string? DeathCause = null, ResourceType? CarriedResourceType = null, int CarriedResourceQuantity = 0, ResourceNodeId? TargetResourceNodeId = null, CitizenActionPhase ActionPhase = CitizenActionPhase.None, WorldMinute? DeathMinute = null, StructureId? HomeStructureId = null, StructureId? TargetStructureId = null, string Occupation = CitizenOccupation.Generalist, long LifetimeForagingMinutes = 0, long LifetimeWoodcuttingMinutes = 0, long LifetimeStoneworkingMinutes = 0, long LifetimeConstructionMinutes = 0, long LifetimeHaulingMinutes = 0, CitizenId? ParentAId = null, CitizenId? ParentBId = null, CitizenId? PartnerId = null, HouseholdId? HouseholdId = null, IReadOnlyList<string>? ChildrenIds = null, CitizenId? TargetCitizenId = null, long BirthMinute = 0, CitizenMovementPlanSnapshot? MovementPlan = null);
public sealed record CitizenDecisionEvaluation(CitizenAction Action, int BaseUtility, int NeedContribution, int TraitContribution, int Variation, int FinalScore, int SkillContribution = 0, int StockpileContribution = 0, int TravelPenalty = 0);

public sealed record SimulationStatusSnapshot
{
    public SimulationStatusSnapshot(WorldSeed seed, WorldMinute worldMinute, int pendingEventCount, int processedEventCount, IReadOnlyList<SyntheticEventExecution> processedEvents, WorldMap? world = null, IReadOnlyList<CitizenReadSnapshot>? citizens = null, SettlementState? settlement = null, IReadOnlyList<ResourceState>? resourceStates = null, IReadOnlyList<Structure>? structures = null, IReadOnlyList<StructureContribution>? structureContributions = null, IReadOnlyList<RelationshipState>? relationships = null, IReadOnlyList<Household>? households = null, HistoryReadSnapshot? history = null)
    { Seed = seed; WorldMinute = worldMinute; PendingEventCount = pendingEventCount; ProcessedEventCount = processedEventCount; ProcessedEvents = Array.AsReadOnly(processedEvents.ToArray()); World = world; Citizens = Array.AsReadOnly((citizens ?? Array.Empty<CitizenReadSnapshot>()).ToArray()); Settlement = settlement is null ? null : new SettlementState(settlement.FoodStored, settlement.WoodStored, settlement.StoneStored, settlement.BaseStorageCapacity, settlement.DemandUpdatedMinute, settlement.ExposureConsequencesStartMinute); ResourceStates = Array.AsReadOnly((resourceStates ?? Array.Empty<ResourceState>()).Select(x => new ResourceState(x.ResourceNodeId, x.CurrentQuantity)).ToArray()); Structures = Array.AsReadOnly((structures ?? Array.Empty<Structure>()).OrderBy(x => x.Id.Value).Select(SimulationPersistenceSnapshot.CloneStructure).ToArray()); StructureContributions = Array.AsReadOnly((structureContributions ?? Array.Empty<StructureContribution>()).OrderBy(x => x.StructureId.Value).ThenBy(x => x.CitizenId.Value).Select(SimulationPersistenceSnapshot.CloneContribution).ToArray()); Relationships = Array.AsReadOnly((relationships ?? Array.Empty<RelationshipState>()).OrderBy(x => x.CitizenAId.Value).ThenBy(x => x.CitizenBId.Value).ToArray()); Households = Array.AsReadOnly((households ?? Array.Empty<Household>()).OrderBy(x => x.Id.Value).Select(x => new Household(x.Id, x.CreatedMinute) { DissolvedMinute = x.DissolvedMinute, DwellingStructureId = x.DwellingStructureId }).ToArray()); History = history; }
    public WorldSeed Seed { get; } public WorldMinute WorldMinute { get; } public int PendingEventCount { get; } public int ProcessedEventCount { get; } public IReadOnlyList<SyntheticEventExecution> ProcessedEvents { get; } public WorldMap? World { get; } public IReadOnlyList<CitizenReadSnapshot> Citizens { get; } public SettlementState? Settlement { get; } public IReadOnlyList<ResourceState> ResourceStates { get; } public IReadOnlyList<Structure> Structures { get; } public IReadOnlyList<StructureContribution> StructureContributions { get; } public IReadOnlyList<RelationshipState> Relationships { get; } public IReadOnlyList<Household> Households { get; } public HistoryReadSnapshot? History { get; }
}

public sealed record SimulationPersistenceSnapshot
{
    private static readonly int[] M6PopulationMilestones = [25, 50, 100, 250, 500, 1000, 2000];
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents) : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, null, null, 0) { }
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents, WorldMap? world) : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, world, null, 0) { }
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents, WorldMap? world, IReadOnlyList<Citizen>? citizens, int citizenGenerationVersion = 0, IReadOnlyList<ResourceState>? resourceStates = null, SettlementState? settlement = null, int survivalVersion = 0, int settlementVersion = 0, IReadOnlyList<Structure>? structures = null, IReadOnlyList<StructureContribution>? structureContributions = null, int socialVersion = 0, IReadOnlyList<RelationshipState>? relationships = null, IReadOnlyList<Household>? households = null, int historyVersion = 0, HistoryState? historyState = null, IReadOnlyList<HistoricalEvent>? historicalEvents = null, IReadOnlyList<HistoricalEventCitizenLink>? historicalEventCitizens = null, IReadOnlyList<HistoricalEventStructureLink>? historicalEventStructures = null, IReadOnlyList<StatisticsSample>? statisticsSamples = null, IReadOnlyList<CitizenMemory>? memories = null)
    {
        Seed = seed; WorldMinute = worldMinute; WorldSchemaVersion = RequireMetadata(worldSchemaVersion, nameof(worldSchemaVersion)); SimulationRulesVersion = RequireMetadata(simulationRulesVersion, nameof(simulationRulesVersion)); ApplicationVersion = RequireMetadata(applicationVersion, nameof(applicationVersion)); WorldConfiguration = worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration)); Counters = counters.Validate();
        ArgumentNullException.ThrowIfNull(scheduledEvents);
        var events = scheduledEvents.Select(static x => x.Validate()).ToArray();
        if (events.Select(x => x.Id.Value).Distinct().Count() != events.Length || events.Select(x => x.Order.Sequence).Distinct().Count() != events.Length) throw new ArgumentException("Queued event identities must be unique.", nameof(scheduledEvents));
        if (events.Any(x => x.Order.Sequence >= Counters.NextScheduledEventSequence)) throw new ArgumentException("The next scheduled sequence must exceed queued events.", nameof(counters));
        if (citizenGenerationVersion is not (0 or 1)) throw new NotSupportedException($"Citizen generation version '{citizenGenerationVersion}' is not supported.");
        if (survivalVersion is not (0 or SimulationEngine.SurvivalVersion)) throw new NotSupportedException($"Survival version '{survivalVersion}' is not supported.");
        var m2 = simulationRulesVersion == SimulationEngine.M2SimulationRulesVersion;
        var m3 = simulationRulesVersion == SimulationEngine.M3SimulationRulesVersion;
        var m4 = simulationRulesVersion == SimulationEngine.M4SimulationRulesVersion;
        var m5 = simulationRulesVersion is SimulationEngine.M5SimulationRulesVersion or SimulationEngine.M6SimulationRulesVersion or SimulationEngine.M8SimulationRulesVersion or SimulationEngine.GrowthSimulationRulesVersion;
        var m6 = simulationRulesVersion is SimulationEngine.M6SimulationRulesVersion or SimulationEngine.M8SimulationRulesVersion or SimulationEngine.GrowthSimulationRulesVersion;
        if ((m3 || m4 || m5) && survivalVersion != SimulationEngine.SurvivalVersion) throw new ArgumentException("M3+ snapshots require survival version 1.", nameof(survivalVersion));
        if (!m3 && !m4 && !m5 && survivalVersion != 0) throw new ArgumentException("Only M3+ snapshots may carry survival state.", nameof(survivalVersion));
        if ((m3 || m4 || m5) && citizenGenerationVersion == 0) throw new ArgumentException("M3+ rules require the citizen generation sentinel.", nameof(citizenGenerationVersion));
        if ((m4 || m5) && settlementVersion != CitizenSimulationRules.SettlementVersion) throw new ArgumentException("M4+ snapshots require settlement version 1.", nameof(settlementVersion));
        if (!m4 && !m5 && settlementVersion != 0) throw new ArgumentException("Only M4+ snapshots may carry settlement state.", nameof(settlementVersion));
        if (settlementVersion is not (0 or CitizenSimulationRules.SettlementVersion)) throw new NotSupportedException($"Settlement version '{settlementVersion}' is not supported.");
        if (socialVersion is not (0 or SimulationEngine.SocialVersion) || (m5 && socialVersion != SimulationEngine.SocialVersion) || (!m5 && socialVersion != 0)) throw new ArgumentException("Social version is incompatible with rules.", nameof(socialVersion));
        if (!m5 && ((relationships?.Count ?? 0) != 0 || (households?.Count ?? 0) != 0)) throw new ArgumentException("Pre-M5 snapshots cannot carry social rows.");
        if (historyVersion is not (0 or SimulationEngine.HistoryVersion)) throw new NotSupportedException($"History version '{historyVersion}' is not supported.");
        if (!m6 && (historyVersion != 0 || (historicalEvents?.Count ?? 0) != 0 || (historicalEventCitizens?.Count ?? 0) != 0 || (historicalEventStructures?.Count ?? 0) != 0 || (statisticsSamples?.Count ?? 0) != 0 || (memories?.Count ?? 0) != 0 || historyState is not null)) throw new ArgumentException("Only M6 snapshots may carry history state.", nameof(historyVersion));
        if (m6 && historyVersion != SimulationEngine.HistoryVersion) throw new ArgumentException("M6 snapshots require history version 1.", nameof(historyVersion));
        var citizensInput = (citizens ?? Array.Empty<Citizen>()).ToArray();
        var resourcesInput = (resourceStates ?? Array.Empty<ResourceState>()).ToArray();
        var structuresInput = (structures ?? Array.Empty<Structure>()).ToArray();
        var contributionsInput = (structureContributions ?? Array.Empty<StructureContribution>()).ToArray();
        var relationshipsInput = (relationships ?? Array.Empty<RelationshipState>()).ToArray();
        var householdsInput = (households ?? Array.Empty<Household>()).ToArray();
        var historicalEventsInput = (historicalEvents ?? Array.Empty<HistoricalEvent>()).ToArray();
        var historicalCitizenLinksInput = (historicalEventCitizens ?? Array.Empty<HistoricalEventCitizenLink>()).ToArray();
        var historicalStructureLinksInput = (historicalEventStructures ?? Array.Empty<HistoricalEventStructureLink>()).ToArray();
        var statisticsInput = (statisticsSamples ?? Array.Empty<StatisticsSample>()).ToArray();
        var memoriesInput = (memories ?? Array.Empty<CitizenMemory>()).ToArray();
        RequireOrder(historicalEventsInput, historicalEventsInput.OrderBy(x => x.Id.Value), "historical events");
        RequireOrder(historicalCitizenLinksInput, historicalCitizenLinksInput.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.CitizenId.Value).ThenBy(x => x.Role, StringComparer.Ordinal), "historical citizen links");
        RequireOrder(historicalStructureLinksInput, historicalStructureLinksInput.OrderBy(x => x.HistoricalEventId.Value).ThenBy(x => x.StructureId.Value).ThenBy(x => x.Role, StringComparer.Ordinal), "historical structure links");
        RequireOrder(statisticsInput, statisticsInput.OrderBy(x => x.WorldMinute), "statistics samples");
        RequireOrder(memoriesInput, memoriesInput.OrderBy(x => x.CitizenId.Value).ThenBy(x => x.HistoricalEventId.Value).ThenBy(x => x.MemoryType), "memories");
        ScheduledEvents = Array.AsReadOnly(events); World = world; Citizens = Array.AsReadOnly(citizensInput.OrderBy(x => x.Id.Value).Select(CloneCitizen).ToArray()); CitizenGenerationVersion = citizenGenerationVersion; ResourceStates = Array.AsReadOnly(resourcesInput.OrderBy(x => x.ResourceNodeId.Value).Select(x => new ResourceState(x.ResourceNodeId, x.CurrentQuantity)).ToArray()); Settlement = settlement is null ? null : CloneSettlement(settlement); SurvivalVersion = survivalVersion; SettlementVersion = settlementVersion; Structures = Array.AsReadOnly(structuresInput.OrderBy(x => x.Id.Value).Select(CloneStructure).ToArray()); StructureContributions = Array.AsReadOnly(contributionsInput.OrderBy(x => x.StructureId.Value).ThenBy(x => x.CitizenId.Value).Select(CloneContribution).ToArray()); SocialVersion = socialVersion; Relationships = Array.AsReadOnly(relationshipsInput.OrderBy(x => x.CitizenAId.Value).ThenBy(x => x.CitizenBId.Value).Select(x => x.Validate()).ToArray()); Households = Array.AsReadOnly(householdsInput.OrderBy(x => x.Id.Value).Select(CloneHousehold).ToArray()); HistoryVersion = historyVersion; HistoryState = historyState is null ? null : CloneHistoryState(historyState); HistoricalEvents = Array.AsReadOnly(historicalEventsInput.Select(x => x.Validate(worldMinute.Value)).ToArray()); HistoricalEventCitizens = Array.AsReadOnly(historicalCitizenLinksInput.ToArray()); HistoricalEventStructures = Array.AsReadOnly(historicalStructureLinksInput.ToArray()); StatisticsSamples = Array.AsReadOnly(statisticsInput.ToArray()); Memories = Array.AsReadOnly(memoriesInput.ToArray());
        if (m6)
        {
            HistoryState?.Validate(worldMinute.Value);
            foreach (var link in HistoricalEventCitizens) link.Validate();
            foreach (var link in HistoricalEventStructures) link.Validate();
            foreach (var sample in StatisticsSamples) sample.Validate(worldMinute.Value);
            foreach (var memory in Memories) memory.Validate(worldMinute.Value);
            if (HistoryState is null) throw new ArgumentException("M6 snapshots require history state.", nameof(historyState));
            ValidateM6State(Seed, HistoricalEvents, HistoricalEventCitizens, HistoricalEventStructures, StatisticsSamples, Memories, HistoryState!, Counters, Citizens, Structures, StructureContributions, Households, ScheduledEvents, World!, Settlement ?? throw new ArgumentException("History snapshots require settlement state.", nameof(settlement)), worldMinute, simulationRulesVersion);
        }
        if (citizenGenerationVersion == 1 && m2) ValidateM2Roster(Citizens, ScheduledEvents, world, worldMinute);
        if (m3) ValidateM3State(Citizens, ScheduledEvents, ResourceStates, Settlement, world, worldMinute);
        if (m4) ValidateM4State(Citizens, ScheduledEvents, ResourceStates, Settlement, Structures, StructureContributions, Counters, world, worldMinute);
        if (m5) ValidateM5State(Citizens, ScheduledEvents, ResourceStates, Settlement, Structures, StructureContributions, Relationships, Households, Counters, world, worldMinute, SimulationEngine.GrowthSystemsEnabled(simulationRulesVersion));
    }
    public WorldSeed Seed { get; } public WorldMinute WorldMinute { get; } public string WorldSchemaVersion { get; } public string SimulationRulesVersion { get; } public string ApplicationVersion { get; } public string WorldConfiguration { get; } public DeterministicCountersSnapshot Counters { get; } public IReadOnlyList<ScheduledEventSnapshot> ScheduledEvents { get; } public WorldMap? World { get; } public IReadOnlyList<Citizen> Citizens { get; } public int CitizenGenerationVersion { get; } public IReadOnlyList<ResourceState> ResourceStates { get; } public SettlementState? Settlement { get; } public int SurvivalVersion { get; } public int SettlementVersion { get; } public IReadOnlyList<Structure> Structures { get; } public IReadOnlyList<StructureContribution> StructureContributions { get; } public int SocialVersion { get; } public IReadOnlyList<RelationshipState> Relationships { get; } public IReadOnlyList<Household> Households { get; } public int HistoryVersion { get; } public HistoryState? HistoryState { get; } public IReadOnlyList<HistoricalEvent> HistoricalEvents { get; } public IReadOnlyList<HistoricalEventCitizenLink> HistoricalEventCitizens { get; } public IReadOnlyList<HistoricalEventStructureLink> HistoricalEventStructures { get; } public IReadOnlyList<StatisticsSample> StatisticsSamples { get; } public IReadOnlyList<CitizenMemory> Memories { get; }
    private static HistoryState CloneHistoryState(HistoryState value) => new(value.HistoryStartMinute, value.HistoryStartEventId, value.PeriodStartMinute, value.BirthsSinceSample, value.DeathsSinceSample, value.FoodProducedSinceSample, value.FoodConsumedSinceSample, value.ActiveFoodShortage, value.PopulationMilestoneWatermark);
    private static void RequireOrder<T>(IReadOnlyList<T> actual, IEnumerable<T> ordered, string name)
    {
        var expected = ordered.ToArray();
        if (actual.Count != expected.Length || actual.Zip(expected).Any(pair => !EqualityComparer<T>.Default.Equals(pair.First, pair.Second))) throw new ArgumentException($"{name} must be in canonical order.", name);
    }
    private static void ValidateM6State(WorldSeed seed, IReadOnlyList<HistoricalEvent> events, IReadOnlyList<HistoricalEventCitizenLink> citizenLinks, IReadOnlyList<HistoricalEventStructureLink> structureLinks, IReadOnlyList<StatisticsSample> statistics, IReadOnlyList<CitizenMemory> memories, HistoryState state, DeterministicCountersSnapshot counters, IReadOnlyList<Citizen> citizens, IReadOnlyList<Structure> structures, IReadOnlyList<StructureContribution> contributions, IReadOnlyList<Household> households, IReadOnlyList<ScheduledEventSnapshot> scheduled, WorldMap world, SettlementState settlement, WorldMinute minute, string rulesVersion)
    {
        state.Validate(minute.Value);
        if (events.Count == 0 || events[0].Id.Value != state.HistoryStartEventId || events.Zip(events.Skip(1)).Any(pair => pair.Second.Id.Value != pair.First.Id.Value + 1) || counters.NextHistoricalEventId != checked(events[^1].Id.Value + 1)) throw new ArgumentException("M6 historical event IDs and counter are invalid.", nameof(events));
        if (events.Zip(events.Skip(1)).Any(pair => pair.Second.WorldMinute < pair.First.WorldMinute)) throw new ArgumentException("M6 historical events are not in canonical time order.", nameof(events));
        var citizenIds = citizens.Select(x => x.Id.Value).ToHashSet(); var structureIds = structures.Select(x => x.Id.Value).ToHashSet(); var eventIds = events.Select(x => x.Id.Value).ToHashSet();
        foreach (var link in citizenLinks) { link.Validate(); if (!eventIds.Contains(link.HistoricalEventId.Value) || !citizenIds.Contains(link.CitizenId.Value)) throw new ArgumentException("M6 citizen history link references unknown state.", nameof(citizenLinks)); }
        foreach (var link in structureLinks) { link.Validate(); if (!eventIds.Contains(link.HistoricalEventId.Value) || !structureIds.Contains(link.StructureId.Value)) throw new ArgumentException("M6 structure history link references unknown state.", nameof(structureLinks)); }
        if (citizenLinks.Select(x => (x.HistoricalEventId.Value, x.CitizenId.Value, x.Role)).Distinct().Count() != citizenLinks.Count || structureLinks.Select(x => (x.HistoricalEventId.Value, x.StructureId.Value, x.Role)).Distinct().Count() != structureLinks.Count) throw new ArgumentException("M6 history links must be unique.", nameof(citizenLinks));
        var lastPopulationMilestone = 0;
        foreach (var (historicalEvent, eventIndex) in events.Select((item, index) => (item, index)))
        {
            var eventCitizens = citizenLinks.Where(x => x.HistoricalEventId == historicalEvent.Id).ToArray();
            var eventStructures = structureLinks.Where(x => x.HistoricalEventId == historicalEvent.Id).ToArray();
            historicalEvent.ValidateLinks(eventCitizens, eventStructures);
            ValidateHistoricalReferences(seed, historicalEvent, eventIndex, events, eventCitizens, eventStructures, citizens, structures, contributions, households, world, state, rulesVersion, citizenLinks);
            if (historicalEvent.EventType == HistoricalEventType.PopulationMilestone)
            {
                using var document = JsonDocument.Parse(historicalEvent.PayloadJson);
                var milestone = document.RootElement.GetProperty("population").GetInt32();
                var expectedMilestone = M6PopulationMilestones.FirstOrDefault(value => value > lastPopulationMilestone);
                if (milestone != expectedMilestone) throw new ArgumentException("Population milestones must be emitted once in canonical threshold order.", nameof(events));
                if (ObservableLivingPopulationAt(citizens, events, eventIndex, historicalEvent.WorldMinute) < milestone) throw new ArgumentException("Population milestone exceeds the observable living population at its event minute.", nameof(events));
                lastPopulationMilestone = milestone;
            }
        }
        if (state.PopulationMilestoneWatermark != lastPopulationMilestone) throw new ArgumentException("M6 population milestone watermark does not match the historical prefix.", nameof(state));
        if (SimulationEngine.GrowthSystemsEnabled(rulesVersion)) GrowthHistoryValidation.ValidatePartnerships(events, citizenLinks, citizens);
        ValidateFoodShortageHistory(events, statistics, state, citizens, settlement, rulesVersion);
        ValidateSpecializationTransitions(events, citizenLinks, citizens);
        ValidateStatistics(statistics, state, minute);
        if (memories.Select(x => (x.CitizenId.Value, x.HistoricalEventId.Value, x.MemoryType)).Distinct().Count() != memories.Count) throw new ArgumentException("M6 memories are not unique.");
        var lowerMemoryCounts = memories.Where(x => x.MemoryType is MemoryType.FriendshipFormed or MemoryType.RivalryFormed or MemoryType.StructureCompleted).GroupBy(x => x.CitizenId.Value).ToArray();
        if (lowerMemoryCounts.Any(group => group.Count() > 64)) throw new ArgumentException("M6 lower-level memories exceed the per-citizen bound.", nameof(memories));
        foreach (var memory in memories)
        {
            var historicalEvent = events.Single(x => x.Id == memory.HistoricalEventId);
            if (memory.CreatedMinute != historicalEvent.WorldMinute || memory.Importance != historicalEvent.Importance || !MemoryMatchesEvent(memory, historicalEvent, citizenLinks, citizens, events)) throw new ArgumentException($"M6 memory for citizen {memory.CitizenId.Value}, event {historicalEvent.Id.Value} at minute {historicalEvent.WorldMinute} does not match its historical event.", nameof(memories));
        }
        var sampleEvent = scheduled.Where(x => x.Name == CitizenEventNames.StatisticsSample).ToArray();
        var expected = new WorldMinute(checked(((minute.Value / (30L * WorldCalendar.MinutesPerDay)) + 1) * (30L * WorldCalendar.MinutesPerDay)));
        if (sampleEvent.Length != 1 || sampleEvent[0].Order.Priority != CitizenEventNames.StatisticsSamplePriority || sampleEvent[0].Order.EntitySortKey != 0 || sampleEvent[0].Order.DueWorldMinute != expected || sampleEvent[0].PayloadJson != "{\"version\":1}") throw new ArgumentException("M6 requires one canonical statistics event at the next strict monthly boundary.", nameof(scheduled));
    }

    private static void ValidateFoodShortageHistory(IReadOnlyList<HistoricalEvent> events, IReadOnlyList<StatisticsSample> statistics, HistoryState state,
        IReadOnlyList<Citizen> citizens, SettlementState settlement, string rulesVersion)
    {
        var expectedStart = true;
        foreach (var historicalEvent in events.Where(x => x.EventType is HistoricalEventType.ResourceShortageStarted or HistoricalEventType.ResourceShortageEnded))
        {
            var isStart = historicalEvent.EventType == HistoricalEventType.ResourceShortageStarted;
            if (isStart != expectedStart) throw new ArgumentException("Food shortage history must alternate start and end events.", nameof(events));
            using var document = JsonDocument.Parse(historicalEvent.PayloadJson);
            var payload = document.RootElement;
            var quantity = payload.GetProperty("quantity").GetInt32();
            var living = payload.GetProperty("livingPopulation").GetInt32();
            if (isStart)
            {
                if (living <= 0 || (long)quantity >= checked((long)living * 10)) throw new ArgumentException("Food shortage start is not canonical.", nameof(events));
            }
            else
            {
                if (payload.GetProperty("preexistingAtHistoryStart").GetBoolean()) throw new ArgumentException("Food shortage end cannot be preexisting.", nameof(events));
                if ((long)quantity < checked((long)living * SimulationEngine.FoodShortageRecoveryMultiplier(rulesVersion))) throw new ArgumentException("Food shortage end does not meet its version-specific recovery threshold.", nameof(events));
                if (SimulationEngine.UsesSampledShortageRecovery(rulesVersion) && living > 0)
                {
                    var period = 30L * WorldCalendar.MinutesPerDay;
                    if (historicalEvent.WorldMinute <= 0 || historicalEvent.WorldMinute % period != 0) throw new ArgumentException("M8 nonzero-population shortage ends must occur at statistics-sample boundaries.", nameof(events));
                    var sample = statistics.SingleOrDefault(x => x.WorldMinute == historicalEvent.WorldMinute);
                    if (sample is null || sample.Population != living || sample.FoodStored != quantity) throw new ArgumentException("M8 shortage end must match its statistics sample.", nameof(events));
                }
            }
            expectedStart = !expectedStart;
        }

        var latestActive = !expectedStart;
        if (state.ActiveFoodShortage != latestActive) throw new ArgumentException("History shortage state does not match its latest shortage event.", nameof(state));
        var livingPopulation = citizens.Count(static citizen => citizen.IsAlive);
        if (!state.ActiveFoodShortage && livingPopulation > 0 && settlement.FoodStored < checked(livingPopulation * 10L)) throw new ArgumentException("Inactive history shortage state is below its start threshold.", nameof(state));
        if (rulesVersion == SimulationEngine.M6SimulationRulesVersion)
        {
            // M6 uses immediate hysteresis: an active shortage needs the recovery
            // threshold to end, while an inactive state only starts below 10x.
            var expectedActive = state.ActiveFoodShortage
                ? livingPopulation > 0 && settlement.FoodStored < checked(livingPopulation * SimulationEngine.FoodShortageRecoveryMultiplier(rulesVersion))
                : livingPopulation > 0 && settlement.FoodStored < checked(livingPopulation * 10L);
            if (state.ActiveFoodShortage != expectedActive) throw new ArgumentException("M6 history shortage state does not match its immediate recovery threshold.", nameof(state));
        }
    }

    private static void ValidateStatistics(IReadOnlyList<StatisticsSample> statistics, HistoryState state, WorldMinute minute)
    {
        const long period = 30L * WorldCalendar.MinutesPerDay;
        var expectedMinute = checked(((state.HistoryStartMinute / period) + 1) * period);
        var expectedPeriodStart = state.HistoryStartMinute;
        foreach (var sample in statistics)
        {
            sample.Validate(minute.Value);
            if (sample.WorldMinute != expectedMinute || sample.PeriodStartMinute != expectedPeriodStart) throw new ArgumentException("M6 statistics must use exact 30-day cadence and period chaining.", nameof(statistics));
            expectedPeriodStart = sample.WorldMinute;
            expectedMinute = checked(expectedMinute + period);
        }
    }

    private static void ValidateSpecializationTransitions(IReadOnlyList<HistoricalEvent> events, IReadOnlyList<HistoricalEventCitizenLink> citizenLinks, IReadOnlyList<Citizen> citizens)
    {
        var transitions = events.Where(x => x.EventType == HistoricalEventType.CitizenSpecializationChanged)
            .Select(item => new
            {
                Event = item,
                Subject = citizenLinks.Single(x => x.HistoricalEventId == item.Id && x.Role == "subject").CitizenId
            })
            .GroupBy(x => x.Subject.Value);

        foreach (var subjectTransitions in transitions)
        {
            var citizen = citizens.SingleOrDefault(x => x.Id.Value == subjectTransitions.Key) ?? throw new ArgumentException("Specialization subject is unknown.", nameof(citizenLinks));
            string? previousTo = null;
            foreach (var transition in subjectTransitions)
            {
                using var document = System.Text.Json.JsonDocument.Parse(transition.Event.PayloadJson);
                var payload = document.RootElement;
                var from = payload.GetProperty("from").GetString()!;
                var to = payload.GetProperty("to").GetString()!;
                if (previousTo is not null && !string.Equals(from, previousTo, StringComparison.Ordinal)) throw new ArgumentException("M6 specialization transitions are not a canonical chain.", nameof(events));
                previousTo = to;
            }
            if (previousTo is not null && !string.Equals(previousTo, citizen.Occupation, StringComparison.Ordinal)) throw new ArgumentException("M6 specialization history does not match the current occupation.", nameof(events));
        }
    }

    private static bool MemoryMatchesEvent(CitizenMemory memory, HistoricalEvent historicalEvent, IReadOnlyList<HistoricalEventCitizenLink> links, IReadOnlyList<Citizen> citizens, IReadOnlyList<HistoricalEvent> events)
    {
        var expectedType = historicalEvent.EventType switch
        {
            HistoricalEventType.CitizenBorn => MemoryType.ChildBorn,
            HistoricalEventType.CitizenDied => MemoryType.PartnerDied,
            HistoricalEventType.PartnershipFormed => MemoryType.PartnershipFormed,
            HistoricalEventType.FriendshipFormed => MemoryType.FriendshipFormed,
            HistoricalEventType.RivalryFormed => MemoryType.RivalryFormed,
            HistoricalEventType.StructureCompleted => MemoryType.StructureCompleted,
            _ => (MemoryType?)null
        };
        if (expectedType is null || memory.MemoryType != expectedType.Value) return false;
        var expectedValence = memory.MemoryType switch
        {
            MemoryType.ChildBorn => 8000,
            MemoryType.PartnerDied => -9000,
            MemoryType.PartnershipFormed => 8000,
            MemoryType.FriendshipFormed => 6000,
            MemoryType.RivalryFormed => -7000,
            MemoryType.StructureCompleted => 4000,
            _ => 0
        };
        if (memory.EmotionalValence != expectedValence) return false;
        if (historicalEvent.EventType == HistoricalEventType.CitizenDied)
        {
            // CitizenDied intentionally persists only the deceased subject link. The
            // memory recipient is reconstructed from the deceased's canonical partner
            // state and must have been alive when the death occurred. A later
            // death in the same minute does not invalidate an existing memory:
            // the append-only event order proves the recipient was still alive.
            var deceasedLink = links.SingleOrDefault(x => x.HistoricalEventId == historicalEvent.Id && x.Role == "subject");
            var deceased = deceasedLink is null ? null : citizens.SingleOrDefault(x => x.Id == deceasedLink.CitizenId);
            return deceased?.PartnerId is { } partnerId
                && partnerId == memory.CitizenId
                && citizens.SingleOrDefault(x => x.Id == partnerId) is { } partner
                && partner.BirthMinute <= historicalEvent.WorldMinute
                && (partner.DeathMinute is null || partner.DeathMinute.Value > historicalEvent.WorldMinute
                    || (partner.DeathMinute.Value == historicalEvent.WorldMinute
                        && historicalEvent.Origin == HistoricalEventOrigin.Live
                        && events.Any(item => item.EventType == HistoricalEventType.CitizenDied
                            && item.Origin == HistoricalEventOrigin.Live
                            && item.WorldMinute == historicalEvent.WorldMinute
                            && item.Id.Value > historicalEvent.Id.Value
                            && links.Any(link => link.HistoricalEventId == item.Id && link.CitizenId == partnerId && link.Role == "subject"))));
        }
        var eligibleRoles = historicalEvent.EventType switch
        {
            HistoricalEventType.CitizenBorn => new[] { "parent" },
            HistoricalEventType.PartnershipFormed => new[] { "partner" },
            HistoricalEventType.FriendshipFormed or HistoricalEventType.RivalryFormed => new[] { "participant" },
            HistoricalEventType.StructureCompleted => new[] { "contributor" },
            _ => Array.Empty<string>()
        };
        return links.Any(x => x.HistoricalEventId == historicalEvent.Id && x.CitizenId == memory.CitizenId && eligibleRoles.Contains(x.Role, StringComparer.Ordinal));
    }

    private static int ObservableLivingPopulationAt(IReadOnlyList<Citizen> citizens, IReadOnlyList<HistoricalEvent> events, int eventIndex, long minute)
    {
        var population = citizens.Count(citizen => citizen.BirthMinute <= minute && (citizen.DeathMinute is null || citizen.DeathMinute.Value > minute));
        for (var index = eventIndex + 1; index < events.Count && events[index].WorldMinute == minute; index++)
        {
            population += events[index].EventType switch
            {
                HistoricalEventType.CitizenBorn => -1,
                HistoricalEventType.CitizenDied => 1,
                _ => 0
            };
        }
        return population;
    }

    private static HistoricalImportance CanonicalImportance(HistoricalEventType eventType) => eventType switch
    {
        HistoricalEventType.WorldCreated or HistoricalEventType.SettlementFounded => HistoricalImportance.Historic,
        HistoricalEventType.CitizenBorn or HistoricalEventType.CitizenDied or HistoricalEventType.PartnershipFormed or HistoricalEventType.StructureCompleted or HistoricalEventType.ResourceShortageStarted or HistoricalEventType.ResourceShortageEnded => HistoricalImportance.Notable,
        HistoricalEventType.PopulationMilestone => HistoricalImportance.Major,
        HistoricalEventType.FriendshipFormed or HistoricalEventType.RivalryFormed or HistoricalEventType.HouseholdCreated or HistoricalEventType.StructureStarted or HistoricalEventType.CitizenSpecializationChanged => HistoricalImportance.Personal,
        HistoricalEventType.SeasonStarted => HistoricalImportance.Routine,
        _ => throw new ArgumentException("Historical event type is unsupported.", nameof(eventType))
    };

    private static void ValidateHistoricalReferences(WorldSeed seed, HistoricalEvent historicalEvent, int eventIndex, IReadOnlyList<HistoricalEvent> events,
        HistoricalEventCitizenLink[] citizenLinks, IReadOnlyList<HistoricalEventStructureLink> structureLinks,
        IReadOnlyList<Citizen> citizens, IReadOnlyList<Structure> structures, IReadOnlyList<StructureContribution> contributions,
        IReadOnlyList<Household> households, WorldMap world, HistoryState state, string rulesVersion,
        IReadOnlyList<HistoricalEventCitizenLink> allCitizenLinks)
    {
        if (historicalEvent.Importance != CanonicalImportance(historicalEvent.EventType)) throw new ArgumentException("Historical event importance is not canonical for its event type.", nameof(historicalEvent));
        using var payloadDocument = System.Text.Json.JsonDocument.Parse(historicalEvent.PayloadJson);
        var payload = payloadDocument.RootElement;
        var subject = citizenLinks.SingleOrDefault(x => x.Role == "subject");
        var linkedStructure = structureLinks.SingleOrDefault(x => x.Role == "subject");
        switch (historicalEvent.EventType)
        {
            case HistoricalEventType.WorldCreated:
                if (historicalEvent.WorldMinute != 0 || historicalEvent.Location != world.StartingSite || payload.GetProperty("seed").GetString() != seed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new ArgumentException("WorldCreated does not match the canonical world.");
                break;
            case HistoricalEventType.SettlementFounded:
                var founders = citizens.Where(x => x.FounderOrdinal is not null).Select(x => x.Id.Value).ToHashSet();
                if (historicalEvent.WorldMinute != 0 || historicalEvent.Location != world.StartingSite || payload.GetProperty("founderCount").GetInt32() != founders.Count || !citizenLinks.Select(x => x.CitizenId.Value).ToHashSet().SetEquals(founders)) throw new ArgumentException("SettlementFounded does not match the canonical founder links.");
                break;
            case HistoricalEventType.CitizenBorn:
                if (subject is null || citizenLinks.Count(x => x.Role == "parent") != 2) throw new ArgumentException("CitizenBorn requires exactly two parents and one subject.");
                var born = citizens.SingleOrDefault(x => x.Id == subject.CitizenId) ?? throw new ArgumentException("CitizenBorn subject is unknown.");
                var parentIds = citizenLinks.Where(x => x.Role == "parent").Select(x => x.CitizenId.Value).OrderBy(x => x).ToArray();
                var expectedParents = new[] { born.ParentAId?.Value ?? 0, born.ParentBId?.Value ?? 0 }.OrderBy(x => x).ToArray();
                if (born.FounderOrdinal is not null || born.BirthMinute < 0 || !parentIds.SequenceEqual(expectedParents) || historicalEvent.WorldMinute != born.BirthMinute || historicalEvent.Origin == HistoricalEventOrigin.Live && (historicalEvent.Location is null || historicalEvent.Location.Value.X >= world.Width || historicalEvent.Location.Value.Y >= world.Height)) throw new ArgumentException("CitizenBorn does not match the canonical birth transition.");
                if (payload.TryGetProperty("householdId", out var bornHousehold))
                {
                    if (!long.TryParse(bornHousehold.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var householdId)) throw new ArgumentException("CitizenBorn household reference is not canonical.");
                    var referencedHousehold = households.SingleOrDefault(x => x.Id.Value == householdId);
                    if (referencedHousehold is null) throw new ArgumentException("CitizenBorn household reference is not canonical.");
                    if (historicalEvent.Origin == HistoricalEventOrigin.Live)
                    {
                        if (referencedHousehold.CreatedMinute > born.BirthMinute || referencedHousehold.DissolvedMinute is { } dissolved && dissolved <= born.BirthMinute || !HasBirthHouseholdFormationProof(events, eventIndex, allCitizenLinks, referencedHousehold, householdId, parentIds))
                            throw new ArgumentException("CitizenBorn household reference is not provably canonical.");
                    }
                    else
                    {
                        var parents = citizenLinks.Where(x => x.Role == "parent").Select(x => citizens.SingleOrDefault(c => c.Id == x.CitizenId)).ToArray();
                        var provenBirthHousehold = parents.Length == 2 && parents[0] is { } firstParent && parents[1] is { } secondParent
                            && firstParent.PartnerId == secondParent.Id && secondParent.PartnerId == firstParent.Id
                            && firstParent.HouseholdId == referencedHousehold.Id && secondParent.HouseholdId == referencedHousehold.Id
                            && referencedHousehold.CreatedMinute <= born.BirthMinute
                            && (referencedHousehold.DissolvedMinute is null || referencedHousehold.DissolvedMinute.Value > born.BirthMinute);
                        if (!provenBirthHousehold) throw new ArgumentException("Migration CitizenBorn household reference is not provably canonical.");
                    }
                }
                else if (historicalEvent.Origin == HistoricalEventOrigin.Live) throw new ArgumentException("Live CitizenBorn is missing its canonical household reference.");
                break;
            case HistoricalEventType.CitizenDied:
                if (subject is null) throw new ArgumentException("CitizenDied requires a subject link.");
                var deceased = citizens.SingleOrDefault(x => x.Id == subject.CitizenId) ?? throw new ArgumentException("CitizenDied subject is unknown.");
                if (deceased.IsAlive || deceased.DeathMinute != historicalEvent.WorldMinute || historicalEvent.Location != deceased.Location || !string.Equals(payload.GetProperty("cause").GetString(), deceased.DeathCause, StringComparison.Ordinal) || payload.GetProperty("ageYears").GetInt32() != deceased.AgeYears(new WorldMinute(historicalEvent.WorldMinute))) throw new ArgumentException("CitizenDied does not match the canonical death transition.");
                break;
            case HistoricalEventType.FriendshipFormed:
            case HistoricalEventType.RivalryFormed:
                var relationshipParticipants = citizenLinks.Where(x => x.Role == "participant").Select(x => x.CitizenId).OrderBy(x => x.Value).ToArray();
                var relationshipPair = RelationshipState.Normalize(relationshipParticipants[0], relationshipParticipants[1]);
                var relationship = new RelationshipState(relationshipPair.A, relationshipPair.B,
                    payload.GetProperty("familiarity").GetInt32(), payload.GetProperty("affinity").GetInt32(),
                    payload.GetProperty("trust").GetInt32(), payload.GetProperty("conflict").GetInt32(), historicalEvent.WorldMinute, 1);
                var relationshipLabel = RelationshipLabels.Derive(relationship, isPartner: false, isFamily: false);
                var reachesFormationLabel = historicalEvent.EventType == HistoricalEventType.FriendshipFormed
                    ? relationshipLabel is RelationshipLabels.Friend or RelationshipLabels.CloseFriend
                    : relationshipLabel == RelationshipLabels.Rival;
                if (!reachesFormationLabel) throw new ArgumentException("Relationship history payload does not satisfy its canonical formation threshold.");
                break;
            case HistoricalEventType.PartnershipFormed:
            case HistoricalEventType.HouseholdCreated:
                var relationshipHouseholdId = long.Parse(payload.GetProperty("householdId").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var household = households.SingleOrDefault(x => x.Id.Value == relationshipHouseholdId);
                var members = citizenLinks.Select(x => citizens.SingleOrDefault(c => c.Id == x.CitizenId) ?? throw new ArgumentException("Household event references an unknown citizen.")).ToArray();
                if (household is null || members.Length != 2 || (!SimulationEngine.GrowthSystemsEnabled(rulesVersion) && (members.Any(x => x.HouseholdId?.Value != relationshipHouseholdId) || members[0].PartnerId != members[1].Id || members[1].PartnerId != members[0].Id)) || household.CreatedMinute != historicalEvent.WorldMinute) throw new ArgumentException("Household event does not match the canonical partnership.");
                break;
            case HistoricalEventType.StructureStarted:
                if (linkedStructure is null) throw new ArgumentException("StructureStarted requires a structure subject.");
                var started = structures.SingleOrDefault(x => x.Id == linkedStructure.StructureId) ?? throw new ArgumentException("StructureStarted subject is unknown.");
                if (historicalEvent.WorldMinute != started.ConstructionStartedMinute || historicalEvent.Location != started.Location || payload.GetProperty("structureType").GetString() != started.Type.ToString() || payload.GetProperty("requiredWood").GetInt32() != started.RequiredWood || payload.GetProperty("requiredStone").GetInt32() != started.RequiredStone || payload.GetProperty("requiredWork").GetInt32() != started.RequiredWork) throw new ArgumentException("StructureStarted does not match the canonical construction start.");
                break;
            case HistoricalEventType.StructureCompleted:
                if (linkedStructure is null) throw new ArgumentException("StructureCompleted requires a structure subject.");
                var completed = structures.SingleOrDefault(x => x.Id == linkedStructure.StructureId) ?? throw new ArgumentException("StructureCompleted subject is unknown.");
                var expectedContributors = contributions.Where(x => x.StructureId == completed.Id && (x.ConstructionWork > 0 || x.WoodDelivered > 0 || x.StoneDelivered > 0)).Select(x => x.CitizenId.Value).OrderBy(x => x).ToArray();
                var actualContributors = citizenLinks.Where(x => x.Role == "contributor").Select(x => x.CitizenId.Value).OrderBy(x => x).ToArray();
                if (completed.Status != StructureStatus.Complete || completed.CompletedMinute != historicalEvent.WorldMinute || historicalEvent.Location != completed.Location || payload.GetProperty("structureType").GetString() != completed.Type.ToString() || !actualContributors.SequenceEqual(expectedContributors)) throw new ArgumentException("StructureCompleted does not match the canonical construction completion.");
                break;
            case HistoricalEventType.CitizenSpecializationChanged:
                if (subject is null || citizens.All(x => x.Id != subject.CitizenId)) throw new ArgumentException("Specialization subject is unknown.");
                break;
            case HistoricalEventType.PopulationMilestone:
                var milestone = payload.GetProperty("population").GetInt32();
                if (milestone is not (25 or 50 or 100 or 250 or 500 or 1000 or 2000)) throw new ArgumentException("Population milestone is not canonical.");
                break;
            case HistoricalEventType.ResourceShortageStarted:
                var startedQuantity = payload.GetProperty("quantity").GetInt32();
                var startedLivingPopulation = payload.GetProperty("livingPopulation").GetInt32();
                if (payload.GetProperty("resourceType").GetString() != ResourceType.Food.ToString() || (long)startedQuantity >= checked((long)startedLivingPopulation * 10)) throw new ArgumentException("Food shortage start is not canonical.");
                break;
            case HistoricalEventType.ResourceShortageEnded:
                var endedQuantity = payload.GetProperty("quantity").GetInt32();
                var endedLivingPopulation = payload.GetProperty("livingPopulation").GetInt32();
                if (payload.GetProperty("resourceType").GetString() != ResourceType.Food.ToString() || payload.GetProperty("preexistingAtHistoryStart").GetBoolean() || (long)endedQuantity < checked((long)endedLivingPopulation * SimulationEngine.FoodShortageRecoveryMultiplier(rulesVersion))) throw new ArgumentException("Food shortage end is not canonical.");
                break;
            case HistoricalEventType.SeasonStarted:
                var calendar = new WorldMinute(historicalEvent.WorldMinute).ToCalendar();
                if (historicalEvent.WorldMinute % NeedsProjection.MinutesPerSeason != 0 || payload.GetProperty("season").GetString() != calendar.Season.ToString() || payload.GetProperty("year").GetInt64() != calendar.Year) throw new ArgumentException("SeasonStarted does not match the canonical calendar boundary.");
                break;
        }
    }

    private static bool HasBirthHouseholdFormationProof(IReadOnlyList<HistoricalEvent> events, int birthEventIndex,
        IReadOnlyList<HistoricalEventCitizenLink> allCitizenLinks, Household household, long householdId, IReadOnlyList<long> parentIds)
    {
        static bool LinksMatch(IReadOnlyList<HistoricalEventCitizenLink> links, HistoricalEventId eventId, string role, IReadOnlyList<long> expected)
            => links.Where(link => link.HistoricalEventId == eventId && link.Role == role).Select(link => link.CitizenId.Value).OrderBy(id => id).SequenceEqual(expected);

        var priorEvents = events.Take(birthEventIndex).Where(item => item.WorldMinute == household.CreatedMinute && HasHouseholdPayload(item.PayloadJson, householdId));
        var partnershipProven = priorEvents.Any(item => item.EventType == HistoricalEventType.PartnershipFormed && LinksMatch(allCitizenLinks, item.Id, "partner", parentIds));
        var householdCreationProven = priorEvents.Any(item => item.EventType == HistoricalEventType.HouseholdCreated && LinksMatch(allCitizenLinks, item.Id, "member", parentIds));
        return partnershipProven && householdCreationProven;
    }

    private static bool HasHouseholdPayload(string payloadJson, long expectedHouseholdId)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        return root.TryGetProperty("householdId", out var householdId)
            && householdId.ValueKind == JsonValueKind.String
            && long.TryParse(householdId.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedHouseholdId)
            && parsedHouseholdId == expectedHouseholdId;
    }
    private static void ValidateM2Roster(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, WorldMap? world, WorldMinute minute)
    {
        if (citizens.Count != CitizenGenerator.FounderCount || !citizens.Select(c => c.FounderOrdinal).OfType<int>().OrderBy(x => x).SequenceEqual(Enumerable.Range(0, CitizenGenerator.FounderCount))) throw new ArgumentException("M2 requires exactly 20 founders with ordinals 0..19.", nameof(citizens));
        if (citizens.Select(c => c.Id.Value).Distinct().Count() != citizens.Count || citizens.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != citizens.Count) throw new ArgumentException("M2 founder IDs and names must be unique.", nameof(citizens));
        if (world is null) throw new ArgumentException("M2 snapshots require a persisted world map.", nameof(world));

        var reservedEvents = events.Where(static e => e.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete).ToArray();
        if (reservedEvents.Length != citizens.Count) throw new ArgumentException("M2 requires exactly one reserved event per citizen.", nameof(events));
        var citizenIds = citizens.Select(c => c.Id.Value).ToHashSet();
        foreach (var citizen in citizens)
        {
            citizen.Validate(world);
            if (citizen.BirthMinute >= 0 || citizen.DeathMinute is not null || citizen.ParentAId is not null || citizen.ParentBId is not null || citizen.PartnerId is not null || citizen.HouseholdId is not null || citizen.HomeStructureId is not null) throw new ArgumentException("M2 founders contain unsupported lifecycle state.", nameof(citizens));
            if (citizen.CurrentAction != CitizenAction.None && (citizen.ActionStartedMinute is null || citizen.ActionCompletesMinute is null || citizen.ActionStartedMinute.Value > minute || citizen.ActionCompletesMinute.Value < minute)) throw new ArgumentException("Citizen action timing is incoherent.", nameof(citizens));

            var (expectedName, expectedPriority, expectedDue) = citizen.CurrentAction switch
            {
                CitizenAction.None => (CitizenEventNames.Decision, CitizenEventNames.DecisionPriority, minute),
                CitizenAction.Idle or CitizenAction.Rest => (CitizenEventNames.ActionComplete, CitizenEventNames.CompletionPriority, citizen.ActionCompletesMinute!.Value),
                CitizenAction.Wander or CitizenAction.Explore => ValidateMovingCitizen(citizen, world, minute),
                _ => throw new ArgumentException("Invalid citizen action.", nameof(citizens))
            };

            var matching = reservedEvents.Where(e => TryReadCitizenPayload(e.PayloadJson, out var payload) && payload.Id == citizen.Id.Value).ToArray();
            if (matching.Length != 1) throw new ArgumentException("M2 requires one next event per citizen.", nameof(events));
            var scheduled = matching[0];
            var payloadValid = TryReadCitizenPayload(scheduled.PayloadJson, out var payload);
            if (scheduled.Order.EntitySortKey != citizen.Id.Value || scheduled.Name != expectedName || scheduled.Order.Priority != expectedPriority || scheduled.Order.DueWorldMinute != expectedDue || !payloadValid || payload.Id != citizen.Id.Value || payload.Sequence != citizen.ActionSequence) throw new ArgumentException($"M2 citizen event does not match citizen state: id={citizen.Id.Value}, action={citizen.CurrentAction}, due={scheduled.Order.DueWorldMinute.Value}/{expectedDue.Value}, priority={scheduled.Order.Priority}/{expectedPriority}, sequence={payload.Sequence}/{citizen.ActionSequence}, name={scheduled.Name}/{expectedName}.", nameof(events));
        }

        if (reservedEvents.Any(e => !TryReadCitizenPayload(e.PayloadJson, out var payload) || !citizenIds.Contains(payload.Id) || e.Order.EntitySortKey != payload.Id)) throw new ArgumentException("M2 citizen event references an unknown citizen.", nameof(events));
    }

    private static (string Name, int Priority, WorldMinute Due) ValidateMovingCitizen(Citizen citizen, WorldMap world, WorldMinute minute)
    {
        if (citizen.ActionTarget is not { } target) throw new ArgumentException("Moving citizen must have a target.", nameof(citizen));
        var path = DeterministicPathfinder.Find(world, citizen.Location, target);
        if (path is null || path.Count < 2) throw new ArgumentException("Moving citizen must have a reachable next step.", nameof(citizen));
        var nextStepCost = SimulationEngine.StepCost(path[0], path[1], world);
        var remainingPathCost = SimulationEngine.RemainingPathCost(path, world);
        var completion = citizen.ActionCompletesMinute!.Value;
        if (completion.Value < remainingPathCost) throw new ArgumentException("Moving citizen completion timing does not match the remaining deterministic path.", nameof(citizen));
        var anchor = new WorldMinute(completion.Value - remainingPathCost);
        if (anchor < citizen.ActionStartedMinute!.Value) throw new ArgumentException("Moving citizen completion timing implies movement before the action started.", nameof(citizen));
        if (anchor > minute) throw new ArgumentException("Moving citizen completion timing is in the future of the snapshot.", nameof(citizen));
        var expectedDue = anchor.Add(nextStepCost);
        if (expectedDue < minute) throw new ArgumentException("Moving citizen next-step event is due before the snapshot minute.", nameof(citizen));
        return (CitizenEventNames.MoveStep, CitizenEventNames.MovementPriority, expectedDue);
    }
    private static void ValidateM3State(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, IReadOnlyList<ResourceState> resources, SettlementState? settlement, WorldMap? world, WorldMinute minute)
    {
        if (world is null || settlement is null) throw new ArgumentException("M3 snapshots require world and settlement state.");
        settlement.Validate();
        if (citizens.Count != CitizenSimulationRules.FounderCount || citizens.Select(x => x.Id.Value).Distinct().Count() != citizens.Count || citizens.Select(x => x.FounderOrdinal).OfType<int>().OrderBy(x => x).SequenceEqual(Enumerable.Range(0, CitizenSimulationRules.FounderCount)) is false) throw new ArgumentException("M3 requires the canonical founder roster.", nameof(citizens));
        if (resources.Count != world.Resources.Count || resources.Select(x => x.ResourceNodeId.Value).Distinct().Count() != resources.Count) throw new ArgumentException("M3 requires exactly one resource state per node.");
        foreach (var state in resources) state.Validate(world.Resources.SingleOrDefault(x => x.Id == state.ResourceNodeId) ?? throw new ArgumentException("M3 resource state references an unknown node.", nameof(resources)));
        var living = citizens.Where(x => x.IsAlive).ToArray();
        var reservedEvents = events.Where(static x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck).ToArray();
        var citizenIds = citizens.Select(x => x.Id.Value).ToHashSet();
        if (events.Count(x => x.Name == CitizenEventNames.ResourceRegenerate) != 1 || events.Single(x => x.Name == CitizenEventNames.ResourceRegenerate).Order.EntitySortKey != 0 || events.Single(x => x.Name == CitizenEventNames.ResourceRegenerate).Order.Priority != CitizenEventNames.RegenerationPriority) throw new ArgumentException("M3 requires one resource regeneration event.");
        var regeneration = events.Single(x => x.Name == CitizenEventNames.ResourceRegenerate);
        if (regeneration.Order.DueWorldMinute <= minute || regeneration.Order.DueWorldMinute.Value != checked(((minute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay)) throw new ArgumentException("M3 regeneration must be scheduled at the next strict day boundary.", nameof(events));
        foreach (var citizen in citizens)
        {
            citizen.Validate(world);
            if (citizen.NeedsUpdatedMinute > minute.Value || citizen.HealthUpdatedMinute > minute.Value || citizen.DeathMinute is < 0 || citizen.DeathMinute is { } death && death > minute.Value) throw new ArgumentException("M3 citizen update/death boundaries are invalid.", nameof(citizens));
            if (citizen.IsAlive)
            {
                if (citizen.Health is < 1 or > 10000 || citizen.DeathMinute is not null || citizen.DeathCause is not null) throw new ArgumentException("A living citizen must have positive health and no death state.", nameof(citizens));
            }
            else if (citizen.Health != 0 || citizen.DeathMinute is null || citizen.DeathCause is not ("starvation" or "exhaustion" or "deprivation") || citizen.CurrentAction != CitizenAction.Dead || citizen.ActionPhase != CitizenActionPhase.None || citizen.ActionTarget is not null || citizen.TargetResourceNodeId is not null || citizen.ActionStartedMinute is not null || citizen.ActionCompletesMinute is not null || citizen.CarriedResourceType is not null || citizen.CarriedResourceQuantity != 0) throw new ArgumentException("A dead citizen does not have canonical cleared state.", nameof(citizens));
            if (citizen.Skills.Foraging < 0 || citizen.Skills.Woodcutting < 0 || citizen.Skills.Stoneworking < 0 || citizen.Skills.Construction < 0 || citizen.Skills.Hauling < 0 || citizen.Skills.Domestic < 0 || citizen.LifetimeMovementSteps < 0 || citizen.LifetimeMovementCost < 0) throw new ArgumentException("M3 citizen counters and skills must be non-negative.", nameof(citizens));
            if (citizen.TargetResourceNodeId is { } targetId)
            {
                var targetNode = world.Resources.SingleOrDefault(x => x.Id == targetId) ?? throw new ArgumentException("M3 citizen target references an unknown node.", nameof(citizens));
                var expectedType = citizen.CurrentAction switch { CitizenAction.GatherFood => ResourceType.Food, CitizenAction.GatherWood => ResourceType.Wood, CitizenAction.GatherStone => ResourceType.Stone, _ => (ResourceType?)null };
                if (expectedType is null || targetNode.Type != expectedType.Value || citizen.ActionPhase == CitizenActionPhase.TravelToTarget && citizen.ActionTarget != targetNode.Coordinate || citizen.ActionPhase == CitizenActionPhase.Perform && (citizen.ActionTarget is not null || citizen.Location != targetNode.Coordinate) || citizen.ActionPhase == CitizenActionPhase.ReturnToStockpile && citizen.ActionTarget != world.StartingSite) throw new ArgumentException("M3 citizen resource target state is invalid.", nameof(citizens));
            }
            if (citizen.CurrentAction is (CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone) && citizen.TargetResourceNodeId is null) throw new ArgumentException("M3 gathering requires a resource target.", nameof(citizens));
            if (citizen.CurrentAction == CitizenAction.Eat && ((citizen.ActionPhase == CitizenActionPhase.TravelToTarget && citizen.ActionTarget != world.StartingSite) || (citizen.ActionPhase == CitizenActionPhase.Perform && (citizen.Location != world.StartingSite || citizen.ActionTarget is not null)))) throw new ArgumentException("M3 eating must use the starting site.", nameof(citizens));
            if (citizen.ActionPhase is CitizenActionPhase.TravelToTarget or CitizenActionPhase.Perform && citizen.CarriedResourceQuantity != 0 || citizen.ActionPhase != CitizenActionPhase.ReturnToStockpile && citizen.CarriedResourceType is not null) throw new ArgumentException("M3 carried resources are only valid during return.", nameof(citizens));
            var citizenEvents = reservedEvents.Where(x => TryReadEventCitizenId(x, out var eventCitizenId) && eventCitizenId == citizen.Id.Value).ToArray();
            var count = citizenEvents.Length;
            if (citizen.IsAlive && (count != 2 || citizenEvents.Count(x => x.Name == CitizenEventNames.SurvivalCheck) != 1 || citizenEvents.Count(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) != 1)) throw new ArgumentException("M3 living citizens require one action and one survival event.");
            if (citizen.IsAlive)
            {
                var survival = citizenEvents.Single(x => x.Name == CitizenEventNames.SurvivalCheck);
                var expectedSurvivalDue = new WorldMinute(checked(citizen.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes));
                if (expectedSurvivalDue < minute || survival.Order.DueWorldMinute != expectedSurvivalDue || survival.Order.Priority != CitizenEventNames.SurvivalPriority) throw new ArgumentException("M3 survival must be scheduled at the next interval from the health update boundary.", nameof(events));
                if (!TryReadEventCitizenId(survival, out var survivalCitizenId) || survival.Order.EntitySortKey != citizen.Id.Value || survivalCitizenId != citizen.Id.Value) throw new ArgumentException("M3 survival event payload does not match citizen state.", nameof(events));
                var action = citizenEvents.Single(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete);
                if (citizen.CurrentAction is (CitizenAction.Eat or CitizenAction.Wander or CitizenAction.Explore or CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone) && citizen.ActionPhase is (CitizenActionPhase.TravelToTarget or CitizenActionPhase.ReturnToStockpile))
                {
                    var expectedMove = ExpectedMoveEvent(citizen, world, minute);
                    if (action.Name != expectedMove.Name || action.Order.Priority != expectedMove.Priority || action.Order.DueWorldMinute != expectedMove.Due) throw new ArgumentException($"M3 action event does not match citizen movement state: id={citizen.Id.Value}, event={action.Name}/{action.Order.Priority}/{action.Order.DueWorldMinute.Value}.", nameof(events));
                }
                else
                {
                    var expected = citizen.CurrentAction switch
                    {
                        CitizenAction.None => (CitizenEventNames.Decision, CitizenEventNames.DecisionPriority, minute),
                        CitizenAction.Idle or CitizenAction.Rest or CitizenAction.Eat or CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone when citizen.ActionPhase == CitizenActionPhase.Perform => (CitizenEventNames.ActionComplete, CitizenEventNames.CompletionPriority, citizen.ActionCompletesMinute!.Value),
                        _ => throw new ArgumentException($"M3 citizen action phase is invalid: id={citizen.Id.Value}, action={citizen.CurrentAction}, phase={citizen.ActionPhase}, event={action.Name}, minute={minute.Value}.", nameof(citizens))
                    };
                    if (expected.Item3 < minute || action.Name != expected.Item1 || action.Order.Priority != expected.Item2 || action.Order.DueWorldMinute != expected.Item3) throw new ArgumentException($"M3 action event does not match citizen state: id={citizen.Id.Value}, event={action.Name}/{action.Order.Priority}/{action.Order.DueWorldMinute.Value}, expected={expected.Item1}/{expected.Item2}/{expected.Item3.Value}, action={citizen.CurrentAction}/{citizen.ActionPhase}.", nameof(events));
                }
                if (action.Order.EntitySortKey != citizen.Id.Value || !TryReadCitizenPayload(action.PayloadJson, out var actionPayload) || actionPayload.Id != citizen.Id.Value || actionPayload.Sequence != citizen.ActionSequence) throw new ArgumentException("M3 action event payload does not match citizen state.", nameof(events));
            }
            if (!citizen.IsAlive && count != 0) throw new ArgumentException("Dead citizens cannot have scheduled events.");
        }
        if (reservedEvents.Any(x => !TryReadEventCitizenId(x, out var eventCitizenId) || !citizenIds.Contains(eventCitizenId) || x.Order.EntitySortKey != eventCitizenId)) throw new ArgumentException("M3 citizen event references an unknown citizen.", nameof(events));
        if (events.Count(x => x.Name == CitizenEventNames.SurvivalCheck) != living.Length) throw new ArgumentException("M3 requires one survival event per living citizen.");
    }

    private static void ValidateM4State(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, IReadOnlyList<ResourceState> resources, SettlementState? settlement, IReadOnlyList<Structure> structures, IReadOnlyList<StructureContribution> contributions, DeterministicCountersSnapshot counters, WorldMap? world, WorldMinute minute)
    {
        ValidateM4SettlementInvariants(citizens, events, resources, settlement, structures, contributions, counters, world, minute, requireFounderRoster: true);
        if (!citizens.Select(x => x.FounderOrdinal).All(x => x is not null) || !citizens.Select(x => x.FounderOrdinal!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, CitizenGenerator.FounderCount))) throw new ArgumentException("M4 requires exactly the 20 unique founder ordinals 0..19.", nameof(citizens));
        var structureIds = structures.Select(x => x.Id.Value).ToHashSet();
        var citizenIds = citizens.Select(x => x.Id.Value).ToHashSet();
        var reserved = events.Where(static x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck).ToArray();
        foreach (var citizen in citizens)
        {
            citizen.Validate(world);
            if (citizen.CurrentAction == CitizenAction.Socialize || citizen.TargetCitizenId is not null || citizen.ParentAId is not null || citizen.ParentBId is not null || citizen.PartnerId is not null || citizen.HouseholdId is not null)
                throw new ArgumentException("M4 snapshots cannot carry M5 social state.", nameof(citizens));
            if (citizen.HomeStructureId is { } home && (!structureIds.Contains(home.Value) || structures.Single(x => x.Id == home).Type != StructureType.Shelter || structures.Single(x => x.Id == home).Status != StructureStatus.Complete)) throw new ArgumentException("M4 home must reference a completed shelter.", nameof(citizens));
            if (citizen.TargetStructureId is { } target && !structureIds.Contains(target.Value)) throw new ArgumentException("M4 target structure is unknown.", nameof(citizens));
            if (citizen.CurrentAction is CitizenAction.HaulConstruction or CitizenAction.Build)
            {
                if (citizen.TargetStructureId is not { } constructionTarget || structures.Single(x => x.Id == constructionTarget).Status != StructureStatus.UnderConstruction) throw new ArgumentException("M4 construction action requires the active construction project.", nameof(citizens));
            }
            if (citizen.CurrentAction == CitizenAction.Rest && citizen.ActionPhase == CitizenActionPhase.TravelToTarget)
            {
                if (citizen.HomeStructureId is not { } assignedHome || citizen.TargetStructureId != assignedHome || citizen.ActionTarget != structures.Single(x => x.Id == assignedHome).Location) throw new ArgumentException("M4 rest travel must target the assigned completed home.", nameof(citizens));
            }
            var citizenEvents = reserved.Where(x => TryReadEventCitizenId(x, out var eventId) && eventId == citizen.Id.Value).ToArray();
            if (citizen.IsAlive && (citizenEvents.Length != 2 || citizenEvents.Count(x => x.Name == CitizenEventNames.SurvivalCheck) != 1 || citizenEvents.Count(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) != 1)) throw new ArgumentException("M4 living citizens require one action and one survival event.", nameof(events));
            if (!citizen.IsAlive) { if (citizenEvents.Length != 0) throw new ArgumentException("Dead M4 citizens cannot have gameplay events.", nameof(events)); continue; }
            var survival = citizenEvents.Single(x => x.Name == CitizenEventNames.SurvivalCheck);
            if (survival.Order.Priority != CitizenEventNames.SurvivalPriority || survival.Order.EntitySortKey != citizen.Id.Value || survival.Order.DueWorldMinute != new WorldMinute(checked(citizen.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes))) throw new ArgumentException("M4 survival event does not match the health boundary.", nameof(events));
            var action = citizenEvents.Single(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete);
            var expected = citizen.ActionPhase switch
            {
                CitizenActionPhase.None when citizen.CurrentAction == CitizenAction.None => (CitizenEventNames.Decision, CitizenEventNames.DecisionPriority, minute),
                CitizenActionPhase.TravelToTarget or CitizenActionPhase.ReturnToStockpile or CitizenActionPhase.TravelToStockpile or CitizenActionPhase.TransportToConstruction => ExpectedMoveEvent(citizen, world!, minute),
                CitizenActionPhase.Perform or CitizenActionPhase.WaitingForStorage => (CitizenEventNames.ActionComplete, CitizenEventNames.CompletionPriority, citizen.ActionCompletesMinute ?? throw new ArgumentException("M4 action completion has no due minute.", nameof(citizens))),
                _ => throw new ArgumentException("M4 action phase is invalid for a living citizen.", nameof(citizens))
            };
            if (action.Name != expected.Item1 || action.Order.Priority != expected.Item2 || action.Order.DueWorldMinute != expected.Item3 || action.Order.EntitySortKey != citizen.Id.Value || !TryReadCitizenPayload(action.PayloadJson, out var payload) || payload.Id != citizen.Id.Value || payload.Sequence != citizen.ActionSequence) throw new ArgumentException("M4 action event does not match action state.", nameof(events));
        }
        if (reserved.Any(x => !TryReadEventCitizenId(x, out var eventId) || !citizenIds.Contains(eventId) || x.Order.EntitySortKey != eventId)) throw new ArgumentException("M4 reserved event references an unknown citizen.", nameof(events));
    }

    private static void ValidateM4SettlementInvariants(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, IReadOnlyList<ResourceState> resources, SettlementState? settlement, IReadOnlyList<Structure> structures, IReadOnlyList<StructureContribution> contributions, DeterministicCountersSnapshot counters, WorldMap? world, WorldMinute minute, bool requireFounderRoster)
    {
        if (world is null || settlement is null) throw new ArgumentException("M4 snapshots require world and settlement state.");
        settlement.Validate();
        var latestExposureConsequencesStartMinute = minute.Value <= long.MaxValue - CitizenSimulationRules.ExposureGraceDurationMinutes ? checked(minute.Value + CitizenSimulationRules.ExposureGraceDurationMinutes) : long.MaxValue;
        if (settlement.BaseStorageCapacity < CitizenSimulationRules.BaseStorageCapacity || settlement.DemandUpdatedMinute > minute.Value || settlement.ExposureConsequencesStartMinute > latestExposureConsequencesStartMinute) throw new ArgumentException("M4 settlement boundaries are invalid.", nameof(settlement));
        if ((requireFounderRoster && citizens.Count != CitizenSimulationRules.FounderCount) || citizens.Select(x => x.Id.Value).Distinct().Count() != citizens.Count || resources.Count != world.Resources.Count || resources.Select(x => x.ResourceNodeId.Value).Distinct().Count() != resources.Count) throw new ArgumentException("M4 canonical roster or resources are invalid.");
        foreach (var state in resources) state.Validate(world.Resources.SingleOrDefault(x => x.Id == state.ResourceNodeId) ?? throw new ArgumentException("M4 resource state references an unknown node.", nameof(resources)));
        if (structures.Select(x => x.Id.Value).Distinct().Count() != structures.Count || structures.Select(x => x.Location).Distinct().Count() != structures.Count || structures.Count(x => x.Status == StructureStatus.UnderConstruction) > 1) throw new ArgumentException("M4 structures must have unique IDs, locations, and at most one active project.", nameof(structures));
        var structureIds = structures.Select(x => x.Id.Value).ToHashSet(); var citizenIds = citizens.Select(x => x.Id.Value).ToHashSet();
        if (structureIds.Overlaps(citizenIds) || counters.NextEntityId <= structures.Select(x => x.Id.Value).Concat(citizens.Select(x => x.Id.Value)).DefaultIfEmpty(0).Max()) throw new ArgumentException("M4 entity IDs and next entity counter are invalid.", nameof(counters));
        foreach (var structure in structures)
        {
            structure.Validate();
            if (structure.ConstructionStartedMinute > minute.Value || (structure.Status == StructureStatus.Complete && (structure.CompletedMinute < structure.ConstructionStartedMinute || structure.CompletedMinute > minute.Value))) throw new ArgumentException("M4 structure timeline is invalid.", nameof(structures));
            if (structure.Location == world.StartingSite || !world.GetTile(structure.Location).Buildable || world.GetTile(structure.Location).Terrain == TerrainType.Freshwater || world.GetResources(structure.Location).Count != 0) throw new ArgumentException("M4 structure location is invalid.", nameof(structures));
        }
        if (contributions.Select(x => (x.StructureId.Value, x.CitizenId.Value)).Distinct().Count() != contributions.Count) throw new ArgumentException("M4 contributions must be unique by structure and citizen.", nameof(contributions));
        foreach (var contribution in contributions) { contribution.Validate(); if (!structureIds.Contains(contribution.StructureId.Value) || !citizenIds.Contains(contribution.CitizenId.Value)) throw new ArgumentException("M4 contribution references an unknown entity.", nameof(contributions)); }
        foreach (var structure in structures) if (contributions.Where(x => x.StructureId == structure.Id).Sum(x => x.WoodDelivered) != structure.DeliveredWood || contributions.Where(x => x.StructureId == structure.Id).Sum(x => x.StoneDelivered) != structure.DeliveredStone || contributions.Where(x => x.StructureId == structure.Id).Sum(x => x.ConstructionWork) != structure.CompletedWork) throw new ArgumentException("M4 contribution totals must equal structure totals.", nameof(contributions));
        var capacity = checked(settlement.BaseStorageCapacity + structures.Count(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Stockpile) * CitizenSimulationRules.StockpileStorageBonus);
        if (settlement.StorageUsed > capacity) throw new ArgumentException("M4 settlement storage exceeds capacity.", nameof(settlement));
        var demand = events.Where(x => x.Name == CitizenEventNames.SettlementEvaluateDemand).ToArray();
        if (demand.Length != 1 || demand[0].PayloadJson != "{\"version\":1}" || demand[0].Order.Priority != CitizenEventNames.SettlementDemandPriority || demand[0].Order.EntitySortKey != 0 || demand[0].Order.DueWorldMinute != new WorldMinute(checked(settlement.DemandUpdatedMinute + CitizenSimulationRules.SettlementDemandIntervalMinutes))) throw new ArgumentException("M4 requires one correctly scheduled settlement-demand event.", nameof(events));
        if (events.Count(x => x.Name == CitizenEventNames.ResourceRegenerate) != 1) throw new ArgumentException("M4 requires one resource regeneration event.", nameof(events));
        var regeneration = events.Single(x => x.Name == CitizenEventNames.ResourceRegenerate);
        if (regeneration.Order.Priority != CitizenEventNames.RegenerationPriority || regeneration.Order.EntitySortKey != 0 || regeneration.Order.DueWorldMinute != new WorldMinute(checked(((minute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay))) throw new ArgumentException("M4 regeneration event is not at the next strict day boundary.", nameof(events));
        foreach (var citizen in citizens)
        {
            citizen.Validate(world);
            if (citizen.HomeStructureId is { } home && (!structureIds.Contains(home.Value) || structures.Single(x => x.Id == home).Type != StructureType.Shelter || structures.Single(x => x.Id == home).Status != StructureStatus.Complete)) throw new ArgumentException("M4 home must reference a completed shelter.", nameof(citizens));
            if (citizen.TargetStructureId is { } target && !structureIds.Contains(target.Value)) throw new ArgumentException("M4 target structure is unknown.", nameof(citizens));
            if (citizen.CurrentAction is (CitizenAction.HaulConstruction or CitizenAction.Build) && (citizen.TargetStructureId is not { } constructionTarget || structures.Single(x => x.Id == constructionTarget).Status != StructureStatus.UnderConstruction)) throw new ArgumentException("M4 construction action requires the active construction project.", nameof(citizens));
            if (citizen.CurrentAction == CitizenAction.Rest && citizen.ActionPhase == CitizenActionPhase.TravelToTarget && (citizen.HomeStructureId is not { } assignedHome || citizen.TargetStructureId != assignedHome || citizen.ActionTarget != structures.Single(x => x.Id == assignedHome).Location)) throw new ArgumentException("M4 rest travel must target the assigned completed home.", nameof(citizens));
        }
        var shelters = structures.Where(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Shelter).Select(x => x.Id.Value).ToHashSet();
        if (citizens.Where(x => x.IsAlive && x.HomeStructureId is not null).GroupBy(x => x.HomeStructureId!.Value.Value).Any(x => !shelters.Contains(x.Key) || x.Count() > CitizenSimulationRules.ShelterCapacityPerBuilding)) throw new ArgumentException("M4 shelter occupancy is invalid.", nameof(citizens));
        foreach (var structure in structures.Where(x => x.Status == StructureStatus.UnderConstruction)) foreach (var type in new[] { ResourceType.Wood, ResourceType.Stone }) { var required = type == ResourceType.Wood ? structure.RequiredWood : structure.RequiredStone; var delivered = type == ResourceType.Wood ? structure.DeliveredWood : structure.DeliveredStone; var transit = citizens.Where(x => x.IsAlive && x.CurrentAction == CitizenAction.HaulConstruction && x.TargetStructureId == structure.Id && x.ActionPhase == CitizenActionPhase.TransportToConstruction && x.CarriedResourceType == type).Sum(x => x.CarriedResourceQuantity); if (delivered + transit > required) throw new ArgumentException("M4 construction transit exceeds remaining material.", nameof(citizens)); }
    }

    private static (string Name, int Priority, WorldMinute Due) ExpectedMoveEvent(Citizen citizen, WorldMap world, WorldMinute minute)
    {
        if (citizen.ActionTarget is not { } target) throw new ArgumentException("M3 movement requires an action target.", nameof(citizen));
        var path = DeterministicPathfinder.Find(world, citizen.Location, target);
        if (path is null || path.Count < 2) throw new ArgumentException("M3 movement requires a reachable next step.", nameof(citizen));
        var stepCost = checked((long)((path[0].X == path[1].X || path[0].Y == path[1].Y) ? 10 : 14) * world.GetTile(path[1]).MovementCost);
        if (citizen.ActionCompletesMinute is not { } completion || completion < minute) throw new ArgumentException("M3 movement completion is not at or after the snapshot minute.", nameof(citizen));
        var remaining = SimulationEngine.RemainingPathCost(path, world);
        if (completion.Value < remaining) throw new ArgumentException("M3 movement completion is shorter than the remaining path.", nameof(citizen));
        var movementAnchor = new WorldMinute(completion.Value - remaining);
        if (citizen.ActionStartedMinute is not { } started || movementAnchor < started || movementAnchor > minute) throw new ArgumentException("M3 movement timing does not match the checkpoint state.", nameof(citizen));
        var expectedDue = movementAnchor.Add(stepCost);
        if (expectedDue < minute) throw new ArgumentException("M3 movement event is stale or delayed.", nameof(citizen));
        return (CitizenEventNames.MoveStep, CitizenEventNames.MovementPriority, expectedDue);
    }

    private static void ValidateM5State(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, IReadOnlyList<ResourceState> resourceStates, SettlementState? settlement, IReadOnlyList<Structure> structures, IReadOnlyList<StructureContribution> contributions, IReadOnlyList<RelationshipState> relationships, IReadOnlyList<Household> households, DeterministicCountersSnapshot counters, WorldMap? world, WorldMinute minute, bool growthEnabled = false)
    {
        ValidateM4SettlementInvariants(citizens, events, resourceStates, settlement, structures, contributions, counters, world, minute, requireFounderRoster: false);
        if (world is null || settlement is null) throw new ArgumentException("M5 requires complete M4 state.");
        if (resourceStates.Count != world.Resources.Count || resourceStates.Select(x => x.ResourceNodeId).Distinct().Count() != resourceStates.Count) throw new ArgumentException("M5 requires exactly one resource state per world resource.", nameof(resourceStates));
        foreach (var resourceState in resourceStates) resourceState.Validate(world.Resources.SingleOrDefault(x => x.Id == resourceState.ResourceNodeId) ?? throw new ArgumentException("M5 resource state references an unknown resource.", nameof(resourceStates)));
        var regeneration = events.Where(x => x.Name == CitizenEventNames.ResourceRegenerate).ToArray();
        if (regeneration.Length != 1 || regeneration[0].Order.Priority != CitizenEventNames.RegenerationPriority || regeneration[0].Order.EntitySortKey != 0 || regeneration[0].Order.DueWorldMinute != new WorldMinute(checked(((minute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay))) throw new ArgumentException("M5 regeneration event is not at the next strict day boundary.", nameof(events));
        foreach (var citizen in citizens)
        {
            if (citizen.BirthMinute > minute.Value || citizen.NeedsUpdatedMinute > minute.Value || citizen.HealthUpdatedMinute > minute.Value || citizen.DeathMinute is < 0 || citizen.DeathMinute is { } death && (death > minute.Value || death < citizen.BirthMinute))
                throw new ArgumentException("M5 citizen temporal state is invalid.", nameof(citizens));
            if (citizen.IsAlive && citizen.CurrentAction != CitizenAction.None && (citizen.ActionStartedMinute is not { } started || citizen.ActionCompletesMinute is not { } completes || started.Value > minute.Value || completes.Value < minute.Value || completes.Value < started.Value))
                throw new ArgumentException("M5 living citizen action timing is invalid.", nameof(citizens));
            citizen.Validate(world);
        }
        var founders = citizens.Where(x => x.FounderOrdinal is not null).Select(x => x.FounderOrdinal!.Value).OrderBy(x => x).ToArray();
        if (!founders.SequenceEqual(Enumerable.Range(0, CitizenGenerator.FounderCount))) throw new ArgumentException("M5 requires exactly the 20 founder ordinals.");
        var ids = citizens.Select(x => x.Id.Value).ToHashSet();
        var structureIds = structures.Select(x => x.Id.Value).ToHashSet();
        var householdIds = households.Select(x => x.Id.Value).ToHashSet();
        if (ids.Count != citizens.Count || structureIds.Count != structures.Count || householdIds.Count != households.Count || structureIds.Overlaps(ids) || householdIds.Overlaps(ids) || householdIds.Overlaps(structureIds)) throw new ArgumentException("M5 entity IDs must be unique within each collection and globally disjoint.");
        var maximumId = new[] { ids.DefaultIfEmpty(0).Max(), structureIds.DefaultIfEmpty(0).Max(), householdIds.DefaultIfEmpty(0).Max() }.Max();
        if (counters.NextEntityId <= maximumId) throw new ArgumentException("M5 entity counter is stale.");
        foreach (var relationship in relationships)
        {
            relationship.Validate();
            if (!ids.Contains(relationship.CitizenAId.Value) || !ids.Contains(relationship.CitizenBId.Value) || relationship.LastInteractionMinute > minute.Value) throw new ArgumentException("M5 relationship references invalid state.");
            if (minute.Value - relationship.LastInteractionMinute < 0) throw new ArgumentException("M5 relationship elapsed time cannot be negative.");
        }
        if (relationships.Select(x => (x.CitizenAId, x.CitizenBId)).Distinct().Count() != relationships.Count) throw new ArgumentException("M5 relationships duplicate a pair.");
        foreach (var citizen in citizens)
        {
            if (citizen.FounderOrdinal is not null)
            {
                if (citizen.ParentAId is not null || citizen.ParentBId is not null || citizen.BirthMinute >= 0) throw new ArgumentException("M5 founders must retain founder ancestry.");
            }
            else
            {
                if (citizen.ParentAId is not { } parentA || citizen.ParentBId is not { } parentB || parentA.Value >= parentB.Value || !ids.Contains(parentA.Value) || !ids.Contains(parentB.Value)) throw new ArgumentException("M5 descendants require canonical known parents.");
                var firstParent = citizens.Single(x => x.Id == parentA); var secondParent = citizens.Single(x => x.Id == parentB);
                var firstParentAgeAtBirth = checked((int)((citizen.BirthMinute - firstParent.BirthMinute) / WorldCalendar.MinutesPerYear));
                var secondParentAgeAtBirth = checked((int)((citizen.BirthMinute - secondParent.BirthMinute) / WorldCalendar.MinutesPerYear));
                if (citizen.BirthMinute < 0 || firstParent.BirthMinute >= citizen.BirthMinute || secondParent.BirthMinute >= citizen.BirthMinute || firstParent.DeathMinute is { } firstDeath && firstDeath < citizen.BirthMinute || secondParent.DeathMinute is { } secondDeath && secondDeath < citizen.BirthMinute || firstParentAgeAtBirth is < 18 or > 45 || secondParentAgeAtBirth is < 18 or > 45) throw new ArgumentException("M5 descendant birth state is invalid.");
                if (HasAncestor(citizen.Id.Value, citizen.Id.Value, citizens)) throw new ArgumentException("M5 ancestry contains a cycle.");
            }
            if (citizen.PartnerId is { } partnerId)
            {
                var partner = citizens.SingleOrDefault(x => x.Id == partnerId) ?? throw new ArgumentException("M5 partner is unknown.");
                if (partnerId == citizen.Id || ((!growthEnabled || citizen.IsAlive) && (partner.PartnerId != citizen.Id || citizen.HouseholdId is null || citizen.HouseholdId != partner.HouseholdId || (growthEnabled && !partner.IsAlive))) || GetRelationship(relationships, citizen.Id, partner.Id) is null || (growthEnabled ? GrowthKinship.AreCloseKin(citizen.Id, partner.Id, citizens.ToDictionary(x => x.Id.Value)) : AreFamily(citizen.Id, partner.Id, citizens))) throw new ArgumentException("M5 partnership is invalid.");
            }
            if (citizen.HouseholdId is { } householdId && !households.Any(x => x.Id == householdId)) throw new ArgumentException("M5 citizen household is unknown.");
            if (citizen.CurrentAction == CitizenAction.Socialize)
            {
                var target = citizens.SingleOrDefault(x => x.Id == citizen.TargetCitizenId) ?? throw new ArgumentException("M5 social target is unknown.");
                // Proximity is an action-selection and completion condition.  A target may
                // legitimately move away while an in-flight Socialize action is checkpointed.
                if (target.Id == citizen.Id || !target.IsAlive || citizen.ActionStartedMinute is not { } started || citizen.ActionCompletesMinute is not { } completes || started.Value > minute.Value || completes.Value < minute.Value || completes.Value - started.Value != CitizenSimulationRules.SocializeDurationMinutes)
                    throw new ArgumentException("M5 social target or timing is invalid.");
            }
        }
        foreach (var household in households)
        {
            if (household.CreatedMinute > minute.Value || household.DissolvedMinute is { } dissolvedMinute && (dissolvedMinute > minute.Value || dissolvedMinute < household.CreatedMinute)) throw new ArgumentException("M5 household time is invalid.");
            var members = citizens.Where(c => c.IsAlive && c.HouseholdId == household.Id).ToArray();
            if (members.Length > CitizenSimulationRules.MaximumHouseholdSize || (household.DissolvedMinute is null && members.Length == 0) || (household.DissolvedMinute is not null && members.Length != 0)) throw new ArgumentException("M5 household membership is invalid.");
            if (household.DwellingStructureId is { } dwelling)
            {
                var shelter = structures.SingleOrDefault(x => x.Id == dwelling);
                if (household.DissolvedMinute is not null || shelter is null || shelter.Type != StructureType.Shelter || shelter.Status != StructureStatus.Complete || members.Any(x => x.HomeStructureId != dwelling)) throw new ArgumentException("M5 household dwelling is invalid.");
            }
            else if (members.Any(x => x.HomeStructureId is not null)) throw new ArgumentException("Unhoused M5 household members cannot retain individual homes.");
        }
        var completedShelters = structures.Where(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Shelter).Select(x => x.Id.Value).ToHashSet();
        if (citizens.Where(x => x.IsAlive && x.HomeStructureId is not null).GroupBy(x => x.HomeStructureId!.Value.Value).Any(group => !completedShelters.Contains(group.Key) || group.Count() > CitizenSimulationRules.ShelterCapacityPerBuilding))
            throw new ArgumentException("M5 aggregate shelter occupancy is invalid.", nameof(citizens));
        ValidateM5GameplayEvents(citizens, events, world, minute);
        ValidateGlobalEvent(events, CitizenEventNames.FamilyCheck, CitizenEventNames.FamilyCheckPriority, minute);
        ValidateGlobalEvent(events, CitizenEventNames.LifecycleCheck, CitizenEventNames.LifecycleCheckPriority, minute);
    }

    private static void ValidateM5GameplayEvents(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, WorldMap world, WorldMinute minute)
    {
        var citizenIds = citizens.Select(x => x.Id.Value).ToHashSet();
        var reserved = events.Where(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck).ToArray();
        foreach (var citizen in citizens)
        {
            var citizenEvents = reserved.Where(x => TryReadEventCitizenId(x, out var eventId) && eventId == citizen.Id.Value).ToArray();
            if (!citizen.IsAlive)
            {
                if (citizenEvents.Length != 0) throw new ArgumentException("Dead M5 citizens cannot have gameplay events.", nameof(events));
                continue;
            }
            if (citizenEvents.Length != 2 || citizenEvents.Count(x => x.Name == CitizenEventNames.SurvivalCheck) != 1 || citizenEvents.Count(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) != 1)
                throw new ArgumentException("M5 living citizens require one coherent action event and one survival event.", nameof(events));
            var survival = citizenEvents.Single(x => x.Name == CitizenEventNames.SurvivalCheck);
            if (survival.Order.Priority != CitizenEventNames.SurvivalPriority || survival.Order.EntitySortKey != citizen.Id.Value || survival.Order.DueWorldMinute != new WorldMinute(checked(citizen.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes)))
                throw new ArgumentException("M5 survival event does not match the health boundary.", nameof(events));
            var action = citizenEvents.Single(x => x.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete);
            var expected = citizen.ActionPhase switch
            {
                CitizenActionPhase.None when citizen.CurrentAction == CitizenAction.None => (CitizenEventNames.Decision, CitizenEventNames.DecisionPriority, minute),
                CitizenActionPhase.TravelToTarget or CitizenActionPhase.ReturnToStockpile or CitizenActionPhase.TravelToStockpile or CitizenActionPhase.TransportToConstruction => ExpectedMoveEvent(citizen, world!, minute),
                CitizenActionPhase.Perform or CitizenActionPhase.WaitingForStorage => (CitizenEventNames.ActionComplete, CitizenEventNames.CompletionPriority, citizen.ActionCompletesMinute ?? throw new ArgumentException("M5 action completion has no due minute.", nameof(citizens))),
                _ => throw new ArgumentException("M5 action phase is invalid for a living citizen.", nameof(citizens))
            };
            if (expected.Item3 < minute || action.Name != expected.Item1 || action.Order.Priority != expected.Item2 || action.Order.DueWorldMinute != expected.Item3 || action.Order.EntitySortKey != citizen.Id.Value || !TryReadCitizenPayload(action.PayloadJson, out var payload) || payload.Id != citizen.Id.Value || payload.Sequence != citizen.ActionSequence)
                throw new ArgumentException("M5 action event does not match action state.", nameof(events));
        }
        if (reserved.Any(x => !TryReadEventCitizenId(x, out var eventId) || !citizenIds.Contains(eventId) || x.Order.EntitySortKey != eventId))
            throw new ArgumentException("M5 reserved event references an unknown citizen.", nameof(events));
    }
    private static RelationshipState? GetRelationship(IReadOnlyList<RelationshipState> relationships, CitizenId first, CitizenId second)
    {
        var pair = RelationshipState.Normalize(first, second); return relationships.SingleOrDefault(x => x.CitizenAId == pair.A && x.CitizenBId == pair.B);
    }
    private static bool AreFamily(CitizenId first, CitizenId second, IReadOnlyList<Citizen> citizens)
    {
        static HashSet<long> Ancestors(CitizenId start, IReadOnlyList<Citizen> all)
        {
            var found = new HashSet<long>(); var pending = new Stack<long>(); pending.Push(start.Value);
            while (pending.TryPop(out var id) && found.Add(id)) { var citizen = all.SingleOrDefault(x => x.Id.Value == id); if (citizen?.ParentAId is { } a) pending.Push(a.Value); if (citizen?.ParentBId is { } b) pending.Push(b.Value); }
            return found;
        }
        return Ancestors(first, citizens).Overlaps(Ancestors(second, citizens));
    }
    private static bool HasAncestor(long target, long current, IReadOnlyList<Citizen> citizens)
    {
        var pending = new Stack<long>(); var seen = new HashSet<long>(); pending.Push(current);
        while (pending.TryPop(out var id) && seen.Add(id))
        {
            var citizen = citizens.SingleOrDefault(x => x.Id.Value == id); if (citizen?.ParentAId is { } a) { if (a.Value == target) return true; pending.Push(a.Value); } if (citizen?.ParentBId is { } b) { if (b.Value == target) return true; pending.Push(b.Value); }
        }
        return false;
    }
    private static void ValidateGlobalEvent(IReadOnlyList<ScheduledEventSnapshot> events, string name, int priority, WorldMinute minute)
    {
        var found = events.Where(x => x.Name == name).ToArray();
        var expectedDue = new WorldMinute(checked(((minute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay));
        if (found.Length != 1 || found[0].Order.Priority != priority || found[0].Order.EntitySortKey != 0 || found[0].PayloadJson != "{\"version\":1}" || found[0].Order.DueWorldMinute != expectedDue) throw new ArgumentException($"M5 requires one canonical {name} event.");
    }

    private static bool TryReadCitizenPayload(string json, out (long Id, long Sequence) payload)
    {
        payload = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 || !root.TryGetProperty("citizenId", out var id) || !root.TryGetProperty("actionSequence", out var sequence) || id.ValueKind != JsonValueKind.String || !long.TryParse(id.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0 || parsedId.ToString(System.Globalization.CultureInfo.InvariantCulture) != id.GetString() || !sequence.TryGetInt64(out var parsedSequence) || parsedSequence < 0) return false;
            payload = (parsedId, parsedSequence);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    internal static bool TryReadEventCitizenId(ScheduledEventSnapshot item, out long citizenId)
    {
        citizenId = 0;
        if (item.Name == CitizenEventNames.SurvivalCheck)
        {
            try
            {
                using var document = JsonDocument.Parse(item.PayloadJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("citizenId", out var id) || id.ValueKind != JsonValueKind.String || !long.TryParse(id.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0 || parsedId.ToString(System.Globalization.CultureInfo.InvariantCulture) != id.GetString() || item.PayloadJson != $"{{\"citizenId\":\"{parsedId}\"}}") return false;
                citizenId = parsedId;
                return true;
            }
            catch (JsonException) { return false; }
        }
        if (!TryReadCitizenPayload(item.PayloadJson, out var payload)) return false;
        citizenId = payload.Id;
        return true;
    }
    private static string RequireMetadata(string value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Metadata must not be empty.", parameterName) : value;
    private static Citizen CloneCitizen(Citizen c) => new(c.Id, c.FounderOrdinal, c.GivenName, c.FamilyName, c.BirthMinute, c.Location, new CitizenTraits(c.Traits.Industriousness, c.Traits.Sociability, c.Traits.Curiosity, c.Traits.Cooperativeness, c.Traits.RiskTolerance, c.Traits.Resilience), new CitizenSkills(c.Skills.Foraging, c.Skills.Woodcutting, c.Skills.Stoneworking, c.Skills.Construction, c.Skills.Hauling, c.Skills.Domestic), new CitizenNeeds(c.Needs.Hunger, c.Needs.Rest, c.Needs.Shelter, c.Needs.Social)) { Health = c.Health, CurrentAction = c.CurrentAction, ActionPhase = c.ActionPhase, ActionSequence = c.ActionSequence, ActionStartedMinute = c.ActionStartedMinute, ActionCompletesMinute = c.ActionCompletesMinute, ActionTarget = c.ActionTarget, TargetResourceNodeId = c.TargetResourceNodeId, TargetStructureId = c.TargetStructureId, TargetCitizenId = c.TargetCitizenId, CarriedResourceType = c.CarriedResourceType, CarriedResourceQuantity = c.CarriedResourceQuantity, NeedsUpdatedMinute = c.NeedsUpdatedMinute, HealthUpdatedMinute = c.HealthUpdatedMinute, LifetimeMovementSteps = c.LifetimeMovementSteps, LifetimeMovementCost = c.LifetimeMovementCost, LifetimeForagingMinutes = c.LifetimeForagingMinutes, LifetimeWoodcuttingMinutes = c.LifetimeWoodcuttingMinutes, LifetimeStoneworkingMinutes = c.LifetimeStoneworkingMinutes, LifetimeConstructionMinutes = c.LifetimeConstructionMinutes, LifetimeHaulingMinutes = c.LifetimeHaulingMinutes, DeathMinute = c.DeathMinute, DeathCause = c.DeathCause, ParentAId = c.ParentAId, ParentBId = c.ParentBId, PartnerId = c.PartnerId, HouseholdId = c.HouseholdId, HomeStructureId = c.HomeStructureId };
    private static Household CloneHousehold(Household value) => new(value.Id, value.CreatedMinute) { DissolvedMinute = value.DissolvedMinute, DwellingStructureId = value.DwellingStructureId };
    private static SettlementState CloneSettlement(SettlementState state) => new(state.FoodStored, state.WoodStored, state.StoneStored, state.BaseStorageCapacity, state.DemandUpdatedMinute, state.ExposureConsequencesStartMinute);
    internal static Structure CloneStructure(Structure value) => new(value.Id, value.Type, value.Location, value.ConstructionStartedMinute, value.RequiredWood, value.RequiredStone, value.RequiredWork) { Status = value.Status, CompletedMinute = value.CompletedMinute, DeliveredWood = value.DeliveredWood, DeliveredStone = value.DeliveredStone, CompletedWork = value.CompletedWork };
    internal static StructureContribution CloneContribution(StructureContribution value) => new(value.StructureId, value.CitizenId, value.ConstructionWork, value.WoodDelivered, value.StoneDelivered);
}

public sealed partial class SimulationEngine
{
    public const string CurrentWorldSchemaVersion = "0.1";
    public const string M2SimulationRulesVersion = "m2-rng1-citizen1";
    public const string M3SimulationRulesVersion = "m3-rng1-survival1";
    public const string M4SimulationRulesVersion = "m4-rng1-settlement1";
    public const string M5SimulationRulesVersion = "m5-rng1-social1";
    public const string M6SimulationRulesVersion = "m6-rng1-history1";
    public const string M8SimulationRulesVersion = "m8-rng1-balance1";
    public const string GrowthSimulationRulesVersion = "m9-rng1-growth1";
    public const string CurrentSimulationRulesVersion = M8SimulationRulesVersion;
    // Retained source-compatibility alias for M2-only callers; new code must use the explicit names.
    public const int CitizenGenerationVersion = 1;
    public const int SurvivalVersion = 1;
    public const int SettlementVersion = CitizenSimulationRules.SettlementVersion;
    public const int SocialVersion = 1;
    public const int HistoryVersion = 1;
    public const int HistoricalEventSchemaVersion = 1;
    public static bool SocialSystemsEnabled(string rulesVersion) => rulesVersion is M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion;
    public static bool HistorySystemsEnabled(string rulesVersion) => rulesVersion is M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion;
    public static bool IsHistoryRulesVersion(string rulesVersion) => HistorySystemsEnabled(rulesVersion);
    public static int FoodShortageRecoveryMultiplier(string rulesVersion) => rulesVersion switch
    {
        M6SimulationRulesVersion => 20,
        M8SimulationRulesVersion or GrowthSimulationRulesVersion => 20,
        _ => throw new ArgumentException($"Rules '{rulesVersion}' do not define history shortage thresholds.", nameof(rulesVersion))
    };
    private sealed record PendingEvent(ScheduledEventId Id, ScheduledEventOrder Order, string Name, string PayloadJson);
    private readonly SortedSet<PendingEvent> _scheduledEvents = new(Comparer<PendingEvent>.Create(static (a, b) => a.Order.CompareTo(b.Order)));
    private readonly Queue<SyntheticEventExecution> _processedEvents = new();
    private const int DiagnosticCapacity = 32;
    private readonly Dictionary<long, Citizen> _citizens = new();
    private readonly Dictionary<long, ResourceState> _resourceStates = new();
    private readonly Dictionary<long, Structure> _structures = new();
    private readonly Dictionary<(long CitizenAId, long CitizenBId), RelationshipState> _relationships = new();
    private readonly Dictionary<long, Household> _households = new();
    private readonly Dictionary<(long StructureId, long CitizenId), StructureContribution> _structureContributions = new();
    private readonly Dictionary<(TileCoordinate Start, TileCoordinate End), IReadOnlyList<TileCoordinate>?> _pathCache = new();
    private readonly Queue<(TileCoordinate Start, TileCoordinate End)> _pathCacheInsertionOrder = new();
    private readonly Dictionary<TileCoordinate, IReadOnlyDictionary<TileCoordinate, long>> _travelCostCache = new();
    private readonly Queue<TileCoordinate> _travelCostCacheInsertionOrder = new();
    private readonly Dictionary<(long CitizenId, long ActionSequence), IReadOnlyList<TileCoordinate>> _activePaths = new();
    private readonly List<FamilyCheckDiagnostic> _familyCheckDiagnostics = [];
    private readonly DeterministicCounters _counters;
    private readonly bool _captureFamilyCheckDiagnostics;
    private const int PathCacheCapacity = 512;
    private const int TravelCostCacheCapacity = 128;
    private int _processedEventCount;
    // Keep the construction default at the explicit M4 compatibility boundary; new
    // persisted worlds reach M5 through the upgrade chain and M5 callers opt in by version.
    public SimulationEngine(WorldSeed seed, WorldMinute initialMinute = default, string worldSchemaVersion = CurrentWorldSchemaVersion, string simulationRulesVersion = M4SimulationRulesVersion, string applicationVersion = "0.1.0", string worldConfiguration = "{}", bool captureFamilyCheckDiagnostics = false)
    { if (initialMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(initialMinute)); if (HistorySystemsEnabled(simulationRulesVersion) && initialMinute.Value != 0) throw new ArgumentException("A fresh history simulation must start at minute 0.", nameof(initialMinute)); Seed = seed; CurrentMinute = initialMinute; WorldSchemaVersion = worldSchemaVersion; SimulationRulesVersion = simulationRulesVersion; ApplicationVersion = applicationVersion; _captureFamilyCheckDiagnostics = captureFamilyCheckDiagnostics; _counters = new DeterministicCounters(); World = CreateWorld(seed, worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration))); WorldConfiguration = World.Configuration.CanonicalJson; var socialEnabled = SocialSystemsEnabled(simulationRulesVersion); var historyEnabled = HistorySystemsEnabled(simulationRulesVersion); var survivalEnabled = simulationRulesVersion is M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion or M4SimulationRulesVersion or M3SimulationRulesVersion; var settlementEnabled = simulationRulesVersion is M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion or M4SimulationRulesVersion; Settlement = settlementEnabled ? new SettlementState(400, 0, 0, CitizenSimulationRules.BaseStorageCapacity, CurrentMinute.Value, CurrentMinute.Add(CitizenSimulationRules.ExposureGraceDurationMinutes).Value) : survivalEnabled ? new SettlementState() : new SettlementState(0, 0, 0); if (survivalEnabled) foreach (var node in World.Resources) _resourceStates[node.Id.Value] = new ResourceState(node.Id, node.InitialQuantity); if (survivalEnabled || simulationRulesVersion == M2SimulationRulesVersion) GenerateFounders(); if (survivalEnabled) { foreach (var citizen in _citizens.Values) { citizen.NeedsUpdatedMinute = CurrentMinute.Value; citizen.HealthUpdatedMinute = CurrentMinute.Value; } InitializeSurvivalEvents(); if (settlementEnabled) ScheduleSettlementDemand(); if (socialEnabled) { ScheduleFamilyCheck(); ScheduleLifecycleCheck(); } if (historyEnabled) InitializeHistory(); if (GrowthSystemsEnabled(SimulationRulesVersion)) EnsureIndependentHouseholds(); } }
    public SimulationEngine(SimulationPersistenceSnapshot snapshot)
    { ArgumentNullException.ThrowIfNull(snapshot); ValidatePersistenceSnapshotCompatibility(snapshot); Seed = snapshot.Seed; CurrentMinute = snapshot.WorldMinute; WorldSchemaVersion = snapshot.WorldSchemaVersion; SimulationRulesVersion = snapshot.SimulationRulesVersion; ApplicationVersion = snapshot.ApplicationVersion; WorldConfiguration = snapshot.WorldConfiguration; _captureFamilyCheckDiagnostics = false; _counters = new DeterministicCounters(snapshot.Counters); World = snapshot.World ?? CreateWorld(snapshot.Seed, snapshot.WorldConfiguration); Settlement = snapshot.Settlement is null ? new SettlementState(0, 0, 0) : CloneSettlement(snapshot.Settlement); if (snapshot.SurvivalVersion == SurvivalVersion) foreach (var node in World.Resources) _resourceStates[node.Id.Value] = new ResourceState(node.Id, node.InitialQuantity); foreach (var state in snapshot.ResourceStates) { state.Validate(World.Resources.Single(x => x.Id == state.ResourceNodeId)); _resourceStates[state.ResourceNodeId.Value] = new ResourceState(state.ResourceNodeId, state.CurrentQuantity); } foreach (var citizen in snapshot.Citizens) _citizens.Add(citizen.Id.Value, CloneCitizen(citizen)); foreach (var structure in snapshot.Structures) _structures.Add(structure.Id.Value, CloneStructure(structure)); foreach (var household in snapshot.Households) _households.Add(household.Id.Value, CloneHousehold(household)); foreach (var relationship in snapshot.Relationships) _relationships.Add((relationship.CitizenAId.Value, relationship.CitizenBId.Value), relationship); foreach (var contribution in snapshot.StructureContributions) _structureContributions.Add((contribution.StructureId.Value, contribution.CitizenId.Value), CloneContribution(contribution)); foreach (var item in snapshot.ScheduledEvents) { if (item.Order.DueWorldMinute < CurrentMinute) { if (snapshot.SettlementVersion == 0 && item.Name == CitizenEventNames.SettlementEvaluateDemand) continue; throw new ArgumentException("A restored scheduled event cannot be due in the past.", nameof(snapshot)); } AddPending(item.Id, item.Order, item.Name, item.PayloadJson); } if (snapshot.CitizenGenerationVersion == CitizenGenerationVersion && _citizens.Count == 0) throw new InvalidDataException("An M2 snapshot must contain citizens."); if (snapshot.HistoryVersion == HistoryVersion) LoadHistory(snapshot); }
    public WorldSeed Seed { get; } public WorldMinute CurrentMinute { get; private set; } public string WorldSchemaVersion { get; } public string SimulationRulesVersion { get; } public string ApplicationVersion { get; } public string WorldConfiguration { get; } public WorldMap World { get; } public SettlementState Settlement { get; } public int PendingEventCount => _scheduledEvents.Count; public int ProcessedEventCount => _processedEventCount; internal WorldMinute? NextScheduledEventMinute => _scheduledEvents.Count == 0 ? null : _scheduledEvents.Min!.Order.DueWorldMinute; public DeterministicCountersSnapshot CounterSnapshot => _counters.Snapshot; public IReadOnlyList<FamilyCheckDiagnostic> FamilyCheckDiagnostics => Array.AsReadOnly(_familyCheckDiagnostics.ToArray()); public int Population => SimulationRulesVersion is M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion or M4SimulationRulesVersion or M3SimulationRulesVersion ? LivingPopulation : TotalCitizenCount; public int LivingPopulation => _citizens.Values.Count(x => x.IsAlive); public int DeadPopulation => _citizens.Values.Count(x => !x.IsAlive); public int TotalCitizenCount => _citizens.Count; public IReadOnlyList<Citizen> Citizens => Array.AsReadOnly(_citizens.Values.OrderBy(x => x.Id.Value).Select(CloneCitizen).ToArray()); public IReadOnlyList<ResourceState> ResourceStates => Array.AsReadOnly(_resourceStates.Values.OrderBy(x => x.ResourceNodeId.Value).Select(x => new ResourceState(x.ResourceNodeId, x.CurrentQuantity)).ToArray()); public IReadOnlyList<Structure> Structures => Array.AsReadOnly(_structures.Values.OrderBy(x => x.Id.Value).Select(CloneStructure).ToArray()); public IReadOnlyList<RelationshipState> Relationships => Array.AsReadOnly(_relationships.Values.OrderBy(x => x.CitizenAId.Value).ThenBy(x => x.CitizenBId.Value).ToArray()); public IReadOnlyList<Household> Households => Array.AsReadOnly(_households.Values.OrderBy(x => x.Id.Value).Select(CloneHousehold).ToArray()); public IReadOnlyList<StructureContribution> StructureContributions => Array.AsReadOnly(_structureContributions.Values.OrderBy(x => x.StructureId.Value).ThenBy(x => x.CitizenId.Value).Select(CloneContribution).ToArray()); public int StorageCapacity => checked(Settlement.BaseStorageCapacity + _structures.Values.Count(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Stockpile) * CitizenSimulationRules.StockpileStorageBonus); public int ShelterCapacity => checked(_structures.Values.Count(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Shelter) * CitizenSimulationRules.ShelterCapacityPerBuilding); public Structure? ActiveConstructionProject => _structures.Values.Where(x => x.Status == StructureStatus.UnderConstruction).OrderBy(x => x.Id.Value).Select(CloneStructure).SingleOrDefault(); public ResourceState? GetResourceState(ResourceNodeId id) => _resourceStates.TryGetValue(id.Value, out var state) ? new ResourceState(state.ResourceNodeId, state.CurrentQuantity) : null; public Citizen? GetCitizen(CitizenId id) => _citizens.TryGetValue(id.Value, out var citizen) ? CloneCitizen(citizen) : null; public Structure? GetStructure(StructureId id) => _structures.TryGetValue(id.Value, out var structure) ? CloneStructure(structure) : null;
    public FamilyCheckDiagnostic CaptureFamilyCheckCounterfactualDiagnostic()
    {
        var diagnostics = new FamilyCheckDiagnosticBuilder(CurrentMinute, _households.Values.Count(x => x.DissolvedMinute is null));
        foreach (var household in _households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)) diagnostics.RecordCounterfactual(CreateFamilyCheckOpportunityDiagnostic(household));
        return diagnostics.ToSnapshot();
    }

    public IReadOnlyList<CitizenDecisionEvaluation> EvaluateDecision(CitizenId id)
    {
        if (!_citizens.TryGetValue(id.Value, out var citizen)) return Array.Empty<CitizenDecisionEvaluation>();
        return EvaluateDecision(citizen);
    }
    internal static CitizenAction SelectDecision(IReadOnlyList<CitizenDecisionEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        if (evaluations.Count == 0) throw new ArgumentException("At least one decision evaluation is required.", nameof(evaluations));
        var m4Candidates = evaluations.Any(static evaluation => evaluation.Action is CitizenAction.HaulConstruction or CitizenAction.Build);
        return evaluations.OrderByDescending(evaluation => evaluation.FinalScore).ThenBy(evaluation => ActionTieRank(evaluation.Action, m4Candidates)).First().Action;
    }
    private List<CitizenDecisionEvaluation> EvaluateDecision(Citizen citizen)
    {
        if (!citizen.IsAlive) return [];
        if (GrowthSystemsEnabled(SimulationRulesVersion)) return EvaluateGrowthDecision(citizen);
        var needs = citizen.GetProjectedNeeds(CurrentMinute); var random = new DeterministicRandom(Seed);
        var result = new List<CitizenDecisionEvaluation>();
        void Add(CitizenAction action, int baseUtility, int need, int trait, ulong purpose, int travelPenalty = 0, int skill = 0, int stockpile = 0)
        {
            var age = citizen.AgeYears(CurrentMinute);
            if (SocialSystemsEnabled(SimulationRulesVersion) && !IsAgeEligible(action, age)) return;
            var variation = Variation(random, citizen, purpose); var score = checked(baseUtility + need + trait + skill + stockpile + variation - travelPenalty); result.Add(new(action, baseUtility, need, trait, variation, score, skill, stockpile, travelPenalty));
        }
        var food = Settlement.FoodStored;
        var travelCosts = GetTravelCostsCached(citizen.Location);
        var canReachStockpile = travelCosts.TryGetValue(World.StartingSite, out var eatTravelCost);
        if (food > 0 && canReachStockpile)
        {
            Add(CitizenAction.Eat, 3000, needs.Hunger * 4, 0, 1, checked((int)Math.Min(int.MaxValue, eatTravelCost)));
        }
        Add(CitizenAction.Rest, 0, needs.Rest * CitizenSimulationRules.RestUtilityWeight, 0, 2);
        if (SocialSystemsEnabled(SimulationRulesVersion) && needs.Hunger < CitizenSimulationRules.StarvationThreshold && needs.Rest < CitizenSimulationRules.ExhaustionThreshold && SelectSocialTarget(citizen) is not null)
            Add(CitizenAction.Socialize, 0, needs.Social * 2, citizen.Traits.Sociability / 5, 101);
        AddGather(CitizenAction.GatherFood, ResourceType.Food, needs.Hunger, 3);
        AddGather(CitizenAction.GatherWood, ResourceType.Wood, 4, 4);
        AddGather(CitizenAction.GatherStone, ResourceType.Stone, 3, 5);
        if ((SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion) && ActiveConstructionProject is { } project)
        {
            var travel = travelCosts.TryGetValue(project.Location, out var constructionTravelCost) ? (int)Math.Min(int.MaxValue, constructionTravelCost) : 0;
            var woodMissing = ConstructionMissing(project, ResourceType.Wood);
            var stoneMissing = ConstructionMissing(project, ResourceType.Stone);
            if (woodMissing > 0 || stoneMissing > 0)
            {
                var materialAvailable = (woodMissing > 0 && Settlement.WoodStored > 0) || (stoneMissing > 0 && Settlement.StoneStored > 0);
                if (materialAvailable) Add(CitizenAction.HaulConstruction, 2200, 0, citizen.Traits.Industriousness / 20, 9, travel, citizen.Skills.Hauling / 1000);
            }
            else if (project.CompletedWork < project.RequiredWork) Add(CitizenAction.Build, 2400, 0, citizen.Traits.Industriousness / 20, 10, travel, citizen.Skills.Construction / 1000);
        }
        Add(CitizenAction.Explore, CitizenSimulationRules.ExploreBaseUtility, 0, citizen.Traits.Curiosity / 4 + citizen.Traits.RiskTolerance / 8, 6);
        Add(CitizenAction.Wander, CitizenSimulationRules.WanderBaseUtility, 0, citizen.Traits.Curiosity / 10, 7);
        Add(CitizenAction.Idle, CitizenSimulationRules.IdleBaseUtility, 0, (10000 - citizen.Traits.Industriousness) / 20, 8);
        return result;
        void AddGather(CitizenAction action, ResourceType type, int need, ulong purpose)
        { if ((SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion) && Settlement.StorageUsed >= StorageCapacity) return; var target = SelectResourceTarget(citizen, type); if (target is null || !travelCosts.TryGetValue(target.Coordinate, out var travelCost)) return; var travel = (int)Math.Min(int.MaxValue, travelCost); var stock = type == ResourceType.Food ? food : type == ResourceType.Wood ? Settlement.WoodStored : Settlement.StoneStored; var targetValue = type == ResourceType.Food ? CitizenSimulationRules.FoodTarget : type == ResourceType.Wood ? CitizenSimulationRules.WoodTarget : CitizenSimulationRules.StoneTarget; var stockContribution = stock < targetValue ? 1800 : 500; if (type == ResourceType.Food) stockContribution = checked(stockContribution + FoodSecurityGatherContribution()); Add(action, 0, need * (type == ResourceType.Food ? 3 : 1), citizen.Traits.Industriousness / 20, purpose, travel / 10, RelevantSkill(action, citizen) / 1000, stockContribution); }
    }
    public ScheduledEventId ScheduleSyntheticEvent(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name) => ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name, "{}");
    public ScheduledEventId ScheduleSyntheticEvent(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name, string payloadJson)
    { if (name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck or CitizenEventNames.ResourceRegenerate or CitizenEventNames.SettlementEvaluateDemand or CitizenEventNames.FamilyCheck or CitizenEventNames.LifecycleCheck or CitizenEventNames.StatisticsSample) throw new ArgumentException("Reserved gameplay event names may only be scheduled by the runtime.", nameof(name)); ArgumentOutOfRangeException.ThrowIfLessThan(dueWorldMinute, CurrentMinute); var sequence = _counters.AllocateScheduledEventSequence(); var id = new ScheduledEventId(sequence); AddPending(id, new ScheduledEventOrder(dueWorldMinute, priority, entitySortKey, sequence), name, payloadJson); return id; }
    public ScheduledEventId Schedule(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name) => ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name);
    public bool ProcessNextEvent()
    {
        if (_scheduledEvents.Count == 0) return false;
        var next = _scheduledEvents.Min!;
        _scheduledEvents.Remove(next);
        var foodBefore = Settlement.FoodStored;
        CurrentMinute = CurrentMinute.AdvanceTo(next.Order.DueWorldMinute);
        _processedEventCount++;
        if (next.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck) DispatchCitizenEvent(next);
        else if (next.Name == CitizenEventNames.ResourceRegenerate) { RegenerateResources(); ScheduleResourceRegeneration(); }
        else if (next.Name == CitizenEventNames.SettlementEvaluateDemand) EvaluateSettlementDemand();
        else if (next.Name == CitizenEventNames.FamilyCheck) { RunFamilyCheck(); ScheduleFamilyCheck(); }
        else if (next.Name == CitizenEventNames.LifecycleCheck) { RunLifecycleCheck(); ScheduleLifecycleCheck(); }
        else if (next.Name == CitizenEventNames.StatisticsSample) ProcessStatisticsSample();
        else { if (_processedEvents.Count == DiagnosticCapacity) _processedEvents.Dequeue(); _processedEvents.Enqueue(new SyntheticEventExecution(next.Id, next.Order, next.Name, next.PayloadJson)); }
        if (HistorySystemsEnabled(SimulationRulesVersion)) RecordHistoryTransitions(foodBefore);
        return true;
    }
    public int AdvanceEvents(int maximumEvents) { ArgumentOutOfRangeException.ThrowIfNegative(maximumEvents); var count = 0; while (count < maximumEvents && ProcessNextEvent()) count++; return count; }
    public int AdvanceUntil(WorldMinute targetMinute) { ArgumentOutOfRangeException.ThrowIfLessThan(targetMinute, CurrentMinute); var count = 0; while (_scheduledEvents.Count > 0 && _scheduledEvents.Min!.Order.DueWorldMinute <= targetMinute) { ProcessNextEvent(); count++; } CurrentMinute = CurrentMinute.AdvanceTo(targetMinute); return count; }
    public SimulationStatusSnapshot CreateReadSnapshot() => new(Seed, CurrentMinute, PendingEventCount, ProcessedEventCount, _processedEvents.ToArray(), World, CreateCitizenSnapshots(), CloneSettlement(Settlement), ResourceStates, _structures.Values.OrderBy(x => x.Id.Value).Select(CloneStructure).ToArray(), _structureContributions.Values.OrderBy(x => x.StructureId.Value).ThenBy(x => x.CitizenId.Value).Select(CloneContribution).ToArray(), Relationships, Households, CreateHistoryReadSnapshot());
    public string ComputeSurvivalFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static string I<T>(T value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        static void Add(IncrementalHash hash, string value) { var bytes = Encoding.UTF8.GetBytes(value); hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":")); hash.AppendData(bytes); }
        var counters = _counters.Snapshot;
        var citizenGeneration = SimulationRulesVersion is M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion or M4SimulationRulesVersion or M3SimulationRulesVersion or M2SimulationRulesVersion ? CitizenGenerationVersion : 0;
        var survival = SimulationRulesVersion is M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion or M4SimulationRulesVersion or M3SimulationRulesVersion ? SurvivalVersion : 0;
        Add(hash, $"seed={I(Seed.Value)}"); Add(hash, $"minute={I(CurrentMinute.Value)}"); Add(hash, $"world-schema={WorldSchemaVersion}"); Add(hash, $"world={World.Fingerprint}"); Add(hash, $"world-configuration={WorldConfiguration}"); Add(hash, $"rules={SimulationRulesVersion}"); Add(hash, $"citizen-generation={I(citizenGeneration)}"); Add(hash, $"survival={I(survival)}"); Add(hash, $"settlement-food={I(Settlement.FoodStored)}"); Add(hash, $"settlement-wood={I(Settlement.WoodStored)}"); Add(hash, $"settlement-stone={I(Settlement.StoneStored)}"); Add(hash, $"counter-next-entity={I(counters.NextEntityId)}"); Add(hash, $"counter-next-historical-event={I(counters.NextHistoricalEventId)}"); Add(hash, $"counter-next-scheduled={I(counters.NextScheduledEventSequence)}");
        foreach (var state in ResourceStates) Add(hash, $"resource={I(state.ResourceNodeId.Value)}:{I(state.CurrentQuantity)}");
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value))
        {
            Add(hash, $"citizen-id={I(citizen.Id.Value)}"); Add(hash, $"citizen-ordinal={I(citizen.FounderOrdinal)}"); Add(hash, $"citizen-given={citizen.GivenName}"); Add(hash, $"citizen-family={citizen.FamilyName}"); Add(hash, $"citizen-birth={I(citizen.BirthMinute)}"); Add(hash, $"citizen-parent-a={citizen.ParentAId?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-parent-b={citizen.ParentBId?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-partner={citizen.PartnerId?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-household={citizen.HouseholdId?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-home={citizen.HomeStructureId?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-location={I(citizen.Location.X)},{I(citizen.Location.Y)}"); Add(hash, $"citizen-health={I(citizen.Health)}"); Add(hash, $"citizen-death-minute={citizen.DeathMinute?.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-death-cause={citizen.DeathCause ?? "null"}"); Add(hash, $"citizen-needs={I(citizen.Needs.Hunger)},{I(citizen.Needs.Rest)},{I(citizen.Needs.Shelter)},{I(citizen.Needs.Social)}"); Add(hash, $"citizen-needs-updated={I(citizen.NeedsUpdatedMinute)}"); Add(hash, $"citizen-health-updated={I(citizen.HealthUpdatedMinute)}"); Add(hash, $"citizen-traits={I(citizen.Traits.Industriousness)},{I(citizen.Traits.Sociability)},{I(citizen.Traits.Curiosity)},{I(citizen.Traits.Cooperativeness)},{I(citizen.Traits.RiskTolerance)},{I(citizen.Traits.Resilience)}"); Add(hash, $"citizen-skills={I(citizen.Skills.Foraging)},{I(citizen.Skills.Woodcutting)},{I(citizen.Skills.Stoneworking)},{I(citizen.Skills.Construction)},{I(citizen.Skills.Hauling)},{I(citizen.Skills.Domestic)}"); Add(hash, $"citizen-action={I((int)citizen.CurrentAction)}"); Add(hash, $"citizen-phase={I((int)citizen.ActionPhase)}"); Add(hash, $"citizen-sequence={I(citizen.ActionSequence)}"); Add(hash, $"citizen-action-started={citizen.ActionStartedMinute?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-action-completes={citizen.ActionCompletesMinute?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-action-target={(citizen.ActionTarget is { } target ? $"{I(target.X)},{I(target.Y)}" : "null")}"); Add(hash, $"citizen-resource-target={citizen.TargetResourceNodeId?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}"); Add(hash, $"citizen-carried-type={(citizen.CarriedResourceType is { } carriedType ? I((int)carriedType) : "null")}"); Add(hash, $"citizen-carried-quantity={I(citizen.CarriedResourceQuantity)}"); Add(hash, $"citizen-movement={I(citizen.LifetimeMovementSteps)},{I(citizen.LifetimeMovementCost)}");
        }
        foreach (var item in _scheduledEvents) Add(hash, $"event-id={I(item.Id.Value)};due={I(item.Order.DueWorldMinute.Value)};priority={I(item.Order.Priority)};entity={I(item.Order.EntitySortKey)};sequence={I(item.Order.Sequence)};name={item.Name};payload={item.PayloadJson}");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public string SurvivalFingerprint => ComputeSurvivalFingerprint();
    public string ComputeSettlementFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static string I<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        static void Add(IncrementalHash value, string text) { var bytes = Encoding.UTF8.GetBytes(text); value.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":")); value.AppendData(bytes); }
        Add(hash, "survival=" + ComputeSurvivalFingerprint());
        Add(hash, "settlement-version=" + I(SettlementVersion));
        Add(hash, "settlement-base=" + I(Settlement.BaseStorageCapacity));
        Add(hash, "settlement-demand=" + I(Settlement.DemandUpdatedMinute));
        Add(hash, "settlement-exposure=" + I(Settlement.ExposureConsequencesStartMinute));
        foreach (var structure in _structures.Values.OrderBy(x => x.Id.Value)) Add(hash, $"structure={I(structure.Id.Value)}:{I((int)structure.Type)}:{I((int)structure.Status)}:{I(structure.Location.X)},{I(structure.Location.Y)}:{I(structure.ConstructionStartedMinute)}:{(structure.CompletedMinute is { } completedMinute ? I(completedMinute) : "null")}:{I(structure.RequiredWood)}:{I(structure.DeliveredWood)}:{I(structure.RequiredStone)}:{I(structure.DeliveredStone)}:{I(structure.RequiredWork)}:{I(structure.CompletedWork)}");
        foreach (var contribution in _structureContributions.Values.OrderBy(x => x.StructureId.Value).ThenBy(x => x.CitizenId.Value)) Add(hash, $"contribution={I(contribution.StructureId.Value)}:{I(contribution.CitizenId.Value)}:{I(contribution.ConstructionWork)}:{I(contribution.WoodDelivered)}:{I(contribution.StoneDelivered)}");
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value)) Add(hash, $"citizen-m4={I(citizen.Id.Value)}:{(citizen.HomeStructureId is { } homeStructureId ? I(homeStructureId.Value) : "null")}:{(citizen.TargetStructureId is { } targetStructureId ? I(targetStructureId.Value) : "null")}:{I(citizen.LifetimeForagingMinutes)}:{I(citizen.LifetimeWoodcuttingMinutes)}:{I(citizen.LifetimeStoneworkingMinutes)}:{I(citizen.LifetimeConstructionMinutes)}:{I(citizen.LifetimeHaulingMinutes)}");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public string SettlementFingerprint => ComputeSettlementFingerprint();
    public string ComputeSocialFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static string I<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        static void Add(IncrementalHash value, string text) { var bytes = Encoding.UTF8.GetBytes(text); value.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":")); value.AppendData(bytes); }
        Add(hash, "settlement=" + ComputeSettlementFingerprint());
        Add(hash, "social-version=" + I(SocialSystemsEnabled(SimulationRulesVersion) ? SocialVersion : 0));
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value))
            Add(hash, $"citizen-social={I(citizen.Id.Value)}:{(citizen.FounderOrdinal is { } ordinal ? I(ordinal) : "null")}:{(citizen.ParentAId is { } parentA ? I(parentA.Value) : "null")}:{(citizen.ParentBId is { } parentB ? I(parentB.Value) : "null")}:{(citizen.PartnerId is { } partner ? I(partner.Value) : "null")}:{(citizen.HouseholdId is { } household ? I(household.Value) : "null")}:{(citizen.TargetCitizenId is { } target ? I(target.Value) : "null")}");
        foreach (var relationship in _relationships.Values.OrderBy(x => x.CitizenAId.Value).ThenBy(x => x.CitizenBId.Value))
            Add(hash, $"relationship={I(relationship.CitizenAId.Value)}:{I(relationship.CitizenBId.Value)}:{I(relationship.Familiarity)}:{I(relationship.Affinity)}:{I(relationship.Trust)}:{I(relationship.Conflict)}:{I(relationship.LastInteractionMinute)}:{I(relationship.InteractionCount)}");
        foreach (var household in _households.Values.OrderBy(x => x.Id.Value))
            Add(hash, $"household={I(household.Id.Value)}:{I(household.CreatedMinute)}:{(household.DissolvedMinute is { } dissolved ? I(dissolved) : "null")}:{(household.DwellingStructureId is { } dwelling ? I(dwelling.Value) : "null")}");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public string SocialFingerprint => ComputeSocialFingerprint();
    public SimulationStatusSnapshot CreateStatusSnapshot() => CreateReadSnapshot();
    public SimulationPersistenceSnapshot CreatePersistenceSnapshot() => new(Seed, CurrentMinute, WorldSchemaVersion, SimulationRulesVersion, ApplicationVersion, WorldConfiguration, _counters.Snapshot, _scheduledEvents.Select(x => new ScheduledEventSnapshot(x.Id, x.Order, x.Name, x.PayloadJson)).ToArray(), World, _citizens.Values.Select(CloneCitizen).ToArray(), SimulationRulesVersion is GrowthSimulationRulesVersion or M8SimulationRulesVersion or M6SimulationRulesVersion or M5SimulationRulesVersion or M4SimulationRulesVersion or M3SimulationRulesVersion or M2SimulationRulesVersion ? CitizenGenerationVersion : 0, ResourceStates, CloneSettlement(Settlement), SimulationRulesVersion is GrowthSimulationRulesVersion or M8SimulationRulesVersion or M6SimulationRulesVersion or M5SimulationRulesVersion or M4SimulationRulesVersion or M3SimulationRulesVersion ? SurvivalVersion : 0, SimulationRulesVersion is GrowthSimulationRulesVersion or M8SimulationRulesVersion or M6SimulationRulesVersion or M5SimulationRulesVersion or M4SimulationRulesVersion ? SettlementVersion : 0, Structures, StructureContributions, SocialSystemsEnabled(SimulationRulesVersion) ? SocialVersion : 0, Relationships, Households, HistorySystemsEnabled(SimulationRulesVersion) ? HistoryVersion : 0, HistoryState, HistoricalEvents, HistoricalEventCitizens, HistoricalEventStructures, StatisticsSamples, Memories);
    public static SimulationEngine FromPersistenceSnapshot(SimulationPersistenceSnapshot snapshot) => new(snapshot);
    public static void ValidatePersistenceSnapshotCompatibility(SimulationPersistenceSnapshot snapshot) { if (snapshot.WorldSchemaVersion != CurrentWorldSchemaVersion) throw new NotSupportedException($"World schema version '{snapshot.WorldSchemaVersion}' is not supported; expected '{CurrentWorldSchemaVersion}'."); if (snapshot.SimulationRulesVersion is not ("m0-rng1" or M2SimulationRulesVersion or M3SimulationRulesVersion or M4SimulationRulesVersion or M5SimulationRulesVersion or M6SimulationRulesVersion or M8SimulationRulesVersion or GrowthSimulationRulesVersion)) throw new NotSupportedException($"Simulation rules version '{snapshot.SimulationRulesVersion}' is not supported."); if (snapshot.CitizenGenerationVersion == CitizenGenerationVersion && snapshot.SimulationRulesVersion == "m0-rng1") throw new NotSupportedException("M2 citizens require the M2 simulation rules version."); }
    private void GenerateFounders() { foreach (var citizen in CitizenGenerator.Generate(Seed, World, _counters, CurrentMinute)) _citizens.Add(citizen.Id.Value, citizen); foreach (var citizen in _citizens.Values) ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); }
    private void InitializeSurvivalEvents() { foreach (var citizen in _citizens.Values.Where(c => c.IsAlive)) ScheduleSurvival(citizen); ScheduleResourceRegeneration(); }
    private void ScheduleSettlementDemand() { var due = Settlement.DemandUpdatedMinute == CurrentMinute.Value ? CurrentMinute.Add(CitizenSimulationRules.SettlementDemandIntervalMinutes) : new WorldMinute(checked(Settlement.DemandUpdatedMinute + CitizenSimulationRules.SettlementDemandIntervalMinutes)); var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(due, CitizenEventNames.SettlementDemandPriority, 0, seq), CitizenEventNames.SettlementEvaluateDemand, "{\"version\":1}"); }
    private void ScheduleFamilyCheck() => ScheduleGlobal(CitizenEventNames.FamilyCheck, CitizenEventNames.FamilyCheckPriority);
    private void ScheduleLifecycleCheck() => ScheduleGlobal(CitizenEventNames.LifecycleCheck, CitizenEventNames.LifecycleCheckPriority);
    private void ScheduleGlobal(string name, int priority)
    {
        var due = new WorldMinute(checked(((CurrentMinute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay));
        var sequence = _counters.AllocateScheduledEventSequence();
        AddPending(new ScheduledEventId(sequence), new ScheduledEventOrder(due, priority, 0, sequence), name, "{\"version\":1}");
    }
    private void DispatchCitizenEvent(PendingEvent item)
    { using var doc = JsonDocument.Parse(item.PayloadJson); var root = doc.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("citizenId", out var idElement) || idElement.ValueKind != JsonValueKind.String || !long.TryParse(idElement.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(System.Globalization.CultureInfo.InvariantCulture) != idElement.GetString() || !_citizens.TryGetValue(id, out var citizen)) throw new InvalidDataException("Citizen event references an unknown citizen."); if (item.Name == CitizenEventNames.SurvivalCheck) { ApplySurvival(citizen); return; } if (!root.TryGetProperty("actionSequence", out var seqElement) || !seqElement.TryGetInt64(out var sequence) || sequence < 0) throw new InvalidDataException("Citizen event has no action sequence."); if (item.Order.EntitySortKey != id || sequence != citizen.ActionSequence) throw new InvalidDataException($"Citizen event identity or action sequence is stale: {item.Name} id={id} payload={sequence} state={citizen.ActionSequence} action={citizen.CurrentAction} phase={citizen.ActionPhase}."); if (item.Name == CitizenEventNames.Decision) { if (citizen.CurrentAction != CitizenAction.None) throw new InvalidDataException("A decision event requires a citizen at a decision boundary."); Decide(citizen); } else if (item.Name == CitizenEventNames.ActionComplete) { if (citizen.CurrentAction is not (CitizenAction.Idle or CitizenAction.Rest or CitizenAction.Eat or CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone or CitizenAction.Build or CitizenAction.Socialize) || citizen.ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.Perform or CitizenActionPhase.WaitingForStorage)) throw new InvalidDataException("An action-complete event does not match citizen state."); if (citizen.ActionPhase == CitizenActionPhase.WaitingForStorage) { Deposit(citizen); if (citizen.ActionPhase != CitizenActionPhase.WaitingForStorage) CompleteAction(citizen, gatheringAlreadyDeposited: true); } else CompleteAction(citizen); } else MoveStep(citizen); }
    private void Decide(Citizen citizen)
    {
        ClearCarriedState(citizen);
        citizen.TargetResourceNodeId = null;
        citizen.TargetStructureId = null;
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.Needs = needs;
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        citizen.ActionSequence = checked(citizen.ActionSequence + 1);
        var action = SelectDecision(EvaluateDecision(citizen));
        var random = new DeterministicRandom(Seed);
        if (action == CitizenAction.Socialize)
        {
            var target = SelectSocialTarget(citizen);
            if (target is not null)
            {
                citizen.CurrentAction = CitizenAction.Socialize;
                citizen.ActionPhase = CitizenActionPhase.Perform;
                citizen.TargetCitizenId = target.Id;
                citizen.ActionTarget = null;
                citizen.ActionStartedMinute = CurrentMinute;
                citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.SocializeDurationMinutes);
                ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
                return;
            }
            action = CitizenAction.Idle;
        }
        if (action is CitizenAction.Wander or CitizenAction.Explore)
        {
            var target = CitizenGenerator.SelectTarget(Seed, World, citizen, action == CitizenAction.Wander ? CitizenSimulationRules.WanderRadius : CitizenSimulationRules.ExploreRadius);
            if (target is null) action = CitizenAction.Idle;
            else { BeginTravel(citizen, action, target.Value, null); return; }
        }
        if (action is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone)
        {
            var type = action == CitizenAction.GatherFood ? ResourceType.Food : action == CitizenAction.GatherWood ? ResourceType.Wood : ResourceType.Stone;
            var node = SelectResourceTarget(citizen, type);
            if (node is not null) { BeginTravel(citizen, action, node.Coordinate, node.Id); return; }
            action = CitizenAction.Idle;
        }
        if (action == CitizenAction.HaulConstruction && ActiveConstructionProject is { } haulingProject)
        {
            BeginTravel(citizen, action, World.StartingSite, null, CitizenActionPhase.TravelToStockpile, haulingProject.Id);
            return;
        }
        if (action == CitizenAction.Build && ActiveConstructionProject is { } buildingProject)
        {
            BeginTravel(citizen, action, buildingProject.Location, null, CitizenActionPhase.TravelToTarget, buildingProject.Id);
            return;
        }
        if (action == CitizenAction.Rest && citizen.HomeStructureId is { } homeId && _structures.TryGetValue(homeId.Value, out var home) && home.Status == StructureStatus.Complete && home.Type == StructureType.Shelter && citizen.Location != home.Location)
        {
            BeginTravel(citizen, action, home.Location, null, CitizenActionPhase.TravelToTarget, home.Id);
            return;
        }
        if (action == CitizenAction.Eat && citizen.Location != World.StartingSite) { BeginTravel(citizen, action, World.StartingSite, null); return; }
        citizen.CurrentAction = action;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionTarget = null;
        citizen.TargetResourceNodeId = null;
        citizen.ActionStartedMinute = CurrentMinute;
        var duration = action == CitizenAction.Rest ? CitizenSimulationRules.RestDurationMinutes : action == CitizenAction.Eat ? CitizenSimulationRules.EatDurationMinutes : CitizenSimulationRules.IdleMinimumMinutes + (int)(random.NextUInt64(RandomDomain.DecisionVariation, (ulong)citizen.Id.Value, (ulong)citizen.ActionSequence, 5) % (CitizenSimulationRules.IdleMaximumMinutes - CitizenSimulationRules.IdleMinimumMinutes + 1));
        citizen.ActionCompletesMinute = CurrentMinute.Add(duration);
        ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }
    private void BeginTravel(Citizen citizen, CitizenAction action, TileCoordinate target, ResourceNodeId? node, CitizenActionPhase phase = CitizenActionPhase.TravelToTarget, StructureId? structureId = null)
    {
        var route = FindPathCached(citizen.Location, target);
        citizen.CurrentAction = action;
        citizen.ActionPhase = phase;
        citizen.ActionTarget = target;
        citizen.TargetResourceNodeId = node;
        citizen.TargetStructureId = structureId;
        citizen.ActionStartedMinute = CurrentMinute;
        if ((SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion) && citizen.Location == target)
        {
            citizen.ActionCompletesMinute = CurrentMinute;
            ArriveAtTarget(citizen);
            return;
        }
        if (route is null || route.Count < 2)
        {
            ClearCarriedState(citizen);
            citizen.TargetResourceNodeId = null;
            citizen.CurrentAction = CitizenAction.Idle;
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.ActionStartedMinute = CurrentMinute;
            citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.IdleMinimumMinutes);
            ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
            return;
        }
        _activePaths[(citizen.Id.Value, citizen.ActionSequence)] = route;
        citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(route, World));
        ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(StepCost(route[0], route[1], World)), CitizenEventNames.MovementPriority);
    }
    private void MoveStep(Citizen citizen)
    {
        if (citizen.ActionTarget is not { } target || citizen.ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.TravelToTarget or CitizenActionPhase.ReturnToStockpile or CitizenActionPhase.TravelToStockpile or CitizenActionPhase.TransportToConstruction)) throw new InvalidDataException("Movement event does not match citizen action phase.");
        var key = (citizen.Id.Value, citizen.ActionSequence);
        if (!_activePaths.TryGetValue(key, out var path))
        {
            path = FindPathCached(citizen.Location, target);
            if (path is null || path.Count < 2)
            {
                if (citizen.Location == target) ArriveAtTarget(citizen);
                else StartIdleAfterFailure(citizen);
                return;
            }
            _activePaths[key] = path;
        }
        var index = -1;
        for (var candidate = 0; candidate < path.Count; candidate++) if (path[candidate] == citizen.Location) { index = candidate; break; }
        if (index < 0 || index + 1 >= path.Count)
        {
            if (citizen.Location == target) ArriveAtTarget(citizen);
            else StartIdleAfterFailure(citizen);
            _activePaths.Remove(key);
            return;
        }
        var next = path[index + 1];
        var stepCost = StepCost(path[index], next, World);
        citizen.Location = next;
        citizen.LifetimeMovementSteps = checked(citizen.LifetimeMovementSteps + 1);
        citizen.LifetimeMovementCost = checked(citizen.LifetimeMovementCost + stepCost);
        if (next == target)
        {
            _activePaths.Remove(key);
            ArriveAtTarget(citizen);
        }
        else
        {
            var remainingPath = path.Skip(index + 1).ToArray();
            citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(remainingPath, World));
            ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(StepCost(remainingPath[0], remainingPath[1], World)), CitizenEventNames.MovementPriority);
        }
    }
    private void ArriveAtTarget(Citizen citizen)
    {
        if (citizen.ActionPhase == CitizenActionPhase.ReturnToStockpile)
        {
            Deposit(citizen);
            if (citizen.ActionPhase != CitizenActionPhase.WaitingForStorage) CompleteAction(citizen, gatheringAlreadyDeposited: true);
            return;
        }
        if (citizen.CurrentAction == CitizenAction.HaulConstruction)
        {
            if (citizen.ActionPhase == CitizenActionPhase.TravelToStockpile) { PickUpConstructionMaterial(citizen); return; }
            if (citizen.ActionPhase == CitizenActionPhase.TransportToConstruction) { DeliverConstructionMaterial(citizen); return; }
        }
        if (citizen.CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone)
        {
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.ActionStartedMinute = CurrentMinute;
            var skill = RelevantSkill(citizen.CurrentAction, citizen);
            var duration = Math.Max(CitizenSimulationRules.MinimumGatherDurationMinutes, CitizenSimulationRules.BaseGatherDurationMinutes - skill / 100);
            citizen.ActionCompletesMinute = CurrentMinute.Add(duration);
            ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
            return;
        }
        if (citizen.CurrentAction == CitizenAction.Eat)
        {
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.TargetResourceNodeId = null;
            citizen.ActionStartedMinute = CurrentMinute;
            citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.EatDurationMinutes);
            ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
            return;
        }
        if (citizen.CurrentAction == CitizenAction.Rest)
        {
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.ActionStartedMinute = CurrentMinute;
            citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.RestDurationMinutes);
            ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
            return;
        }
        if (citizen.CurrentAction == CitizenAction.Build)
        {
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.ActionStartedMinute = CurrentMinute;
            citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.ConstructionShiftDurationMinutes);
            ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
            return;
        }
        citizen.TargetResourceNodeId = null;
        CompleteAction(citizen);
    }
    private void CompleteAction(Citizen citizen, bool gatheringAlreadyDeposited = false)
    {
        citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        if (citizen.CurrentAction == CitizenAction.Rest)
        {
            var shelterReduction = citizen.HomeStructureId is { } homeId && _structures.TryGetValue(homeId.Value, out var home) && home.Type == StructureType.Shelter && home.Status == StructureStatus.Complete && citizen.Location == home.Location ? 7000 : 0;
            citizen.Needs = new CitizenNeeds(citizen.Needs.Hunger, Math.Max(0, citizen.Needs.Rest - CitizenSimulationRules.RestNeedReduction), Math.Max(0, citizen.Needs.Shelter - shelterReduction), citizen.Needs.Social);
        }
        else if (citizen.CurrentAction == CitizenAction.Eat) Eat(citizen);
        else if (citizen.CurrentAction == CitizenAction.Socialize) CompleteSocialize(citizen);
        else if (!gatheringAlreadyDeposited && citizen.CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone) Gather(citizen);
        else if (citizen.CurrentAction == CitizenAction.Build) PerformConstruction(citizen);
        if (!gatheringAlreadyDeposited && citizen.ActionPhase == CitizenActionPhase.ReturnToStockpile) return;
        FinishAction(citizen);
        ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }
    private Citizen? SelectSocialTarget(Citizen citizen)
    {
        var random = new DeterministicRandom(Seed);
        var candidates = _citizens.Values.Where(other => other.IsAlive && other.Id != citizen.Id && ChebyshevDistance(citizen.Location, other.Location) <= CitizenSimulationRules.SocialRadius)
            .Select(other =>
            {
                var relationship = GetRelationship(citizen.Id, other.Id);
                var score = 1000 + other.Traits.Sociability / 20 + (relationship?.Familiarity ?? 0) / 5 + (relationship?.Affinity ?? 0) / 10 - (relationship?.Conflict ?? 0) / 5;
                score -= SocialRecentInteractionPenalty(relationship, CurrentMinute);
                if (citizen.PartnerId == other.Id) score += 1000;
                if (IsFamily(citizen.Id, other.Id)) score += 500;
                if (RelationshipLabels.Derive(relationship, false, false) == RelationshipLabels.Rival) score -= 1000;
                score += (int)(random.NextUInt64(RandomDomain.Relationships, (ulong)citizen.Id.Value, (ulong)other.Id.Value, (ulong)citizen.ActionSequence ^ 0x53544f4349414cUL) % 501) - 250;
                return new SocialTargetCandidate(other.Id, score, ChebyshevDistance(citizen.Location, other.Location));
            }).ToArray();
        return candidates.Length == 0 ? null : _citizens[SelectSocialTargetCandidate(candidates).Value];
    }
    internal static CitizenId SelectSocialTargetCandidate(IReadOnlyList<SocialTargetCandidate> candidates)
    {
        if (candidates.Count == 0) throw new ArgumentException("At least one social target candidate is required.", nameof(candidates));
        return candidates.OrderByDescending(x => x.Score).ThenBy(x => x.Distance).ThenBy(x => x.CitizenId.Value).First().CitizenId;
    }
    internal static int SocialRecentInteractionPenalty(RelationshipState? relationship, WorldMinute currentMinute)
    {
        if (relationship is null) return 0;
        var elapsed = currentMinute.Value - relationship.LastInteractionMinute;
        if (elapsed < 0) throw new ArgumentException("Relationship interaction time cannot follow the current minute.", nameof(relationship));
        return Math.Max(0, CitizenSimulationRules.SocialRecentInteractionPenalty - (int)Math.Min((long)CitizenSimulationRules.SocialRecentInteractionPenalty, elapsed / CitizenSimulationRules.SocialRecentInteractionPenaltyDecayMinutesPerPoint));
    }
    private void CompleteSocialize(Citizen initiator)
    {
        if (initiator.TargetCitizenId is not { } targetId || !_citizens.TryGetValue(targetId.Value, out var target) || !target.IsAlive || ChebyshevDistance(initiator.Location, target.Location) > CitizenSimulationRules.SocialRadius) return;
        var pair = RelationshipState.Normalize(initiator.Id, target.Id);
        var previous = _relationships.GetValueOrDefault((pair.A.Value, pair.B.Value));
        var count = checked((previous?.InteractionCount ?? 0) + 1);
        var averageCooperation = (initiator.Traits.Cooperativeness + target.Traits.Cooperativeness) / 2;
        var averageRisk = (initiator.Traits.RiskTolerance + target.Traits.RiskTolerance) / 2;
        var chance = Math.Clamp(500 + (10000 - averageCooperation) / 10 + averageRisk / 20 + (previous?.Conflict ?? 0) / 20, 500, 2500);
        var draw = new DeterministicRandom(Seed).NextUInt64(RandomDomain.Relationships, (ulong)pair.A.Value, (ulong)pair.B.Value, (ulong)count ^ 0x544f4e45UL) % 10000;
        var negative = draw < (ulong)chance;
        var next = new RelationshipState(pair.A, pair.B,
            Math.Clamp((previous?.Familiarity ?? 0) + (negative ? 350 : 500), 0, 10000),
            Math.Clamp((previous?.Affinity ?? 0) + (negative ? -500 : 400), -10000, 10000),
            Math.Clamp((previous?.Trust ?? 0) + (negative ? -250 : 300), 0, 10000),
            Math.Clamp((previous?.Conflict ?? 0) + (negative ? 600 : -150), 0, 10000), CurrentMinute.Value, count);
        _relationships[(pair.A.Value, pair.B.Value)] = next;
        RecordSocialInteractionHistory(previous, next);
        initiator.Needs = new CitizenNeeds(initiator.Needs.Hunger, initiator.Needs.Rest, initiator.Needs.Shelter, Math.Max(0, initiator.Needs.Social - 5000));
        target.Needs = target.GetProjectedNeeds(CurrentMinute);
        target.NeedsUpdatedMinute = CurrentMinute.Value;
        target.Needs = new CitizenNeeds(target.Needs.Hunger, target.Needs.Rest, target.Needs.Shelter, Math.Max(0, target.Needs.Social - 2500));
        TryFormPartnership(initiator, target, next);
        RecordPartnershipHistory(initiator, target);
    }
    private RelationshipState? GetRelationship(CitizenId first, CitizenId second) { var pair = RelationshipState.Normalize(first, second); return _relationships.GetValueOrDefault((pair.A.Value, pair.B.Value)); }
    private static int ChebyshevDistance(TileCoordinate a, TileCoordinate b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    private static bool IsAgeEligible(CitizenAction action, int age) => age switch
    {
        <= 5 => action is CitizenAction.Eat or CitizenAction.Rest or CitizenAction.Socialize or CitizenAction.Idle,
        <= 12 => action is CitizenAction.Eat or CitizenAction.Rest or CitizenAction.Socialize or CitizenAction.Idle or CitizenAction.Wander or CitizenAction.GatherFood,
        _ => true
    };
    private bool IsFamily(CitizenId first, CitizenId second)
    {
        if (first == second) return true;
        var left = AncestorsInclusive(first); var right = AncestorsInclusive(second);
        return left.Overlaps(right);
    }
    private HashSet<long> AncestorsInclusive(CitizenId citizenId)
    {
        var visited = new HashSet<long>(); var pending = new Stack<long>(); pending.Push(citizenId.Value);
        while (pending.TryPop(out var id) && visited.Add(id) && _citizens.TryGetValue(id, out var citizen))
        {
            if (citizen.ParentAId is { } parentA) pending.Push(parentA.Value);
            if (citizen.ParentBId is { } parentB) pending.Push(parentB.Value);
        }
        return visited;
    }
    private void TryFormPartnership(Citizen first, Citizen second, RelationshipState relationship)
    {
        if (!first.IsAlive || !second.IsAlive || first.PartnerId is not null || second.PartnerId is not null || first.AgeYears(CurrentMinute) < 18 || second.AgeYears(CurrentMinute) < 18 || (GrowthSystemsEnabled(SimulationRulesVersion) ? GrowthKinship.AreCloseKin(first.Id, second.Id, _citizens) : IsFamily(first.Id, second.Id)) || relationship.Familiarity < 5000 || relationship.Affinity < 4500 || relationship.Trust < 3500 || relationship.Conflict > 1500) return;
        if (GrowthSystemsEnabled(SimulationRulesVersion) && GrowthPartnershipMembers(first, second).Length > CitizenSimulationRules.MaximumHouseholdSize) return;
        var pair = RelationshipState.Normalize(first.Id, second.Id);
        var variation = (int)(new DeterministicRandom(Seed).NextUInt64(RandomDomain.Relationships, (ulong)pair.A.Value, (ulong)pair.B.Value, (ulong)relationship.InteractionCount ^ 0x504152544e4552UL) % 1001) - 500;
        if (relationship.Familiarity + relationship.Affinity + relationship.Trust - relationship.Conflict + variation < 13000) return;
        var household = new Household(_counters.AllocateHouseholdId(), CurrentMinute.Value);
        _households.Add(household.Id.Value, household);
        if (GrowthSystemsEnabled(SimulationRulesVersion))
            foreach (var member in GrowthPartnershipMembers(first, second)) member.HouseholdId = household.Id;
        first.PartnerId = second.Id; second.PartnerId = first.Id; first.HouseholdId = household.Id; second.HouseholdId = household.Id;
        ReconcileHouseholdsAndHousing();
    }
    private void RunFamilyCheck()
    {
        if (GrowthSystemsEnabled(SimulationRulesVersion)) EnsureIndependentHouseholds();
        ReconcileHouseholdsAndHousing();
        var diagnostics = _captureFamilyCheckDiagnostics ? new FamilyCheckDiagnosticBuilder(CurrentMinute, _households.Values.Count(x => x.DissolvedMinute is null)) : null;
        var homesBeforeRelocation = diagnostics is null ? null : _citizens.Values.Where(x => x.IsAlive).ToDictionary(x => x.Id, x => x.HomeStructureId);
        if (diagnostics is not null)
        {
            foreach (var household in _households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)) diagnostics.RecordBeforeRelocation(household.Id, EvaluateReproductionReadiness(household));
        }
        TryRelocateOneHousingBlockedReproductiveHousehold();
        if (diagnostics is not null)
        {
            diagnostics.HomeChanges = _citizens.Values.Count(x => x.IsAlive && homesBeforeRelocation![x.Id] != x.HomeStructureId);
            foreach (var household in _households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)) diagnostics.RecordAfterRelocation(household.Id, EvaluateReproductionReadiness(household));
        }
        foreach (var household in _households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value).ToArray()) TryBirth(household, diagnostics);
        ReconcileHouseholdsAndHousing();
        if (diagnostics is not null) _familyCheckDiagnostics.Add(diagnostics.ToSnapshot());
    }
    private void TryBirth(Household household, FamilyCheckDiagnosticBuilder? diagnostics = null)
    {
        diagnostics?.RecordCounterfactual(CreateFamilyCheckOpportunityDiagnostic(household));
        if (!IsReproductionReady(household, requireDwellingCapacity: true, out var parents, out var relation)) return;
        var existingChildren = _citizens.Values.Count(c => c.ParentAId == parents[0].Id && c.ParentBId == parents[1].Id);
        var chance = CalculateBirthChance(relation, existingChildren, CurrentMinute.ToCalendar().Season);
        diagnostics?.RecordBirthRandomOpportunity();
        if (CalculateBirthDraw(household) >= chance) return;
        diagnostics?.RecordBirthRandomHit();
        CreateChild(parents[0], parents[1], household);
    }
    // Settlement demand may anticipate a birth blocked by shelter capacity, but must retain
    // every non-housing birth prerequisite (including the two-year per-pair cooldown).
    private bool IsReproductionReady(Household household, bool requireDwellingCapacity, out Citizen[] parents, out RelationshipState relationship)
    {
        var readiness = EvaluateReproductionReadiness(household);
        parents = readiness.Parents;
        relationship = readiness.Relationship!;
        return readiness.ReadyIgnoringHousing && (!requireDwellingCapacity || readiness.HasDwellingCapacity);
    }
    private ReproductionReadiness EvaluateReproductionReadiness(Household household)
    {
        var pair = _citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id && x.PartnerId is not null).OrderBy(x => x.Id.Value).Take(2).ToArray();
        var pairValid = household.DissolvedMinute is null && pair.Length == 2 && pair[0].PartnerId == pair[1].Id;
        var agesValid = pairValid && pair.All(x => x.AgeYears(CurrentMinute) is >= 18 and <= 45);
        var healthAndHungerValid = agesValid && pair.All(x => x.Health >= 7000 && x.GetProjectedNeeds(CurrentMinute).Hunger < 8000);
        var foodValid = healthAndHungerValid && Settlement.FoodStored >= checked(LivingPopulation * 10);
        var populationValid = LivingPopulation < CitizenSimulationRules.MaximumPopulation;
        var householdSizeValid = _citizens.Values.Count(x => x.IsAlive && x.HouseholdId == household.Id) < CitizenSimulationRules.MaximumHouseholdSize;
        var cooldownValid = pairValid && !_citizens.Values.Any(c => c.ParentAId == pair.ElementAtOrDefault(0)?.Id && c.ParentBId == pair.ElementAtOrDefault(1)?.Id && CurrentMinute.Value - c.BirthMinute < CitizenSimulationRules.BirthCooldownMinutes);
        var relationship = pairValid && agesValid && healthAndHungerValid && populationValid && householdSizeValid && cooldownValid ? GetRelationship(pair[0].Id, pair[1].Id) : null;
        var readyIgnoringHousing = foodValid && populationValid && householdSizeValid && cooldownValid && relationship is not null;
        var hasDwellingCapacityIgnoringFood = household.DwellingStructureId is { } dwelling && _citizens.Values.Count(x => x.IsAlive && x.HomeStructureId == dwelling) < CitizenSimulationRules.ShelterCapacityPerBuilding;
        var readyExceptFood = !foodValid && populationValid && householdSizeValid && cooldownValid && relationship is not null;
        var hasDwellingCapacity = readyIgnoringHousing && hasDwellingCapacityIgnoringFood;
        return new ReproductionReadiness(pair, relationship, readyIgnoringHousing, hasDwellingCapacity, readyExceptFood, hasDwellingCapacityIgnoringFood, !agesValid && pairValid, !healthAndHungerValid && agesValid, !foodValid && healthAndHungerValid, !cooldownValid && householdSizeValid);
    }
    private sealed record ReproductionReadiness(Citizen[] Parents, RelationshipState? Relationship, bool ReadyIgnoringHousing, bool HasDwellingCapacity, bool ReadyExceptFood, bool HasDwellingCapacityIgnoringFood, bool AgesBlocked, bool HungerOrHealthBlocked, bool FoodBlocked, bool CooldownBlocked);
    private int FoodSecurityGatherContribution()
    {
        if (!SocialSystemsEnabled(SimulationRulesVersion) || !_households.Values.Where(household => household.DissolvedMinute is null).Any(household =>
            {
                var readiness = EvaluateReproductionReadiness(household);
                return readiness.FoodBlocked && readiness.ReadyExceptFood && readiness.HasDwellingCapacityIgnoringFood;
            })) return 0;
        return CalculateFoodSecurityGatherContribution(LivingPopulation, Settlement.FoodStored);
    }
    internal static int CalculateFoodSecurityGatherContribution(int livingPopulation, int foodStored)
    {
        var deficit = Math.Max(0, checked(checked(livingPopulation * 10) - foodStored));
        return Math.Min(CitizenSimulationRules.FoodSecurityGatherContributionMaximum, checked(deficit * 10));
    }
    private static int CalculateBirthChance(RelationshipState relationship, int existingChildren, WorldSeason season) => Math.Clamp(20 + (season == WorldSeason.Spring ? 8 : season == WorldSeason.Summer ? 4 : season == WorldSeason.Winter ? -8 : 0) + Math.Max(0, (relationship.Affinity - 5000) / 500) + Math.Max(0, (relationship.Trust - 5000) / 1000) - (existingChildren == 1 ? 5 : 0), 1, 50);
    private int CalculateBirthDraw(Household household) => (int)(new DeterministicRandom(Seed).NextUInt64(RandomDomain.Reproduction, (ulong)household.Id.Value, (ulong)(CurrentMinute.Value / WorldCalendar.MinutesPerDay), 0x4249525448UL) % 10000);
    private FamilyCheckOpportunityDiagnostic CreateFamilyCheckOpportunityDiagnostic(Household household)
    {
        var members = _citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id).OrderBy(x => x.Id.Value).ToArray();
        var pair = members.Where(x => x.PartnerId is not null).Take(2).ToArray();
        var partnershipValid = household.DissolvedMinute is null && pair.Length == 2 && pair[0].PartnerId == pair[1].Id;
        var first = pair.ElementAtOrDefault(0); var second = pair.ElementAtOrDefault(1);
        var firstNeeds = first?.GetProjectedNeeds(CurrentMinute); var secondNeeds = second?.GetProjectedNeeds(CurrentMinute);
        var agesValid = partnershipValid && pair.All(x => x.AgeYears(CurrentMinute) is >= 18 and <= 45);
        var healthAndHungerValid = partnershipValid && pair.All(x => x.Health >= 7000 && x.GetProjectedNeeds(CurrentMinute).Hunger < 8000);
        var foodRequired = checked(LivingPopulation * 10);
        var foodValid = Settlement.FoodStored >= foodRequired;
        var populationValid = LivingPopulation < CitizenSimulationRules.MaximumPopulation;
        var householdSizeValid = members.Length < CitizenSimulationRules.MaximumHouseholdSize;
        var cooldownValid = partnershipValid && !_citizens.Values.Any(c => c.ParentAId == first?.Id && c.ParentBId == second?.Id && CurrentMinute.Value - c.BirthMinute < CitizenSimulationRules.BirthCooldownMinutes);
        var relationship = partnershipValid ? GetRelationship(first!.Id, second!.Id) : null;
        var dwellingOccupancy = household.DwellingStructureId is { } dwelling ? _citizens.Values.Count(x => x.IsAlive && x.HomeStructureId == dwelling) : 0;
        var dwellingValid = household.DwellingStructureId is not null && dwellingOccupancy < CitizenSimulationRules.ShelterCapacityPerBuilding;
        var blockers = FamilyCheckBlocker.None;
        if (!partnershipValid) blockers |= FamilyCheckBlocker.Partnership;
        if (partnershipValid && !agesValid) blockers |= FamilyCheckBlocker.Age;
        if (partnershipValid && !healthAndHungerValid) blockers |= FamilyCheckBlocker.HealthOrProjectedHunger;
        if (!foodValid) blockers |= FamilyCheckBlocker.Food;
        if (!populationValid) blockers |= FamilyCheckBlocker.Population;
        if (!householdSizeValid) blockers |= FamilyCheckBlocker.HouseholdSize;
        if (partnershipValid && !cooldownValid) blockers |= FamilyCheckBlocker.Cooldown;
        if (partnershipValid && relationship is null) blockers |= FamilyCheckBlocker.Relationship;
        if (!dwellingValid) blockers |= FamilyCheckBlocker.ActualDwelling;
        var chance = relationship is null ? (int?)null : CalculateBirthChance(relationship, _citizens.Values.Count(c => c.ParentAId == first!.Id && c.ParentBId == second!.Id), CurrentMinute.ToCalendar().Season);
        var draw = chance is null ? (int?)null : CalculateBirthDraw(household);
        var foodNodes = World.Resources.Where(node => node.Type == ResourceType.Food && _resourceStates.TryGetValue(node.Id.Value, out var state) && state.CurrentQuantity > 0).ToArray();
        var firstAccessible = first is not null && foodNodes.Any(node => DeterministicPathfinder.Find(World, first.Location, node.Coordinate) is not null);
        var secondAccessible = second is not null && foodNodes.Any(node => DeterministicPathfinder.Find(World, second.Location, node.Coordinate) is not null);
        return new FamilyCheckOpportunityDiagnostic(household.Id, CurrentMinute.Value / WorldCalendar.MinutesPerDay, first?.Id, second?.Id, chance, draw, blockers, foodRequired, Settlement.FoodStored, household.DwellingStructureId, dwellingOccupancy, first is null ? null : first.AgeYears(CurrentMinute), second is null ? null : second.AgeYears(CurrentMinute), first?.Health, second?.Health, firstNeeds?.Hunger, secondNeeds?.Hunger, firstAccessible, secondAccessible);
    }
    private sealed class FamilyCheckDiagnosticBuilder
    {
        private readonly WorldMinute _minute; private readonly int _activeHouseholds;
        private readonly HashSet<HouseholdId> _housingBlockedBeforeRelocation = [];
        private readonly List<FamilyCheckOpportunityDiagnostic> _opportunities = [];
        private int _readyIgnoringHousing; private int _blockedActualDwelling; private int _readyWithHousingAfterRelocation; private int _readyUnlockedByRelocation; private int _birthRandomOpportunities; private int _birthRandomHits; private int _foodBlocked; private int _hungerOrHealthBlocked; private int _cooldownBlocked; private int _agesBlocked; private int _dwellingBlocked;
        public FamilyCheckDiagnosticBuilder(WorldMinute minute, int activeHouseholds) { _minute = minute; _activeHouseholds = activeHouseholds; }
        public int HomeChanges { get; set; }
        public void RecordBeforeRelocation(HouseholdId householdId, ReproductionReadiness readiness)
        {
            if (readiness.ReadyIgnoringHousing) { _readyIgnoringHousing++; if (!readiness.HasDwellingCapacity) { _blockedActualDwelling++; _dwellingBlocked++; _housingBlockedBeforeRelocation.Add(householdId); } }
            if (readiness.FoodBlocked) _foodBlocked++; if (readiness.HungerOrHealthBlocked) _hungerOrHealthBlocked++; if (readiness.CooldownBlocked) _cooldownBlocked++; if (readiness.AgesBlocked) _agesBlocked++;
        }
        public void RecordAfterRelocation(HouseholdId householdId, ReproductionReadiness readiness)
        {
            if (readiness.HasDwellingCapacity) _readyWithHousingAfterRelocation++;
            if (_housingBlockedBeforeRelocation.Contains(householdId) && readiness.HasDwellingCapacity) _readyUnlockedByRelocation++;
        }
        public void RecordBirthRandomOpportunity() => _birthRandomOpportunities++;
        public void RecordBirthRandomHit() => _birthRandomHits++;
        public void RecordCounterfactual(FamilyCheckOpportunityDiagnostic opportunity) => _opportunities.Add(opportunity);
        public FamilyCheckDiagnostic ToSnapshot() => new(_minute, _activeHouseholds, _readyIgnoringHousing, _blockedActualDwelling, HomeChanges, _readyWithHousingAfterRelocation, _readyUnlockedByRelocation, _birthRandomOpportunities, _birthRandomHits, _foodBlocked, _hungerOrHealthBlocked, _cooldownBlocked, _agesBlocked, _dwellingBlocked, Array.AsReadOnly(_opportunities.ToArray()));
    }
    private sealed record HousingGroup(long Key, Household? Household, Citizen[] Members, StructureId Home);
    private void TryRelocateOneHousingBlockedReproductiveHousehold()
    {
        var shelters = _structures.Values.Where(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Shelter).OrderBy(x => x.Id.Value).ToArray();
        var usage = shelters.ToDictionary(shelter => shelter.Id.Value, shelter => _citizens.Values.Count(citizen => citizen.IsAlive && citizen.HomeStructureId == shelter.Id));
        var householdGroups = _households.Values.Where(household => household.DissolvedMinute is null && household.DwellingStructureId is not null)
            .OrderBy(household => household.Id.Value)
            .Select(household => new HousingGroup(household.Id.Value, household, _citizens.Values.Where(citizen => citizen.IsAlive && citizen.HouseholdId == household.Id).OrderBy(citizen => citizen.Id.Value).ToArray(), household.DwellingStructureId!.Value))
            .Where(group => group.Members.Length > 0 && group.Members.All(member => member.HomeStructureId == group.Home))
            .ToArray();
        var singletonGroups = _citizens.Values.Where(citizen => citizen.IsAlive && citizen.HouseholdId is null && citizen.HomeStructureId is not null).OrderBy(citizen => citizen.Id.Value)
            .Select(citizen => new HousingGroup(citizen.Id.Value, null, [citizen], citizen.HomeStructureId!.Value));
        var groups = householdGroups.Concat(singletonGroups).OrderBy(group => group.Key).ToArray();

        foreach (var candidate in householdGroups.Where(group => IsReproductionReady(group.Household!, requireDwellingCapacity: false, out _, out _) && !IsReproductionReady(group.Household!, requireDwellingCapacity: true, out _, out _)).OrderBy(group => group.Household!.Id.Value))
        {
            if (HasRestTiedToHome(candidate)) continue;
            var moves = groups.Where(donor => donor.Key != candidate.Key && donor.Home == candidate.Home && !HasRestTiedToHome(donor) && (donor.Household is null || !IsReproductionReady(donor.Household, requireDwellingCapacity: true, out _, out _)))
                .SelectMany(donor => shelters.Where(destination => destination.Id != candidate.Home && CitizenSimulationRules.ShelterCapacityPerBuilding - usage[destination.Id.Value] >= donor.Members.Length)
                    .Select(destination => (Donor: donor, Destination: destination)))
                .OrderBy(move => move.Donor.Members.Length).ThenBy(_ => 1).ThenBy(move => usage[move.Destination.Id.Value]).ThenBy(move => move.Destination.Id.Value).ThenBy(move => move.Donor.Key)
                .FirstOrDefault();
            if (moves.Donor is null) continue;

            foreach (var member in moves.Donor.Members) SetHomeStructure(member, moves.Destination.Id);
            if (moves.Donor.Household is not null) moves.Donor.Household.DwellingStructureId = moves.Destination.Id;
            usage[candidate.Home.Value] -= moves.Donor.Members.Length;
            usage[moves.Destination.Id.Value] += moves.Donor.Members.Length;
            return;
        }
    }
    private static bool HasRestTiedToHome(HousingGroup group) => group.Members.Any(member => member.CurrentAction == CitizenAction.Rest && member.HomeStructureId == group.Home);
    private void CreateChild(Citizen first, Citizen second, Household household)
    {
        var parentA = first.Id.Value < second.Id.Value ? first : second; var parentB = parentA == first ? second : first;
        var id = _counters.AllocateCitizenId(); var random = new DeterministicRandom(Seed);
        int Trait(int index, int a, int b) => Math.Clamp((a + b) / 2 + (int)(random.NextUInt64(RandomDomain.Reproduction, (ulong)id.Value, (ulong)index, 0x5452414954UL) % 2001) - 1000, 0, 10000);
        var child = new Citizen(id, null, CitizenGenerator.SelectDescendantGivenName(Seed, id), parentA.FamilyName, CurrentMinute.Value, parentA.Location,
            new CitizenTraits(Trait(1, parentA.Traits.Industriousness, parentB.Traits.Industriousness), Trait(2, parentA.Traits.Sociability, parentB.Traits.Sociability), Trait(3, parentA.Traits.Curiosity, parentB.Traits.Curiosity), Trait(4, parentA.Traits.Cooperativeness, parentB.Traits.Cooperativeness), Trait(5, parentA.Traits.RiskTolerance, parentB.Traits.RiskTolerance), Trait(6, parentA.Traits.Resilience, parentB.Traits.Resilience)), new CitizenSkills(0, 0, 0, 0, 0, 0))
        { ParentAId = parentA.Id, ParentBId = parentB.Id, HouseholdId = household.Id, HomeStructureId = household.DwellingStructureId, NeedsUpdatedMinute = CurrentMinute.Value, HealthUpdatedMinute = CurrentMinute.Value };
        _citizens.Add(id.Value, child);
        foreach (var parent in new[] { parentA, parentB }) SetRelationship(child.Id, parent.Id, 8000, 8000, 7000, 0);
        foreach (var sibling in _citizens.Values.Where(c => c.IsAlive && c.Id != child.Id && c.ParentAId == parentA.Id && c.ParentBId == parentB.Id).OrderBy(c => c.Id.Value)) if (GetRelationship(child.Id, sibling.Id) is null) SetRelationship(child.Id, sibling.Id, 6000, 6000, 5000, 0);
        ScheduleCitizen(child, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); ScheduleSurvival(child);
        RecordBirthHistory(child);
    }
    private void SetRelationship(CitizenId first, CitizenId second, int familiarity, int affinity, int trust, int conflict)
    {
        var pair = RelationshipState.Normalize(first, second); _relationships[(pair.A.Value, pair.B.Value)] = new RelationshipState(pair.A, pair.B, familiarity, affinity, trust, conflict, CurrentMinute.Value, 1);
    }
    private void RunLifecycleCheck()
    {
        var random = new DeterministicRandom(Seed); var day = (ulong)(CurrentMinute.Value / WorldCalendar.MinutesPerDay);
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive).OrderBy(x => x.Id.Value).ToArray())
        {
            var risk = NaturalMortalityRisk(citizen, CurrentMinute); if (risk > 0 && random.NextUInt64(RandomDomain.Mortality, (ulong)citizen.Id.Value, day, 0x4d4f5254414cUL) % 1_000_000 < (ulong)risk) KillNatural(citizen);
        }
        ReconcileHouseholdsAndHousing();
    }
    public static int NaturalMortalityRisk(Citizen citizen, WorldMinute currentMinute)
    {
        var age = citizen.AgeYears(currentMinute);
        var baseRisk = age switch { < 18 => 0, < 40 => 1, < 50 => 3, < 60 => 10, < 70 => 50, < 80 => 150, < 90 => 500, < 100 => 1500, _ => 5000 };
        return Math.Clamp(checked(checked(baseRisk * (10000 + (10000 - citizen.Health)) / 10000) * (10000 - citizen.Traits.Resilience / 5) / 10000), 0, 1_000_000);
    }
    public static int NaturalMortalityRisk(Citizen citizen) => NaturalMortalityRisk(citizen, new WorldMinute(Math.Max(0, citizen.DeathMinute ?? 0)));
    private void KillNatural(Citizen citizen)
    {
        citizen.Health = 0; citizen.DeathMinute = CurrentMinute.Value; citizen.DeathCause = "natural"; citizen.CurrentAction = CitizenAction.Dead; citizen.ActionPhase = CitizenActionPhase.None; citizen.ActionTarget = null; citizen.TargetCitizenId = null; citizen.TargetResourceNodeId = null; citizen.TargetStructureId = null; citizen.ActionStartedMinute = null; citizen.ActionCompletesMinute = null; citizen.CarriedResourceType = null; citizen.CarriedResourceQuantity = 0;
        foreach (var item in _scheduledEvents.Where(e => IsReservedCitizenEventFor(e, citizen.Id.Value)).ToArray()) _scheduledEvents.Remove(item);
        CancelSocialActionsTargeting(citizen.Id);
        RecordDeathHistory(citizen);
        ReconcileM5Death(citizen);
    }
    private void CancelSocialActionsTargeting(CitizenId targetId)
    {
        foreach (var initiator in _citizens.Values.Where(x => x.IsAlive && x.CurrentAction == CitizenAction.Socialize && x.TargetCitizenId == targetId).OrderBy(x => x.Id.Value).ToArray())
        {
            foreach (var item in _scheduledEvents.Where(e => e.Name == CitizenEventNames.ActionComplete && IsReservedCitizenEventFor(e, initiator.Id.Value)).ToArray()) _scheduledEvents.Remove(item);
            FinishAction(initiator);
            ScheduleCitizen(initiator, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
        }
    }
    private void SetHomeStructure(Citizen citizen, StructureId? home)
    {
        var previousHome = citizen.HomeStructureId;
        if (previousHome == home) return;

        var resetRestTravel = SocialSystemsEnabled(SimulationRulesVersion) && citizen.IsAlive && citizen.CurrentAction == CitizenAction.Rest && citizen.ActionPhase == CitizenActionPhase.TravelToTarget && previousHome is not null && citizen.TargetStructureId == previousHome && _structures.TryGetValue(previousHome.Value.Value, out var previousStructure) && citizen.ActionTarget == previousStructure.Location;
        if (resetRestTravel)
        {
            var actionEvents = _scheduledEvents.Where(item => item.Name == CitizenEventNames.MoveStep && IsReservedCitizenEventFor(item, citizen.Id.Value)).ToArray();
            if (actionEvents.Length > 1) throw new InvalidDataException("A resting citizen has duplicate action-flow events.");
            if (actionEvents.Length == 1) _scheduledEvents.Remove(actionEvents[0]);
            _activePaths.Remove((citizen.Id.Value, citizen.ActionSequence));
        }

        citizen.HomeStructureId = home;
        if (!resetRestTravel) return;
        FinishAction(citizen);
        ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }
    private void ReconcileHouseholdsAndHousing()
    {
        foreach (var household in _households.Values.OrderBy(x => x.Id.Value))
        {
            var members = _citizens.Values.Where(c => c.IsAlive && c.HouseholdId == household.Id).OrderBy(c => c.Id.Value).ToArray();
            if (members.Length == 0) { household.DissolvedMinute ??= CurrentMinute.Value; household.DwellingStructureId = null; }
        }
        if (!SocialSystemsEnabled(SimulationRulesVersion)) { ReconcileShelterAssignments(); return; }
        var shelters = _structures.Values.Where(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Shelter).OrderBy(x => x.Id.Value).ToArray();
        var shelterIds = shelters.Select(x => x.Id.Value).ToHashSet();
        var usage = shelters.ToDictionary(x => x.Id.Value, _ => 0);
        var activeHouseholds = _households.Values.Where(x => x.DissolvedMinute is null)
            .Select(x => (Household: x, Members: _citizens.Values.Where(c => c.IsAlive && c.HouseholdId == x.Id).OrderBy(c => c.Id.Value).ToArray()))
            .OrderBy(x => x.Household.Id.Value).ToArray();

        // M5 housing is stable: retain each valid whole household and each valid singleton
        // home, then deterministically pack only invalid or unassigned groups.
        foreach (var group in activeHouseholds)
        {
            if (group.Household.DwellingStructureId is not { } dwelling || !shelterIds.Contains(dwelling.Value) || group.Members.Length == 0 || group.Members.Any(c => c.HomeStructureId != dwelling) || usage[dwelling.Value] + group.Members.Length > CitizenSimulationRules.ShelterCapacityPerBuilding)
            {
                group.Household.DwellingStructureId = null;
                foreach (var member in group.Members) SetHomeStructure(member, null);
                continue;
            }
            usage[dwelling.Value] += group.Members.Length;
        }
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive && x.HouseholdId is null).OrderBy(x => x.Id.Value))
        {
            if (citizen.HomeStructureId is not { } home || !shelterIds.Contains(home.Value) || usage[home.Value] >= CitizenSimulationRules.ShelterCapacityPerBuilding) { SetHomeStructure(citizen, null); continue; }
            usage[home.Value]++;
        }
        foreach (var group in activeHouseholds.Where(x => x.Household.DwellingStructureId is null).OrderByDescending(x => x.Members.Length).ThenBy(x => x.Household.Id.Value))
        {
            var dwelling = shelters.Where(x => CitizenSimulationRules.ShelterCapacityPerBuilding - usage[x.Id.Value] >= group.Members.Length).OrderBy(x => usage[x.Id.Value]).ThenBy(x => x.Id.Value).FirstOrDefault();
            group.Household.DwellingStructureId = dwelling?.Id;
            if (dwelling is null) continue;
            foreach (var member in group.Members) SetHomeStructure(member, dwelling.Id);
            usage[dwelling.Id.Value] += group.Members.Length;
        }
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive && x.HouseholdId is null && x.HomeStructureId is null).OrderBy(x => x.Id.Value))
        {
            var shelter = shelters.Where(x => usage[x.Id.Value] < CitizenSimulationRules.ShelterCapacityPerBuilding).OrderBy(x => usage[x.Id.Value]).ThenBy(x => x.Id.Value).FirstOrDefault();
            if (shelter is null) break;
            SetHomeStructure(citizen, shelter.Id); usage[shelter.Id.Value]++;
        }
    }
    private void Eat(Citizen citizen) { var consumed = Math.Min(CitizenSimulationRules.MealFoodUnits, Settlement.FoodStored); Settlement.FoodStored = checked(Settlement.FoodStored - consumed); var hungerReduction = checked((CitizenSimulationRules.FullHungerReduction * consumed) / CitizenSimulationRules.MealFoodUnits); citizen.Needs = new CitizenNeeds(Math.Max(0, citizen.Needs.Hunger - hungerReduction), citizen.Needs.Rest, citizen.Needs.Shelter, citizen.Needs.Social); }
    private void Gather(Citizen citizen)
    {
        if (citizen.TargetResourceNodeId is not { } id || !_resourceStates.TryGetValue(id.Value, out var state)) { FinishAction(citizen); return; }
        var node = World.Resources.Single(x => x.Id == id);
        var skill = RelevantSkill(citizen.CurrentAction, citizen);
        var baseYield = citizen.CurrentAction == CitizenAction.GatherFood ? CitizenSimulationRules.FoodBaseYield : citizen.CurrentAction == CitizenAction.GatherWood ? CitizenSimulationRules.WoodBaseYield : CitizenSimulationRules.StoneBaseYield;
        var calculated = checked(baseYield + skill / 1000);
        if (SocialSystemsEnabled(SimulationRulesVersion))
        {
            var productivity = LifeStages.ProductivityBasisPoints(citizen.AgeYears(CurrentMinute));
            calculated = productivity == 0 ? 0 : Math.Max(1, checked(calculated * productivity / 10000));
        }
        var actual = Math.Min(calculated, state.CurrentQuantity);
        if (SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion) actual = Math.Min(actual, Math.Max(0, StorageCapacity - Settlement.StorageUsed));
        state.CurrentQuantity -= actual;
        if (actual == 0)
        {
            ClearCarriedState(citizen);
            FinishAction(citizen);
            return;
        }
        citizen.CarriedResourceType = node.Type;
        citizen.CarriedResourceQuantity = actual;
        var duration = citizen.ActionStartedMinute is { } started ? checked(CurrentMinute.Value - started.Value) : 0;
        AddWorkMinutes(citizen, citizen.CurrentAction, duration);
        AddSkill(citizen, citizen.CurrentAction, CitizenSimulationRules.GatherExperienceGain);
        citizen.ActionPhase = CitizenActionPhase.ReturnToStockpile;
        citizen.ActionTarget = World.StartingSite;
        citizen.ActionStartedMinute = CurrentMinute;
        var path = FindPathCached(citizen.Location, World.StartingSite);
        if (path is null || path.Count < 2)
        {
            Deposit(citizen);
            FinishAction(citizen);
            return;
        }
        _activePaths[(citizen.Id.Value, citizen.ActionSequence)] = path;
        citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(path, World));
        ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(StepCost(path[0], path[1], World)), CitizenEventNames.MovementPriority);
    }
    private void PickUpConstructionMaterial(Citizen citizen)
    {
        if (citizen.TargetStructureId is not { } id || !_structures.TryGetValue(id.Value, out var structure) || structure.Status != StructureStatus.UnderConstruction) { FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); return; }
        var woodMissing = ConstructionMissing(structure, ResourceType.Wood);
        var stoneMissing = ConstructionMissing(structure, ResourceType.Stone);
        var type = woodMissing > 0 && Settlement.WoodStored > 0 ? ResourceType.Wood : stoneMissing > 0 && Settlement.StoneStored > 0 ? ResourceType.Stone : (ResourceType?)null;
        if (type is null) { FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); return; }
        var stock = type == ResourceType.Wood ? Settlement.WoodStored : Settlement.StoneStored;
        var capacity = CitizenSimulationRules.BaseConstructionCarryCapacity + Math.Min(CitizenSimulationRules.MaximumConstructionCarrySkillBonus, citizen.Skills.Hauling / 1000);
        if (SocialSystemsEnabled(SimulationRulesVersion)) capacity = Math.Max(1, checked(capacity * LifeStages.ProductivityBasisPoints(citizen.AgeYears(CurrentMinute)) / 10000));
        var available = type == ResourceType.Wood ? woodMissing : stoneMissing;
        var amount = Math.Min(capacity, Math.Min(stock, available));
        if (amount <= 0) { FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); return; }
        if (type == ResourceType.Wood) Settlement.WoodStored -= amount; else Settlement.StoneStored -= amount;
        citizen.CarriedResourceType = type;
        citizen.CarriedResourceQuantity = amount;
        BeginConstructionTransport(citizen, structure);
    }
    private int ConstructionMissing(Structure structure, ResourceType type)
    {
        var delivered = type == ResourceType.Wood ? structure.DeliveredWood : structure.DeliveredStone;
        var required = type == ResourceType.Wood ? structure.RequiredWood : structure.RequiredStone;
        var inTransit = _citizens.Values.Where(c => c.IsAlive && c.CurrentAction == CitizenAction.HaulConstruction && c.TargetStructureId == structure.Id && c.ActionPhase == CitizenActionPhase.TransportToConstruction && c.CarriedResourceType == type).Sum(c => c.CarriedResourceQuantity);
        return Math.Max(0, checked(required - delivered - inTransit));
    }
    private void BeginConstructionTransport(Citizen citizen, Structure structure)
    {
        var path = FindPathCached(citizen.Location, structure.Location);
        citizen.ActionPhase = CitizenActionPhase.TransportToConstruction;
        citizen.ActionTarget = structure.Location;
        citizen.ActionCompletesMinute = CurrentMinute;
        if (citizen.Location == structure.Location) { DeliverConstructionMaterial(citizen); return; }
        if (path is null || path.Count < 2) { FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); return; }
        _activePaths[(citizen.Id.Value, citizen.ActionSequence)] = path;
        citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(path, World));
        ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(StepCost(path[0], path[1], World)), CitizenEventNames.MovementPriority);
    }
    private void DeliverConstructionMaterial(Citizen citizen)
    {
        if (citizen.TargetStructureId is not { } id || !_structures.TryGetValue(id.Value, out var structure) || structure.Status != StructureStatus.UnderConstruction || citizen.CarriedResourceType is not { } type || citizen.CarriedResourceQuantity <= 0) { FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); return; }
        var remaining = type == ResourceType.Wood ? structure.RequiredWood - structure.DeliveredWood : structure.RequiredStone - structure.DeliveredStone;
        var applied = Math.Min(remaining, citizen.CarriedResourceQuantity);
        if (type == ResourceType.Wood) structure.DeliveredWood += applied; else structure.DeliveredStone += applied;
        var contribution = ContributionFor(structure.Id, citizen.Id);
        if (type == ResourceType.Wood) contribution.WoodDelivered += applied; else contribution.StoneDelivered += applied;
        if (applied > 0) { AddSkill(citizen, CitizenAction.HaulConstruction, CitizenSimulationRules.HaulingExperienceGain); if (citizen.ActionStartedMinute is { } started) AddWorkMinutes(citizen, CitizenAction.HaulConstruction, checked(CurrentMinute.Value - started.Value)); }
        ClearCarriedState(citizen);
        FinishAction(citizen);
        ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }
    private void PerformConstruction(Citizen citizen)
    {
        if (citizen.TargetStructureId is not { } id || !_structures.TryGetValue(id.Value, out var structure) || structure.Status != StructureStatus.UnderConstruction || structure.DeliveredWood < structure.RequiredWood || structure.DeliveredStone < structure.RequiredStone) return;
        var baseWork = checked(CitizenSimulationRules.BaseConstructionWorkPerShift + citizen.Skills.Construction / 1000);
        if (SocialSystemsEnabled(SimulationRulesVersion)) baseWork = checked(baseWork * LifeStages.ProductivityBasisPoints(citizen.AgeYears(CurrentMinute)) / 10000);
        if (_structures.Values.Any(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Workshop)) baseWork = checked(baseWork * CitizenSimulationRules.WorkshopConstructionMultiplierBasisPoints / 10000);
        var applied = Math.Min(baseWork, structure.RequiredWork - structure.CompletedWork);
        if (applied <= 0) return;
        structure.CompletedWork += applied;
        ContributionFor(structure.Id, citizen.Id).ConstructionWork += applied;
        AddSkill(citizen, CitizenAction.Build, CitizenSimulationRules.ConstructionExperienceGain);
        AddWorkMinutes(citizen, CitizenAction.Build, CitizenSimulationRules.ConstructionShiftDurationMinutes);
        if (structure.CompletedWork == structure.RequiredWork)
        {
            structure.Status = StructureStatus.Complete;
            structure.CompletedMinute = CurrentMinute.Value;
            RecordStructureCompletedHistory(structure);
            // A project can complete while other workers still have a queued build/haul
            // completion.  Those actions are no longer coherent persisted state: cancel
            // their sole action event and return them to a deterministic decision boundary.
            if (SocialSystemsEnabled(SimulationRulesVersion))
            {
                CancelConstructionActionsTargeting(structure.Id, citizen.Id);
                ReconcileHouseholdsAndHousing();
            }
            else ReconcileShelterAssignments();
        }
    }
    private StructureContribution ContributionFor(StructureId structureId, CitizenId citizenId)
    {
        var key = (structureId.Value, citizenId.Value);
        if (!_structureContributions.TryGetValue(key, out var contribution)) { contribution = new StructureContribution(structureId, citizenId); _structureContributions.Add(key, contribution); }
        return contribution;
    }
    private static void ClearCarriedState(Citizen citizen) { citizen.CarriedResourceQuantity = 0; citizen.CarriedResourceType = null; }
    private void CancelConstructionActionsTargeting(StructureId structureId, CitizenId completingCitizenId)
    {
        foreach (var worker in _citizens.Values.Where(x => x.Id != completingCitizenId && x.IsAlive && x.TargetStructureId == structureId && x.CurrentAction is CitizenAction.HaulConstruction or CitizenAction.Build).OrderBy(x => x.Id.Value).ToArray())
        {
            foreach (var item in _scheduledEvents.Where(e => e.Name != CitizenEventNames.SurvivalCheck && IsReservedCitizenEventFor(e, worker.Id.Value)).ToArray()) _scheduledEvents.Remove(item);
            FinishAction(worker);
            ScheduleCitizen(worker, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
        }
    }
    private static void FinishAction(Citizen citizen)
    {
        citizen.CurrentAction = CitizenAction.None;
        citizen.ActionPhase = CitizenActionPhase.None;
        citizen.ActionTarget = null;
        citizen.TargetResourceNodeId = null;
        citizen.TargetStructureId = null;
        citizen.TargetCitizenId = null;
        citizen.ActionStartedMinute = null;
        citizen.ActionCompletesMinute = null;
        ClearCarriedState(citizen);
    }
    private void StartIdleAfterFailure(Citizen citizen)
    {
        ClearCarriedState(citizen);
        citizen.TargetResourceNodeId = null;
        citizen.CurrentAction = CitizenAction.Idle;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionTarget = null;
        citizen.ActionStartedMinute = CurrentMinute;
        citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.IdleMinimumMinutes);
        ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }
    private void Deposit(Citizen citizen)
    {
        if (citizen.CarriedResourceQuantity <= 0 || citizen.CarriedResourceType is not { } type)
        {
            ClearCarriedState(citizen);
            return;
        }
        var accepted = SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion ? Math.Min(citizen.CarriedResourceQuantity, Math.Max(0, StorageCapacity - Settlement.StorageUsed)) : citizen.CarriedResourceQuantity;
        if (type == ResourceType.Food) Settlement.FoodStored = checked(Settlement.FoodStored + accepted);
        else if (type == ResourceType.Wood) Settlement.WoodStored = checked(Settlement.WoodStored + accepted);
        else Settlement.StoneStored = checked(Settlement.StoneStored + accepted);
        citizen.CarriedResourceQuantity -= accepted;
        if (citizen.CarriedResourceQuantity == 0) { ClearCarriedState(citizen); return; }
        citizen.ActionPhase = CitizenActionPhase.WaitingForStorage;
        citizen.ActionTarget = null;
        citizen.ActionStartedMinute = CurrentMinute;
        citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.StorageRetryIntervalMinutes);
        ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }
    private void ScheduleCitizen(Citizen citizen, string name, WorldMinute due, int priority) { var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(due, priority, citizen.Id.Value, seq), name, name == CitizenEventNames.SurvivalCheck ? $"{{\"citizenId\":\"{citizen.Id.Value}\"}}" : $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}"); }
    private static int RelevantSkill(CitizenAction action, Citizen citizen) => action == CitizenAction.GatherFood ? citizen.Skills.Foraging : action == CitizenAction.GatherWood ? citizen.Skills.Woodcutting : action == CitizenAction.GatherStone ? citizen.Skills.Stoneworking : action == CitizenAction.Build ? citizen.Skills.Construction : citizen.Skills.Hauling;
    private static void AddSkill(Citizen citizen, CitizenAction action, int amount) { static int SafeAdd(int current, int delta) => (int)Math.Min(int.MaxValue, (long)current + delta); if (action == CitizenAction.GatherFood) citizen.Skills.Foraging = SafeAdd(citizen.Skills.Foraging, amount); else if (action == CitizenAction.GatherWood) citizen.Skills.Woodcutting = SafeAdd(citizen.Skills.Woodcutting, amount); else if (action == CitizenAction.GatherStone) citizen.Skills.Stoneworking = SafeAdd(citizen.Skills.Stoneworking, amount); else if (action == CitizenAction.Build) citizen.Skills.Construction = SafeAdd(citizen.Skills.Construction, amount); else if (action == CitizenAction.HaulConstruction) citizen.Skills.Hauling = SafeAdd(citizen.Skills.Hauling, amount); }
    private void AddWorkMinutes(Citizen citizen, CitizenAction action, long minutes)
    {
        if (minutes <= 0) return;
        var previousOccupation = citizen.Occupation;
        if (action == CitizenAction.GatherFood) citizen.LifetimeForagingMinutes = checked(citizen.LifetimeForagingMinutes + minutes);
        else if (action == CitizenAction.GatherWood) citizen.LifetimeWoodcuttingMinutes = checked(citizen.LifetimeWoodcuttingMinutes + minutes);
        else if (action == CitizenAction.GatherStone) citizen.LifetimeStoneworkingMinutes = checked(citizen.LifetimeStoneworkingMinutes + minutes);
        else if (action == CitizenAction.Build) citizen.LifetimeConstructionMinutes = checked(citizen.LifetimeConstructionMinutes + minutes);
        else if (action == CitizenAction.HaulConstruction) citizen.LifetimeHaulingMinutes = checked(citizen.LifetimeHaulingMinutes + minutes);
        RecordSpecializationHistory(citizen, previousOccupation);
    }
    private ResourceNode? SelectResourceTarget(Citizen citizen, ResourceType type)
    { var costs = GetTravelCostsCached(citizen.Location); return World.Resources.Where(n => n.Type == type && _resourceStates.TryGetValue(n.Id.Value, out var state) && state.CurrentQuantity > 0).Select(n => (Node: n, Reachable: costs.TryGetValue(n.Coordinate, out var cost), Cost: costs.GetValueOrDefault(n.Coordinate))).Where(x => x.Reachable).OrderBy(x => x.Cost).ThenByDescending(x => _resourceStates[x.Node.Id.Value].CurrentQuantity).ThenBy(x => x.Node.Id.Value).Select(x => x.Node).FirstOrDefault(); }
    private IReadOnlyList<TileCoordinate>? FindPathCached(TileCoordinate start, TileCoordinate destination)
    {
        var key = (start, destination);
        if (_pathCache.TryGetValue(key, out var path)) return path;
        path = DeterministicPathfinder.Find(World, start, destination);
        _pathCache.Add(key, path);
        _pathCacheInsertionOrder.Enqueue(key);
        while (_pathCache.Count > PathCacheCapacity) _pathCache.Remove(_pathCacheInsertionOrder.Dequeue());
        return path;
    }
    private IReadOnlyDictionary<TileCoordinate, long> GetTravelCostsCached(TileCoordinate start)
    {
        if (_travelCostCache.TryGetValue(start, out var costs)) return costs;
        costs = DeterministicPathfinder.ComputeTravelCosts(World, start);
        _travelCostCache.Add(start, costs);
        _travelCostCacheInsertionOrder.Enqueue(start);
        while (_travelCostCache.Count > TravelCostCacheCapacity) _travelCostCache.Remove(_travelCostCacheInsertionOrder.Dequeue());
        return costs;
    }
    private void ScheduleSurvival(Citizen citizen) { var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(CurrentMinute.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes), CitizenEventNames.SurvivalPriority, citizen.Id.Value, seq), CitizenEventNames.SurvivalCheck, $"{{\"citizenId\":\"{citizen.Id.Value}\"}}"); }
    private void ScheduleResourceRegeneration() { var next = checked(((CurrentMinute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay); var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(new WorldMinute(next), CitizenEventNames.RegenerationPriority, 0, seq), CitizenEventNames.ResourceRegenerate, "{\"version\":1}"); }
    private void RegenerateResources()
    { var season = CurrentMinute.ToCalendar().Season; var foodBasis = season switch { WorldSeason.Spring => CitizenSimulationRules.FoodSpringBasisPoints, WorldSeason.Summer => CitizenSimulationRules.FoodSummerBasisPoints, WorldSeason.Autumn => CitizenSimulationRules.FoodAutumnBasisPoints, _ => CitizenSimulationRules.FoodWinterBasisPoints }; foreach (var node in World.Resources.OrderBy(x => x.Id.Value)) { var state = _resourceStates[node.Id.Value]; var amount = node.Type switch { ResourceType.Food => checked((node.RegenerationPotential * foodBasis) / 10000), ResourceType.Wood => checked(node.RegenerationPotential / CitizenSimulationRules.WoodRegenerationDivisor), _ => 0 }; state.CurrentQuantity = Math.Min(node.MaximumQuantity, checked(state.CurrentQuantity + amount)); } }
    private void EvaluateSettlementDemand()
    {
        Settlement.DemandUpdatedMinute = CurrentMinute.Value;
        if (ActiveConstructionProject is null && SelectSettlementDemand() is { } type && SelectConstructionSite() is { } site) CreateStructure(type, site);
        // Older rule versions retain their locked replay behavior. M8 enforces
        // whole-household housing at the settlement-demand boundary as well.
        if (UsesSampledShortageRecovery(SimulationRulesVersion)) ReconcileHouseholdsAndHousing();
        else ReconcileShelterAssignments();
        ScheduleSettlementDemand();
    }
    private StructureType? SelectSettlementDemand()
    {
        if (ShelterCapacity < LivingPopulation) return StructureType.Shelter;
        if (SocialSystemsEnabled(SimulationRulesVersion) && ShelterCapacity - LivingPopulation < CitizenSimulationRules.DesiredSpareShelterSlots && HasReproductionReadyHouseholdIgnoringHousing()) return StructureType.Shelter;
        if (Settlement.StorageUsed * 100 >= StorageCapacity * 80) return StructureType.Stockpile;
        return !_structures.Values.Any(x => x.Type == StructureType.Workshop && x.Status == StructureStatus.Complete) && _structures.Values.Count(x => x.Status == StructureStatus.Complete) >= 3 ? StructureType.Workshop : null;
    }
    private bool HasReproductionReadyHouseholdIgnoringHousing() => _households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value).Any(household => IsReproductionReady(household, requireDwellingCapacity: false, out _, out _));
    private TileCoordinate? SelectConstructionSite()
    {
        var occupied = _structures.Values.Select(x => x.Location).ToHashSet(); var resources = World.Resources.Select(x => x.Coordinate).ToHashSet(); var costs = GetTravelCostsCached(World.StartingSite);
        return World.Tiles.Where(x => x.Walkable && x.Buildable && x.Terrain != TerrainType.Freshwater && x.Coordinate != World.StartingSite && !occupied.Contains(x.Coordinate) && !resources.Contains(x.Coordinate) && costs.ContainsKey(x.Coordinate)).Select(x => (x.Coordinate, Cost: costs[x.Coordinate], Distance: Math.Abs(x.Coordinate.X - World.StartingSite.X) + Math.Abs(x.Coordinate.Y - World.StartingSite.Y))).OrderBy(x => x.Cost).ThenBy(x => x.Distance).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X).Select(x => (TileCoordinate?)x.Coordinate).FirstOrDefault();
    }
    private void CreateStructure(StructureType type, TileCoordinate location)
    {
        var costs = type switch { StructureType.Shelter => (CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork), StructureType.Stockpile => (CitizenSimulationRules.StockpileRequiredWood, CitizenSimulationRules.StockpileRequiredStone, CitizenSimulationRules.StockpileRequiredWork), StructureType.Workshop => (CitizenSimulationRules.WorkshopRequiredWood, CitizenSimulationRules.WorkshopRequiredStone, CitizenSimulationRules.WorkshopRequiredWork), _ => throw new ArgumentOutOfRangeException(nameof(type)) };
        var structure = new Structure(_counters.AllocateStructureId(), type, location, CurrentMinute.Value, costs.Item1, costs.Item2, costs.Item3); _structures.Add(structure.Id.Value, structure);
        RecordStructureStartedHistory(structure);
    }
    private void ReconcileShelterAssignments()
    {
        var shelters = _structures.Values.Where(x => x.Status == StructureStatus.Complete && x.Type == StructureType.Shelter).OrderBy(x => x.Id.Value).ToArray(); var usage = shelters.ToDictionary(x => x.Id.Value, _ => 0);
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive).OrderBy(x => x.Id.Value)) if (citizen.HomeStructureId is { } home && usage.TryGetValue(home.Value, out var used) && used < CitizenSimulationRules.ShelterCapacityPerBuilding) usage[home.Value] = used + 1; else SetHomeStructure(citizen, null);
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive && x.HomeStructureId is null).OrderBy(x => x.Id.Value)) { var shelter = shelters.FirstOrDefault(x => usage[x.Id.Value] < CitizenSimulationRules.ShelterCapacityPerBuilding); if (shelter is null) break; SetHomeStructure(citizen, shelter.Id); usage[shelter.Id.Value]++; }
    }
    private void ApplySurvival(Citizen citizen)
    {
        if (SimulationRulesVersion == M3SimulationRulesVersion) { ApplyM3Survival(citizen); return; }
        if (!citizen.IsAlive) return; if (CurrentMinute.Value < citizen.HealthUpdatedMinute) throw new InvalidDataException("Survival checks cannot precede the health update boundary."); var needs = citizen.GetProjectedNeeds(CurrentMinute); citizen.Needs = needs; citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        var hunger = needs.Hunger >= CitizenSimulationRules.StarvationThreshold; var rest = needs.Rest >= CitizenSimulationRules.ExhaustionThreshold; var exposure = (SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion) && CurrentMinute.Value >= Settlement.ExposureConsequencesStartMinute && needs.Shelter >= CitizenSimulationRules.ShelterCriticalThreshold;
        var damage = (hunger ? CitizenSimulationRules.StarvationDamagePerCheck : 0) + (rest ? CitizenSimulationRules.ExhaustionDamagePerCheck : 0) + (exposure ? CurrentMinute.ToCalendar().Season == WorldSeason.Winter ? CitizenSimulationRules.WinterExposureDamagePerCheck : CitizenSimulationRules.ExposureDamagePerCheck : 0);
        damage = checked(damage * (10000 - citizen.Traits.Resilience / 4) / 10000);
        if (damage > 0) citizen.Health = Math.Max(0, citizen.Health - damage); else if (needs.Hunger < CitizenSimulationRules.RecoveryHungerThreshold && needs.Rest < CitizenSimulationRules.RecoveryRestThreshold && (!(SocialSystemsEnabled(SimulationRulesVersion) || SimulationRulesVersion == M4SimulationRulesVersion) || CurrentMinute.Value < Settlement.ExposureConsequencesStartMinute || needs.Shelter < CitizenSimulationRules.RecoveryShelterThreshold)) citizen.Health = Math.Min(10000, checked(citizen.Health + CitizenSimulationRules.HealthRecoveryPerCheck + citizen.Traits.Resilience / 1000));
        citizen.HealthUpdatedMinute = CurrentMinute.Value; if (citizen.Health == 0) Kill(citizen, hunger, rest, exposure); else ScheduleSurvival(citizen);
    }
    private void ApplyM3Survival(Citizen citizen)
    {
        if (!citizen.IsAlive) return; if (CurrentMinute.Value < citizen.HealthUpdatedMinute) throw new InvalidDataException("Survival checks cannot precede the health update boundary."); var needs = citizen.GetProjectedNeeds(CurrentMinute); citizen.Needs = needs; citizen.NeedsUpdatedMinute = CurrentMinute.Value; var damage = 0; if (needs.Hunger >= CitizenSimulationRules.StarvationThreshold) damage = checked(damage + CitizenSimulationRules.StarvationDamagePerCheck); if (needs.Rest >= CitizenSimulationRules.ExhaustionThreshold) damage = checked(damage + CitizenSimulationRules.ExhaustionDamagePerCheck); var resilienceMultiplier = 10000 - (citizen.Traits.Resilience / 4); damage = checked((damage * resilienceMultiplier) / 10000); if (damage > 0) citizen.Health = Math.Max(0, citizen.Health - damage); else if (needs.Hunger < CitizenSimulationRules.RecoveryHungerThreshold && needs.Rest < CitizenSimulationRules.RecoveryRestThreshold) citizen.Health = Math.Min(10000, checked(citizen.Health + CitizenSimulationRules.HealthRecoveryPerCheck + citizen.Traits.Resilience / 1000)); citizen.NeedsUpdatedMinute = CurrentMinute.Value; citizen.HealthUpdatedMinute = CurrentMinute.Value; if (citizen.Health == 0) KillM3(citizen, needs); else ScheduleSurvival(citizen);
    }
    private void KillM3(Citizen citizen, CitizenNeeds needs) { citizen.DeathMinute = CurrentMinute.Value; citizen.DeathCause = needs.Hunger >= CitizenSimulationRules.StarvationThreshold && needs.Rest >= CitizenSimulationRules.ExhaustionThreshold ? "deprivation" : needs.Hunger >= CitizenSimulationRules.StarvationThreshold ? "starvation" : "exhaustion"; citizen.CurrentAction = CitizenAction.Dead; citizen.ActionPhase = CitizenActionPhase.None; citizen.ActionTarget = null; citizen.TargetCitizenId = null; citizen.TargetResourceNodeId = null; citizen.TargetStructureId = null; citizen.ActionStartedMinute = null; citizen.ActionCompletesMinute = null; citizen.CarriedResourceQuantity = 0; citizen.CarriedResourceType = null; foreach (var item in _scheduledEvents.Where(e => IsReservedCitizenEventFor(e, citizen.Id.Value)).ToArray()) _scheduledEvents.Remove(item); RecordDeathHistory(citizen); }
    private void Kill(Citizen citizen, bool hunger, bool rest, bool exposure) { citizen.DeathMinute = CurrentMinute.Value; var sources = (hunger ? 1 : 0) + (rest ? 1 : 0) + (exposure ? 1 : 0); citizen.DeathCause = sources > 1 ? "deprivation" : hunger ? "starvation" : rest ? "exhaustion" : "exposure"; citizen.CurrentAction = CitizenAction.Dead; citizen.ActionPhase = CitizenActionPhase.None; citizen.ActionTarget = null; citizen.TargetCitizenId = null; citizen.TargetResourceNodeId = null; citizen.TargetStructureId = null; citizen.ActionStartedMinute = null; citizen.ActionCompletesMinute = null; citizen.CarriedResourceQuantity = 0; citizen.CarriedResourceType = null; foreach (var item in _scheduledEvents.Where(e => IsReservedCitizenEventFor(e, citizen.Id.Value)).ToArray()) _scheduledEvents.Remove(item); CancelSocialActionsTargeting(citizen.Id); RecordDeathHistory(citizen); if (SocialSystemsEnabled(SimulationRulesVersion)) ReconcileM5Death(citizen); else ReconcileShelterAssignments(); }
    private void ReconcileM5Death(Citizen citizen)
    {
        if (GrowthSystemsEnabled(SimulationRulesVersion) && citizen.PartnerId is { } partnerId && _citizens.TryGetValue(partnerId.Value, out var partner) && partner.IsAlive && partner.PartnerId == citizen.Id) partner.PartnerId = null;
        // Parent, partnership, household, and home links are canonical history in M5.
        // Housing is reassigned only among living people; the deceased links remain intact.
        ReconcileHouseholdsAndHousing();
    }
    private static bool IsReservedCitizenEventFor(PendingEvent eventItem, long citizenId)
    {
        if (eventItem.Name is not (CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck)) return false;
        return SimulationPersistenceSnapshot.TryReadEventCitizenId(new ScheduledEventSnapshot(eventItem.Id, eventItem.Order, eventItem.Name, eventItem.PayloadJson), out var eventCitizenId) && eventCitizenId == citizenId;
    }
    private void AddPending(ScheduledEventId id, ScheduledEventOrder order, string name, string payload) { if (!_scheduledEvents.Add(new PendingEvent(id, order, name, payload))) throw new ArgumentException("Duplicate scheduled event ordering tuple."); }
    private static int Variation(DeterministicRandom random, Citizen citizen, ulong purpose) => (int)(random.NextUInt64(RandomDomain.DecisionVariation, (ulong)citizen.Id.Value, (ulong)citizen.ActionSequence, purpose) % 101) - 50;
    private static int ActionTieRank(CitizenAction action, bool m4Candidates) => m4Candidates ? action switch { CitizenAction.Eat => 0, CitizenAction.Rest => 1, CitizenAction.GatherFood => 2, CitizenAction.Socialize => 3, CitizenAction.HaulConstruction => 4, CitizenAction.Build => 5, CitizenAction.GatherWood => 6, CitizenAction.GatherStone => 7, CitizenAction.Explore => 8, CitizenAction.Wander => 9, CitizenAction.Idle => 10, _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Only decision actions have a tie rank.") } : action switch { CitizenAction.Eat => 0, CitizenAction.Rest => 1, CitizenAction.GatherFood => 2, CitizenAction.Socialize => 3, CitizenAction.GatherWood => 4, CitizenAction.GatherStone => 5, CitizenAction.Explore => 6, CitizenAction.Wander => 7, CitizenAction.Idle => 8, _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Only decision actions have a tie rank.") };
    internal static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => checked((long)((from.X == to.X || from.Y == to.Y) ? 10 : 14) * world.GetTile(to).MovementCost);
    internal static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world)
    { var cost = 0L; for (var index = 1; index < path.Count; index++) cost = checked(cost + StepCost(path[index - 1], path[index], world)); return cost; }
    private CitizenReadSnapshot[] CreateCitizenSnapshots() => _citizens.Values.OrderBy(x => x.Id.Value).Select(CreateCitizenSnapshot).ToArray();
    private CitizenReadSnapshot CreateCitizenSnapshot(Citizen c) => new(c.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), c.FounderOrdinal, c.GivenName, c.FamilyName, c.Name, c.AgeYears(CurrentMinute), c.LifeStage(CurrentMinute), c.Location, c.Health, c.IsAlive ? c.GetProjectedNeeds(CurrentMinute) : c.Needs, new CitizenTraits(c.Traits.Industriousness, c.Traits.Sociability, c.Traits.Curiosity, c.Traits.Cooperativeness, c.Traits.RiskTolerance, c.Traits.Resilience), new CitizenSkills(c.Skills.Foraging, c.Skills.Woodcutting, c.Skills.Stoneworking, c.Skills.Construction, c.Skills.Hauling, c.Skills.Domestic), c.CurrentAction, c.ActionStartedMinute, c.ActionCompletesMinute, c.ActionTarget, c.ActionSequence, c.IsAlive, c.DeathCause, c.CarriedResourceType, c.CarriedResourceQuantity, c.TargetResourceNodeId, c.ActionPhase, c.DeathMinute is { } death ? new WorldMinute(death) : null, c.HomeStructureId, c.TargetStructureId, c.Occupation, c.LifetimeForagingMinutes, c.LifetimeWoodcuttingMinutes, c.LifetimeStoneworkingMinutes, c.LifetimeConstructionMinutes, c.LifetimeHaulingMinutes, c.ParentAId, c.ParentBId, c.PartnerId, c.HouseholdId, _citizens.Values.Where(x => x.ParentAId == c.Id || x.ParentBId == c.Id).OrderBy(x => x.Id.Value).Select(x => x.Id.Value.ToString(CultureInfo.InvariantCulture)).ToArray(), c.TargetCitizenId, c.BirthMinute, CreateMovementPlan(c));
    private CitizenMovementPlanSnapshot? CreateMovementPlan(Citizen citizen)
    {
        if (!citizen.IsAlive || citizen.ActionTarget is not { } target || citizen.ActionPhase is not (CitizenActionPhase.TravelToTarget or CitizenActionPhase.ReturnToStockpile or CitizenActionPhase.TravelToStockpile or CitizenActionPhase.TransportToConstruction)) return null;
        var route = FindPathCached(citizen.Location, target);
        if (route is null || route.Count < 2) return null;
        if (citizen.ActionCompletesMinute is not { } completes || completes <= CurrentMinute) return null;
        var costAfterNext = 0L;
        for (var index = 2; index < route.Count; index++) costAfterNext = checked(costAfterNext + StepCost(route[index - 1], route[index], World));
        var firstArrivalValue = checked(completes.Value - costAfterNext);
        if (firstArrivalValue <= CurrentMinute.Value) return null;
        var arrival = CurrentMinute;
        var waypoints = new List<CitizenMovementWaypointSnapshot>(route.Count) { new(route[0], arrival) };
        for (var index = 1; index < route.Count; index++)
        {
            arrival = index == 1 ? new WorldMinute(firstArrivalValue) : arrival.Add(StepCost(route[index - 1], route[index], World));
            waypoints.Add(new CitizenMovementWaypointSnapshot(route[index], arrival));
        }
        var segmentStarted = new WorldMinute(checked(firstArrivalValue - StepCost(route[0], route[1], World)));
        return new CitizenMovementPlanSnapshot(citizen.ActionSequence, CurrentMinute, Array.AsReadOnly(waypoints.ToArray()), segmentStarted);
    }
    private static Citizen CloneCitizen(Citizen c) { return new Citizen(c.Id, c.FounderOrdinal, c.GivenName, c.FamilyName, c.BirthMinute, c.Location, new CitizenTraits(c.Traits.Industriousness, c.Traits.Sociability, c.Traits.Curiosity, c.Traits.Cooperativeness, c.Traits.RiskTolerance, c.Traits.Resilience), new CitizenSkills(c.Skills.Foraging, c.Skills.Woodcutting, c.Skills.Stoneworking, c.Skills.Construction, c.Skills.Hauling, c.Skills.Domestic), new CitizenNeeds(c.Needs.Hunger, c.Needs.Rest, c.Needs.Shelter, c.Needs.Social)) { Health = c.Health, CurrentAction = c.CurrentAction, ActionPhase = c.ActionPhase, ActionSequence = c.ActionSequence, ActionStartedMinute = c.ActionStartedMinute, ActionCompletesMinute = c.ActionCompletesMinute, ActionTarget = c.ActionTarget, TargetResourceNodeId = c.TargetResourceNodeId, TargetStructureId = c.TargetStructureId, TargetCitizenId = c.TargetCitizenId, CarriedResourceType = c.CarriedResourceType, CarriedResourceQuantity = c.CarriedResourceQuantity, NeedsUpdatedMinute = c.NeedsUpdatedMinute, HealthUpdatedMinute = c.HealthUpdatedMinute, LifetimeMovementSteps = c.LifetimeMovementSteps, LifetimeMovementCost = c.LifetimeMovementCost, LifetimeForagingMinutes = c.LifetimeForagingMinutes, LifetimeWoodcuttingMinutes = c.LifetimeWoodcuttingMinutes, LifetimeStoneworkingMinutes = c.LifetimeStoneworkingMinutes, LifetimeConstructionMinutes = c.LifetimeConstructionMinutes, LifetimeHaulingMinutes = c.LifetimeHaulingMinutes, DeathMinute = c.DeathMinute, DeathCause = c.DeathCause, ParentAId = c.ParentAId, ParentBId = c.ParentBId, PartnerId = c.PartnerId, HouseholdId = c.HouseholdId, HomeStructureId = c.HomeStructureId }; }
    internal static Household CloneHousehold(Household value) => new(value.Id, value.CreatedMinute) { DissolvedMinute = value.DissolvedMinute, DwellingStructureId = value.DwellingStructureId };
    private static SettlementState CloneSettlement(SettlementState state) => new(state.FoodStored, state.WoodStored, state.StoneStored, state.BaseStorageCapacity, state.DemandUpdatedMinute, state.ExposureConsequencesStartMinute);
    private static Structure CloneStructure(Structure value) => new(value.Id, value.Type, value.Location, value.ConstructionStartedMinute, value.RequiredWood, value.RequiredStone, value.RequiredWork) { Status = value.Status, CompletedMinute = value.CompletedMinute, DeliveredWood = value.DeliveredWood, DeliveredStone = value.DeliveredStone, CompletedWork = value.CompletedWork };
    private static StructureContribution CloneContribution(StructureContribution value) => new(value.StructureId, value.CitizenId, value.ConstructionWork, value.WoodDelivered, value.StoneDelivered);
    private static WorldMap CreateWorld(WorldSeed seed, string configuration) { var parsed = string.IsNullOrWhiteSpace(configuration) || configuration == "{}" ? WorldGenerationConfiguration.Default : TryParseConfiguration(configuration); return new WorldGenerator().Generate(seed, parsed); }
    private static WorldGenerationConfiguration TryParseConfiguration(string configuration) { try { return WorldGenerationConfiguration.FromCanonicalJson(configuration); } catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or NotSupportedException) { if (configuration.Contains("\"version\"", StringComparison.Ordinal)) throw; return WorldGenerationConfiguration.Default; } }
}
