using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Headless;

internal sealed record HeadlessEngineRun(SimulationEngine Engine, double ElapsedMilliseconds, int PeakLiving);

internal sealed record HeadlessSnapshotComparison(
    bool Equivalent,
    IReadOnlyList<string> Mismatches,
    string LeftFingerprint,
    string RightFingerprint);

internal static class HeadlessSnapshotComparer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static HeadlessSnapshotComparison Compare(SimulationPersistenceSnapshot left, SimulationPersistenceSnapshot right)
    {
        var leftComponents = Components(left);
        var rightComponents = Components(right);
        var mismatches = leftComponents.Keys
            .Where(key => !string.Equals(JsonSerializer.Serialize(leftComponents[key], JsonOptions), JsonSerializer.Serialize(rightComponents[key], JsonOptions), StringComparison.Ordinal))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        var leftJson = JsonSerializer.Serialize(leftComponents, JsonOptions);
        var rightJson = JsonSerializer.Serialize(rightComponents, JsonOptions);
        var leftFingerprint = Fingerprint(leftJson);
        var rightFingerprint = Fingerprint(rightJson);
        return new HeadlessSnapshotComparison(mismatches.Length == 0 && string.Equals(leftFingerprint, rightFingerprint, StringComparison.Ordinal), mismatches, leftFingerprint, rightFingerprint);
    }

    private static Dictionary<string, object?> Components(SimulationPersistenceSnapshot snapshot) => new(StringComparer.Ordinal)
    {
        ["metadata"] = new { Seed = snapshot.Seed.Value, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.CitizenGenerationVersion, snapshot.SurvivalVersion, snapshot.SettlementVersion, snapshot.SocialVersion, snapshot.HistoryVersion },
        ["counters"] = snapshot.Counters,
        ["world"] = snapshot.World,
        ["scheduledEvents"] = snapshot.ScheduledEvents.OrderBy(item => item.Order.DueWorldMinute).ThenBy(item => item.Order.Priority).ThenBy(item => item.Order.EntitySortKey).ThenBy(item => item.Order.Sequence).ToArray(),
        ["resourceStates"] = snapshot.ResourceStates.OrderBy(item => item.ResourceNodeId.Value).ToArray(),
        ["citizens"] = snapshot.Citizens.OrderBy(item => item.Id.Value).ToArray(),
        ["settlement"] = snapshot.Settlement,
        ["structures"] = snapshot.Structures.OrderBy(item => item.Id.Value).ToArray(),
        ["structureContributions"] = snapshot.StructureContributions.OrderBy(item => item.StructureId.Value).ThenBy(item => item.CitizenId.Value).ToArray(),
        ["relationships"] = snapshot.Relationships.OrderBy(item => item.CitizenAId.Value).ThenBy(item => item.CitizenBId.Value).ToArray(),
        ["households"] = snapshot.Households.OrderBy(item => item.Id.Value).ToArray(),
        ["historyState"] = snapshot.HistoryState,
        ["historicalEvents"] = snapshot.HistoricalEvents.OrderBy(item => item.Id.Value).ToArray(),
        ["historicalEventCitizens"] = snapshot.HistoricalEventCitizens.OrderBy(item => item.HistoricalEventId.Value).ThenBy(item => item.CitizenId.Value).ThenBy(item => item.Role, StringComparer.Ordinal).ToArray(),
        ["historicalEventStructures"] = snapshot.HistoricalEventStructures.OrderBy(item => item.HistoricalEventId.Value).ThenBy(item => item.StructureId.Value).ThenBy(item => item.Role, StringComparer.Ordinal).ToArray(),
        ["statisticsSamples"] = snapshot.StatisticsSamples.OrderBy(item => item.WorldMinute).ToArray(),
        ["memories"] = snapshot.Memories.OrderBy(item => item.CitizenId.Value).ThenBy(item => item.HistoricalEventId.Value).ThenBy(item => item.MemoryType).ToArray()
    };

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal static class HeadlessInvariantValidator
{
    public static IReadOnlyList<HeadlessInvariantResult> Validate(SimulationEngine engine, SimulationPersistenceSnapshot snapshot, long targetMinute)
    {
        var results = new List<HeadlessInvariantResult>
        {
            Check("final-minute", () => engine.CurrentMinute.Value == targetMinute, $"Final minute {engine.CurrentMinute.Value} does not equal target {targetMinute}."),
            Check("rules-schema-sentinels", () => SimulationEngine.IsHistoryRulesVersion(snapshot.SimulationRulesVersion) && snapshot.WorldSchemaVersion == SimulationEngine.CurrentWorldSchemaVersion && snapshot.HistoryVersion == SimulationEngine.HistoryVersion && !string.IsNullOrWhiteSpace(snapshot.ApplicationVersion), "Rules, schema, history, or application sentinel mismatch."),
            Check("persistence-snapshot", () => { SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot); _ = SimulationEngine.FromPersistenceSnapshot(snapshot); return true; }, "Persistence snapshot compatibility validation failed."),
            Check("non-negative-resources", () => { foreach (var resource in snapshot.ResourceStates) resource.Validate(snapshot.World!.Resources.Single(node => node.Id == resource.ResourceNodeId)); snapshot.Settlement!.Validate(); return true; }, "A resource or settlement quantity is negative or outside canonical bounds."),
            Check("ids-and-counters", () => ValidateIdsAndCounters(snapshot), "Canonical IDs are duplicated/invalid or counters do not exceed allocated IDs."),
            Check("parents-and-ancestry", () => ValidateParents(snapshot), "A parent is missing or ancestry contains a cycle."),
            Check("household-references", () => ValidateHouseholds(snapshot), "A household, dwelling, or citizen household reference is invalid."),
            Check("partnership-symmetry", () => ValidatePartners(snapshot), "A partnership reference is not symmetric."),
            Check("scheduled-references", () => ValidateScheduled(snapshot), "A scheduled event is malformed or references a missing citizen."),
            Check("history-links-and-sequence", () => ValidateHistory(snapshot), "History state, events, links, statistics, or memories are malformed."),
            Check("history-fingerprint", () => engine.HistoryFingerprint.Length == 64 && snapshot.HistoryState is not null, "M6 HistoryFingerprint or history state is missing.")
        };
        return results;
    }

    private static HeadlessInvariantResult Check(string name, Func<bool> predicate, string failure)
    {
        try { return predicate() ? new HeadlessInvariantResult(name, true, "PASS") : new HeadlessInvariantResult(name, false, failure); }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        { return new HeadlessInvariantResult(name, false, $"{failure} {exception.Message}"); }
    }

    private static bool ValidateIdsAndCounters(SimulationPersistenceSnapshot snapshot)
    {
        static bool UniquePositive<T>(IEnumerable<T> values, Func<T, long> id) { var ids = values.Select(id).ToArray(); return ids.All(static value => value > 0) && ids.Distinct().Count() == ids.Length; }
        var citizenIds = snapshot.Citizens.Select(item => item.Id.Value).ToHashSet();
        var structureIds = snapshot.Structures.Select(item => item.Id.Value).ToHashSet();
        var householdIds = snapshot.Households.Select(item => item.Id.Value).ToHashSet();
        var historicalIds = snapshot.HistoricalEvents.Select(item => item.Id.Value).ToArray();
        var scheduledIds = snapshot.ScheduledEvents.Select(item => item.Id.Value).ToArray();
        var relationshipPairs = snapshot.Relationships.Select(item => (A: item.CitizenAId.Value, B: item.CitizenBId.Value)).ToArray();
        var validRelationships = relationshipPairs.All(item => item.A > 0 && item.B > item.A) && relationshipPairs.Distinct().Count() == relationshipPairs.Length;
        var valid = UniquePositive(snapshot.Citizens, item => item.Id.Value) && UniquePositive(snapshot.Structures, item => item.Id.Value) && UniquePositive(snapshot.Households, item => item.Id.Value) && UniquePositive(snapshot.ResourceStates, item => item.ResourceNodeId.Value) && validRelationships && historicalIds.All(static value => value > 0) && historicalIds.Distinct().Count() == historicalIds.Length && scheduledIds.All(static value => value > 0) && scheduledIds.Distinct().Count() == scheduledIds.Length;
        var maxEntity = citizenIds.Concat(structureIds).Concat(householdIds).DefaultIfEmpty(0).Max();
        var maxHistorical = historicalIds.DefaultIfEmpty(0).Max();
        var maxScheduled = snapshot.ScheduledEvents.Select(item => item.Order.Sequence).DefaultIfEmpty(0).Max();
        return valid && snapshot.Counters.Validate().NextEntityId > maxEntity && snapshot.Counters.NextHistoricalEventId > maxHistorical && snapshot.Counters.NextScheduledEventSequence > maxScheduled;
    }

    private static bool ValidateParents(SimulationPersistenceSnapshot snapshot)
    {
        var byId = snapshot.Citizens.ToDictionary(item => item.Id.Value);
        foreach (var citizen in snapshot.Citizens)
        {
            if (citizen.ParentAId is { } parentA && !byId.ContainsKey(parentA.Value) || citizen.ParentBId is { } parentB && !byId.ContainsKey(parentB.Value)) return false;
            _ = HeadlessFactEvidence.AncestryDepth(citizen, byId);
        }
        return true;
    }

    private static bool ValidateHouseholds(SimulationPersistenceSnapshot snapshot)
    {
        var households = snapshot.Households.ToDictionary(item => item.Id.Value);
        var structures = snapshot.Structures.Select(item => item.Id.Value).ToHashSet();
        foreach (var citizen in snapshot.Citizens)
            if (citizen.HouseholdId is { } household && !households.ContainsKey(household.Value)) return false;
        foreach (var household in snapshot.Households)
            if (household.DwellingStructureId is { } dwelling && !structures.Contains(dwelling.Value) || household.DissolvedMinute is { } dissolved && dissolved < household.CreatedMinute) return false;
        return true;
    }

    private static bool ValidatePartners(SimulationPersistenceSnapshot snapshot)
    {
        var byId = snapshot.Citizens.ToDictionary(item => item.Id.Value);
        return snapshot.Citizens.All(item => item.PartnerId is null || byId.TryGetValue(item.PartnerId.Value.Value, out var partner) && partner.PartnerId == item.Id);
    }

    private static bool ValidateScheduled(SimulationPersistenceSnapshot snapshot)
    {
        var citizenIds = snapshot.Citizens.Select(item => item.Id.Value).ToHashSet();
        var knownNames = new HashSet<string>(StringComparer.Ordinal) { CitizenEventNames.Decision, CitizenEventNames.MoveStep, CitizenEventNames.ActionComplete, CitizenEventNames.SurvivalCheck, CitizenEventNames.ResourceRegenerate, CitizenEventNames.SettlementEvaluateDemand, CitizenEventNames.FamilyCheck, CitizenEventNames.LifecycleCheck, CitizenEventNames.StatisticsSample };
        foreach (var item in snapshot.ScheduledEvents)
        {
            item.Validate();
            if (!knownNames.Contains(item.Name)) return false;
            if (item.Order.EntitySortKey > 0 && !citizenIds.Contains(item.Order.EntitySortKey)) return false;
        }
        return true;
    }

    private static bool ValidateHistory(SimulationPersistenceSnapshot snapshot)
    {
        var events = snapshot.HistoricalEvents;
        if (snapshot.HistoryState is null) return false;
        snapshot.HistoryState.Validate(snapshot.WorldMinute.Value);
        if (events.Count == 0 || events.Select(item => item.Id.Value).Distinct().Count() != events.Count) return false;
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            item.Validate(snapshot.WorldMinute.Value);
            if (index > 0 && item.Id.Value != events[index - 1].Id.Value + 1) return false;
            var citizenLinks = snapshot.HistoricalEventCitizens.Where(link => link.HistoricalEventId == item.Id).ToArray();
            var structureLinks = snapshot.HistoricalEventStructures.Where(link => link.HistoricalEventId == item.Id).ToArray();
            item.ValidateLinks(citizenLinks, structureLinks);
        }
        foreach (var link in snapshot.HistoricalEventCitizens) link.Validate();
        foreach (var link in snapshot.HistoricalEventStructures) link.Validate();
        foreach (var sample in snapshot.StatisticsSamples) sample.Validate(snapshot.WorldMinute.Value);
        foreach (var memory in snapshot.Memories) memory.Validate(snapshot.WorldMinute.Value);
        return true;
    }
}

