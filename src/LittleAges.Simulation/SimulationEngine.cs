using LittleAges.Domain;
using System.Text.Json;

namespace LittleAges.Simulation;

public static class CitizenEventNames
{
    public const string Decision = "citizen.decision.v1";
    public const string MoveStep = "citizen.move-step.v1";
    public const string ActionComplete = "citizen.action-complete.v1";
    public const int MovementPriority = 10;
    public const int CompletionPriority = 15;
    public const int DecisionPriority = 20;
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
            if (Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 || !root.TryGetProperty("citizenId", out var id) || id.ValueKind != JsonValueKind.String || !long.TryParse(id.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0 || parsedId.ToString(System.Globalization.CultureInfo.InvariantCulture) != id.GetString() || !root.TryGetProperty("actionSequence", out var sequence) || !sequence.TryGetInt64(out var parsedSequence) || parsedSequence < 0 || Order.EntitySortKey != parsedId) throw new ArgumentException("Citizen event payload is not canonical.", nameof(PayloadJson));
            }
        }
        catch (JsonException exception) { throw new ArgumentException("Event payload must be valid JSON.", nameof(PayloadJson), exception); }
        return this;
    }
}
public sealed record SyntheticEventExecution(ScheduledEventId Id, ScheduledEventOrder Order, string Name, string PayloadJson = "{}");
public sealed record CitizenReadSnapshot(string CitizenId, int FounderOrdinal, string GivenName, string FamilyName, string Name, int Age, string LifeStage, TileCoordinate Location, int Health, CitizenNeeds ProjectedNeeds, CitizenTraits Traits, CitizenSkills Skills, CitizenAction CurrentAction, WorldMinute? ActionStartedMinute, WorldMinute? ActionCompletesMinute, TileCoordinate? Target, long ActionSequence);
public sealed record CitizenDecisionEvaluation(CitizenAction Action, int BaseUtility, int NeedContribution, int TraitContribution, int Variation, int FinalScore);

public sealed record SimulationStatusSnapshot
{
    public SimulationStatusSnapshot(WorldSeed seed, WorldMinute worldMinute, int pendingEventCount, int processedEventCount, IReadOnlyList<SyntheticEventExecution> processedEvents, WorldMap? world = null, IReadOnlyList<CitizenReadSnapshot>? citizens = null)
    { Seed = seed; WorldMinute = worldMinute; PendingEventCount = pendingEventCount; ProcessedEventCount = processedEventCount; ProcessedEvents = Array.AsReadOnly(processedEvents.ToArray()); World = world; Citizens = Array.AsReadOnly((citizens ?? Array.Empty<CitizenReadSnapshot>()).ToArray()); }
    public WorldSeed Seed { get; } public WorldMinute WorldMinute { get; } public int PendingEventCount { get; } public int ProcessedEventCount { get; } public IReadOnlyList<SyntheticEventExecution> ProcessedEvents { get; } public WorldMap? World { get; } public IReadOnlyList<CitizenReadSnapshot> Citizens { get; }
}

