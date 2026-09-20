using System.Globalization;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

internal enum CheckpointFailurePoint
{
    AfterRowsWritten
}

internal enum LegacyUpgradeFailurePoint
{
    AfterRowsWritten
}
internal enum M2UpgradeFailurePoint
{
    AfterRowsWritten
}
internal enum M3UpgradeFailurePoint
{
    AfterRowsWritten
}
internal enum M4UpgradeFailurePoint
{
    AfterRowsWritten
}
internal enum M5UpgradeFailurePoint
{
    AfterRowsWritten
}
internal enum M6UpgradeFailurePoint
{
    AfterRowsWritten
}

/// <summary>Persists and restores the complete M1 canonical snapshot in one explicit transaction.</summary>
public sealed class WorldCheckpointStore
{
    private const int SingletonWorldId = 1;
    private readonly LittleAgesDbContext _context;

    internal WorldCheckpointStore(LittleAgesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    internal async Task<bool> UpgradeM1ToM2IfNeededAsync(M2UpgradeFailurePoint? failurePoint = null, CancellationToken cancellationToken = default)
    {
        var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
        if (metadataRows.Count == 0) return false;
        if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId) throw new InvalidDataException("Expected exactly one world_meta row with id 1.");
        var metadata = metadataRows[0];
        if (metadata.SurvivalVersion is not (0 or SimulationEngine.SurvivalVersion)) throw new NotSupportedException($"Survival version '{metadata.SurvivalVersion}' is not supported.");
        var citizenCount = await _context.Citizens.CountAsync(cancellationToken);
        if (metadata.SurvivalVersion == SimulationEngine.SurvivalVersion)
        {
            if (metadata.CitizenGenerationVersion != SimulationEngine.CitizenGenerationVersion) throw new InvalidDataException("An M3 checkpoint has an incomplete citizen sentinel.");
            return false;
        }
        if (metadata.CitizenGenerationVersion == SimulationEngine.CitizenGenerationVersion && citizenCount == CitizenGenerator.FounderCount) return false;
        if (metadata.CitizenGenerationVersion != 0 || citizenCount != 0) throw new InvalidDataException("The M2 citizen sentinel is partial or corrupt.");
        if (!string.Equals(metadata.SimulationRulesVersion, "m0-rng1", StringComparison.Ordinal))
        {
            if (!string.Equals(metadata.SimulationRulesVersion, SimulationEngine.M5SimulationRulesVersion, StringComparison.Ordinal))
                throw new NotSupportedException($"Simulation rules version '{metadata.SimulationRulesVersion}' is not supported.");
            throw new InvalidDataException("An M1-to-M2 upgrade requires the known pre-M2 simulation rules version.");
        }
        var m1 = await LoadM1SnapshotAsync(cancellationToken);
        if (m1.ScheduledEvents.Any(e => e.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete)) throw new InvalidDataException("An M1 sentinel cannot contain citizen events.");
        var counters = new DeterministicCounters(m1.Counters);
        var citizens = CitizenGenerator.Generate(m1.Seed, m1.World!, counters, m1.WorldMinute);
        var events = m1.ScheduledEvents.ToList();
        foreach (var citizen in citizens)
        {
            var sequence = counters.AllocateScheduledEventSequence();
            events.Add(new ScheduledEventSnapshot(new ScheduledEventId(sequence), new ScheduledEventOrder(m1.WorldMinute, CitizenEventNames.DecisionPriority, citizen.Id.Value, sequence), CitizenEventNames.Decision, $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":0}}"));
        }
        var snapshot = new SimulationPersistenceSnapshot(m1.Seed, m1.WorldMinute, m1.WorldSchemaVersion, SimulationEngine.M2SimulationRulesVersion, m1.ApplicationVersion, m1.WorldConfiguration, counters.Snapshot, events, m1.World, citizens, SimulationEngine.CitizenGenerationVersion);
        var checkpointUtc = DateTime.SpecifyKind(metadata.LastCheckpointUtc, DateTimeKind.Utc);
        await CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint == M2UpgradeFailurePoint.AfterRowsWritten ? CheckpointFailurePoint.AfterRowsWritten : null, cancellationToken);
        return true;
    }
    internal Task<bool> UpgradeM1ToM2IfNeededAsync(CancellationToken cancellationToken) => UpgradeM1ToM2IfNeededAsync(null, cancellationToken);

    internal async Task<bool> UpgradeM2ToM3IfNeededAsync(M3UpgradeFailurePoint? failurePoint = null, CancellationToken cancellationToken = default)
    {
        var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
        if (metadataRows.Count == 0) return false;
        if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId) throw new InvalidDataException("Expected exactly one world_meta row with id 1.");
        var metadata = metadataRows[0];
        if (metadata.SurvivalVersion == SimulationEngine.SurvivalVersion)
        {
            // Opening an M3 database must validate it, never repair it.
            _ = await LoadAsync(cancellationToken);
            return false;
        }
        if (metadata.SurvivalVersion != 0) throw new NotSupportedException($"Survival version '{metadata.SurvivalVersion}' is not supported.");
        if (!string.Equals(metadata.SimulationRulesVersion, SimulationEngine.M2SimulationRulesVersion, StringComparison.Ordinal))
        {
            if (SimulationEngine.HistorySystemsEnabled(metadata.SimulationRulesVersion)) throw new InvalidDataException("History rules require survival version 1.");
            return false;
        }

        var resourceCount = await _context.ResourceStates.AsNoTracking().CountAsync(cancellationToken);
        var settlementCount = await _context.SettlementStates.AsNoTracking().CountAsync(cancellationToken);
        var events = await _context.ScheduledEvents.AsNoTracking().ToListAsync(cancellationToken);
        if (resourceCount != 0 || settlementCount != 0 || events.Any(e => e.EventName is CitizenEventNames.SurvivalCheck or CitizenEventNames.ResourceRegenerate))
            throw new InvalidDataException("The M2-to-M3 sentinel contains partial M3 state.");

        var m2 = await LoadAsync(cancellationToken);
        if (m2.CitizenGenerationVersion != SimulationEngine.CitizenGenerationVersion || m2.Citizens.Count != CitizenGenerator.FounderCount)
            throw new InvalidDataException("The M2-to-M3 upgrade requires the complete 20-founder M2 roster.");
        var citizens = m2.Citizens.ToArray();
        foreach (var citizen in citizens)
        {
            citizen.ActionPhase = citizen.CurrentAction switch
            {
                CitizenAction.None => CitizenActionPhase.None,
                CitizenAction.Idle or CitizenAction.Rest => CitizenActionPhase.Perform,
                CitizenAction.Wander or CitizenAction.Explore => CitizenActionPhase.TravelToTarget,
                _ => throw new InvalidDataException("The M2 roster contains an unsupported in-flight action.")
            };
            citizen.HealthUpdatedMinute = m2.WorldMinute.Value;
        }

        var counters = new DeterministicCounters(m2.Counters);
        var upgradedEvents = m2.ScheduledEvents.ToList();
        foreach (var citizen in citizens.Where(x => x.IsAlive).OrderBy(x => x.Id.Value))
        {
            var sequence = counters.AllocateScheduledEventSequence();
            upgradedEvents.Add(new ScheduledEventSnapshot(
                new ScheduledEventId(sequence),
                new ScheduledEventOrder(m2.WorldMinute.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes), CitizenEventNames.SurvivalPriority, citizen.Id.Value, sequence),
                CitizenEventNames.SurvivalCheck,
                $"{{\"citizenId\":\"{citizen.Id.Value}\"}}"));
        }
        var regenerationMinute = new WorldMinute(checked(((m2.WorldMinute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay));
        var regenerationSequence = counters.AllocateScheduledEventSequence();
        upgradedEvents.Add(new ScheduledEventSnapshot(
            new ScheduledEventId(regenerationSequence),
            new ScheduledEventOrder(regenerationMinute, CitizenEventNames.RegenerationPriority, 0, regenerationSequence),
            CitizenEventNames.ResourceRegenerate,
            "{\"version\":1}"));
        var resourceStates = m2.World!.Resources.OrderBy(x => x.Id.Value).Select(x => new ResourceState(x.Id, x.InitialQuantity)).ToArray();
        var snapshot = new SimulationPersistenceSnapshot(
            m2.Seed, m2.WorldMinute, m2.WorldSchemaVersion, SimulationEngine.M3SimulationRulesVersion, m2.ApplicationVersion,
            m2.WorldConfiguration, counters.Snapshot, upgradedEvents, m2.World, citizens, SimulationEngine.CitizenGenerationVersion,
            resourceStates, new SettlementState(), SimulationEngine.SurvivalVersion);
        var checkpointUtc = DateTime.SpecifyKind(metadata.LastCheckpointUtc, DateTimeKind.Utc);
        await CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint == M3UpgradeFailurePoint.AfterRowsWritten ? CheckpointFailurePoint.AfterRowsWritten : null, cancellationToken);
        return true;
    }

    internal Task<bool> UpgradeM2ToM3IfNeededAsync(CancellationToken cancellationToken) => UpgradeM2ToM3IfNeededAsync(null, cancellationToken);

    internal async Task<bool> UpgradeM3ToM4IfNeededAsync(M4UpgradeFailurePoint? failurePoint = null, CancellationToken cancellationToken = default)
    {
        var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
        if (metadataRows.Count == 0) return false;
        if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId) throw new InvalidDataException("Expected exactly one world_meta row with id 1.");
        var metadata = metadataRows[0];
        if (metadata.SettlementVersion == SimulationEngine.SettlementVersion)
        {
            _ = await LoadAsync(cancellationToken);
            return false;
        }
        if (metadata.SettlementVersion != 0) throw new NotSupportedException($"Settlement version '{metadata.SettlementVersion}' is not supported.");
        if (metadata.SurvivalVersion != SimulationEngine.SurvivalVersion || !string.Equals(metadata.SimulationRulesVersion, SimulationEngine.M3SimulationRulesVersion, StringComparison.Ordinal))
        {
            if (SimulationEngine.HistorySystemsEnabled(metadata.SimulationRulesVersion)) throw new InvalidDataException("History rules require settlement version 1.");
            return false;
        }

        var structures = await _context.Structures.AsNoTracking().CountAsync(cancellationToken);
        var contributions = await _context.StructureContributions.AsNoTracking().CountAsync(cancellationToken);
        var settlementRows = await _context.SettlementStates.AsNoTracking().ToListAsync(cancellationToken);
        var citizens = await _context.Citizens.AsNoTracking().ToListAsync(cancellationToken);
        var events = await _context.ScheduledEvents.AsNoTracking().ToListAsync(cancellationToken);
        if (structures != 0 || contributions != 0 || settlementRows.Count != 1 || settlementRows[0].BaseStorageCapacity != 0 || settlementRows[0].DemandUpdatedMinute != 0 || settlementRows[0].ExposureConsequencesStartMinute is not (0 or long.MaxValue) || events.Any(x => x.EventName == CitizenEventNames.SettlementEvaluateDemand) || citizens.Any(x => x.HomeStructureId is not null || x.TargetStructureId is not null || x.TargetCitizenId is not null || x.LifetimeForagingMinutes != 0 || x.LifetimeWoodcuttingMinutes != 0 || x.LifetimeStoneworkingMinutes != 0 || x.LifetimeConstructionMinutes != 0 || x.LifetimeHaulingMinutes != 0 || x.CurrentAction is (int)CitizenAction.HaulConstruction or (int)CitizenAction.Build or (int)CitizenAction.Socialize || x.ActionPhase is (int)CitizenActionPhase.TravelToStockpile or (int)CitizenActionPhase.TransportToConstruction or (int)CitizenActionPhase.WaitingForStorage))
            throw new InvalidDataException("The M3-to-M4 sentinel contains partial M4 state.");

        var m3 = await LoadAsync(cancellationToken);
        if (m3.CitizenGenerationVersion != SimulationEngine.CitizenGenerationVersion || m3.SurvivalVersion != SimulationEngine.SurvivalVersion || m3.Settlement is null)
            throw new InvalidDataException("The M3-to-M4 upgrade requires complete M3 state.");
        var counters = new DeterministicCounters(m3.Counters);
        var upgradedEvents = m3.ScheduledEvents.ToList();
        var sequence = counters.AllocateScheduledEventSequence();
        upgradedEvents.Add(new ScheduledEventSnapshot(new ScheduledEventId(sequence), new ScheduledEventOrder(m3.WorldMinute.Add(CitizenSimulationRules.SettlementDemandIntervalMinutes), CitizenEventNames.SettlementDemandPriority, 0, sequence), CitizenEventNames.SettlementEvaluateDemand, "{\"version\":1}"));
        var total = checked(m3.Settlement.FoodStored + m3.Settlement.WoodStored + m3.Settlement.StoneStored);
        var settlement = new SettlementState(m3.Settlement.FoodStored, m3.Settlement.WoodStored, m3.Settlement.StoneStored, Math.Max(CitizenSimulationRules.BaseStorageCapacity, total), m3.WorldMinute.Value, m3.WorldMinute.Add(CitizenSimulationRules.ExposureGraceDurationMinutes).Value);
        var snapshot = new SimulationPersistenceSnapshot(m3.Seed, m3.WorldMinute, m3.WorldSchemaVersion, SimulationEngine.M4SimulationRulesVersion, m3.ApplicationVersion, m3.WorldConfiguration, counters.Snapshot, upgradedEvents, m3.World, m3.Citizens, m3.CitizenGenerationVersion, m3.ResourceStates, settlement, m3.SurvivalVersion, SimulationEngine.SettlementVersion, Array.Empty<Structure>(), Array.Empty<StructureContribution>());
        var checkpointUtc = DateTime.SpecifyKind(metadata.LastCheckpointUtc, DateTimeKind.Utc);
        await CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint == M4UpgradeFailurePoint.AfterRowsWritten ? CheckpointFailurePoint.AfterRowsWritten : null, cancellationToken);
        return true;
    }
    internal Task<bool> UpgradeM3ToM4IfNeededAsync(CancellationToken cancellationToken) => UpgradeM3ToM4IfNeededAsync(null, cancellationToken);

    internal async Task<bool> UpgradeM4ToM5IfNeededAsync(M5UpgradeFailurePoint? failurePoint = null, CancellationToken cancellationToken = default)
    {
        var metadata = await _context.WorldMeta.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (metadata is null) return false;
        if (metadata.SocialVersion == SimulationEngine.SocialVersion)
        {
            _ = await LoadAsync(cancellationToken);
            return false;
        }
        if (metadata.SocialVersion != 0 || metadata.SurvivalVersion != SimulationEngine.SurvivalVersion || metadata.SettlementVersion != SimulationEngine.SettlementVersion || !string.Equals(metadata.SimulationRulesVersion, SimulationEngine.M4SimulationRulesVersion, StringComparison.Ordinal)) return false;
        if (await _context.Relationships.AsNoTracking().AnyAsync(cancellationToken) || await _context.Households.AsNoTracking().AnyAsync(cancellationToken) || await _context.ScheduledEvents.AsNoTracking().AnyAsync(e => e.EventName == CitizenEventNames.FamilyCheck || e.EventName == CitizenEventNames.LifecycleCheck, cancellationToken)) throw new InvalidDataException("The M4-to-M5 sentinel contains partial social state.");
        var m4 = await LoadAsync(cancellationToken);
        if (m4.Citizens.Count != CitizenGenerator.FounderCount || !m4.Citizens.Select(c => c.FounderOrdinal).All(c => c is not null) || !m4.Citizens.Select(c => c.FounderOrdinal!.Value).OrderBy(c => c).SequenceEqual(Enumerable.Range(0, CitizenGenerator.FounderCount)) || m4.Citizens.Any(c => c.ParentAId is not null || c.ParentBId is not null || c.PartnerId is not null || c.HouseholdId is not null || c.TargetCitizenId is not null || c.CurrentAction == CitizenAction.Socialize)) throw new InvalidDataException("The M4-to-M5 sentinel requires exactly the untouched M4 founders.");
        var counters = new DeterministicCounters(m4.Counters);
        var events = m4.ScheduledEvents.ToList();
        var due = new WorldMinute(checked(((m4.WorldMinute.Value / WorldCalendar.MinutesPerDay) + 1) * WorldCalendar.MinutesPerDay));
        foreach (var (name, priority) in new[] { (CitizenEventNames.FamilyCheck, CitizenEventNames.FamilyCheckPriority), (CitizenEventNames.LifecycleCheck, CitizenEventNames.LifecycleCheckPriority) })
        {
            var sequence = counters.AllocateScheduledEventSequence();
            events.Add(new ScheduledEventSnapshot(new ScheduledEventId(sequence), new ScheduledEventOrder(due, priority, 0, sequence), name, "{\"version\":1}"));
        }
        var snapshot = new SimulationPersistenceSnapshot(m4.Seed, m4.WorldMinute, m4.WorldSchemaVersion, SimulationEngine.M5SimulationRulesVersion, m4.ApplicationVersion, m4.WorldConfiguration, counters.Snapshot, events, m4.World, m4.Citizens, m4.CitizenGenerationVersion, m4.ResourceStates, m4.Settlement, m4.SurvivalVersion, m4.SettlementVersion, m4.Structures, m4.StructureContributions, SimulationEngine.SocialVersion, Array.Empty<RelationshipState>(), Array.Empty<Household>());
        await CheckpointCoreAsync(snapshot, DateTime.SpecifyKind(metadata.LastCheckpointUtc, DateTimeKind.Utc), failurePoint == M5UpgradeFailurePoint.AfterRowsWritten ? CheckpointFailurePoint.AfterRowsWritten : null, cancellationToken);
        return true;
    }
    internal Task<bool> UpgradeM4ToM5IfNeededAsync(CancellationToken cancellationToken) => UpgradeM4ToM5IfNeededAsync(null, cancellationToken);

    internal async Task<bool> UpgradeM5ToM6IfNeededAsync(M6UpgradeFailurePoint? failurePoint = null, CancellationToken cancellationToken = default)
    {
        var metadata = await _context.WorldMeta.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (metadata is null) return false;
        if (metadata.HistoryVersion is not (0 or SimulationEngine.HistoryVersion)) throw new NotSupportedException($"History version '{metadata.HistoryVersion}' is not supported.");
        if (metadata.HistoryVersion == SimulationEngine.HistoryVersion)
        {
            if (!SimulationEngine.IsHistoryRulesVersion(metadata.SimulationRulesVersion) || metadata.SocialVersion != SimulationEngine.SocialVersion || metadata.SurvivalVersion != SimulationEngine.SurvivalVersion || metadata.SettlementVersion != SimulationEngine.SettlementVersion) throw new InvalidDataException("History metadata is only valid with complete compatibility sentinels.");
            _ = await LoadAsync(cancellationToken);
            return false;
        }
        var hasPartialHistory = await _context.HistoricalEvents.AsNoTracking().AnyAsync(cancellationToken) ||
            await _context.HistoricalEventCitizens.AsNoTracking().AnyAsync(cancellationToken) ||
            await _context.HistoricalEventStructures.AsNoTracking().AnyAsync(cancellationToken) ||
            await _context.StatisticsSamples.AsNoTracking().AnyAsync(cancellationToken) ||
            await _context.Memories.AsNoTracking().AnyAsync(cancellationToken) ||
            await _context.HistoryStates.AsNoTracking().AnyAsync(cancellationToken) ||
            await _context.ScheduledEvents.AsNoTracking().AnyAsync(x => x.EventName == CitizenEventNames.StatisticsSample, cancellationToken);
        if (hasPartialHistory) throw new InvalidDataException("History rows cannot exist while HistoryVersion is zero.");
        if (SimulationEngine.HistorySystemsEnabled(metadata.SimulationRulesVersion)) throw new InvalidDataException("History rules require history version 1.");
        if (metadata.HistoryVersion != 0 || metadata.SocialVersion != SimulationEngine.SocialVersion || metadata.SurvivalVersion != SimulationEngine.SurvivalVersion || metadata.SettlementVersion != SimulationEngine.SettlementVersion || !string.Equals(metadata.SimulationRulesVersion, SimulationEngine.M5SimulationRulesVersion, StringComparison.Ordinal)) return false;
        var m5 = await LoadAsync(cancellationToken);
        var counters = new DeterministicCounters(m5.Counters);
        var events = m5.ScheduledEvents.ToList();
        var historyEvents = new List<HistoricalEvent>();
        var historyCitizenLinks = new List<HistoricalEventCitizenLink>();
        var historyStructureLinks = new List<HistoricalEventStructureLink>();
        var memories = new List<CitizenMemory>();
        BuildM5HistoryBackfill(m5, counters, historyEvents, historyCitizenLinks, historyStructureLinks, memories);
        var due = new WorldMinute(checked(((m5.WorldMinute.Value / (30L * WorldCalendar.MinutesPerDay)) + 1) * (30L * WorldCalendar.MinutesPerDay)));
        var sequence = counters.AllocateScheduledEventSequence();
        events.Add(new ScheduledEventSnapshot(new ScheduledEventId(sequence), new ScheduledEventOrder(due, CitizenEventNames.StatisticsSamplePriority, 0, sequence), CitizenEventNames.StatisticsSample, "{\"version\":1}"));
        var livingCount = m5.Citizens.Count(x => x.IsAlive);
        var settlement = m5.Settlement ?? throw new InvalidDataException("M5 backfill requires settlement state.");
        var activeShortage = livingCount > 0 && settlement.FoodStored < checked(livingCount * 10);
        // Snapshot constructors reject noncanonical input; make the backfill's
        // independently generated links/memories use the same persisted order
        // as live history before constructing the migration snapshot.
        historyCitizenLinks.Sort(static (left, right) =>
        {
            var result = left.HistoricalEventId.Value.CompareTo(right.HistoricalEventId.Value);
            if (result != 0) return result;
            result = left.CitizenId.Value.CompareTo(right.CitizenId.Value);
            return result != 0 ? result : string.CompareOrdinal(left.Role, right.Role);
        });
        historyStructureLinks.Sort(static (left, right) =>
        {
            var result = left.HistoricalEventId.Value.CompareTo(right.HistoricalEventId.Value);
            if (result != 0) return result;
            result = left.StructureId.Value.CompareTo(right.StructureId.Value);
            return result != 0 ? result : string.CompareOrdinal(left.Role, right.Role);
        });
        memories.Sort(static (left, right) =>
        {
            var result = left.CitizenId.Value.CompareTo(right.CitizenId.Value);
            if (result != 0) return result;
            result = left.HistoricalEventId.Value.CompareTo(right.HistoricalEventId.Value);
            return result != 0 ? result : left.MemoryType.CompareTo(right.MemoryType);
        });
        var historyStartEventId = historyEvents.Count == 0 ? counters.Snapshot.NextHistoricalEventId : historyEvents[0].Id.Value;
        var history = new HistoryState(m5.WorldMinute.Value, historyStartEventId, m5.WorldMinute.Value, 0, 0, 0, 0, activeShortage, DetermineMilestoneWatermark(historyEvents));
        var migration = new SimulationPersistenceSnapshot(m5.Seed, m5.WorldMinute, m5.WorldSchemaVersion, SimulationEngine.M6SimulationRulesVersion, m5.ApplicationVersion, m5.WorldConfiguration, counters.Snapshot, events, m5.World, m5.Citizens, m5.CitizenGenerationVersion, m5.ResourceStates, m5.Settlement, m5.SurvivalVersion, m5.SettlementVersion, m5.Structures, m5.StructureContributions, SimulationEngine.SocialVersion, m5.Relationships, m5.Households, SimulationEngine.HistoryVersion, history, historyEvents, historyCitizenLinks, historyStructureLinks, Array.Empty<StatisticsSample>(), memories);
        await CheckpointCoreAsync(migration, DateTime.SpecifyKind(metadata.LastCheckpointUtc, DateTimeKind.Utc), failurePoint == M6UpgradeFailurePoint.AfterRowsWritten ? CheckpointFailurePoint.AfterRowsWritten : null, cancellationToken);
        return true;
    }
    internal Task<bool> UpgradeM5ToM6IfNeededAsync(CancellationToken cancellationToken) => UpgradeM5ToM6IfNeededAsync(null, cancellationToken);

    private static void BuildM5HistoryBackfill(
        SimulationPersistenceSnapshot m5,
        DeterministicCounters counters,
        List<HistoricalEvent> events,
        List<HistoricalEventCitizenLink> citizenLinks,
        List<HistoricalEventStructureLink> structureLinks,
        List<CitizenMemory> memories)
    {
        var world = m5.World ?? throw new InvalidDataException("M5 backfill requires a persisted world.");
        var minute = m5.WorldMinute.Value;
        var candidates = new List<HistoryBackfillCandidate>();
        var founders = m5.Citizens.Where(x => x.FounderOrdinal is not null).OrderBy(x => x.FounderOrdinal).ThenBy(x => x.Id.Value).ToArray();
        candidates.Add(new HistoryBackfillCandidate(0, 0, 0, 0, HistoricalEventType.WorldCreated, HistoricalImportance.Historic, world.StartingSite, HistoricalEventPayloads.WorldCreated(m5.Seed), Array.Empty<(CitizenId, string)>(), Array.Empty<(StructureId, string)>()));
        candidates.Add(new HistoryBackfillCandidate(0, 1, 0, 0, HistoricalEventType.SettlementFounded, HistoricalImportance.Historic, world.StartingSite, HistoricalEventPayloads.SettlementFounded(founders.Length), founders.Select(x => (x.Id, "founder")).ToArray(), Array.Empty<(StructureId, string)>()));

        var seasonLength = checked((long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay);
        for (var seasonMinute = 0L; seasonMinute <= minute; seasonMinute = checked(seasonMinute + seasonLength))
        {
            var date = new WorldMinute(seasonMinute).ToCalendar();
            candidates.Add(new HistoryBackfillCandidate(seasonMinute, 2, 0, seasonMinute, HistoricalEventType.SeasonStarted, HistoricalImportance.Routine, null, HistoricalEventPayloads.SeasonStarted(date.Season, date.Year), Array.Empty<(CitizenId, string)>(), Array.Empty<(StructureId, string)>()));
            if (long.MaxValue - seasonMinute < seasonLength) break;
        }

        foreach (var citizen in m5.Citizens.Where(x => x.FounderOrdinal is null && x.BirthMinute >= 0 && x.BirthMinute <= minute).OrderBy(x => x.BirthMinute).ThenBy(x => x.Id.Value))
        {
            var links = new List<(CitizenId, string)> { (citizen.Id, "subject") };
            if (citizen.ParentAId is { } parentA) links.Add((parentA, "parent"));
            if (citizen.ParentBId is { } parentB) links.Add((parentB, "parent"));
            // M5 persists only the child's current household. A child may have
            // moved households after birth, so the migration cannot infer an
            // exact birth household from this state alone.
            candidates.Add(new HistoryBackfillCandidate(citizen.BirthMinute, 3, citizen.Id.Value, 0, HistoricalEventType.CitizenBorn, HistoricalImportance.Notable, null, HistoricalEventPayloads.CitizenBorn(null), links, Array.Empty<(StructureId, string)>()));
        }

        foreach (var citizen in m5.Citizens.Where(x => x.DeathMinute is { } death && death >= 0 && death <= minute).OrderBy(x => x.DeathMinute).ThenBy(x => x.Id.Value))
        {
            var deathMinute = citizen.DeathMinute!.Value;
            var age = citizen.AgeYears(new WorldMinute(deathMinute));
            var rank = string.Equals(citizen.DeathCause, "natural", StringComparison.Ordinal) ? 17 : 18;
            candidates.Add(new HistoryBackfillCandidate(deathMinute, rank, citizen.Id.Value, 0, HistoricalEventType.CitizenDied, HistoricalImportance.Notable, citizen.Location, HistoricalEventPayloads.CitizenDied(citizen.DeathCause ?? "natural", age), [(citizen.Id, "subject")], Array.Empty<(StructureId, string)>()));
        }

        foreach (var household in m5.Households.OrderBy(x => x.Id.Value))
        {
            var pairs = m5.Citizens.Where(x => x.HouseholdId == household.Id && x.PartnerId is { })
                .Select(x => (Citizen: x, Partner: m5.Citizens.SingleOrDefault(y => y.Id == x.PartnerId)))
                .Where(x => x.Partner is not null && x.Partner!.PartnerId == x.Citizen.Id)
                .Select(x => RelationshipState.Normalize(x.Citizen.Id, x.Partner!.Id))
                .Distinct()
                .OrderBy(x => x.A.Value)
                .ThenBy(x => x.B.Value)
                .ToArray();
            if (pairs.Length != 1) throw new InvalidDataException($"M5 household {household.Id.Value} does not have one unambiguous founding partner pair.");
            var pair = pairs[0];
            candidates.Add(new HistoryBackfillCandidate(household.CreatedMinute, 5, pair.A.Value, pair.B.Value, HistoricalEventType.PartnershipFormed, HistoricalImportance.Notable, null, HistoricalEventPayloads.PartnershipFormed(household.Id), [(pair.A, "partner"), (pair.B, "partner")], Array.Empty<(StructureId, string)>()));
            candidates.Add(new HistoryBackfillCandidate(household.CreatedMinute, 8, pair.A.Value, pair.B.Value, HistoricalEventType.HouseholdCreated, HistoricalImportance.Personal, null, HistoricalEventPayloads.HouseholdCreated(household.Id), [(pair.A, "member"), (pair.B, "member")], Array.Empty<(StructureId, string)>()));
        }

        foreach (var structure in m5.Structures.OrderBy(x => x.Id.Value))
        {
            candidates.Add(new HistoryBackfillCandidate(structure.ConstructionStartedMinute, 9, structure.Id.Value, 0, HistoricalEventType.StructureStarted, HistoricalImportance.Personal, structure.Location, HistoricalEventPayloads.StructureStarted(structure.Type, structure.RequiredWood, structure.RequiredStone, structure.RequiredWork), Array.Empty<(CitizenId, string)>(), [(structure.Id, "subject")]));
            if (structure.Status == StructureStatus.Complete)
            {
                if (structure.CompletedMinute is not { } completedMinute || completedMinute > minute) throw new InvalidDataException($"M5 structure {structure.Id.Value} has an invalid completion minute.");
                var contributors = m5.StructureContributions.Where(x => x.StructureId == structure.Id && (x.ConstructionWork > 0 || x.WoodDelivered > 0 || x.StoneDelivered > 0)).OrderBy(x => x.CitizenId.Value).Select(x => (x.CitizenId, "contributor")).ToArray();
                candidates.Add(new HistoryBackfillCandidate(completedMinute, 10, structure.Id.Value, 0, HistoricalEventType.StructureCompleted, HistoricalImportance.Notable, structure.Location, HistoricalEventPayloads.StructureCompleted(structure.Type), contributors, [(structure.Id, "subject")]));
            }
        }

        var livingCount = m5.Citizens.Count(x => x.IsAlive);
        var settlement = m5.Settlement ?? throw new InvalidDataException("M5 backfill requires settlement state.");
        if (livingCount > 0 && settlement.FoodStored < checked(livingCount * 10))
        {
            candidates.Add(new HistoryBackfillCandidate(minute, 19, 0, 0, HistoricalEventType.ResourceShortageStarted, HistoricalImportance.Notable, world.StartingSite,
                HistoricalEventPayloads.ResourceShortage(ResourceType.Food, settlement.FoodStored, livingCount, true), Array.Empty<(CitizenId, string)>(), Array.Empty<(StructureId, string)>()));
        }

        // Reconstruct only crossings that can be proven from birth/death minutes.
        var lifecycle = candidates.Where(x => x.EventType is HistoricalEventType.CitizenBorn or HistoricalEventType.CitizenDied)
            .OrderBy(x => x.WorldMinute).ThenBy(x => x.Rank).ThenBy(x => x.PrimaryId).ThenBy(x => x.SecondaryId).ToArray();
        var population = founders.Length;
        foreach (var transition in lifecycle)
        {
            population += transition.EventType == HistoricalEventType.CitizenBorn ? 1 : -1;
            foreach (var milestone in new[] { 25, 50, 100, 250, 500, 1000, 2000 })
            {
                if (population >= milestone && !candidates.Any(x => x.EventType == HistoricalEventType.PopulationMilestone && x.PrimaryId == milestone))
                    candidates.Add(new HistoryBackfillCandidate(transition.WorldMinute, 11, milestone, transition.PrimaryId, HistoricalEventType.PopulationMilestone, HistoricalImportance.Major, null, HistoricalEventPayloads.PopulationMilestone(milestone), Array.Empty<(CitizenId, string)>(), Array.Empty<(StructureId, string)>()));
            }
        }

        candidates.Sort(static (left, right) =>
        {
            var result = left.WorldMinute.CompareTo(right.WorldMinute);
            if (result != 0) return result;
            result = left.Rank.CompareTo(right.Rank);
            if (result != 0) return result;
            result = left.PrimaryId.CompareTo(right.PrimaryId);
            return result != 0 ? result : left.SecondaryId.CompareTo(right.SecondaryId);
        });
        var citizenIds = m5.Citizens.Select(x => x.Id.Value).ToHashSet();
        var structureIds = m5.Structures.Select(x => x.Id.Value).ToHashSet();
        foreach (var candidate in candidates)
        {
            if (candidate.CitizenLinks.Any(link => !citizenIds.Contains(link.CitizenId.Value)) || candidate.StructureLinks.Any(link => !structureIds.Contains(link.StructureId.Value))) throw new InvalidDataException("M5 history backfill references an unknown canonical entity.");
            var probe = new HistoricalEvent(new HistoricalEventId(1), candidate.WorldMinute, candidate.EventType, candidate.Importance, HistoricalEventOrigin.MigrationBackfill, candidate.Location, candidate.Payload);
            probe.Validate(minute);
            probe.ValidateLinks(candidate.CitizenLinks.Select(link => new HistoricalEventCitizenLink(probe.Id, link.CitizenId, link.Role)), candidate.StructureLinks.Select(link => new HistoricalEventStructureLink(probe.Id, link.StructureId, link.Role)));
        }
        foreach (var candidate in candidates)
        {
            var id = counters.AllocateHistoricalEventId();
            var historicalEvent = new HistoricalEvent(id, candidate.WorldMinute, candidate.EventType, candidate.Importance, HistoricalEventOrigin.MigrationBackfill, candidate.Location, candidate.Payload);
            historicalEvent.Validate(minute);
            events.Add(historicalEvent);
            foreach (var link in candidate.CitizenLinks) citizenLinks.Add(new HistoricalEventCitizenLink(id, link.CitizenId, link.Role));
            foreach (var link in candidate.StructureLinks) structureLinks.Add(new HistoricalEventStructureLink(id, link.StructureId, link.Role));
            AddBackfillMemories(memories, historicalEvent, candidate.CitizenLinks, m5.Citizens);
        }
    }

    private static int DetermineMilestoneWatermark(IEnumerable<HistoricalEvent> events) => events.Where(x => x.EventType == HistoricalEventType.PopulationMilestone).Select(x => x.PayloadJson).Select(payload =>
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("population").GetInt32();
    }).DefaultIfEmpty(0).Max();

    private static void AddBackfillMemories(List<CitizenMemory> memories, HistoricalEvent historicalEvent, IEnumerable<(CitizenId CitizenId, string Role)> links, IReadOnlyList<Citizen> citizens)
    {
        var (memoryType, valence) = historicalEvent.EventType switch
        {
            HistoricalEventType.CitizenBorn => (MemoryType.ChildBorn, 8000),
            HistoricalEventType.CitizenDied => (MemoryType.PartnerDied, -9000),
            HistoricalEventType.PartnershipFormed => (MemoryType.PartnershipFormed, 8000),
            HistoricalEventType.StructureCompleted => (MemoryType.StructureCompleted, 4000),
            _ => ((MemoryType?)null, 0)
        };
        if (memoryType is null) return;
        var memoryLinks = links.Where(link => historicalEvent.EventType switch
        {
            HistoricalEventType.CitizenBorn => link.Role == "parent",
            HistoricalEventType.PartnershipFormed => link.Role == "partner",
            HistoricalEventType.StructureCompleted => link.Role == "contributor",
            _ => false
        }).ToList();
        if (historicalEvent.EventType == HistoricalEventType.CitizenDied)
        {
            var deceased = links.FirstOrDefault(link => link.Role == "subject").CitizenId;
            var citizen = citizens.SingleOrDefault(value => value.Id == deceased);
            if (citizen?.PartnerId is { } partner && citizens.Any(value => value.Id == partner && value.BirthMinute <= historicalEvent.WorldMinute && (value.DeathMinute is null || value.DeathMinute.Value > historicalEvent.WorldMinute))) memoryLinks.Add((partner, "partner"));
        }
        foreach (var link in memoryLinks)
        {
            if (memories.Any(x => x.CitizenId == link.CitizenId && x.HistoricalEventId == historicalEvent.Id && x.MemoryType == memoryType.Value)) continue;
            memories.Add(new CitizenMemory(link.CitizenId, historicalEvent.Id, memoryType.Value, historicalEvent.Importance, valence, historicalEvent.WorldMinute));
        }
        foreach (var citizenId in memoryLinks.Select(x => x.CitizenId).Distinct())
        {
            var lower = memories.Where(x => x.CitizenId == citizenId && x.MemoryType is MemoryType.FriendshipFormed or MemoryType.RivalryFormed or MemoryType.StructureCompleted)
                .OrderByDescending(x => x.Importance).ThenByDescending(x => x.CreatedMinute).ThenByDescending(x => x.HistoricalEventId.Value).ToArray();
            memories.RemoveAll(x => lower.Skip(64).Contains(x));
        }
    }

    private sealed record HistoryBackfillCandidate(
        long WorldMinute,
        int Rank,
        long PrimaryId,
        long SecondaryId,
        HistoricalEventType EventType,
        HistoricalImportance Importance,
        TileCoordinate? Location,
        string Payload,
        IReadOnlyList<(CitizenId CitizenId, string Role)> CitizenLinks,
        IReadOnlyList<(StructureId StructureId, string Role)> StructureLinks);

    public Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, CancellationToken cancellationToken = default) =>
        CheckpointAsync(snapshot, DateTime.UtcNow, cancellationToken);

    public Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, DateTime checkpointUtc, CancellationToken cancellationToken = default) =>
        CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint: null, cancellationToken);

    internal Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, DateTime checkpointUtc, CheckpointFailurePoint? failurePoint, CancellationToken cancellationToken = default) =>
        CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint, cancellationToken);

    internal async Task<bool> UpgradeLegacyM0IfNeededAsync(
        DateTime? checkpointUtc = null,
        LegacyUpgradeFailurePoint? failurePoint = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
            var tileCount = await _context.WorldTiles.AsNoTracking().CountAsync(cancellationToken);
            var resourceCount = await _context.ResourceNodes.AsNoTracking().CountAsync(cancellationToken);
            var eventCount = await _context.ScheduledEvents.AsNoTracking().CountAsync(cancellationToken);
            var citizenCount = await _context.Citizens.AsNoTracking().CountAsync(cancellationToken);

            if (metadataRows.Count == 0)
            {
                // Later milestones have canonical tables too. Reject every
                // orphaned row group before allowing fresh-world initialization.
                _ = await HasCheckpointAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId)
            {
                throw new InvalidDataException("Expected exactly one legacy world_meta row with id 1.");
            }

            var metadata = metadataRows[0];
            if (metadata.GenerationVersion != 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (metadata.GenerationAttempt != 0 || metadata.StartingX != 0 || metadata.StartingY != 0 || metadata.WorldFingerprint.Length != 0 || tileCount != 0 || resourceCount != 0 || citizenCount != 0)
            {
                throw new InvalidDataException("The M0-to-M1 upgrade sentinel is partial or contains canonical world rows.");
            }

            var legacySnapshot = await ReadLegacyM0SnapshotAsync(metadata, cancellationToken);
            RejectCompleteM1ConfigurationFromLegacy(legacySnapshot.WorldConfiguration);
            var world = new WorldGenerator().Generate(legacySnapshot.Seed, WorldGenerationConfiguration.Default);
            var snapshot = new SimulationPersistenceSnapshot(
                legacySnapshot.Seed,
                legacySnapshot.WorldMinute,
                legacySnapshot.WorldSchemaVersion,
                legacySnapshot.SimulationRulesVersion,
                legacySnapshot.ApplicationVersion,
                world.Configuration.CanonicalJson,
                legacySnapshot.Counters,
                legacySnapshot.ScheduledEvents,
                world);
            var upgradeUtc = checkpointUtc ?? DateTime.UtcNow;
            if (upgradeUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Legacy upgrade metadata must be UTC.", nameof(checkpointUtc));
            await WriteSnapshotRowsAsync(snapshot, world, metadata.CreatedUtc, upgradeUtc, cancellationToken);

            if (failurePoint == LegacyUpgradeFailurePoint.AfterRowsWritten)
            {
                throw new InvalidOperationException("Controlled legacy upgrade failure requested by the test hook.");
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task CheckpointCoreAsync(SimulationPersistenceSnapshot snapshot, DateTime checkpointUtc, CheckpointFailurePoint? failurePoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var world = ValidateSnapshot(snapshot);
        if (checkpointUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Checkpoint metadata must be UTC.", nameof(checkpointUtc));

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.ChangeTracker.Clear();
            var existingMetadata = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
            if (existingMetadata.Count > 1) throw new InvalidDataException("A checkpoint cannot replace a database with multiple world_meta rows.");
            if (existingMetadata.Count == 1) LivingValidation.ValidateRetainedFacts(existingMetadata[0].LivingStateJson, snapshot.LivingStateJson);
            var createdUtc = existingMetadata.Count == 1 ? existingMetadata[0].CreatedUtc : checkpointUtc;

            await WriteSnapshotRowsAsync(snapshot, world, createdUtc, checkpointUtc, cancellationToken);

            if (failurePoint == CheckpointFailurePoint.AfterRowsWritten) throw new InvalidOperationException("Controlled checkpoint failure requested by the test hook.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task WriteSnapshotRowsAsync(
        SimulationPersistenceSnapshot snapshot,
        WorldMap world,
        DateTime createdUtc,
        DateTime checkpointUtc,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var existingHistoricalEvents = await _context.HistoricalEvents.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var existingCitizenLinks = await _context.HistoricalEventCitizens.AsNoTracking().OrderBy(row => row.EventId).ThenBy(row => row.CitizenId).ThenBy(row => row.Role).ToListAsync(cancellationToken);
        var existingStructureLinks = await _context.HistoricalEventStructures.AsNoTracking().OrderBy(row => row.EventId).ThenBy(row => row.StructureId).ThenBy(row => row.Role).ToListAsync(cancellationToken);
        var existingStatistics = await _context.StatisticsSamples.AsNoTracking().OrderBy(row => row.WorldMinute).ToListAsync(cancellationToken);
        var existingMemories = await _context.Memories.AsNoTracking().OrderBy(row => row.CitizenId).ThenBy(row => row.EventId).ThenBy(row => row.MemoryType).ToListAsync(cancellationToken);
        var existingHistoryState = await _context.HistoryStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        ValidateHistoryPrefix(snapshot, existingHistoricalEvents, existingCitizenLinks, existingStructureLinks, existingStatistics, existingMemories, existingHistoryState);
        if (snapshot.HistoryVersion == SimulationEngine.HistoryVersion)
        {
            await WriteM6SnapshotRowsAsync(snapshot, world, createdUtc, checkpointUtc, existingHistoricalEvents, existingCitizenLinks, existingStructureLinks, existingStatistics, existingMemories, cancellationToken);
            return;
        }
        _context.StructureContributions.RemoveRange(await _context.StructureContributions.ToListAsync(cancellationToken));
        _context.Structures.RemoveRange(await _context.Structures.ToListAsync(cancellationToken));
        _context.Relationships.RemoveRange(await _context.Relationships.ToListAsync(cancellationToken));
        _context.Households.RemoveRange(await _context.Households.ToListAsync(cancellationToken));
        _context.ResourceStates.RemoveRange(await _context.ResourceStates.ToListAsync(cancellationToken));
        _context.SettlementStates.RemoveRange(await _context.SettlementStates.ToListAsync(cancellationToken));
        _context.ResourceNodes.RemoveRange(await _context.ResourceNodes.ToListAsync(cancellationToken));
        _context.WorldTiles.RemoveRange(await _context.WorldTiles.ToListAsync(cancellationToken));
        _context.ScheduledEvents.RemoveRange(await _context.ScheduledEvents.ToListAsync(cancellationToken));
        _context.Citizens.RemoveRange(await _context.Citizens.ToListAsync(cancellationToken));
        _context.WorldMeta.RemoveRange(await _context.WorldMeta.ToListAsync(cancellationToken));
        await _context.SaveChangesAsync(cancellationToken);

        var detectChanges = _context.ChangeTracker.AutoDetectChangesEnabled;
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            _context.WorldMeta.Add(ToWorldMetaRow(snapshot, world, createdUtc, checkpointUtc));
            _context.ScheduledEvents.AddRange(snapshot.ScheduledEvents.Select(ToScheduledEventRow));
            _context.Citizens.AddRange(snapshot.Citizens.Select(ToCitizenRow));
            _context.WorldTiles.AddRange(world.Tiles.Select(tile => ToWorldTileRow(tile, world.Width)).ToArray());
            _context.ResourceNodes.AddRange(world.Resources.Select(node => ToResourceNodeRow(node, world.Width)).ToArray());
            if (snapshot.SurvivalVersion == SimulationEngine.SurvivalVersion)
            {
                if (snapshot.Settlement is null) throw new InvalidDataException("M3 checkpoints require settlement state.");
                _context.ResourceStates.AddRange(snapshot.ResourceStates.OrderBy(x => x.ResourceNodeId.Value).Select(x => new ResourceStateRow { ResourceNodeId = x.ResourceNodeId.Value, CurrentQuantity = x.CurrentQuantity }));
                _context.SettlementStates.Add(new SettlementStateRow { Id = SettlementState.SingletonId, FoodStored = snapshot.Settlement.FoodStored, WoodStored = snapshot.Settlement.WoodStored, StoneStored = snapshot.Settlement.StoneStored, BaseStorageCapacity = snapshot.Settlement.BaseStorageCapacity, DemandUpdatedMinute = snapshot.Settlement.DemandUpdatedMinute, ExposureConsequencesStartMinute = snapshot.Settlement.ExposureConsequencesStartMinute });
            }
            if (snapshot.SettlementVersion == SimulationEngine.SettlementVersion)
            {
                _context.Structures.AddRange(snapshot.Structures.Select(ToStructureRow));
                _context.StructureContributions.AddRange(snapshot.StructureContributions.Select(ToStructureContributionRow));
            }
            if (snapshot.SocialVersion == SimulationEngine.SocialVersion)
            {
                _context.Relationships.AddRange(snapshot.Relationships.Select(ToRelationshipRow));
                _context.Households.AddRange(snapshot.Households.Select(ToHouseholdRow));
            }
            if (snapshot.HistoryVersion == SimulationEngine.HistoryVersion)
            {
                if (snapshot.HistoryState is null) throw new InvalidDataException("M6 checkpoints require history state.");
                if (existingHistoryState is null) _context.HistoryStates.Add(ToHistoryStateRow(snapshot.HistoryState));
                else
                {
                    var state = await _context.HistoryStates.SingleAsync(x => x.Id == 1, cancellationToken);
                    CopyHistoryState(state, snapshot.HistoryState);
                }
                _context.HistoricalEvents.AddRange(snapshot.HistoricalEvents.Skip(existingHistoricalEvents.Count).Select(ToHistoricalEventRow));
                _context.HistoricalEventCitizens.AddRange(snapshot.HistoricalEventCitizens.Skip(existingCitizenLinks.Count).Select(ToHistoricalEventCitizenLinkRow));
                _context.HistoricalEventStructures.AddRange(snapshot.HistoricalEventStructures.Skip(existingStructureLinks.Count).Select(ToHistoricalEventStructureLinkRow));
                _context.StatisticsSamples.AddRange(snapshot.StatisticsSamples.Skip(existingStatistics.Count).Select(ToStatisticsSampleRow));
                await ReconcileMemoryRowsAsync(snapshot.Memories, cancellationToken);
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        }
    }

    public async Task<SimulationPersistenceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
        if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId) throw new InvalidDataException($"Expected exactly one world_meta row with id {SingletonWorldId}, found {metadataRows.Count}.");
        var metadata = metadataRows[0];
        if (metadata.SurvivalVersion is not (0 or SimulationEngine.SurvivalVersion)) throw new NotSupportedException($"Survival version '{metadata.SurvivalVersion}' is not supported.");
        if (metadata.SettlementVersion is not (0 or SimulationEngine.SettlementVersion)) throw new NotSupportedException($"Settlement version '{metadata.SettlementVersion}' is not supported.");
        if (metadata.SurvivalVersion == 0 && SimulationEngine.HistorySystemsEnabled(metadata.SimulationRulesVersion)) throw new InvalidDataException("History rules require survival version 1.");
        if (metadata.SettlementVersion == 0 && SimulationEngine.HistorySystemsEnabled(metadata.SimulationRulesVersion)) throw new InvalidDataException("History rules require settlement version 1.");
        var configuration = ParseConfiguration(metadata.WorldConfigurationJson);
        if (metadata.GenerationVersion != WorldGenerationConfiguration.CurrentVersion) throw new NotSupportedException($"World generation version '{metadata.GenerationVersion}' is not supported.");
        var seed = WorldSeedCodec.Decode(metadata.WorldSeedValue);
        if (metadata.GenerationAttempt < 0 || metadata.GenerationAttempt >= configuration.MaximumAttempts) throw new InvalidDataException("The persisted generation attempt is outside the configured attempt range.");

        var tileRows = await _context.WorldTiles.AsNoTracking().OrderBy(row => row.TileIndex).ToListAsync(cancellationToken);
        var expectedTileCount = checked(configuration.Width * configuration.Height);
        if (tileRows.Count != expectedTileCount) throw new InvalidDataException($"Expected {expectedTileCount} persisted world tiles, found {tileRows.Count}.");
        var tiles = new WorldTile[tileRows.Count];
        var coordinates = new HashSet<TileCoordinate>();
        for (var expectedIndex = 0; expectedIndex < tileRows.Count; expectedIndex++)
        {
            var row = tileRows[expectedIndex];
            if (row.TileIndex != expectedIndex) throw new InvalidDataException("Persisted world tile indexes must be complete row-major indexes.");
            if (row.X < 0 || row.X >= configuration.Width || row.Y < 0 || row.Y >= configuration.Height) throw new InvalidDataException("Persisted world tile coordinates are outside the configured world.");
            var expectedCoordinate = TileCoordinate.FromIndex(row.TileIndex, configuration.Width);
            var coordinate = new TileCoordinate(row.X, row.Y);
            if (coordinate != expectedCoordinate || !coordinates.Add(coordinate)) throw new InvalidDataException("Persisted world tile coordinates are not unique row-major coordinates.");
            if (!Enum.IsDefined((TerrainType)row.Terrain)) throw new InvalidDataException($"Persisted world tile {row.TileIndex} has an unknown terrain value.");
            try { tiles[expectedIndex] = new WorldTile(coordinate, (TerrainType)row.Terrain, row.Elevation, row.Fertility, row.WaterAccess, row.Walkable, row.MovementCost); }
            catch (ArgumentException exception) { throw new InvalidDataException($"Persisted world tile {row.TileIndex} has invalid values.", exception); }
        }

        var resourceRows = await _context.ResourceNodes.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var resources = new ResourceNode[resourceRows.Count];
        var resourceIds = new HashSet<long>();
        foreach (var (row, index) in resourceRows.Select((row, index) => (row, index)))
        {
            if (row.Id <= 0 || !resourceIds.Add(row.Id)) throw new InvalidDataException("Persisted resource node IDs must be unique positive values.");
            if (row.TileIndex < 0 || row.TileIndex >= tileRows.Count || row.X < 0 || row.X >= configuration.Width || row.Y < 0 || row.Y >= configuration.Height) throw new InvalidDataException($"Persisted resource node {row.Id} references an invalid tile.");
            var tileCoordinate = TileCoordinate.FromIndex(row.TileIndex, configuration.Width);
            if (new TileCoordinate(row.X, row.Y) != tileCoordinate) throw new InvalidDataException($"Persisted resource node {row.Id} has a mismatched tile coordinate.");
            if (!Enum.IsDefined((ResourceType)row.Resource)) throw new InvalidDataException($"Persisted resource node {row.Id} has an unknown resource value.");
            try { resources[index] = new ResourceNode(new ResourceNodeId(row.Id), tileCoordinate, (ResourceType)row.Resource, row.InitialQuantity, row.MaximumQuantity, row.RegenerationPotential); }
            catch (ArgumentException exception) { throw new InvalidDataException($"Persisted resource node {row.Id} has invalid values.", exception); }
        }

        if (metadata.StartingX < 0 || metadata.StartingX >= configuration.Width || metadata.StartingY < 0 || metadata.StartingY >= configuration.Height) throw new InvalidDataException("Persisted starting site is outside the configured world.");
        WorldMap world;
        try { world = new WorldMap(seed, metadata.GenerationVersion, metadata.GenerationAttempt, configuration, tiles, resources, new TileCoordinate(metadata.StartingX, metadata.StartingY)); }
        catch (ArgumentException exception) { throw new InvalidDataException("Persisted world data is not a valid world map.", exception); }
        if (string.IsNullOrWhiteSpace(metadata.WorldFingerprint) || !string.Equals(metadata.WorldFingerprint, world.Fingerprint, StringComparison.Ordinal)) throw new InvalidDataException("Persisted world fingerprint does not match the canonical world rows.");

        WorldMinute minute;
        try { minute = new WorldMinute(metadata.WorldMinute); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("The persisted world minute must be non-negative.", exception); }
        var events = await _context.ScheduledEvents.AsNoTracking().OrderBy(row => row.DueWorldMinute).ThenBy(row => row.Priority).ThenBy(row => row.EntitySortKey).ThenBy(row => row.Sequence).ToListAsync(cancellationToken);
        if (metadata.SurvivalVersion == 0 && (await _context.ResourceStates.AsNoTracking().AnyAsync(cancellationToken) || await _context.SettlementStates.AsNoTracking().AnyAsync(cancellationToken) || await _context.Structures.AsNoTracking().AnyAsync(cancellationToken) || await _context.StructureContributions.AsNoTracking().AnyAsync(cancellationToken) || events.Any(row => row.EventName is CitizenEventNames.SurvivalCheck or CitizenEventNames.ResourceRegenerate or CitizenEventNames.SettlementEvaluateDemand)))
            throw new InvalidDataException("M2/pre-M2 checkpoint contains partial M3 state.");
        var resourceStateRows = await _context.ResourceStates.AsNoTracking().OrderBy(row => row.ResourceNodeId).ToListAsync(cancellationToken);
        var settlementRows = await _context.SettlementStates.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var citizensRows = await _context.Citizens.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var relationshipRows = await _context.Relationships.AsNoTracking().OrderBy(row => row.CitizenAId).ThenBy(row => row.CitizenBId).ToListAsync(cancellationToken);
        var householdRows = await _context.Households.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var structureRows = await _context.Structures.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var contributionRows = await _context.StructureContributions.AsNoTracking().OrderBy(row => row.StructureId).ThenBy(row => row.CitizenId).ToListAsync(cancellationToken);
        if (metadata.HistoryVersion is not (0 or SimulationEngine.HistoryVersion)) throw new NotSupportedException($"History version '{metadata.HistoryVersion}' is not supported.");
        var historicalEventRows = await _context.HistoricalEvents.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var historicalCitizenLinkRows = await _context.HistoricalEventCitizens.AsNoTracking().OrderBy(row => row.EventId).ThenBy(row => row.CitizenId).ThenBy(row => row.Role).ToListAsync(cancellationToken);
        var historicalStructureLinkRows = await _context.HistoricalEventStructures.AsNoTracking().OrderBy(row => row.EventId).ThenBy(row => row.StructureId).ThenBy(row => row.Role).ToListAsync(cancellationToken);
        var statisticsRows = await _context.StatisticsSamples.AsNoTracking().OrderBy(row => row.WorldMinute).ToListAsync(cancellationToken);
        var historyStateRows = await _context.HistoryStates.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var memoryRows = await _context.Memories.AsNoTracking().OrderBy(row => row.CitizenId).ThenBy(row => row.EventId).ThenBy(row => row.MemoryType).ToListAsync(cancellationToken);
        if (metadata.HistoryVersion == 0 && (historicalEventRows.Count != 0 || historicalCitizenLinkRows.Count != 0 || historicalStructureLinkRows.Count != 0 || statisticsRows.Count != 0 || historyStateRows.Count != 0 || memoryRows.Count != 0 || events.Any(row => row.EventName == CitizenEventNames.StatisticsSample))) throw new InvalidDataException("Pre-M6 checkpoint contains partial history state.");
        if (metadata.HistoryVersion == SimulationEngine.HistoryVersion && (historyStateRows.Count != 1 || historyStateRows[0].Id != 1)) throw new InvalidDataException("M6 checkpoint requires one history state row.");
        var historicalEvents = historicalEventRows.Select(row => FromHistoricalEventRow(row, minute)).ToArray();
        if (historicalEvents.Select(x => x.Id.Value).Distinct().Count() != historicalEvents.Length || historicalEvents.Zip(historicalEvents.Skip(1)).Any(x => x.Second.Id.Value != x.First.Id.Value + 1)) throw new InvalidDataException("Historical event IDs must be a contiguous ordered prefix.");
        var historicalCitizenLinks = historicalCitizenLinkRows.Select(FromHistoricalEventCitizenLinkRow).ToArray();
        var historicalStructureLinks = historicalStructureLinkRows.Select(FromHistoricalEventStructureLinkRow).ToArray();
        var statistics = statisticsRows.Select(FromStatisticsSampleRow).ToArray();
        var memories = memoryRows.Select(FromCitizenMemoryRow).ToArray();
        if (historicalCitizenLinks.Any(x => !historicalEvents.Any(e => e.Id == x.HistoricalEventId) || !citizensRows.Any(c => c.Id == x.CitizenId.Value)) || historicalStructureLinks.Any(x => !historicalEvents.Any(e => e.Id == x.HistoricalEventId) || !structureRows.Any(s => s.Id == x.StructureId.Value)) || memories.Any(x => !historicalEvents.Any(e => e.Id == x.HistoricalEventId) || !citizensRows.Any(c => c.Id == x.CitizenId.Value))) throw new InvalidDataException("Historical links must reference existing canonical entities.");
        if (metadata.CitizenGenerationVersion == SimulationEngine.CitizenGenerationVersion && metadata.SocialVersion == 0 && citizensRows.Count != CitizenGenerator.FounderCount) throw new InvalidDataException("A pre-M5 M2 checkpoint must contain exactly 20 citizens.");
        if (metadata.CitizenGenerationVersion == 0 && citizensRows.Count != 0) throw new InvalidDataException("A pre-M2 checkpoint must not contain citizen rows.");
        if (metadata.CitizenGenerationVersion is not (0 or SimulationEngine.CitizenGenerationVersion)) throw new NotSupportedException($"Citizen generation version '{metadata.CitizenGenerationVersion}' is not supported.");
        Citizen[] citizens;
        try { citizens = citizensRows.Select(row => FromCitizenRow(row, world, minute, metadata.SurvivalVersion)).ToArray(); }
        catch (InvalidDataException) { throw; }
        catch (ArgumentException exception) { throw new InvalidDataException("A persisted citizen row is not valid.", exception); }
        SimulationPersistenceSnapshot snapshot;
        try
        {
            var survival = metadata.SurvivalVersion == SimulationEngine.SurvivalVersion;
            if (survival && (resourceStateRows.Count != resources.Length || resourceStateRows.Select(x => x.ResourceNodeId).Distinct().Count() != resourceStateRows.Count || settlementRows.Count != 1 || settlementRows[0].Id != SettlementState.SingletonId)) throw new InvalidDataException("M3 checkpoint resource or settlement state is incomplete.");
            if (!survival && (resourceStateRows.Count != 0 || settlementRows.Count != 0)) throw new InvalidDataException("Pre-M3 checkpoint contains M3 rows.");
            var states = resourceStateRows.Select(x => new ResourceState(new ResourceNodeId(x.ResourceNodeId), x.CurrentQuantity)).ToArray();
            var settlement = settlementRows.Count == 1 ? new SettlementState(settlementRows[0].FoodStored, settlementRows[0].WoodStored, settlementRows[0].StoneStored, settlementRows[0].BaseStorageCapacity, settlementRows[0].DemandUpdatedMinute, settlementRows[0].ExposureConsequencesStartMinute) : null;
            var persistedStructures = structureRows.Select(row => FromStructureRow(row, world)).ToArray();
            var persistedContributions = contributionRows.Select(FromStructureContributionRow).ToArray();
            if (metadata.SettlementVersion == 0 && (structureRows.Count != 0 || contributionRows.Count != 0)) throw new InvalidDataException("Pre-M4 checkpoint contains M4 structure rows.");
            if (metadata.SocialVersion == 0 && (relationshipRows.Count != 0 || householdRows.Count != 0)) throw new InvalidDataException("Pre-M5 checkpoint contains social rows.");
            var historyState = historyStateRows.Count == 1 ? FromHistoryStateRow(historyStateRows[0], minute) : null;
            snapshot = new SimulationPersistenceSnapshot(seed, minute, metadata.WorldSchemaVersion, metadata.SimulationRulesVersion, metadata.ApplicationVersion, configuration.CanonicalJson, new DeterministicCountersSnapshot(metadata.NextEntityId, metadata.NextHistoricalEventId, metadata.NextScheduledEventSequence), events.Select(ToScheduledEventSnapshot).ToArray(), world, citizens, metadata.CitizenGenerationVersion, states, settlement, metadata.SurvivalVersion, metadata.SettlementVersion, persistedStructures, persistedContributions, metadata.SocialVersion, relationshipRows.Select(FromRelationshipRow).ToArray(), householdRows.Select(FromHouseholdRow).ToArray(), metadata.HistoryVersion, historyState, historicalEvents, historicalCitizenLinks, historicalStructureLinks, statistics, memories, metadata.LivingStateJson);
            SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        }
        catch (ArgumentException exception) { throw new InvalidDataException("The persisted checkpoint is not a valid persistence snapshot.", exception); }
        return snapshot;
    }

    public async Task<bool> HasCheckpointAsync(CancellationToken cancellationToken = default)
    {
        var metadataCount = await _context.WorldMeta.AsNoTracking().CountAsync(cancellationToken);
        var scheduledEventCount = await _context.ScheduledEvents.AsNoTracking().CountAsync(cancellationToken);
        var tileCount = await _context.WorldTiles.AsNoTracking().CountAsync(cancellationToken);
        var resourceCount = await _context.ResourceNodes.AsNoTracking().CountAsync(cancellationToken);
        var resourceStateCount = await _context.ResourceStates.AsNoTracking().CountAsync(cancellationToken);
        var settlementCount = await _context.SettlementStates.AsNoTracking().CountAsync(cancellationToken);
        var citizenCount = await _context.Citizens.AsNoTracking().CountAsync(cancellationToken);
        var structureCount = await _context.Structures.AsNoTracking().CountAsync(cancellationToken);
        var contributionCount = await _context.StructureContributions.AsNoTracking().CountAsync(cancellationToken);
        var relationshipCount = await _context.Relationships.AsNoTracking().CountAsync(cancellationToken);
        var householdCount = await _context.Households.AsNoTracking().CountAsync(cancellationToken);
        if (metadataCount == 0 && (scheduledEventCount > 0 || tileCount > 0 || resourceCount > 0 || resourceStateCount > 0 || settlementCount > 0 || citizenCount > 0 || structureCount > 0 || contributionCount > 0 || relationshipCount > 0 || householdCount > 0)) throw new InvalidDataException("Checkpoint rows exist without a world_meta checkpoint.");
        if (metadataCount == 0 &&
            (await _context.HistoryStates.AnyAsync(cancellationToken) ||
             await _context.HistoricalEvents.AnyAsync(cancellationToken) ||
             await _context.HistoricalEventCitizens.AnyAsync(cancellationToken) ||
             await _context.HistoricalEventStructures.AnyAsync(cancellationToken) ||
             await _context.StatisticsSamples.AnyAsync(cancellationToken) ||
             await _context.Memories.AnyAsync(cancellationToken)))
            throw new InvalidDataException("History rows exist without a world_meta checkpoint.");
        if (metadataCount > 1) throw new InvalidDataException("More than one world_meta checkpoint exists.");
        return metadataCount == 1;
    }

    private static WorldMap ValidateSnapshot(SimulationPersistenceSnapshot snapshot)
    {
        SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        var world = snapshot.World ?? throw new InvalidDataException("An M1 checkpoint requires a persisted WorldMap.");
        if (world.OriginalSeed != snapshot.Seed) throw new InvalidDataException("The world seed does not match the snapshot seed.");
        var configuration = ParseConfiguration(snapshot.WorldConfiguration);
        if (!string.Equals(configuration.CanonicalJson, world.Configuration.CanonicalJson, StringComparison.Ordinal)) throw new InvalidDataException("The world configuration does not match the persisted WorldMap configuration.");
        if (world.GenerationVersion != configuration.Version) throw new InvalidDataException("The world generation version does not match its configuration.");
        return world;
    }

    private static WorldGenerationConfiguration ParseConfiguration(string configuration)
    {
        try
        {
            var parsed = WorldGenerationConfiguration.FromCanonicalJson(configuration);
            if (!string.Equals(parsed.CanonicalJson, configuration, StringComparison.Ordinal)) throw new InvalidDataException("The persisted world configuration is not canonical JSON.");
            return parsed;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or NotSupportedException) { throw new InvalidDataException("The persisted world configuration is not a valid canonical M1 configuration.", exception); }
    }

    private static void RejectCompleteM1ConfigurationFromLegacy(string configuration)
    {
        try
        {
            _ = WorldGenerationConfiguration.FromCanonicalJson(configuration);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            // Valid arbitrary M0 JSON is intentionally accepted and normalized during upgrade.
            return;
        }

        throw new InvalidDataException("The legacy sentinel contains a complete M1 world configuration but no persisted world rows.");
    }

    private async Task<SimulationPersistenceSnapshot> ReadLegacyM0SnapshotAsync(WorldMetaRow metadata, CancellationToken cancellationToken)
    {
        var seed = WorldSeedCodec.Decode(metadata.WorldSeedValue);
        WorldMinute worldMinute;
        try
        {
            worldMinute = new WorldMinute(metadata.WorldMinute);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The legacy world minute must be non-negative.", exception);
        }

        if (string.IsNullOrWhiteSpace(metadata.WorldSchemaVersion) || string.IsNullOrWhiteSpace(metadata.SimulationRulesVersion) || string.IsNullOrWhiteSpace(metadata.ApplicationVersion))
        {
            throw new InvalidDataException("Legacy checkpoint metadata versions and application version must be non-empty.");
        }
        if (!string.Equals(metadata.SimulationRulesVersion, "m0-rng1", StringComparison.Ordinal)) throw new InvalidDataException("A legacy M0 checkpoint must use m0-rng1 rules.");

        try
        {
            using var configuration = JsonDocument.Parse(metadata.WorldConfigurationJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The legacy world configuration is not valid JSON.", exception);
        }

        var events = await _context.ScheduledEvents.AsNoTracking()
            .OrderBy(row => row.DueWorldMinute)
            .ThenBy(row => row.Priority)
            .ThenBy(row => row.EntitySortKey)
            .ThenBy(row => row.Sequence)
            .ToListAsync(cancellationToken);
        var scheduledEvents = events.Select(ToScheduledEventSnapshot).ToArray();
        if (scheduledEvents.Any(item => item.Order.DueWorldMinute < worldMinute))
        {
            throw new InvalidDataException("A legacy scheduled event cannot be due before the persisted world minute.");
        }

        SimulationPersistenceSnapshot snapshot;
        try
        {
            snapshot = new SimulationPersistenceSnapshot(
                seed,
                worldMinute,
                metadata.WorldSchemaVersion,
                metadata.SimulationRulesVersion,
                metadata.ApplicationVersion,
                metadata.WorldConfigurationJson,
                new DeterministicCountersSnapshot(metadata.NextEntityId, metadata.NextHistoricalEventId, metadata.NextScheduledEventSequence),
                scheduledEvents);
            SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The legacy M0 checkpoint is not a valid persistence snapshot.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException("The legacy M0 checkpoint has unsupported compatibility metadata.", exception);
        }

        return snapshot;
    }

    private static WorldMetaRow ToWorldMetaRow(SimulationPersistenceSnapshot snapshot, WorldMap world, DateTime createdUtc, DateTime checkpointUtc) => new()
    {
        Id = SingletonWorldId, WorldSeedValue = WorldSeedCodec.Encode(snapshot.Seed), WorldMinute = snapshot.WorldMinute.Value,
        WorldSchemaVersion = snapshot.WorldSchemaVersion, SimulationRulesVersion = snapshot.SimulationRulesVersion, ApplicationVersion = snapshot.ApplicationVersion,
        WorldConfigurationJson = world.Configuration.CanonicalJson, GenerationVersion = world.GenerationVersion, GenerationAttempt = world.GenerationAttempt,
        StartingX = world.StartingSite.X, StartingY = world.StartingSite.Y, WorldFingerprint = world.Fingerprint,
        CitizenGenerationVersion = snapshot.CitizenGenerationVersion, SurvivalVersion = snapshot.SurvivalVersion, SettlementVersion = snapshot.SettlementVersion, SocialVersion = snapshot.SocialVersion, HistoryVersion = snapshot.HistoryVersion, NextEntityId = snapshot.Counters.NextEntityId, NextHistoricalEventId = snapshot.Counters.NextHistoricalEventId, NextScheduledEventSequence = snapshot.Counters.NextScheduledEventSequence,
        LivingStateJson = snapshot.LivingStateJson,
        CreatedUtc = createdUtc, LastCheckpointUtc = checkpointUtc
    };

    private static HistoryStateRow ToHistoryStateRow(HistoryState state) => new()
    {
        Id = 1, HistoryStartMinute = state.HistoryStartMinute, HistoryStartEventId = state.HistoryStartEventId,
        PeriodStartMinute = state.PeriodStartMinute, BirthsSinceSample = state.BirthsSinceSample, DeathsSinceSample = state.DeathsSinceSample,
        FoodProducedSinceSample = state.FoodProducedSinceSample, FoodConsumedSinceSample = state.FoodConsumedSinceSample,
        ActiveFoodShortage = state.ActiveFoodShortage, PopulationMilestoneWatermark = state.PopulationMilestoneWatermark
    };
    private static void CopyHistoryState(HistoryStateRow row, HistoryState state)
    {
        row.HistoryStartMinute = state.HistoryStartMinute; row.HistoryStartEventId = state.HistoryStartEventId;
        row.PeriodStartMinute = state.PeriodStartMinute; row.BirthsSinceSample = state.BirthsSinceSample; row.DeathsSinceSample = state.DeathsSinceSample;
        row.FoodProducedSinceSample = state.FoodProducedSinceSample; row.FoodConsumedSinceSample = state.FoodConsumedSinceSample;
        row.ActiveFoodShortage = state.ActiveFoodShortage; row.PopulationMilestoneWatermark = state.PopulationMilestoneWatermark;
    }
    private static HistoricalEventRow ToHistoricalEventRow(HistoricalEvent item) => new()
    {
        Id = item.Id.Value, WorldMinute = item.WorldMinute, EventType = (int)item.EventType, Importance = (int)item.Importance, Origin = (int)item.Origin,
        LocationX = item.Location?.X, LocationY = item.Location?.Y, PayloadJson = item.PayloadJson, SchemaVersion = item.SchemaVersion
    };
    private static HistoricalEventCitizenLinkRow ToHistoricalEventCitizenLinkRow(HistoricalEventCitizenLink link) => new() { EventId = link.HistoricalEventId.Value, CitizenId = link.CitizenId.Value, Role = link.Role };
    private static HistoricalEventStructureLinkRow ToHistoricalEventStructureLinkRow(HistoricalEventStructureLink link) => new() { EventId = link.HistoricalEventId.Value, StructureId = link.StructureId.Value, Role = link.Role };
    private static StatisticsSampleRow ToStatisticsSampleRow(StatisticsSample sample) => new()
    {
        WorldMinute = sample.WorldMinute, PeriodStartMinute = sample.PeriodStartMinute, Population = sample.Population, BirthsPeriod = sample.BirthsPeriod, DeathsPeriod = sample.DeathsPeriod,
        FoodStored = sample.FoodStored, FoodProducedPeriod = sample.FoodProducedPeriod, FoodConsumedPeriod = sample.FoodConsumedPeriod, WoodStored = sample.WoodStored, StoneStored = sample.StoneStored,
        ShelterCapacity = sample.ShelterCapacity, AverageHealth = sample.AverageHealth, AverageHunger = sample.AverageHunger
    };
    private static CitizenMemoryRow ToCitizenMemoryRow(CitizenMemory memory) => new()
    {
        CitizenId = memory.CitizenId.Value, EventId = memory.HistoricalEventId.Value, MemoryType = (int)memory.MemoryType, Importance = (int)memory.Importance,
        EmotionalValence = memory.EmotionalValence, CreatedMinute = memory.CreatedMinute
    };

    private static void ValidateHistoryPrefix(SimulationPersistenceSnapshot snapshot,
        List<HistoricalEventRow> events, List<HistoricalEventCitizenLinkRow> citizenLinks,
        List<HistoricalEventStructureLinkRow> structureLinks, List<StatisticsSampleRow> statistics,
        List<CitizenMemoryRow> memories, HistoryStateRow? state)
    {
        if (snapshot.HistoryVersion == 0)
        {
            if (events.Count != 0 || citizenLinks.Count != 0 || structureLinks.Count != 0 || statistics.Count != 0 || memories.Count != 0 || state is not null) throw new InvalidDataException("Pre-M6 checkpoint contains history rows.");
            return;
        }
        if (state is null) { if (events.Count != 0 || citizenLinks.Count != 0 || structureLinks.Count != 0 || statistics.Count != 0 || memories.Count != 0) throw new InvalidDataException("History rows exist without history state."); return; }
        // Events, links and statistics are append-only.  Memories are bounded
        // mutable records keyed by (citizen, event, type), so pruning or a
        // canonical field update is valid and is reconciled below.
        if (events.Count > snapshot.HistoricalEvents.Count || citizenLinks.Count > snapshot.HistoricalEventCitizens.Count || structureLinks.Count > snapshot.HistoricalEventStructures.Count || statistics.Count > snapshot.StatisticsSamples.Count) throw new InvalidDataException("Persisted history is not a prefix of the checkpoint history.");
        for (var i = 0; i < events.Count; i++)
        {
            var expected = snapshot.HistoricalEvents[i]; var actual = events[i];
            if (actual.Id != expected.Id.Value || actual.WorldMinute != expected.WorldMinute || actual.EventType != (int)expected.EventType || actual.Importance != (int)expected.Importance || actual.Origin != (int)expected.Origin || actual.LocationX != expected.Location?.X || actual.LocationY != expected.Location?.Y || actual.PayloadJson != expected.PayloadJson || actual.SchemaVersion != expected.SchemaVersion) throw new InvalidDataException("Persisted historical event conflicts with the canonical prefix.");
        }
        for (var i = 0; i < citizenLinks.Count; i++)
        {
            var expected = snapshot.HistoricalEventCitizens[i]; var actual = citizenLinks[i];
            if (actual.EventId != expected.HistoricalEventId.Value || actual.CitizenId != expected.CitizenId.Value || actual.Role != expected.Role) throw new InvalidDataException("Persisted historical citizen link conflicts with the canonical prefix.");
        }
        for (var i = 0; i < structureLinks.Count; i++)
        {
            var expected = snapshot.HistoricalEventStructures[i]; var actual = structureLinks[i];
            if (actual.EventId != expected.HistoricalEventId.Value || actual.StructureId != expected.StructureId.Value || actual.Role != expected.Role) throw new InvalidDataException("Persisted historical structure link conflicts with the canonical prefix.");
        }
        for (var i = 0; i < statistics.Count; i++)
        {
            var expected = snapshot.StatisticsSamples[i]; var actual = statistics[i];
            if (actual.WorldMinute != expected.WorldMinute || actual.PeriodStartMinute != expected.PeriodStartMinute || actual.Population != expected.Population || actual.BirthsPeriod != expected.BirthsPeriod || actual.DeathsPeriod != expected.DeathsPeriod || actual.FoodStored != expected.FoodStored || actual.FoodProducedPeriod != expected.FoodProducedPeriod || actual.FoodConsumedPeriod != expected.FoodConsumedPeriod || actual.WoodStored != expected.WoodStored || actual.StoneStored != expected.StoneStored || actual.ShelterCapacity != expected.ShelterCapacity || actual.AverageHealth != expected.AverageHealth || actual.AverageHunger != expected.AverageHunger) throw new InvalidDataException("Persisted statistics conflict with the canonical prefix.");
        }
        foreach (var memory in memories)
        {
            try { _ = FromCitizenMemoryRow(memory); }
            catch (InvalidDataException exception) { throw new InvalidDataException("Persisted memory is invalid and cannot be reconciled.", exception); }
        }
    }

    private async Task WriteM6SnapshotRowsAsync(
        SimulationPersistenceSnapshot snapshot,
        WorldMap world,
        DateTime createdUtc,
        DateTime checkpointUtc,
        List<HistoricalEventRow> existingHistoricalEvents,
        List<HistoricalEventCitizenLinkRow> existingCitizenLinks,
        List<HistoricalEventStructureLinkRow> existingStructureLinks,
        List<StatisticsSampleRow> existingStatistics,
        List<CitizenMemoryRow> existingMemories,
        CancellationToken cancellationToken)
    {
        var detectChanges = _context.ChangeTracker.AutoDetectChangesEnabled;
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var persistedMetadata = await _context.WorldMeta.SingleOrDefaultAsync(x => x.Id == SingletonWorldId, cancellationToken);
            var metadataRow = ToWorldMetaRow(snapshot, world, createdUtc, checkpointUtc);
            if (persistedMetadata is null) _context.WorldMeta.Add(metadataRow);
            else
            {
                _context.Entry(persistedMetadata).CurrentValues.SetValues(metadataRow);
                _context.Entry(persistedMetadata).State = EntityState.Modified;
            }

            // M6 checkpoints are append-only for history, but the immutable world
            // rows still need to be written on the first checkpoint of a new DB.
            // Validate retained rows before committing: success must not leave
            // a checkpoint that will fail to reopen. Never repair missing rows.
            var persistedTiles = await _context.WorldTiles.OrderBy(row => row.TileIndex).ToListAsync(cancellationToken);
            if (persistedTiles.Count == 0 && persistedMetadata is null)
            {
                _context.WorldTiles.AddRange(world.Tiles.Select(tile => ToWorldTileRow(tile, world.Width)));
            }
            else if (persistedTiles.Count != world.Tiles.Count)
            {
                throw new InvalidDataException("The persisted world tile set is incomplete.");
            }
            else
            {
                for (var index = 0; index < persistedTiles.Count; index++)
                {
                    var actual = persistedTiles[index];
                    var expected = ToWorldTileRow(world.Tiles[index], world.Width);
                    if (actual.TileIndex != expected.TileIndex || actual.X != expected.X || actual.Y != expected.Y ||
                        actual.Terrain != expected.Terrain || actual.Elevation != expected.Elevation ||
                        actual.Fertility != expected.Fertility || actual.WaterAccess != expected.WaterAccess ||
                        actual.Walkable != expected.Walkable || actual.MovementCost != expected.MovementCost)
                        throw new InvalidDataException("Persisted immutable world tiles conflict with the checkpoint.");
                }
            }
            var persistedNodes = await _context.ResourceNodes.OrderBy(row => row.Id).ToListAsync(cancellationToken);
            if (persistedNodes.Count == 0 && persistedMetadata is null)
            {
                _context.ResourceNodes.AddRange(world.Resources.Select(node => ToResourceNodeRow(node, world.Width)));
            }
            else if (persistedNodes.Count != world.Resources.Count)
            {
                throw new InvalidDataException("The persisted resource node set is incomplete.");
            }
            else
            {
                for (var index = 0; index < persistedNodes.Count; index++)
                {
                    var actual = persistedNodes[index];
                    var expected = ToResourceNodeRow(world.Resources[index], world.Width);
                    if (actual.Id != expected.Id || actual.TileIndex != expected.TileIndex ||
                        actual.X != expected.X || actual.Y != expected.Y || actual.Resource != expected.Resource ||
                        actual.InitialQuantity != expected.InitialQuantity || actual.MaximumQuantity != expected.MaximumQuantity ||
                        actual.RegenerationPotential != expected.RegenerationPotential)
                        throw new InvalidDataException("Persisted immutable resource nodes conflict with the checkpoint.");
                }
            }

            _context.ScheduledEvents.RemoveRange(await _context.ScheduledEvents.ToListAsync(cancellationToken));

            var citizenRows = await _context.Citizens.ToListAsync(cancellationToken);
            var citizensById = citizenRows.ToDictionary(x => x.Id);
            var snapshotCitizenIds = snapshot.Citizens.Select(x => x.Id.Value).ToHashSet();
            if (citizensById.Keys.Any(id => !snapshotCitizenIds.Contains(id))) throw new InvalidDataException("An M6 checkpoint cannot remove a citizen referenced by history.");
            foreach (var citizen in snapshot.Citizens)
            {
                var row = ToCitizenRow(citizen);
                if (citizensById.TryGetValue(row.Id, out var existing)) { _context.Entry(existing).CurrentValues.SetValues(row); _context.Entry(existing).State = EntityState.Modified; }
                else _context.Citizens.Add(row);
            }

            var resourceRows = await _context.ResourceStates.ToListAsync(cancellationToken);
            var resourceById = resourceRows.ToDictionary(x => x.ResourceNodeId);
            var snapshotResourceIds = snapshot.ResourceStates.Select(x => x.ResourceNodeId.Value).ToHashSet();
            foreach (var stale in resourceRows.Where(x => !snapshotResourceIds.Contains(x.ResourceNodeId))) _context.ResourceStates.Remove(stale);
            foreach (var state in snapshot.ResourceStates)
            {
                var row = new ResourceStateRow { ResourceNodeId = state.ResourceNodeId.Value, CurrentQuantity = state.CurrentQuantity };
                if (resourceById.TryGetValue(row.ResourceNodeId, out var existing)) { _context.Entry(existing).CurrentValues.SetValues(row); _context.Entry(existing).State = EntityState.Modified; }
                else _context.ResourceStates.Add(row);
            }

            var settlementRow = await _context.SettlementStates.SingleOrDefaultAsync(x => x.Id == SettlementState.SingletonId, cancellationToken);
            if (snapshot.Settlement is null) { if (settlementRow is not null) _context.SettlementStates.Remove(settlementRow); }
            else
            {
                var row = new SettlementStateRow { Id = SettlementState.SingletonId, FoodStored = snapshot.Settlement.FoodStored, WoodStored = snapshot.Settlement.WoodStored, StoneStored = snapshot.Settlement.StoneStored, BaseStorageCapacity = snapshot.Settlement.BaseStorageCapacity, DemandUpdatedMinute = snapshot.Settlement.DemandUpdatedMinute, ExposureConsequencesStartMinute = snapshot.Settlement.ExposureConsequencesStartMinute };
                if (settlementRow is null) _context.SettlementStates.Add(row); else { _context.Entry(settlementRow).CurrentValues.SetValues(row); _context.Entry(settlementRow).State = EntityState.Modified; }
            }

            var structureRows = await _context.Structures.ToListAsync(cancellationToken);
            var structuresById = structureRows.ToDictionary(x => x.Id);
            var snapshotStructureIds = snapshot.Structures.Select(x => x.Id.Value).ToHashSet();
            if (structuresById.Keys.Any(id => !snapshotStructureIds.Contains(id))) throw new InvalidDataException("An M6 checkpoint cannot remove a structure referenced by history.");
            foreach (var structure in snapshot.Structures)
            {
                var row = ToStructureRow(structure);
                if (structuresById.TryGetValue(row.Id, out var existing)) { _context.Entry(existing).CurrentValues.SetValues(row); _context.Entry(existing).State = EntityState.Modified; }
                else _context.Structures.Add(row);
            }

            _context.StructureContributions.RemoveRange(await _context.StructureContributions.ToListAsync(cancellationToken));
            _context.Relationships.RemoveRange(await _context.Relationships.ToListAsync(cancellationToken));
            _context.Households.RemoveRange(await _context.Households.ToListAsync(cancellationToken));

            // Flush removals before re-inserting canonical replacement rows with
            // the same keys.  The surrounding checkpoint transaction preserves
            // atomicity while avoiding tracked-key collisions in EF.
            await _context.SaveChangesAsync(cancellationToken);
            _context.ScheduledEvents.AddRange(snapshot.ScheduledEvents.Select(ToScheduledEventRow));
            _context.StructureContributions.AddRange(snapshot.StructureContributions.Select(ToStructureContributionRow));
            _context.Relationships.AddRange(snapshot.Relationships.Select(ToRelationshipRow));
            _context.Households.AddRange(snapshot.Households.Select(ToHouseholdRow));

            var historyState = await _context.HistoryStates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (historyState is null) _context.HistoryStates.Add(ToHistoryStateRow(snapshot.HistoryState!));
            else
            {
                CopyHistoryState(historyState, snapshot.HistoryState!);
                _context.Entry(historyState).State = EntityState.Modified;
            }
            _context.HistoricalEvents.AddRange(snapshot.HistoricalEvents.Skip(existingHistoricalEvents.Count).Select(ToHistoricalEventRow));
            _context.HistoricalEventCitizens.AddRange(snapshot.HistoricalEventCitizens.Skip(existingCitizenLinks.Count).Select(ToHistoricalEventCitizenLinkRow));
            _context.HistoricalEventStructures.AddRange(snapshot.HistoricalEventStructures.Skip(existingStructureLinks.Count).Select(ToHistoricalEventStructureLinkRow));
            _context.StatisticsSamples.AddRange(snapshot.StatisticsSamples.Skip(existingStatistics.Count).Select(ToStatisticsSampleRow));
            await ReconcileMemoryRowsAsync(snapshot.Memories, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        }
    }

    private async Task ReconcileMemoryRowsAsync(IReadOnlyList<CitizenMemory> desired, CancellationToken cancellationToken)
    {
        var desiredByKey = desired.ToDictionary(
            memory => (memory.CitizenId.Value, memory.HistoricalEventId.Value, (int)memory.MemoryType));
        foreach (var memory in desired)
        {
            memory.Validate();
        }

        var persisted = await _context.Memories.ToListAsync(cancellationToken);
        var persistedByKey = persisted.ToDictionary(row => (row.CitizenId, row.EventId, row.MemoryType));
        foreach (var row in persisted)
        {
            if (!desiredByKey.ContainsKey((row.CitizenId, row.EventId, row.MemoryType))) _context.Memories.Remove(row);
        }
        foreach (var memory in desired)
        {
            var key = (memory.CitizenId.Value, memory.HistoricalEventId.Value, (int)memory.MemoryType);
            if (persistedByKey.TryGetValue(key, out var row))
            {
                row.Importance = (int)memory.Importance;
                row.EmotionalValence = memory.EmotionalValence;
                row.CreatedMinute = memory.CreatedMinute;
                _context.Entry(row).State = EntityState.Modified;
            }
            else
            {
                _context.Memories.Add(ToCitizenMemoryRow(memory));
            }
        }
    }

    private static HistoryState FromHistoryStateRow(HistoryStateRow row, WorldMinute currentMinute)
    {
        if (row.Id != 1) throw new InvalidDataException("History state must use singleton id 1.");
        try { return new HistoryState(row.HistoryStartMinute, row.HistoryStartEventId, row.PeriodStartMinute, row.BirthsSinceSample, row.DeathsSinceSample, row.FoodProducedSinceSample, row.FoodConsumedSinceSample, row.ActiveFoodShortage, row.PopulationMilestoneWatermark).Validate(currentMinute.Value); }
        catch (ArgumentException exception) { throw new InvalidDataException("History state row is invalid.", exception); }
    }
    private static HistoricalEvent FromHistoricalEventRow(HistoricalEventRow row, WorldMinute currentMinute)
    {
        if (row.LocationX.HasValue != row.LocationY.HasValue) throw new InvalidDataException("Historical event location coordinates must be paired.");
        try { return new HistoricalEvent(new HistoricalEventId(row.Id), row.WorldMinute, (HistoricalEventType)row.EventType, (HistoricalImportance)row.Importance, (HistoricalEventOrigin)row.Origin, row.LocationX is null ? null : new TileCoordinate(row.LocationX.Value, row.LocationY!.Value), row.PayloadJson, row.SchemaVersion).Validate(currentMinute.Value); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException) { throw new InvalidDataException("Historical event row is invalid.", exception); }
    }
    private static HistoricalEventCitizenLink FromHistoricalEventCitizenLinkRow(HistoricalEventCitizenLinkRow row)
    {
        try { var link = new HistoricalEventCitizenLink(new HistoricalEventId(row.EventId), new CitizenId(row.CitizenId), row.Role); link.Validate(); return link; }
        catch (ArgumentException exception) { throw new InvalidDataException("Historical citizen link row is invalid.", exception); }
    }
    private static HistoricalEventStructureLink FromHistoricalEventStructureLinkRow(HistoricalEventStructureLinkRow row)
    {
        try { var link = new HistoricalEventStructureLink(new HistoricalEventId(row.EventId), new StructureId(row.StructureId), row.Role); link.Validate(); return link; }
        catch (ArgumentException exception) { throw new InvalidDataException("Historical structure link row is invalid.", exception); }
    }
    private static StatisticsSample FromStatisticsSampleRow(StatisticsSampleRow row)
    {
        try { var sample = new StatisticsSample(row.WorldMinute, row.PeriodStartMinute, row.Population, row.BirthsPeriod, row.DeathsPeriod, row.FoodStored, row.FoodProducedPeriod, row.FoodConsumedPeriod, row.WoodStored, row.StoneStored, row.ShelterCapacity, row.AverageHealth, row.AverageHunger); sample.Validate(); return sample; }
        catch (ArgumentException exception) { throw new InvalidDataException("Statistics sample row is invalid.", exception); }
    }
    private static CitizenMemory FromCitizenMemoryRow(CitizenMemoryRow row)
    {
        try { var memory = new CitizenMemory(new CitizenId(row.CitizenId), new HistoricalEventId(row.EventId), (MemoryType)row.MemoryType, (HistoricalImportance)row.Importance, row.EmotionalValence, row.CreatedMinute); memory.Validate(); return memory; }
        catch (ArgumentException exception) { throw new InvalidDataException("Citizen memory row is invalid.", exception); }
    }

    private static WorldTileRow ToWorldTileRow(WorldTile tile, int width) => new()
    {
        TileIndex = tile.Coordinate.ToIndex(width), X = tile.Coordinate.X, Y = tile.Coordinate.Y, Terrain = (int)tile.Terrain,
        Elevation = tile.Elevation, Fertility = tile.Fertility, WaterAccess = tile.WaterAccess, Walkable = tile.Walkable, MovementCost = tile.MovementCost
    };

    private static ResourceNodeRow ToResourceNodeRow(ResourceNode node, int width) => new()
    {
        Id = node.Id.Value, TileIndex = node.Coordinate.ToIndex(width), X = node.Coordinate.X, Y = node.Coordinate.Y, Resource = (int)node.Type,
        InitialQuantity = node.InitialQuantity, MaximumQuantity = node.MaximumQuantity, RegenerationPotential = node.RegenerationPotential
    };

    private static ScheduledEventRow ToScheduledEventRow(ScheduledEventSnapshot scheduledEvent) => new()
    {
        Id = scheduledEvent.Id.Value, DueWorldMinute = scheduledEvent.Order.DueWorldMinute.Value, Priority = scheduledEvent.Order.Priority,
        EntitySortKey = scheduledEvent.Order.EntitySortKey, Sequence = scheduledEvent.Order.Sequence, EventName = scheduledEvent.Name, EventPayloadJson = scheduledEvent.PayloadJson
    };

    private static ScheduledEventSnapshot ToScheduledEventSnapshot(ScheduledEventRow row)
    {
        if (row.Id <= 0 || row.Sequence <= 0 || row.Id != row.Sequence) throw new InvalidDataException("A scheduled event must have one positive identity equal to its sequence.");
        if (string.IsNullOrWhiteSpace(row.EventName) || string.IsNullOrWhiteSpace(row.EventPayloadJson)) throw new InvalidDataException("A scheduled event must contain pure data name and payload values.");
        try { using var payload = JsonDocument.Parse(row.EventPayloadJson); }
        catch (JsonException exception) { throw new InvalidDataException("A scheduled event payload must be valid JSON.", exception); }
        try { return new ScheduledEventSnapshot(new ScheduledEventId(row.Id), new ScheduledEventOrder(new WorldMinute(row.DueWorldMinute), row.Priority, row.EntitySortKey, row.Sequence), row.EventName, row.EventPayloadJson); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("A scheduled event due minute must be non-negative.", exception); }
    }

    private async Task<SimulationPersistenceSnapshot> LoadM1SnapshotAsync(CancellationToken cancellationToken)
    {
        // M1-to-M2 upgrade needs the already validated map and legacy events without requiring M2 citizens.
        var original = await LoadAsync(cancellationToken);
        return original;
    }

    private static CitizenRow ToCitizenRow(Citizen citizen) => new()
    {
        Id = citizen.Id.Value, FounderOrdinal = citizen.FounderOrdinal, TargetCitizenId = citizen.TargetCitizenId?.Value, GivenName = citizen.GivenName, FamilyName = citizen.FamilyName, BirthMinute = citizen.BirthMinute, DeathMinute = citizen.DeathMinute, DeathCause = citizen.DeathCause, ParentAId = citizen.ParentAId?.Value, ParentBId = citizen.ParentBId?.Value, PartnerId = citizen.PartnerId?.Value, HouseholdId = citizen.HouseholdId?.Value, HomeStructureId = citizen.HomeStructureId?.Value, LocationX = citizen.Location.X, LocationY = citizen.Location.Y, Health = citizen.Health,
        Hunger = citizen.Needs.Hunger, Rest = citizen.Needs.Rest, Shelter = citizen.Needs.Shelter, Social = citizen.Needs.Social, Industriousness = citizen.Traits.Industriousness, Sociability = citizen.Traits.Sociability, Curiosity = citizen.Traits.Curiosity, Cooperativeness = citizen.Traits.Cooperativeness, RiskTolerance = citizen.Traits.RiskTolerance, Resilience = citizen.Traits.Resilience,
        Foraging = citizen.Skills.Foraging, Woodcutting = citizen.Skills.Woodcutting, Stoneworking = citizen.Skills.Stoneworking, Construction = citizen.Skills.Construction, Hauling = citizen.Skills.Hauling, Domestic = citizen.Skills.Domestic, CurrentAction = (int)citizen.CurrentAction, ActionSequence = citizen.ActionSequence, ActionStartedMinute = citizen.ActionStartedMinute?.Value, ActionCompletesMinute = citizen.ActionCompletesMinute?.Value, ActionTargetX = citizen.ActionTarget?.X, ActionTargetY = citizen.ActionTarget?.Y, NeedsUpdatedMinute = citizen.NeedsUpdatedMinute, LifetimeMovementSteps = citizen.LifetimeMovementSteps, LifetimeMovementCost = citizen.LifetimeMovementCost, HealthUpdatedMinute = citizen.HealthUpdatedMinute, ActionPhase = (int)citizen.ActionPhase, TargetResourceNodeId = citizen.TargetResourceNodeId?.Value, CarriedResourceType = citizen.CarriedResourceType is null ? null : (int)citizen.CarriedResourceType.Value, CarriedResourceQuantity = citizen.CarriedResourceQuantity, TargetStructureId = citizen.TargetStructureId?.Value, LifetimeForagingMinutes = citizen.LifetimeForagingMinutes, LifetimeWoodcuttingMinutes = citizen.LifetimeWoodcuttingMinutes, LifetimeStoneworkingMinutes = citizen.LifetimeStoneworkingMinutes, LifetimeConstructionMinutes = citizen.LifetimeConstructionMinutes, LifetimeHaulingMinutes = citizen.LifetimeHaulingMinutes
    };

    private static RelationshipRow ToRelationshipRow(RelationshipState relationship) => new()
    {
        CitizenAId = relationship.CitizenAId.Value, CitizenBId = relationship.CitizenBId.Value, Familiarity = relationship.Familiarity,
        Affinity = relationship.Affinity, Trust = relationship.Trust, Conflict = relationship.Conflict,
        LastInteractionMinute = relationship.LastInteractionMinute, InteractionCount = relationship.InteractionCount
    };

    private static RelationshipState FromRelationshipRow(RelationshipRow row) =>
        new RelationshipState(new CitizenId(row.CitizenAId), new CitizenId(row.CitizenBId), row.Familiarity, row.Affinity, row.Trust, row.Conflict, row.LastInteractionMinute, row.InteractionCount).Validate();

    private static HouseholdRow ToHouseholdRow(Household household) => new()
    {
        Id = household.Id.Value, CreatedMinute = household.CreatedMinute, DissolvedMinute = household.DissolvedMinute, DwellingStructureId = household.DwellingStructureId?.Value
    };

    private static Household FromHouseholdRow(HouseholdRow row) => new(new HouseholdId(row.Id), row.CreatedMinute)
    {
        DissolvedMinute = row.DissolvedMinute,
        DwellingStructureId = row.DwellingStructureId is null ? null : new StructureId(row.DwellingStructureId.Value)
    };
    private static StructureRow ToStructureRow(Structure structure) => new()
    {
        Id = structure.Id.Value, Type = (int)structure.Type, Status = (int)structure.Status, Condition = structure.Condition, LocationX = structure.Location.X, LocationY = structure.Location.Y,
        ConstructionStartedMinute = structure.ConstructionStartedMinute, CompletedMinute = structure.CompletedMinute, RequiredWood = structure.RequiredWood, DeliveredWood = structure.DeliveredWood,
        RequiredStone = structure.RequiredStone, DeliveredStone = structure.DeliveredStone, RequiredWork = structure.RequiredWork, CompletedWork = structure.CompletedWork
    };

    private static StructureContributionRow ToStructureContributionRow(StructureContribution contribution) => new()
    {
        StructureId = contribution.StructureId.Value, CitizenId = contribution.CitizenId.Value, ConstructionWork = contribution.ConstructionWork,
        WoodDelivered = contribution.WoodDelivered, StoneDelivered = contribution.StoneDelivered
    };

    private static Structure FromStructureRow(StructureRow row, WorldMap world)
    {
        if (row.Id <= 0 || !Enum.IsDefined((StructureType)row.Type) || !Enum.IsDefined((StructureStatus)row.Status) || row.Condition != ((StructureStatus)row.Status == StructureStatus.Complete ? 10000 : 0) || row.LocationX < 0 || row.LocationY < 0 || row.LocationX >= world.Width || row.LocationY >= world.Height) throw new InvalidDataException("Structure row contains invalid canonical state.");
        try
        {
            return new Structure(new StructureId(row.Id), (StructureType)row.Type, new TileCoordinate(row.LocationX, row.LocationY), row.ConstructionStartedMinute, row.RequiredWood, row.RequiredStone, row.RequiredWork)
            {
                Status = (StructureStatus)row.Status, CompletedMinute = row.CompletedMinute, DeliveredWood = row.DeliveredWood, DeliveredStone = row.DeliveredStone, CompletedWork = row.CompletedWork
            }.Validate();
        }
        catch (ArgumentException exception) { throw new InvalidDataException("Structure row contains invalid canonical state.", exception); }
    }

    private static StructureContribution FromStructureContributionRow(StructureContributionRow row)
    {
        if (row.StructureId <= 0 || row.CitizenId <= 0) throw new InvalidDataException("Structure contribution references an invalid entity.");
        try { return new StructureContribution(new StructureId(row.StructureId), new CitizenId(row.CitizenId), row.ConstructionWork, row.WoodDelivered, row.StoneDelivered); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("Structure contribution contains invalid values.", exception); }
    }

    private static Citizen FromCitizenRow(CitizenRow row, WorldMap world, WorldMinute minute, int survivalVersion)
    {
        if (!Enum.IsDefined((CitizenAction)row.CurrentAction) || row.Id <= 0 || row.FounderOrdinal is < 0 or > 19 || (survivalVersion == 0 && row.Health != 10000) || (row.FounderOrdinal is not null && row.BirthMinute >= 0) || row.LocationX < 0 || row.LocationY < 0 || row.LocationX >= world.Width || row.LocationY >= world.Height || !world.GetTile(row.LocationX, row.LocationY).Walkable) throw new InvalidDataException("Citizen row contains invalid canonical state.");
        if (row.ActionTargetX.HasValue != row.ActionTargetY.HasValue) throw new InvalidDataException("Citizen action target coordinates must be both present or both absent.");
        if (row.ActionPhase < (int)CitizenActionPhase.None || row.ActionPhase > (int)CitizenActionPhase.WaitingForStorage || row.CarriedResourceType is < 1 or > 3 || row.TargetResourceNodeId is <= 0 || row.TargetStructureId is <= 0 || row.CarriedResourceQuantity < 0 || row.LifetimeForagingMinutes < 0 || row.LifetimeWoodcuttingMinutes < 0 || row.LifetimeStoneworkingMinutes < 0 || row.LifetimeConstructionMinutes < 0 || row.LifetimeHaulingMinutes < 0) throw new InvalidDataException("Citizen M4 state contains an invalid enum or quantity.");
        var citizen = new Citizen(new CitizenId(row.Id), row.FounderOrdinal, row.GivenName, row.FamilyName, row.BirthMinute, new TileCoordinate(row.LocationX, row.LocationY), new CitizenTraits(row.Industriousness, row.Sociability, row.Curiosity, row.Cooperativeness, row.RiskTolerance, row.Resilience), new CitizenSkills(row.Foraging, row.Woodcutting, row.Stoneworking, row.Construction, row.Hauling, row.Domestic)) { Needs = new CitizenNeeds(row.Hunger, row.Rest, row.Shelter, row.Social), DeathMinute = row.DeathMinute, DeathCause = row.DeathCause, ParentAId = row.ParentAId is null ? null : new CitizenId(row.ParentAId.Value), ParentBId = row.ParentBId is null ? null : new CitizenId(row.ParentBId.Value), PartnerId = row.PartnerId is null ? null : new CitizenId(row.PartnerId.Value), HouseholdId = row.HouseholdId is null ? null : new HouseholdId(row.HouseholdId.Value), HomeStructureId = row.HomeStructureId is null ? null : new StructureId(row.HomeStructureId.Value), TargetCitizenId = row.TargetCitizenId is null ? null : new CitizenId(row.TargetCitizenId.Value), Health = row.Health, CurrentAction = (CitizenAction)row.CurrentAction, ActionPhase = (CitizenActionPhase)row.ActionPhase, ActionSequence = row.ActionSequence, ActionStartedMinute = row.ActionStartedMinute is null ? null : new WorldMinute(row.ActionStartedMinute.Value), ActionCompletesMinute = row.ActionCompletesMinute is null ? null : new WorldMinute(row.ActionCompletesMinute.Value), ActionTarget = row.ActionTargetX is null || row.ActionTargetY is null ? null : new TileCoordinate(row.ActionTargetX.Value, row.ActionTargetY.Value), TargetResourceNodeId = row.TargetResourceNodeId is null ? null : new ResourceNodeId(row.TargetResourceNodeId.Value), TargetStructureId = row.TargetStructureId is null ? null : new StructureId(row.TargetStructureId.Value), CarriedResourceType = row.CarriedResourceType is null ? null : (ResourceType)row.CarriedResourceType.Value, CarriedResourceQuantity = row.CarriedResourceQuantity, NeedsUpdatedMinute = row.NeedsUpdatedMinute, HealthUpdatedMinute = row.HealthUpdatedMinute, LifetimeMovementSteps = row.LifetimeMovementSteps, LifetimeMovementCost = row.LifetimeMovementCost, LifetimeForagingMinutes = row.LifetimeForagingMinutes, LifetimeWoodcuttingMinutes = row.LifetimeWoodcuttingMinutes, LifetimeStoneworkingMinutes = row.LifetimeStoneworkingMinutes, LifetimeConstructionMinutes = row.LifetimeConstructionMinutes, LifetimeHaulingMinutes = row.LifetimeHaulingMinutes };
        if ((citizen.CurrentAction is CitizenAction.Wander or CitizenAction.Explore) && citizen.ActionTarget is null) throw new InvalidDataException("Moving citizen must have a target.");
        if (citizen.CurrentAction is not (CitizenAction.None or CitizenAction.Dead) && (citizen.ActionStartedMinute is null || citizen.ActionCompletesMinute is null || citizen.ActionCompletesMinute!.Value < minute || citizen.ActionStartedMinute!.Value > minute)) throw new InvalidDataException("Citizen action timing is incoherent.");
        if (citizen.CurrentAction == CitizenAction.None && (citizen.ActionStartedMinute is not null || citizen.ActionCompletesMinute is not null || citizen.ActionTarget is not null)) throw new InvalidDataException("Decision-boundary citizen has stale action timing.");
        try { citizen.Validate(world); } catch (ArgumentException exception) { throw new InvalidDataException("Citizen row contains invalid canonical state.", exception); }
        return citizen;
    }
}

/// <summary>Lossless UInt64-to-SQLite-text codec for world seeds.</summary>
public static class WorldSeedCodec
{
    public static string Encode(WorldSeed seed) => seed.Value.ToString(CultureInfo.InvariantCulture);

    public static WorldSeed Decode(string value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) throw new InvalidDataException("The persisted world seed is not a lossless UInt64 decimal value.");
        return new WorldSeed(parsed);
    }
}