internal static class HeadlessFactEvidence
{
    public static int AncestryDepth(Citizen citizen, IReadOnlyDictionary<long, Citizen> citizens) => Depth(citizen, citizens, new HashSet<long>());

    private static int Depth(Citizen citizen, IReadOnlyDictionary<long, Citizen> citizens, HashSet<long> path)
    {
        if (!path.Add(citizen.Id.Value)) throw new InvalidDataException("Ancestry contains a cycle.");
        var parents = new[] { citizen.ParentAId, citizen.ParentBId }.Where(item => item is not null).Select(item => item!.Value.Value).Distinct().ToArray();
        var depth = parents.Length == 0 ? 0 : 1 + parents.Select(parent => citizens.TryGetValue(parent, out var value) ? Depth(value, citizens, new HashSet<long>(path)) : throw new InvalidDataException("Ancestry references a missing parent.")).Max();
        return depth;
    }

    public static IReadOnlyList<HeadlessEvidence> Build(SimulationPersistenceSnapshot snapshot)
    {
        var citizens = snapshot.Citizens.ToDictionary(item => item.Id.Value);
        var events = snapshot.HistoricalEvents;
        var evidence = new List<HeadlessEvidence>
        {
            Evidence("longest-lived", snapshot.Citizens.Where(item => item.DeathMinute is not null).OrderByDescending(item => item.DeathMinute!.Value - item.BirthMinute).ThenBy(item => item.Id.Value).Select(item => $"citizen {item.Id.Value} lived {item.DeathMinute!.Value - item.BirthMinute} minutes").FirstOrDefault()),
            Evidence("most-children", events.Where(item => item.EventType == HistoricalEventType.CitizenBorn).SelectMany(item => snapshot.HistoricalEventCitizens.Where(link => link.HistoricalEventId == item.Id && link.Role == "parent")).GroupBy(item => item.CitizenId.Value).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).Select(group => $"citizen {group.Key} has {group.Count()} recorded children").FirstOrDefault()),
            Evidence("top-contributor-builder", snapshot.Citizens.OrderByDescending(item => item.LifetimeConstructionMinutes + item.LifetimeHaulingMinutes).ThenBy(item => item.Id.Value).Select(item => $"citizen {item.Id.Value} contributed {item.LifetimeConstructionMinutes + item.LifetimeHaulingMinutes} construction/hauling minutes").FirstOrDefault()),
            Evidence("family", snapshot.Households.OrderBy(item => item.Id.Value).Select(item => $"household {item.Id.Value} exists").FirstOrDefault()),
            Evidence("population-milestone", events.Where(item => item.EventType == HistoricalEventType.PopulationMilestone).OrderBy(item => item.WorldMinute).ThenBy(item => item.Id.Value).Select(item => $"minute {item.WorldMinute}: {item.SummaryPayload()}").FirstOrDefault()),
            Evidence("shortage-recovery", events.Where(item => item.EventType == HistoricalEventType.ResourceShortageEnded).OrderBy(item => item.WorldMinute).ThenBy(item => item.Id.Value).Select(item => $"shortage recovered at minute {item.WorldMinute}").FirstOrDefault()),
            Evidence("notable-social-transition", events.Where(item => item.EventType is HistoricalEventType.PartnershipFormed or HistoricalEventType.FriendshipFormed or HistoricalEventType.RivalryFormed).OrderBy(item => item.WorldMinute).ThenBy(item => item.Id.Value).Select(item => $"{item.EventType} at minute {item.WorldMinute}").FirstOrDefault())
        };
        return evidence;
    }

    private static HeadlessEvidence Evidence(string name, string? details) => new(name, details ?? "none observed");
}

internal static class HistoricalEventEvidenceExtensions
{
    public static string SummaryPayload(this HistoricalEvent item) => item.PayloadJson;
}