public sealed record SimulationPersistenceSnapshot
{
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents) : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, null, null, 0) { }
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents, WorldMap? world) : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, world, null, 0) { }
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents, WorldMap? world, IReadOnlyList<Citizen>? citizens, int citizenGenerationVersion = 0)
    {
        Seed = seed; WorldMinute = worldMinute; WorldSchemaVersion = RequireMetadata(worldSchemaVersion, nameof(worldSchemaVersion)); SimulationRulesVersion = RequireMetadata(simulationRulesVersion, nameof(simulationRulesVersion)); ApplicationVersion = RequireMetadata(applicationVersion, nameof(applicationVersion)); WorldConfiguration = worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration)); Counters = counters.Validate();
        var events = scheduledEvents.Select(static x => x.Validate()).ToArray();
        if (events.Select(x => x.Id.Value).Distinct().Count() != events.Length || events.Select(x => x.Order.Sequence).Distinct().Count() != events.Length) throw new ArgumentException("Queued event identities must be unique.", nameof(scheduledEvents));
        if (events.Any(x => x.Order.Sequence >= Counters.NextScheduledEventSequence)) throw new ArgumentException("The next scheduled sequence must exceed queued events.", nameof(counters));
        if (citizenGenerationVersion is not (0 or 1)) throw new NotSupportedException($"Citizen generation version '{citizenGenerationVersion}' is not supported.");
        if (citizenGenerationVersion == 0 && simulationRulesVersion == SimulationEngine.CurrentSimulationRulesVersion) throw new ArgumentException("M2 rules require the citizen generation sentinel.", nameof(citizenGenerationVersion));
        ScheduledEvents = Array.AsReadOnly(events); World = world; Citizens = Array.AsReadOnly((citizens ?? Array.Empty<Citizen>()).OrderBy(x => x.Id.Value).ToArray()); CitizenGenerationVersion = citizenGenerationVersion;
        if (citizenGenerationVersion == 1) ValidateM2Roster(Citizens, ScheduledEvents, world, worldMinute);
    }
    public WorldSeed Seed { get; } public WorldMinute WorldMinute { get; } public string WorldSchemaVersion { get; } public string SimulationRulesVersion { get; } public string ApplicationVersion { get; } public string WorldConfiguration { get; } public DeterministicCountersSnapshot Counters { get; } public IReadOnlyList<ScheduledEventSnapshot> ScheduledEvents { get; } public WorldMap? World { get; } public IReadOnlyList<Citizen> Citizens { get; } public int CitizenGenerationVersion { get; }
    private static void ValidateM2Roster(IReadOnlyList<Citizen> citizens, IReadOnlyList<ScheduledEventSnapshot> events, WorldMap? world, WorldMinute minute)
    {
        if (citizens.Count != CitizenGenerator.FounderCount || !citizens.Select(c => c.FounderOrdinal).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, CitizenGenerator.FounderCount))) throw new ArgumentException("M2 requires exactly 20 founders with ordinals 0..19.", nameof(citizens));
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

            var matching = reservedEvents.Where(e => e.Order.EntitySortKey == citizen.Id.Value).ToArray();
            if (matching.Length != 1) throw new ArgumentException("M2 requires one next event per citizen.", nameof(events));
            var scheduled = matching[0];
            var payloadValid = TryReadCitizenPayload(scheduled.PayloadJson, out var payload);
            if (scheduled.Name != expectedName || scheduled.Order.Priority != expectedPriority || scheduled.Order.DueWorldMinute != expectedDue || !payloadValid || payload.Id != citizen.Id.Value || payload.Sequence != citizen.ActionSequence) throw new ArgumentException($"M2 citizen event does not match citizen state: id={citizen.Id.Value}, action={citizen.CurrentAction}, due={scheduled.Order.DueWorldMinute.Value}/{expectedDue.Value}, priority={scheduled.Order.Priority}/{expectedPriority}, sequence={payload.Sequence}/{citizen.ActionSequence}, name={scheduled.Name}/{expectedName}.", nameof(events));
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
    private static string RequireMetadata(string value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Metadata must not be empty.", parameterName) : value;
}

