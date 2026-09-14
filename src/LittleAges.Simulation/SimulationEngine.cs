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
    public const int MovementPriority = 10;
    public const int CompletionPriority = 15;
    public const int DecisionPriority = 20;
    public const int SurvivalPriority = 18;
    public const int RegenerationPriority = 5;
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
public sealed record CitizenReadSnapshot(string CitizenId, int FounderOrdinal, string GivenName, string FamilyName, string Name, int Age, string LifeStage, TileCoordinate Location, int Health, CitizenNeeds ProjectedNeeds, CitizenTraits Traits, CitizenSkills Skills, CitizenAction CurrentAction, WorldMinute? ActionStartedMinute, WorldMinute? ActionCompletesMinute, TileCoordinate? Target, long ActionSequence, bool IsAlive = true, string? DeathCause = null, ResourceType? CarriedResourceType = null, int CarriedResourceQuantity = 0, ResourceNodeId? TargetResourceNodeId = null, CitizenActionPhase ActionPhase = CitizenActionPhase.None, WorldMinute? DeathMinute = null);
public sealed record CitizenDecisionEvaluation(CitizenAction Action, int BaseUtility, int NeedContribution, int TraitContribution, int Variation, int FinalScore, int SkillContribution = 0, int StockpileContribution = 0, int TravelPenalty = 0);

public sealed record SimulationStatusSnapshot
{
    public SimulationStatusSnapshot(WorldSeed seed, WorldMinute worldMinute, int pendingEventCount, int processedEventCount, IReadOnlyList<SyntheticEventExecution> processedEvents, WorldMap? world = null, IReadOnlyList<CitizenReadSnapshot>? citizens = null, SettlementState? settlement = null, IReadOnlyList<ResourceState>? resourceStates = null)
    { Seed = seed; WorldMinute = worldMinute; PendingEventCount = pendingEventCount; ProcessedEventCount = processedEventCount; ProcessedEvents = Array.AsReadOnly(processedEvents.ToArray()); World = world; Citizens = Array.AsReadOnly((citizens ?? Array.Empty<CitizenReadSnapshot>()).ToArray()); Settlement = settlement is null ? null : new SettlementState(settlement.FoodStored, settlement.WoodStored, settlement.StoneStored); ResourceStates = Array.AsReadOnly((resourceStates ?? Array.Empty<ResourceState>()).Select(x => new ResourceState(x.ResourceNodeId, x.CurrentQuantity)).ToArray()); }
    public WorldSeed Seed { get; } public WorldMinute WorldMinute { get; } public int PendingEventCount { get; } public int ProcessedEventCount { get; } public IReadOnlyList<SyntheticEventExecution> ProcessedEvents { get; } public WorldMap? World { get; } public IReadOnlyList<CitizenReadSnapshot> Citizens { get; } public SettlementState? Settlement { get; } public IReadOnlyList<ResourceState> ResourceStates { get; }
}

