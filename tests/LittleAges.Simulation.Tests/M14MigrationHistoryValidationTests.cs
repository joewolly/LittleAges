using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M14MigrationHistoryValidationTests
{
    [Fact]
    public void CompletedMigrationHistoryDoesNotRequireItsPartyToRemainInCurrentState()
    {
        var baseline = new SimulationEngine(new WorldSeed(1441),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        Assert.Empty(baseline.MigrationState!.InTransitParties);

        var snapshot = ReplaceSeasonStartedWithFamilyVisitReturned(baseline);

        Assert.Contains(snapshot.HistoricalEvents, item => item.EventType == HistoricalEventType.FamilyVisitReturned);
        Assert.Empty(snapshot.MigrationState!.InTransitParties);
    }

    [Fact]
    public void MigrationHistoryEventsAreRejectedByOlderRules()
    {
        var baseline = new SimulationEngine(new WorldSeed(1442),
            simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion).CreatePersistenceSnapshot();

        Assert.Throws<ArgumentException>(() => ReplaceSeasonStartedWithFamilyVisitReturned(baseline));
    }

    [Fact]
    public void MigrationHistoryPayloadMustRemainCanonicalDuringSnapshotValidation()
    {
        var baseline = new SimulationEngine(new WorldSeed(1443),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var season = Assert.Single(baseline.HistoricalEvents, item => item.EventType == HistoricalEventType.SeasonStarted);
        var visitorId = baseline.Citizens[0].Id.Value;
        var relativeId = baseline.Citizens[1].Id.Value;
        var nonCanonicalPayload = HistoricalEventPayloads.FamilyVisitReturned(visitorId, relativeId, 1, 2) + " ";

        Assert.Throws<ArgumentException>(() => ReplaceSeasonStartedWithFamilyVisitReturned(baseline, nonCanonicalPayload));
    }

    [Fact]
    public void MigrationHistoryLinksMustMatchTheirCanonicalRolesDuringSnapshotValidation()
    {
        var baseline = new SimulationEngine(new WorldSeed(1444),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();

        Assert.Throws<ArgumentException>(() => ReplaceSeasonStartedWithFamilyVisitReturned(
            baseline, secondRole: "subject"));
    }

    private static SimulationPersistenceSnapshot ReplaceSeasonStartedWithFamilyVisitReturned(
        SimulationPersistenceSnapshot snapshot,
        string? payloadJson = null,
        string firstRole = "subject",
        string secondRole = "participant")
    {
        var season = snapshot.HistoricalEvents.Single(item => item.EventType == HistoricalEventType.SeasonStarted);
        var visitorId = snapshot.Citizens[0].Id;
        var relativeId = snapshot.Citizens[1].Id;
        var payload = payloadJson ?? HistoricalEventPayloads.FamilyVisitReturned(visitorId.Value, relativeId.Value, 1, 2);
        var visitReturned = new HistoricalEvent(season.Id, season.WorldMinute,
            HistoricalEventType.FamilyVisitReturned, HistoricalImportance.Personal,
            HistoricalEventOrigin.Live, snapshot.World!.StartingSite, payload);
        var events = snapshot.HistoricalEvents.Select(item => item.Id == season.Id ? visitReturned : item).ToArray();
        var citizenLinks = snapshot.HistoricalEventCitizens.Where(item => item.HistoricalEventId != season.Id)
            .Append(new HistoricalEventCitizenLink(season.Id, visitorId, firstRole))
            .Append(new HistoricalEventCitizenLink(season.Id, relativeId, secondRole))
            .OrderBy(item => item.HistoricalEventId.Value)
            .ThenBy(item => item.CitizenId.Value)
            .ThenBy(item => item.Role, StringComparer.Ordinal)
            .ToArray();

        return CopySnapshot(snapshot, events, citizenLinks);
    }

    private static SimulationPersistenceSnapshot CopySnapshot(SimulationPersistenceSnapshot snapshot,
        IReadOnlyList<HistoricalEvent> events, IReadOnlyList<HistoricalEventCitizenLink> citizenLinks) => new(
        snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion,
        snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents,
        snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates,
        snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion, snapshot.Structures,
        snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households,
        snapshot.HistoryVersion, snapshot.HistoryState, events, citizenLinks, snapshot.HistoricalEventStructures,
        snapshot.StatisticsSamples, snapshot.Memories, snapshot.Agriculture, snapshot.Economy,
        snapshot.LivingStateJson, snapshot.MigrationStateJson);
}