public sealed class SimulationEngine
{
    public const string CurrentWorldSchemaVersion = "0.1";
    public const string CurrentSimulationRulesVersion = "m2-rng1-citizen1";
    public const int CitizenGenerationVersion = 1;
    private sealed record PendingEvent(ScheduledEventId Id, ScheduledEventOrder Order, string Name, string PayloadJson);
    private readonly SortedSet<PendingEvent> _scheduledEvents = new(Comparer<PendingEvent>.Create(static (a, b) => a.Order.CompareTo(b.Order)));
    private readonly Queue<SyntheticEventExecution> _processedEvents = new();
    private const int DiagnosticCapacity = 32;
    private readonly Dictionary<long, Citizen> _citizens = new();
    private readonly DeterministicCounters _counters;
    private int _processedEventCount;
    public SimulationEngine(WorldSeed seed, WorldMinute initialMinute = default, string worldSchemaVersion = CurrentWorldSchemaVersion, string simulationRulesVersion = CurrentSimulationRulesVersion, string applicationVersion = "0.1.0", string worldConfiguration = "{}")
    { if (initialMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(initialMinute)); Seed = seed; CurrentMinute = initialMinute; WorldSchemaVersion = worldSchemaVersion; SimulationRulesVersion = simulationRulesVersion; ApplicationVersion = applicationVersion; _counters = new DeterministicCounters(); World = CreateWorld(seed, worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration))); WorldConfiguration = World.Configuration.CanonicalJson; if (simulationRulesVersion == CurrentSimulationRulesVersion) GenerateFounders(); }
    public SimulationEngine(SimulationPersistenceSnapshot snapshot)
    { ArgumentNullException.ThrowIfNull(snapshot); ValidatePersistenceSnapshotCompatibility(snapshot); Seed = snapshot.Seed; CurrentMinute = snapshot.WorldMinute; WorldSchemaVersion = snapshot.WorldSchemaVersion; SimulationRulesVersion = snapshot.SimulationRulesVersion; ApplicationVersion = snapshot.ApplicationVersion; WorldConfiguration = snapshot.WorldConfiguration; _counters = new DeterministicCounters(snapshot.Counters); World = snapshot.World ?? CreateWorld(snapshot.Seed, snapshot.WorldConfiguration); foreach (var citizen in snapshot.Citizens) _citizens.Add(citizen.Id.Value, CloneCitizen(citizen)); foreach (var item in snapshot.ScheduledEvents) { if (item.Order.DueWorldMinute < CurrentMinute) throw new ArgumentException("A restored scheduled event cannot be due in the past.", nameof(snapshot)); AddPending(item.Id, item.Order, item.Name, item.PayloadJson); } if (snapshot.CitizenGenerationVersion == CitizenGenerationVersion && _citizens.Count == 0) throw new InvalidDataException("An M2 snapshot must contain citizens."); }
    public WorldSeed Seed { get; } public WorldMinute CurrentMinute { get; private set; } public string WorldSchemaVersion { get; } public string SimulationRulesVersion { get; } public string ApplicationVersion { get; } public string WorldConfiguration { get; } public WorldMap World { get; } public int PendingEventCount => _scheduledEvents.Count; public int ProcessedEventCount => _processedEventCount; public DeterministicCountersSnapshot CounterSnapshot => _counters.Snapshot; public int Population => _citizens.Count; public IReadOnlyList<Citizen> Citizens => Array.AsReadOnly(_citizens.Values.OrderBy(x => x.Id.Value).Select(CloneCitizen).ToArray()); public Citizen? GetCitizen(CitizenId id) => _citizens.TryGetValue(id.Value, out var citizen) ? CloneCitizen(citizen) : null;
    public IReadOnlyList<CitizenDecisionEvaluation> EvaluateDecision(CitizenId id)
    {
        if (!_citizens.TryGetValue(id.Value, out var citizen)) return Array.Empty<CitizenDecisionEvaluation>();
        return EvaluateDecision(citizen);
    }
    internal static CitizenAction SelectDecision(IReadOnlyList<CitizenDecisionEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        if (evaluations.Count == 0) throw new ArgumentException("At least one decision evaluation is required.", nameof(evaluations));
        return evaluations.OrderByDescending(evaluation => evaluation.FinalScore).ThenBy(evaluation => ActionTieRank(evaluation.Action)).First().Action;
    }
    private IReadOnlyList<CitizenDecisionEvaluation> EvaluateDecision(Citizen citizen)
    {
        var needs = citizen.GetProjectedNeeds(CurrentMinute); var random = new DeterministicRandom(Seed);
        var v1 = Variation(random, citizen, 1); var v2 = Variation(random, citizen, 2); var v3 = Variation(random, citizen, 3); var v4 = Variation(random, citizen, 4);
        return [new(CitizenAction.Rest, 0, needs.Rest * CitizenSimulationRules.RestUtilityWeight, 0, v1, needs.Rest * CitizenSimulationRules.RestUtilityWeight + v1), new(CitizenAction.Explore, CitizenSimulationRules.ExploreBaseUtility, 0, citizen.Traits.Curiosity / 4 + citizen.Traits.RiskTolerance / 8, v2, CitizenSimulationRules.ExploreBaseUtility + citizen.Traits.Curiosity / 4 + citizen.Traits.RiskTolerance / 8 + v2), new(CitizenAction.Wander, CitizenSimulationRules.WanderBaseUtility, 0, citizen.Traits.Curiosity / 10, v3, CitizenSimulationRules.WanderBaseUtility + citizen.Traits.Curiosity / 10 + v3), new(CitizenAction.Idle, CitizenSimulationRules.IdleBaseUtility, 0, (10000 - citizen.Traits.Industriousness) / 20, v4, CitizenSimulationRules.IdleBaseUtility + (10000 - citizen.Traits.Industriousness) / 20 + v4)];
    }
    public ScheduledEventId ScheduleSyntheticEvent(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name) => ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name, "{}");
    public ScheduledEventId ScheduleSyntheticEvent(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name, string payloadJson)
    { if (name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) throw new ArgumentException("Reserved citizen event names may only be scheduled by the citizen runtime.", nameof(name)); ArgumentOutOfRangeException.ThrowIfLessThan(dueWorldMinute, CurrentMinute); var sequence = _counters.AllocateScheduledEventSequence(); var id = new ScheduledEventId(sequence); AddPending(id, new ScheduledEventOrder(dueWorldMinute, priority, entitySortKey, sequence), name, payloadJson); return id; }
    public ScheduledEventId Schedule(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name) => ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name);
    public bool ProcessNextEvent()
    { if (_scheduledEvents.Count == 0) return false; var next = _scheduledEvents.Min!; _scheduledEvents.Remove(next); CurrentMinute = CurrentMinute.AdvanceTo(next.Order.DueWorldMinute); _processedEventCount++; if (next.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) DispatchCitizenEvent(next); else { if (_processedEvents.Count == DiagnosticCapacity) _processedEvents.Dequeue(); _processedEvents.Enqueue(new SyntheticEventExecution(next.Id, next.Order, next.Name, next.PayloadJson)); } return true; }
    public int AdvanceEvents(int maximumEvents) { ArgumentOutOfRangeException.ThrowIfNegative(maximumEvents); var count = 0; while (count < maximumEvents && ProcessNextEvent()) count++; return count; }
    public int AdvanceUntil(WorldMinute targetMinute) { ArgumentOutOfRangeException.ThrowIfLessThan(targetMinute, CurrentMinute); var count = 0; while (_scheduledEvents.Count > 0 && _scheduledEvents.Min!.Order.DueWorldMinute <= targetMinute) { ProcessNextEvent(); count++; } CurrentMinute = CurrentMinute.AdvanceTo(targetMinute); return count; }
    public SimulationStatusSnapshot CreateReadSnapshot() => new(Seed, CurrentMinute, PendingEventCount, ProcessedEventCount, _processedEvents.ToArray(), World, CreateCitizenSnapshots());
    public SimulationStatusSnapshot CreateStatusSnapshot() => CreateReadSnapshot();
    public SimulationPersistenceSnapshot CreatePersistenceSnapshot() => new(Seed, CurrentMinute, WorldSchemaVersion, SimulationRulesVersion, ApplicationVersion, WorldConfiguration, _counters.Snapshot, _scheduledEvents.Select(x => new ScheduledEventSnapshot(x.Id, x.Order, x.Name, x.PayloadJson)).ToArray(), World, _citizens.Values.Select(CloneCitizen).ToArray(), SimulationRulesVersion == CurrentSimulationRulesVersion ? CitizenGenerationVersion : 0);
    public static SimulationEngine FromPersistenceSnapshot(SimulationPersistenceSnapshot snapshot) => new(snapshot);
    public static void ValidatePersistenceSnapshotCompatibility(SimulationPersistenceSnapshot snapshot) { if (snapshot.WorldSchemaVersion != CurrentWorldSchemaVersion) throw new NotSupportedException($"World schema version '{snapshot.WorldSchemaVersion}' is not supported; expected '{CurrentWorldSchemaVersion}'."); if (snapshot.SimulationRulesVersion is not ("m0-rng1" or "m2-rng1-citizen1")) throw new NotSupportedException($"Simulation rules version '{snapshot.SimulationRulesVersion}' is not supported."); if (snapshot.CitizenGenerationVersion == CitizenGenerationVersion && snapshot.SimulationRulesVersion != CurrentSimulationRulesVersion) throw new NotSupportedException("M2 citizens require the M2 simulation rules version."); }
    private void GenerateFounders() { foreach (var citizen in CitizenGenerator.Generate(Seed, World, _counters, CurrentMinute)) _citizens.Add(citizen.Id.Value, citizen); foreach (var citizen in _citizens.Values) ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); }
    private void DispatchCitizenEvent(PendingEvent item)
    { using var doc = JsonDocument.Parse(item.PayloadJson); var root = doc.RootElement; if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 || !root.TryGetProperty("citizenId", out var idElement) || idElement.ValueKind != JsonValueKind.String || !long.TryParse(idElement.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(System.Globalization.CultureInfo.InvariantCulture) != idElement.GetString() || !_citizens.TryGetValue(id, out var citizen)) throw new InvalidDataException("Citizen event references an unknown citizen."); if (!root.TryGetProperty("actionSequence", out var seqElement) || !seqElement.TryGetInt64(out var sequence) || sequence < 0) throw new InvalidDataException("Citizen event has no action sequence."); if (item.Order.EntitySortKey != id || sequence != citizen.ActionSequence) throw new InvalidDataException("Citizen event identity or action sequence is stale."); if (item.Name == CitizenEventNames.Decision) { if (citizen.CurrentAction != CitizenAction.None) throw new InvalidDataException("A decision event requires a citizen at a decision boundary."); Decide(citizen); } else if (item.Name == CitizenEventNames.ActionComplete) { if (citizen.CurrentAction is not (CitizenAction.Idle or CitizenAction.Rest)) throw new InvalidDataException("An action-complete event does not match citizen state."); CompleteAction(citizen); } else MoveStep(citizen); }
    private void Decide(Citizen citizen)
    { var needs = citizen.GetProjectedNeeds(CurrentMinute); citizen.Needs = needs; citizen.NeedsUpdatedMinute = CurrentMinute.Value; citizen.ActionSequence = checked(citizen.ActionSequence + 1); var action = SelectDecision(EvaluateDecision(citizen)); var random = new DeterministicRandom(Seed); if (action is CitizenAction.Wander or CitizenAction.Explore) { var target = CitizenGenerator.SelectTarget(Seed, World, citizen, action == CitizenAction.Wander ? CitizenSimulationRules.WanderRadius : CitizenSimulationRules.ExploreRadius); if (target is null) action = CitizenAction.Idle; else { citizen.ActionTarget = target; citizen.CurrentAction = action; citizen.ActionStartedMinute = CurrentMinute; var route = DeterministicPathfinder.Find(World, citizen.Location, target.Value); var firstStep = route is { Count: > 1 } ? StepCost(route[0], route[1], World) : 10; citizen.ActionCompletesMinute = CurrentMinute.Add(route is null ? firstStep : RemainingPathCost(route, World)); ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(firstStep), CitizenEventNames.MovementPriority); return; } } citizen.CurrentAction = action; citizen.ActionTarget = null; citizen.ActionStartedMinute = CurrentMinute; var duration = action == CitizenAction.Rest ? CitizenSimulationRules.RestDurationMinutes : CitizenSimulationRules.IdleMinimumMinutes + (int)(random.NextUInt64(RandomDomain.DecisionVariation, (ulong)citizen.Id.Value, (ulong)citizen.ActionSequence, 5) % (CitizenSimulationRules.IdleMaximumMinutes - CitizenSimulationRules.IdleMinimumMinutes + 1)); citizen.ActionCompletesMinute = CurrentMinute.Add(duration); if (action == CitizenAction.Rest) citizen.Needs = new CitizenNeeds(needs.Hunger, Math.Max(0, needs.Rest - 1000), needs.Shelter, needs.Social); ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority); }
    private void MoveStep(Citizen citizen)
    { if (citizen.ActionTarget is not { } target || citizen.CurrentAction is not (CitizenAction.Wander or CitizenAction.Explore)) throw new InvalidDataException("Movement event does not match citizen action."); var path = DeterministicPathfinder.Find(World, citizen.Location, target); if (path is null || path.Count < 2) { if (citizen.Location == target) CompleteAction(citizen); else { citizen.CurrentAction = CitizenAction.Idle; citizen.ActionTarget = null; citizen.ActionStartedMinute = CurrentMinute; citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.IdleMinimumMinutes); ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority); } return; } var next = path[1]; var stepCost = StepCost(path[0], next, World); citizen.Location = next; citizen.LifetimeMovementSteps = checked(citizen.LifetimeMovementSteps + 1); citizen.LifetimeMovementCost = checked(citizen.LifetimeMovementCost + stepCost); if (next == target) CompleteAction(citizen); else { var remainingPath = DeterministicPathfinder.Find(World, citizen.Location, target) ?? throw new InvalidDataException("A movement route became unreachable after completing a valid step."); var nextStepCost = StepCost(remainingPath[0], remainingPath[1], World); citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(remainingPath, World)); ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(nextStepCost), CitizenEventNames.MovementPriority); } }
    private void CompleteAction(Citizen citizen) { citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute); citizen.NeedsUpdatedMinute = CurrentMinute.Value; citizen.CurrentAction = CitizenAction.None; citizen.ActionStartedMinute = null; citizen.ActionCompletesMinute = null; citizen.ActionTarget = null; ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); }
    private void ScheduleCitizen(Citizen citizen, string name, WorldMinute due, int priority) { var seq = _counters.AllocateScheduledEventSequence(); var id = new ScheduledEventId(seq); AddPending(id, new ScheduledEventOrder(due, priority, citizen.Id.Value, seq), name, $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}"); }
    private void AddPending(ScheduledEventId id, ScheduledEventOrder order, string name, string payload) { if (!_scheduledEvents.Add(new PendingEvent(id, order, name, payload))) throw new ArgumentException("Duplicate scheduled event ordering tuple."); }
    private static int Variation(DeterministicRandom random, Citizen citizen, ulong purpose) => (int)(random.NextUInt64(RandomDomain.DecisionVariation, (ulong)citizen.Id.Value, (ulong)citizen.ActionSequence, purpose) % 101) - 50;
    private static int ActionTieRank(CitizenAction action) => action switch { CitizenAction.Rest => 0, CitizenAction.Explore => 1, CitizenAction.Wander => 2, CitizenAction.Idle => 3, _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Only decision actions have a tie rank.") };
    internal static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => checked((long)((from.X == to.X || from.Y == to.Y) ? 10 : 14) * world.GetTile(to).MovementCost);
    internal static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world)
    { var cost = 0L; for (var index = 1; index < path.Count; index++) cost = checked(cost + StepCost(path[index - 1], path[index], world)); return cost; }
    private CitizenReadSnapshot[] CreateCitizenSnapshots() => _citizens.Values.OrderBy(x => x.Id.Value).Select(c => new CitizenReadSnapshot(c.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), c.FounderOrdinal, c.GivenName, c.FamilyName, c.Name, c.AgeYears(CurrentMinute), c.LifeStage(CurrentMinute), c.Location, c.Health, c.GetProjectedNeeds(CurrentMinute), c.Traits, c.Skills, c.CurrentAction, c.ActionStartedMinute, c.ActionCompletesMinute, c.ActionTarget, c.ActionSequence)).ToArray();
    private static Citizen CloneCitizen(Citizen c) { return new Citizen(c.Id, c.FounderOrdinal, c.GivenName, c.FamilyName, c.BirthMinute, c.Location, c.Traits, c.Skills, c.Needs) { CurrentAction = c.CurrentAction, ActionSequence = c.ActionSequence, ActionStartedMinute = c.ActionStartedMinute, ActionCompletesMinute = c.ActionCompletesMinute, ActionTarget = c.ActionTarget, NeedsUpdatedMinute = c.NeedsUpdatedMinute, LifetimeMovementSteps = c.LifetimeMovementSteps, LifetimeMovementCost = c.LifetimeMovementCost, DeathMinute = c.DeathMinute, DeathCause = c.DeathCause, ParentAId = c.ParentAId, ParentBId = c.ParentBId, PartnerId = c.PartnerId, HouseholdId = c.HouseholdId, HomeStructureId = c.HomeStructureId }; }
    private static WorldMap CreateWorld(WorldSeed seed, string configuration) { var parsed = string.IsNullOrWhiteSpace(configuration) || configuration == "{}" ? WorldGenerationConfiguration.Default : TryParseConfiguration(configuration); return new WorldGenerator().Generate(seed, parsed); }
    private static WorldGenerationConfiguration TryParseConfiguration(string configuration) { try { return WorldGenerationConfiguration.FromCanonicalJson(configuration); } catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or NotSupportedException) { if (configuration.Contains("\"version\"", StringComparison.Ordinal)) throw; return WorldGenerationConfiguration.Default; } }
}