public sealed record SimulationPersistenceSnapshot
{
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents) : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, null, null, 0) { }
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents, WorldMap? world) : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, world, null, 0) { }
    public SimulationPersistenceSnapshot(WorldSeed seed, WorldMinute worldMinute, string worldSchemaVersion, string simulationRulesVersion, string applicationVersion, string worldConfiguration, DeterministicCountersSnapshot counters, IReadOnlyList<ScheduledEventSnapshot> scheduledEvents, WorldMap? world, IReadOnlyList<Citizen>? citizens, int citizenGenerationVersion = 0, IReadOnlyList<ResourceState>? resourceStates = null, SettlementState? settlement = null, int survivalVersion = 0)
    {
        Seed = seed; WorldMinute = worldMinute; WorldSchemaVersion = RequireMetadata(worldSchemaVersion, nameof(worldSchemaVersion)); SimulationRulesVersion = RequireMetadata(simulationRulesVersion, nameof(simulationRulesVersion)); ApplicationVersion = RequireMetadata(applicationVersion, nameof(applicationVersion)); WorldConfiguration = worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration)); Counters = counters.Validate();
        var events = scheduledEvents.Select(static x => x.Validate()).ToArray();
        if (events.Select(x => x.Id.Value).Distinct().Count() != events.Length || events.Select(x => x.Order.Sequence).Distinct().Count() != events.Length) throw new ArgumentException("Queued event identities must be unique.", nameof(scheduledEvents));
        if (events.Any(x => x.Order.Sequence >= Counters.NextScheduledEventSequence)) throw new ArgumentException("The next scheduled sequence must exceed queued events.", nameof(counters));
        if (citizenGenerationVersion is not (0 or 1)) throw new NotSupportedException($"Citizen generation version '{citizenGenerationVersion}' is not supported.");
        if (survivalVersion is not (0 or SimulationEngine.SurvivalVersion)) throw new NotSupportedException($"Survival version '{survivalVersion}' is not supported.");
        if (simulationRulesVersion == SimulationEngine.CurrentSimulationRulesVersion && survivalVersion != SimulationEngine.SurvivalVersion) throw new ArgumentException("M3 snapshots require the current survival version.", nameof(survivalVersion));
        if (simulationRulesVersion != SimulationEngine.CurrentSimulationRulesVersion && survivalVersion != 0) throw new ArgumentException("Only current M3 snapshots may carry survival state.", nameof(survivalVersion));
        if (citizenGenerationVersion == 0 && simulationRulesVersion == SimulationEngine.CurrentSimulationRulesVersion) throw new ArgumentException("Current M3 rules require the citizen generation sentinel.", nameof(citizenGenerationVersion));
        ScheduledEvents = Array.AsReadOnly(events); World = world; Citizens = Array.AsReadOnly((citizens ?? Array.Empty<Citizen>()).OrderBy(x => x.Id.Value).Select(CloneCitizen).ToArray()); CitizenGenerationVersion = citizenGenerationVersion; ResourceStates = Array.AsReadOnly((resourceStates ?? Array.Empty<ResourceState>()).OrderBy(x => x.ResourceNodeId.Value).Select(x => new ResourceState(x.ResourceNodeId, x.CurrentQuantity)).ToArray()); Settlement = settlement is null ? null : new SettlementState(settlement.FoodStored, settlement.WoodStored, settlement.StoneStored); SurvivalVersion = survivalVersion;
        if (citizenGenerationVersion == 1 && simulationRulesVersion != SimulationEngine.CurrentSimulationRulesVersion) ValidateM2Roster(Citizens, ScheduledEvents, world, worldMinute);
        if (survivalVersion == SimulationEngine.SurvivalVersion) ValidateM3State(Citizens, ScheduledEvents, ResourceStates, Settlement, world, worldMinute);
    }
    public WorldSeed Seed { get; } public WorldMinute WorldMinute { get; } public string WorldSchemaVersion { get; } public string SimulationRulesVersion { get; } public string ApplicationVersion { get; } public string WorldConfiguration { get; } public DeterministicCountersSnapshot Counters { get; } public IReadOnlyList<ScheduledEventSnapshot> ScheduledEvents { get; } public WorldMap? World { get; } public IReadOnlyList<Citizen> Citizens { get; } public int CitizenGenerationVersion { get; } public IReadOnlyList<ResourceState> ResourceStates { get; } public SettlementState? Settlement { get; } public int SurvivalVersion { get; }
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
        if (citizens.Count != CitizenSimulationRules.FounderCount || citizens.Select(x => x.Id.Value).Distinct().Count() != citizens.Count || citizens.Select(x => x.FounderOrdinal).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, CitizenSimulationRules.FounderCount)) is false) throw new ArgumentException("M3 requires the canonical founder roster.", nameof(citizens));
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
    private static Citizen CloneCitizen(Citizen c) => new(c.Id, c.FounderOrdinal, c.GivenName, c.FamilyName, c.BirthMinute, c.Location, new CitizenTraits(c.Traits.Industriousness, c.Traits.Sociability, c.Traits.Curiosity, c.Traits.Cooperativeness, c.Traits.RiskTolerance, c.Traits.Resilience), new CitizenSkills(c.Skills.Foraging, c.Skills.Woodcutting, c.Skills.Stoneworking, c.Skills.Construction, c.Skills.Hauling, c.Skills.Domestic), new CitizenNeeds(c.Needs.Hunger, c.Needs.Rest, c.Needs.Shelter, c.Needs.Social)) { Health = c.Health, CurrentAction = c.CurrentAction, ActionPhase = c.ActionPhase, ActionSequence = c.ActionSequence, ActionStartedMinute = c.ActionStartedMinute, ActionCompletesMinute = c.ActionCompletesMinute, ActionTarget = c.ActionTarget, TargetResourceNodeId = c.TargetResourceNodeId, CarriedResourceType = c.CarriedResourceType, CarriedResourceQuantity = c.CarriedResourceQuantity, NeedsUpdatedMinute = c.NeedsUpdatedMinute, HealthUpdatedMinute = c.HealthUpdatedMinute, LifetimeMovementSteps = c.LifetimeMovementSteps, LifetimeMovementCost = c.LifetimeMovementCost, DeathMinute = c.DeathMinute, DeathCause = c.DeathCause, ParentAId = c.ParentAId, ParentBId = c.ParentBId, PartnerId = c.PartnerId, HouseholdId = c.HouseholdId, HomeStructureId = c.HomeStructureId };
}

public sealed class SimulationEngine
{
    public const string CurrentWorldSchemaVersion = "0.1";
    public const string CurrentSimulationRulesVersion = "m3-rng1-survival1";
    public const string PreviousSimulationRulesVersion = "m2-rng1-citizen1";
    public const int CitizenGenerationVersion = 1;
    public const int SurvivalVersion = 1;
    private sealed record PendingEvent(ScheduledEventId Id, ScheduledEventOrder Order, string Name, string PayloadJson);
    private readonly SortedSet<PendingEvent> _scheduledEvents = new(Comparer<PendingEvent>.Create(static (a, b) => a.Order.CompareTo(b.Order)));
    private readonly Queue<SyntheticEventExecution> _processedEvents = new();
    private const int DiagnosticCapacity = 32;
    private readonly Dictionary<long, Citizen> _citizens = new();
    private readonly Dictionary<long, ResourceState> _resourceStates = new();
    private readonly Dictionary<(TileCoordinate Start, TileCoordinate End), IReadOnlyList<TileCoordinate>?> _pathCache = new();
    private readonly Dictionary<TileCoordinate, IReadOnlyDictionary<TileCoordinate, long>> _travelCostCache = new();
    private readonly Dictionary<(long CitizenId, long ActionSequence), IReadOnlyList<TileCoordinate>> _activePaths = new();
    private readonly DeterministicCounters _counters;
    private int _processedEventCount;
    public SimulationEngine(WorldSeed seed, WorldMinute initialMinute = default, string worldSchemaVersion = CurrentWorldSchemaVersion, string simulationRulesVersion = CurrentSimulationRulesVersion, string applicationVersion = "0.1.0", string worldConfiguration = "{}")
    { if (initialMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(initialMinute)); Seed = seed; CurrentMinute = initialMinute; WorldSchemaVersion = worldSchemaVersion; SimulationRulesVersion = simulationRulesVersion; ApplicationVersion = applicationVersion; _counters = new DeterministicCounters(); World = CreateWorld(seed, worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration))); WorldConfiguration = World.Configuration.CanonicalJson; Settlement = simulationRulesVersion == CurrentSimulationRulesVersion ? new SettlementState() : new SettlementState(0, 0, 0); if (simulationRulesVersion == CurrentSimulationRulesVersion) foreach (var node in World.Resources) _resourceStates[node.Id.Value] = new ResourceState(node.Id, node.InitialQuantity); if (simulationRulesVersion == CurrentSimulationRulesVersion || simulationRulesVersion == PreviousSimulationRulesVersion) GenerateFounders(); if (simulationRulesVersion == CurrentSimulationRulesVersion) { foreach (var citizen in _citizens.Values) { citizen.NeedsUpdatedMinute = CurrentMinute.Value; citizen.HealthUpdatedMinute = CurrentMinute.Value; } InitializeSurvivalEvents(); } }
    public SimulationEngine(SimulationPersistenceSnapshot snapshot)
    { ArgumentNullException.ThrowIfNull(snapshot); ValidatePersistenceSnapshotCompatibility(snapshot); Seed = snapshot.Seed; CurrentMinute = snapshot.WorldMinute; WorldSchemaVersion = snapshot.WorldSchemaVersion; SimulationRulesVersion = snapshot.SimulationRulesVersion; ApplicationVersion = snapshot.ApplicationVersion; WorldConfiguration = snapshot.WorldConfiguration; _counters = new DeterministicCounters(snapshot.Counters); World = snapshot.World ?? CreateWorld(snapshot.Seed, snapshot.WorldConfiguration); Settlement = snapshot.Settlement is null ? new SettlementState(0, 0, 0) : new SettlementState(snapshot.Settlement.FoodStored, snapshot.Settlement.WoodStored, snapshot.Settlement.StoneStored); if (snapshot.SurvivalVersion == SurvivalVersion) foreach (var node in World.Resources) _resourceStates[node.Id.Value] = new ResourceState(node.Id, node.InitialQuantity); foreach (var state in snapshot.ResourceStates) { state.Validate(World.Resources.Single(x => x.Id == state.ResourceNodeId)); _resourceStates[state.ResourceNodeId.Value] = new ResourceState(state.ResourceNodeId, state.CurrentQuantity); } foreach (var citizen in snapshot.Citizens) _citizens.Add(citizen.Id.Value, CloneCitizen(citizen)); foreach (var item in snapshot.ScheduledEvents) { if (item.Order.DueWorldMinute < CurrentMinute) throw new ArgumentException("A restored scheduled event cannot be due in the past.", nameof(snapshot)); AddPending(item.Id, item.Order, item.Name, item.PayloadJson); } if (snapshot.CitizenGenerationVersion == CitizenGenerationVersion && _citizens.Count == 0) throw new InvalidDataException("An M2 snapshot must contain citizens."); }
    public WorldSeed Seed { get; } public WorldMinute CurrentMinute { get; private set; } public string WorldSchemaVersion { get; } public string SimulationRulesVersion { get; } public string ApplicationVersion { get; } public string WorldConfiguration { get; } public WorldMap World { get; } public SettlementState Settlement { get; } public int PendingEventCount => _scheduledEvents.Count; public int ProcessedEventCount => _processedEventCount; public DeterministicCountersSnapshot CounterSnapshot => _counters.Snapshot; public int Population => SimulationRulesVersion == CurrentSimulationRulesVersion ? LivingPopulation : TotalCitizenCount; public int LivingPopulation => _citizens.Values.Count(x => x.IsAlive); public int DeadPopulation => _citizens.Values.Count(x => !x.IsAlive); public int TotalCitizenCount => _citizens.Count; public IReadOnlyList<Citizen> Citizens => Array.AsReadOnly(_citizens.Values.OrderBy(x => x.Id.Value).Select(CloneCitizen).ToArray()); public IReadOnlyList<ResourceState> ResourceStates => Array.AsReadOnly(_resourceStates.Values.OrderBy(x => x.ResourceNodeId.Value).Select(x => new ResourceState(x.ResourceNodeId, x.CurrentQuantity)).ToArray()); public ResourceState? GetResourceState(ResourceNodeId id) => _resourceStates.TryGetValue(id.Value, out var state) ? new ResourceState(state.ResourceNodeId, state.CurrentQuantity) : null; public Citizen? GetCitizen(CitizenId id) => _citizens.TryGetValue(id.Value, out var citizen) ? CloneCitizen(citizen) : null;
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
    private List<CitizenDecisionEvaluation> EvaluateDecision(Citizen citizen)
    {
        if (!citizen.IsAlive) return [];
        var needs = citizen.GetProjectedNeeds(CurrentMinute); var random = new DeterministicRandom(Seed);
        var result = new List<CitizenDecisionEvaluation>();
        void Add(CitizenAction action, int baseUtility, int need, int trait, ulong purpose, int travelPenalty = 0, int skill = 0, int stockpile = 0)
        { var variation = Variation(random, citizen, purpose); var score = checked(baseUtility + need + trait + skill + stockpile + variation - travelPenalty); result.Add(new(action, baseUtility, need, trait, variation, score, skill, stockpile, travelPenalty)); }
        var food = Settlement.FoodStored;
        var eatPath = citizen.Location == World.StartingSite ? Array.Empty<TileCoordinate>() : FindPathCached(citizen.Location, World.StartingSite);
        if (food > 0 && (citizen.Location == World.StartingSite || eatPath is { Count: > 1 }))
        {
            var eatTravelCost = eatPath is { Count: > 1 } path ? RemainingPathCost(path, World) : 0;
            Add(CitizenAction.Eat, 3000, needs.Hunger * 4, 0, 1, checked((int)Math.Min(int.MaxValue, eatTravelCost)));
        }
        Add(CitizenAction.Rest, 0, needs.Rest * CitizenSimulationRules.RestUtilityWeight, 0, 2);
        AddGather(CitizenAction.GatherFood, ResourceType.Food, needs.Hunger, 3);
        AddGather(CitizenAction.GatherWood, ResourceType.Wood, 4, 4);
        AddGather(CitizenAction.GatherStone, ResourceType.Stone, 3, 5);
        Add(CitizenAction.Explore, CitizenSimulationRules.ExploreBaseUtility, 0, citizen.Traits.Curiosity / 4 + citizen.Traits.RiskTolerance / 8, 6);
        Add(CitizenAction.Wander, CitizenSimulationRules.WanderBaseUtility, 0, citizen.Traits.Curiosity / 10, 7);
        Add(CitizenAction.Idle, CitizenSimulationRules.IdleBaseUtility, 0, (10000 - citizen.Traits.Industriousness) / 20, 8);
        return result;
        void AddGather(CitizenAction action, ResourceType type, int need, ulong purpose)
        { var target = SelectResourceTarget(citizen, type); if (target is null) return; var path = FindPathCached(citizen.Location, target.Coordinate); var travel = path is null ? 0 : (int)Math.Min(int.MaxValue, RemainingPathCost(path, World)); var stock = type == ResourceType.Food ? food : type == ResourceType.Wood ? Settlement.WoodStored : Settlement.StoneStored; var targetValue = type == ResourceType.Food ? CitizenSimulationRules.FoodTarget : type == ResourceType.Wood ? CitizenSimulationRules.WoodTarget : CitizenSimulationRules.StoneTarget; var stockContribution = stock < targetValue ? 1800 : 500; Add(action, 0, need * (type == ResourceType.Food ? 3 : 1), citizen.Traits.Industriousness / 20, purpose, travel / 10, RelevantSkill(action, citizen) / 1000, stockContribution); }
    }
    public ScheduledEventId ScheduleSyntheticEvent(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name) => ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name, "{}");
    public ScheduledEventId ScheduleSyntheticEvent(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name, string payloadJson)
    { if (name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck or CitizenEventNames.ResourceRegenerate) throw new ArgumentException("Reserved gameplay event names may only be scheduled by the runtime.", nameof(name)); ArgumentOutOfRangeException.ThrowIfLessThan(dueWorldMinute, CurrentMinute); var sequence = _counters.AllocateScheduledEventSequence(); var id = new ScheduledEventId(sequence); AddPending(id, new ScheduledEventOrder(dueWorldMinute, priority, entitySortKey, sequence), name, payloadJson); return id; }
    public ScheduledEventId Schedule(WorldMinute dueWorldMinute, int priority, long entitySortKey, string name) => ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name);
    public bool ProcessNextEvent()
    { if (_scheduledEvents.Count == 0) return false; var next = _scheduledEvents.Min!; _scheduledEvents.Remove(next); CurrentMinute = CurrentMinute.AdvanceTo(next.Order.DueWorldMinute); _processedEventCount++; if (next.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck) DispatchCitizenEvent(next); else if (next.Name == CitizenEventNames.ResourceRegenerate) { RegenerateResources(); ScheduleResourceRegeneration(); } else { if (_processedEvents.Count == DiagnosticCapacity) _processedEvents.Dequeue(); _processedEvents.Enqueue(new SyntheticEventExecution(next.Id, next.Order, next.Name, next.PayloadJson)); } return true; }
    public int AdvanceEvents(int maximumEvents) { ArgumentOutOfRangeException.ThrowIfNegative(maximumEvents); var count = 0; while (count < maximumEvents && ProcessNextEvent()) count++; return count; }
    public int AdvanceUntil(WorldMinute targetMinute) { ArgumentOutOfRangeException.ThrowIfLessThan(targetMinute, CurrentMinute); var count = 0; while (_scheduledEvents.Count > 0 && _scheduledEvents.Min!.Order.DueWorldMinute <= targetMinute) { ProcessNextEvent(); count++; } CurrentMinute = CurrentMinute.AdvanceTo(targetMinute); return count; }
    public SimulationStatusSnapshot CreateReadSnapshot() => new(Seed, CurrentMinute, PendingEventCount, ProcessedEventCount, _processedEvents.ToArray(), World, CreateCitizenSnapshots(), new SettlementState(Settlement.FoodStored, Settlement.WoodStored, Settlement.StoneStored), ResourceStates);
    public string ComputeSurvivalFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static string I<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        static void Add(IncrementalHash hash, string value) { var bytes = Encoding.UTF8.GetBytes(value); hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":")); hash.AppendData(bytes); }
        var counters = _counters.Snapshot;
        var citizenGeneration = SimulationRulesVersion is CurrentSimulationRulesVersion or PreviousSimulationRulesVersion ? CitizenGenerationVersion : 0;
        var survival = SimulationRulesVersion == CurrentSimulationRulesVersion ? SurvivalVersion : 0;
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
    public SimulationStatusSnapshot CreateStatusSnapshot() => CreateReadSnapshot();
    public SimulationPersistenceSnapshot CreatePersistenceSnapshot() => new(Seed, CurrentMinute, WorldSchemaVersion, SimulationRulesVersion, ApplicationVersion, WorldConfiguration, _counters.Snapshot, _scheduledEvents.Select(x => new ScheduledEventSnapshot(x.Id, x.Order, x.Name, x.PayloadJson)).ToArray(), World, _citizens.Values.Select(CloneCitizen).ToArray(), SimulationRulesVersion is CurrentSimulationRulesVersion or PreviousSimulationRulesVersion ? CitizenGenerationVersion : 0, ResourceStates, new SettlementState(Settlement.FoodStored, Settlement.WoodStored, Settlement.StoneStored), SimulationRulesVersion == CurrentSimulationRulesVersion ? SurvivalVersion : 0);
    public static SimulationEngine FromPersistenceSnapshot(SimulationPersistenceSnapshot snapshot) => new(snapshot);
    public static void ValidatePersistenceSnapshotCompatibility(SimulationPersistenceSnapshot snapshot) { if (snapshot.WorldSchemaVersion != CurrentWorldSchemaVersion) throw new NotSupportedException($"World schema version '{snapshot.WorldSchemaVersion}' is not supported; expected '{CurrentWorldSchemaVersion}'."); if (snapshot.SimulationRulesVersion is not ("m0-rng1" or PreviousSimulationRulesVersion or CurrentSimulationRulesVersion)) throw new NotSupportedException($"Simulation rules version '{snapshot.SimulationRulesVersion}' is not supported."); if (snapshot.CitizenGenerationVersion == CitizenGenerationVersion && snapshot.SimulationRulesVersion == "m0-rng1") throw new NotSupportedException("M2 citizens require the M2 simulation rules version."); }
    private void GenerateFounders() { foreach (var citizen in CitizenGenerator.Generate(Seed, World, _counters, CurrentMinute)) _citizens.Add(citizen.Id.Value, citizen); foreach (var citizen in _citizens.Values) ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); }
    private void InitializeSurvivalEvents() { foreach (var citizen in _citizens.Values.Where(c => c.IsAlive)) ScheduleSurvival(citizen); ScheduleResourceRegeneration(); }
    private void DispatchCitizenEvent(PendingEvent item)
    { using var doc = JsonDocument.Parse(item.PayloadJson); var root = doc.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("citizenId", out var idElement) || idElement.ValueKind != JsonValueKind.String || !long.TryParse(idElement.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(System.Globalization.CultureInfo.InvariantCulture) != idElement.GetString() || !_citizens.TryGetValue(id, out var citizen)) throw new InvalidDataException("Citizen event references an unknown citizen."); if (item.Name == CitizenEventNames.SurvivalCheck) { ApplySurvival(citizen); return; } if (!root.TryGetProperty("actionSequence", out var seqElement) || !seqElement.TryGetInt64(out var sequence) || sequence < 0) throw new InvalidDataException("Citizen event has no action sequence."); if (item.Order.EntitySortKey != id || sequence != citizen.ActionSequence) throw new InvalidDataException($"Citizen event identity or action sequence is stale: {item.Name} id={id} payload={sequence} state={citizen.ActionSequence} action={citizen.CurrentAction} phase={citizen.ActionPhase}."); if (item.Name == CitizenEventNames.Decision) { if (citizen.CurrentAction != CitizenAction.None) throw new InvalidDataException("A decision event requires a citizen at a decision boundary."); Decide(citizen); } else if (item.Name == CitizenEventNames.ActionComplete) { if (citizen.CurrentAction is not (CitizenAction.Idle or CitizenAction.Rest or CitizenAction.Eat or CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone) || citizen.ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.Perform)) throw new InvalidDataException("An action-complete event does not match citizen state."); CompleteAction(citizen); } else MoveStep(citizen); }
    private void Decide(Citizen citizen)
    {
        ClearCarriedState(citizen);
        citizen.TargetResourceNodeId = null;
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.Needs = needs;
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        citizen.ActionSequence = checked(citizen.ActionSequence + 1);
        var action = SelectDecision(EvaluateDecision(citizen));
        var random = new DeterministicRandom(Seed);
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
    private void BeginTravel(Citizen citizen, CitizenAction action, TileCoordinate target, ResourceNodeId? node)
    {
        var route = FindPathCached(citizen.Location, target);
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
        citizen.CurrentAction = action;
        citizen.ActionPhase = CitizenActionPhase.TravelToTarget;
        citizen.ActionTarget = target;
        citizen.TargetResourceNodeId = node;
        citizen.ActionStartedMinute = CurrentMinute;
        citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(route, World));
        ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(StepCost(route[0], route[1], World)), CitizenEventNames.MovementPriority);
    }
    private void MoveStep(Citizen citizen)
    {
        if (citizen.ActionTarget is not { } target || citizen.ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.TravelToTarget or CitizenActionPhase.ReturnToStockpile)) throw new InvalidDataException("Movement event does not match citizen action phase.");
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
            CompleteAction(citizen, gatheringAlreadyDeposited: true);
            return;
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
        citizen.TargetResourceNodeId = null;
        CompleteAction(citizen);
    }
    private void CompleteAction(Citizen citizen, bool gatheringAlreadyDeposited = false)
    {
        citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        if (citizen.CurrentAction == CitizenAction.Rest) citizen.Needs = new CitizenNeeds(citizen.Needs.Hunger, Math.Max(0, citizen.Needs.Rest - CitizenSimulationRules.RestNeedReduction), citizen.Needs.Shelter, citizen.Needs.Social);
        else if (citizen.CurrentAction == CitizenAction.Eat) Eat(citizen);
        else if (!gatheringAlreadyDeposited && citizen.CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone) Gather(citizen);
        if (!gatheringAlreadyDeposited && citizen.ActionPhase == CitizenActionPhase.ReturnToStockpile) return;
        FinishAction(citizen);
        ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }
    private void Eat(Citizen citizen) { var consumed = Math.Min(CitizenSimulationRules.MealFoodUnits, Settlement.FoodStored); Settlement.FoodStored = checked(Settlement.FoodStored - consumed); var hungerReduction = checked((CitizenSimulationRules.FullHungerReduction * consumed) / CitizenSimulationRules.MealFoodUnits); citizen.Needs = new CitizenNeeds(Math.Max(0, citizen.Needs.Hunger - hungerReduction), citizen.Needs.Rest, citizen.Needs.Shelter, citizen.Needs.Social); }
    private void Gather(Citizen citizen)
    {
        if (citizen.TargetResourceNodeId is not { } id || !_resourceStates.TryGetValue(id.Value, out var state)) { FinishAction(citizen); return; }
        var node = World.Resources.Single(x => x.Id == id);
        var skill = RelevantSkill(citizen.CurrentAction, citizen);
        var baseYield = citizen.CurrentAction == CitizenAction.GatherFood ? CitizenSimulationRules.FoodBaseYield : citizen.CurrentAction == CitizenAction.GatherWood ? CitizenSimulationRules.WoodBaseYield : CitizenSimulationRules.StoneBaseYield;
        var calculated = checked(baseYield + skill / 1000);
        var actual = Math.Min(calculated, state.CurrentQuantity);
        state.CurrentQuantity -= actual;
        if (actual == 0)
        {
            ClearCarriedState(citizen);
            FinishAction(citizen);
            return;
        }
        citizen.CarriedResourceType = node.Type;
        citizen.CarriedResourceQuantity = actual;
        AddSkill(citizen, citizen.CurrentAction, CitizenSimulationRules.GatherExperienceGain);
        citizen.ActionPhase = CitizenActionPhase.ReturnToStockpile;
        citizen.ActionTarget = World.StartingSite;
        citizen.ActionStartedMinute = CurrentMinute;
        var path = DeterministicPathfinder.Find(World, citizen.Location, World.StartingSite);
        if (path is null || path.Count < 2)
        {
            Deposit(citizen);
            FinishAction(citizen);
            return;
        }
        citizen.ActionCompletesMinute = CurrentMinute.Add(RemainingPathCost(path, World));
        ScheduleCitizen(citizen, CitizenEventNames.MoveStep, CurrentMinute.Add(StepCost(path[0], path[1], World)), CitizenEventNames.MovementPriority);
    }
    private static void ClearCarriedState(Citizen citizen) { citizen.CarriedResourceQuantity = 0; citizen.CarriedResourceType = null; }
    private static void FinishAction(Citizen citizen)
    {
        citizen.CurrentAction = CitizenAction.None;
        citizen.ActionPhase = CitizenActionPhase.None;
        citizen.ActionTarget = null;
        citizen.TargetResourceNodeId = null;
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
        if (type == ResourceType.Food) Settlement.FoodStored = checked(Settlement.FoodStored + citizen.CarriedResourceQuantity);
        else if (type == ResourceType.Wood) Settlement.WoodStored = checked(Settlement.WoodStored + citizen.CarriedResourceQuantity);
        else Settlement.StoneStored = checked(Settlement.StoneStored + citizen.CarriedResourceQuantity);
        ClearCarriedState(citizen);
    }
    private void ScheduleCitizen(Citizen citizen, string name, WorldMinute due, int priority) { var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(due, priority, citizen.Id.Value, seq), name, name == CitizenEventNames.SurvivalCheck ? $"{{\"citizenId\":\"{citizen.Id.Value}\"}}" : $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}"); }
    private static int RelevantSkill(CitizenAction action, Citizen citizen) => action == CitizenAction.GatherFood ? citizen.Skills.Foraging : action == CitizenAction.GatherWood ? citizen.Skills.Woodcutting : citizen.Skills.Stoneworking;
    private static void AddSkill(Citizen citizen, CitizenAction action, int amount) { static int SafeAdd(int current, int delta) => (int)Math.Min(int.MaxValue, (long)current + delta); if (action == CitizenAction.GatherFood) citizen.Skills.Foraging = SafeAdd(citizen.Skills.Foraging, amount); else if (action == CitizenAction.GatherWood) citizen.Skills.Woodcutting = SafeAdd(citizen.Skills.Woodcutting, amount); else citizen.Skills.Stoneworking = SafeAdd(citizen.Skills.Stoneworking, amount); }
    private ResourceNode? SelectResourceTarget(Citizen citizen, ResourceType type)
    { if (!_travelCostCache.TryGetValue(citizen.Location, out var costs)) { costs = DeterministicPathfinder.ComputeTravelCosts(World, citizen.Location); _travelCostCache[citizen.Location] = costs; } return World.Resources.Where(n => n.Type == type && _resourceStates.TryGetValue(n.Id.Value, out var state) && state.CurrentQuantity > 0).Select(n => (Node: n, Reachable: costs.TryGetValue(n.Coordinate, out var cost), Cost: costs.GetValueOrDefault(n.Coordinate))).Where(x => x.Reachable).OrderBy(x => x.Cost).ThenByDescending(x => _resourceStates[x.Node.Id.Value].CurrentQuantity).ThenBy(x => x.Node.Id.Value).Select(x => x.Node).FirstOrDefault(); }
    private IReadOnlyList<TileCoordinate>? FindPathCached(TileCoordinate start, TileCoordinate destination) { var key = (start, destination); if (_pathCache.TryGetValue(key, out var path)) return path; path = DeterministicPathfinder.Find(World, start, destination); _pathCache[key] = path; return path; }
    private void ScheduleSurvival(Citizen citizen) { var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(CurrentMinute.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes), CitizenEventNames.SurvivalPriority, citizen.Id.Value, seq), CitizenEventNames.SurvivalCheck, $"{{\"citizenId\":\"{citizen.Id.Value}\"}}"); }
    private void ScheduleResourceRegeneration() { var next = checked(((CurrentMinute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay); var seq = _counters.AllocateScheduledEventSequence(); AddPending(new ScheduledEventId(seq), new ScheduledEventOrder(new WorldMinute(next), CitizenEventNames.RegenerationPriority, 0, seq), CitizenEventNames.ResourceRegenerate, "{\"version\":1}"); }
    private void RegenerateResources()
    { var season = CurrentMinute.ToCalendar().Season; var foodBasis = season switch { WorldSeason.Spring => CitizenSimulationRules.FoodSpringBasisPoints, WorldSeason.Summer => CitizenSimulationRules.FoodSummerBasisPoints, WorldSeason.Autumn => CitizenSimulationRules.FoodAutumnBasisPoints, _ => CitizenSimulationRules.FoodWinterBasisPoints }; foreach (var node in World.Resources.OrderBy(x => x.Id.Value)) { var state = _resourceStates[node.Id.Value]; var amount = node.Type switch { ResourceType.Food => checked((node.RegenerationPotential * foodBasis) / 10000), ResourceType.Wood => checked(node.RegenerationPotential / CitizenSimulationRules.WoodRegenerationDivisor), _ => 0 }; state.CurrentQuantity = Math.Min(node.MaximumQuantity, checked(state.CurrentQuantity + amount)); } _travelCostCache.Clear(); _pathCache.Clear(); }
    private void ApplySurvival(Citizen citizen)
    { if (!citizen.IsAlive) return; if (CurrentMinute.Value < citizen.HealthUpdatedMinute) throw new InvalidDataException("Survival checks cannot precede the health update boundary."); var needs = citizen.GetProjectedNeeds(CurrentMinute); citizen.Needs = needs; citizen.NeedsUpdatedMinute = CurrentMinute.Value; var damage = 0; if (needs.Hunger >= CitizenSimulationRules.StarvationThreshold) damage = checked(damage + CitizenSimulationRules.StarvationDamagePerCheck); if (needs.Rest >= CitizenSimulationRules.ExhaustionThreshold) damage = checked(damage + CitizenSimulationRules.ExhaustionDamagePerCheck); var resilienceMultiplier = 10000 - (citizen.Traits.Resilience / 4); damage = checked((damage * resilienceMultiplier) / 10000); if (damage > 0) citizen.Health = Math.Max(0, citizen.Health - damage); else if (needs.Hunger < CitizenSimulationRules.RecoveryHungerThreshold && needs.Rest < CitizenSimulationRules.RecoveryRestThreshold) citizen.Health = Math.Min(10000, checked(citizen.Health + CitizenSimulationRules.HealthRecoveryPerCheck + citizen.Traits.Resilience / 1000)); citizen.NeedsUpdatedMinute = CurrentMinute.Value; citizen.HealthUpdatedMinute = CurrentMinute.Value; if (citizen.Health == 0) Kill(citizen, needs); else ScheduleSurvival(citizen); }
    private void Kill(Citizen citizen, CitizenNeeds needs) { citizen.DeathMinute = CurrentMinute.Value; citizen.DeathCause = needs.Hunger >= CitizenSimulationRules.StarvationThreshold && needs.Rest >= CitizenSimulationRules.ExhaustionThreshold ? "deprivation" : needs.Hunger >= CitizenSimulationRules.StarvationThreshold ? "starvation" : "exhaustion"; citizen.CurrentAction = CitizenAction.Dead; citizen.ActionPhase = CitizenActionPhase.None; citizen.ActionTarget = null; citizen.TargetResourceNodeId = null; citizen.ActionStartedMinute = null; citizen.ActionCompletesMinute = null; citizen.CarriedResourceQuantity = 0; citizen.CarriedResourceType = null; foreach (var item in _scheduledEvents.Where(e => IsReservedCitizenEventFor(e, citizen.Id.Value)).ToArray()) _scheduledEvents.Remove(item); }
    private static bool IsReservedCitizenEventFor(PendingEvent eventItem, long citizenId)
    {
        if (eventItem.Name is not (CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck)) return false;
        return SimulationPersistenceSnapshot.TryReadEventCitizenId(new ScheduledEventSnapshot(eventItem.Id, eventItem.Order, eventItem.Name, eventItem.PayloadJson), out var eventCitizenId) && eventCitizenId == citizenId;
    }
    private void AddPending(ScheduledEventId id, ScheduledEventOrder order, string name, string payload) { if (!_scheduledEvents.Add(new PendingEvent(id, order, name, payload))) throw new ArgumentException("Duplicate scheduled event ordering tuple."); }
    private static int Variation(DeterministicRandom random, Citizen citizen, ulong purpose) => (int)(random.NextUInt64(RandomDomain.DecisionVariation, (ulong)citizen.Id.Value, (ulong)citizen.ActionSequence, purpose) % 101) - 50;
    private static int ActionTieRank(CitizenAction action) => action switch { CitizenAction.Eat => 0, CitizenAction.Rest => 1, CitizenAction.GatherFood => 2, CitizenAction.GatherWood => 3, CitizenAction.GatherStone => 4, CitizenAction.Explore => 5, CitizenAction.Wander => 6, CitizenAction.Idle => 7, _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Only decision actions have a tie rank.") };
    internal static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => checked((long)((from.X == to.X || from.Y == to.Y) ? 10 : 14) * world.GetTile(to).MovementCost);
    internal static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world)
    { var cost = 0L; for (var index = 1; index < path.Count; index++) cost = checked(cost + StepCost(path[index - 1], path[index], world)); return cost; }
    private CitizenReadSnapshot[] CreateCitizenSnapshots() => _citizens.Values.OrderBy(x => x.Id.Value).Select(c => new CitizenReadSnapshot(c.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), c.FounderOrdinal, c.GivenName, c.FamilyName, c.Name, c.AgeYears(CurrentMinute), c.LifeStage(CurrentMinute), c.Location, c.Health, c.IsAlive ? c.GetProjectedNeeds(CurrentMinute) : c.Needs, new CitizenTraits(c.Traits.Industriousness, c.Traits.Sociability, c.Traits.Curiosity, c.Traits.Cooperativeness, c.Traits.RiskTolerance, c.Traits.Resilience), new CitizenSkills(c.Skills.Foraging, c.Skills.Woodcutting, c.Skills.Stoneworking, c.Skills.Construction, c.Skills.Hauling, c.Skills.Domestic), c.CurrentAction, c.ActionStartedMinute, c.ActionCompletesMinute, c.ActionTarget, c.ActionSequence, c.IsAlive, c.DeathCause, c.CarriedResourceType, c.CarriedResourceQuantity, c.TargetResourceNodeId, c.ActionPhase, c.DeathMinute is { } death ? new WorldMinute(death) : null)).ToArray();
    private static Citizen CloneCitizen(Citizen c) { return new Citizen(c.Id, c.FounderOrdinal, c.GivenName, c.FamilyName, c.BirthMinute, c.Location, new CitizenTraits(c.Traits.Industriousness, c.Traits.Sociability, c.Traits.Curiosity, c.Traits.Cooperativeness, c.Traits.RiskTolerance, c.Traits.Resilience), new CitizenSkills(c.Skills.Foraging, c.Skills.Woodcutting, c.Skills.Stoneworking, c.Skills.Construction, c.Skills.Hauling, c.Skills.Domestic), new CitizenNeeds(c.Needs.Hunger, c.Needs.Rest, c.Needs.Shelter, c.Needs.Social)) { Health = c.Health, CurrentAction = c.CurrentAction, ActionPhase = c.ActionPhase, ActionSequence = c.ActionSequence, ActionStartedMinute = c.ActionStartedMinute, ActionCompletesMinute = c.ActionCompletesMinute, ActionTarget = c.ActionTarget, TargetResourceNodeId = c.TargetResourceNodeId, CarriedResourceType = c.CarriedResourceType, CarriedResourceQuantity = c.CarriedResourceQuantity, NeedsUpdatedMinute = c.NeedsUpdatedMinute, HealthUpdatedMinute = c.HealthUpdatedMinute, LifetimeMovementSteps = c.LifetimeMovementSteps, LifetimeMovementCost = c.LifetimeMovementCost, DeathMinute = c.DeathMinute, DeathCause = c.DeathCause, ParentAId = c.ParentAId, ParentBId = c.ParentBId, PartnerId = c.PartnerId, HouseholdId = c.HouseholdId, HomeStructureId = c.HomeStructureId }; }
    private static WorldMap CreateWorld(WorldSeed seed, string configuration) { var parsed = string.IsNullOrWhiteSpace(configuration) || configuration == "{}" ? WorldGenerationConfiguration.Default : TryParseConfiguration(configuration); return new WorldGenerator().Generate(seed, parsed); }
    private static WorldGenerationConfiguration TryParseConfiguration(string configuration) { try { return WorldGenerationConfiguration.FromCanonicalJson(configuration); } catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or NotSupportedException) { if (configuration.Contains("\"version\"", StringComparison.Ordinal)) throw; return WorldGenerationConfiguration.Default; } }
}
