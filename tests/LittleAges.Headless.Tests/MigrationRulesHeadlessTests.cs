using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Headless.Tests;

public sealed class MigrationRulesHeadlessTests
{
    [Fact]
    public void M14IsTheCurrentDefaultAndCanBeSelectedExplicitly()
    {
        var run = HeadlessCommandLine.Parse(["run", "--rules", SimulationEngine.MigrationSimulationRulesVersion]);
        var acceptance = HeadlessCommandLine.Parse(["acceptance", "--rules", SimulationEngine.MigrationSimulationRulesVersion,
            "--years", "10", "--checkpoint-year", "5"]);

        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, SimulationEngine.CurrentSimulationRulesVersion);
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, HeadlessOptions.Default(HeadlessCommand.Run).Rules);
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, HeadlessOptions.Default(HeadlessCommand.Acceptance).Rules);
        Assert.True(run.Succeeded, run.Error);
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, run.Options!.Rules);
        Assert.True(acceptance.Succeeded, acceptance.Error);
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, acceptance.Options!.Rules);
    }

    [Fact]
    public void SnapshotComparerDetectsAnM14OnlyDaughterSiteChange()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var original = engine.CreatePersistenceSnapshot();
        var state = original.MigrationState!;
        var site = original.World!.EnumerateTilesRowMajor()
            .First(tile => tile.Walkable && tile.Coordinate != original.World.StartingSite).Coordinate;
        var daughterStock = MigrationSettlementStockState.From(original.Settlement!) with
        {
            FoodStored = 0,
            WoodStored = 0,
            StoneStored = 0
        };
        var daughter = new MigrationDaughterSettlementState(site,
            daughterStock,
            Enum.GetValues<LivingGood>().Select(good => new LivingStock(good, 0)).ToArray());
        var withDaughter = WithMigrationState(original, new MigrationWorldState(1,
            state.CitizenResidences, state.HouseholdResidences, state.StructureOwners, state.FacilityOwners,
            state.WorkOrderOwners, daughter, state.InTransitParties));

        var comparison = HeadlessSnapshotComparer.Compare(original, withDaughter);

        Assert.False(comparison.Equivalent);
        Assert.Contains("migrationState", comparison.Mismatches);
        Assert.True(Assert.Single(HeadlessInvariantValidator.Validate(engine, withDaughter, 0),
            invariant => invariant.Name == "migration-single-daughter").Passed);

        var report = HeadlessReportSerialization.SerializeDeterministicReport(new HeadlessReport
        {
            Rules = SimulationEngine.MigrationSimulationRulesVersion,
            Migration = MigrationValidation.CreateReadSnapshot(withDaughter),
            SettlementMetricsScope = "Top-level stock fields describe settlement 1 only."
        });
        using var document = JsonDocument.Parse(report);
        var settlementIds = document.RootElement.GetProperty("migration").GetProperty("settlements")
            .EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal([1L, MigrationDaughterSettlementState.SettlementId], settlementIds);
    }

    [Fact]
    public void M14ReportCountsLivingResidentsPerSiteWithoutChangingResidentIds()
    {
        var engine = new SimulationEngine(new WorldSeed(42),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var original = engine.CreatePersistenceSnapshot();
        var originalMigration = MigrationValidation.CreateReadSnapshot(original);
        var state = original.MigrationState!;
        Assert.True(original.Citizens.Count >= 2);
        var daughterCitizens = new[] { original.Citizens[0], original.Citizens[1] };
        var daughterCitizenIds = daughterCitizens.Select(citizen => citizen.Id.Value).ToHashSet();
        var daughterHouseholdIds = daughterCitizens.Select(citizen => citizen.HouseholdId!.Value.Value).ToHashSet();
        var daughterStructureIds = new HashSet<long>();
        foreach (var citizen in daughterCitizens)
            if (citizen.HomeStructureId is { } homeStructureId)
                daughterStructureIds.Add(homeStructureId.Value);
        foreach (var household in original.Households)
            if (daughterHouseholdIds.Contains(household.Id.Value) && household.DwellingStructureId is { } dwellingStructureId)
                daughterStructureIds.Add(dwellingStructureId.Value);
        var daughterSite = original.World!.EnumerateTilesRowMajor()
            .First(tile => tile.Walkable && tile.Coordinate != original.World.StartingSite).Coordinate;
        var daughterStock = MigrationSettlementStockState.From(original.Settlement!) with
        {
            FoodStored = 0,
            WoodStored = 0,
            StoneStored = 0
        };
        var daughter = new MigrationDaughterSettlementState(daughterSite, daughterStock,
            Enum.GetValues<LivingGood>().Select(good => new LivingStock(good, 0)).ToArray());
        var daughterState = new MigrationWorldState(1,
            state.CitizenResidences.Select(residence => daughterCitizenIds.Contains(residence.EntityId)
                ? residence with { SettlementId = MigrationDaughterSettlementState.SettlementId }
                : residence).ToArray(),
            state.HouseholdResidences.Select(residence => daughterHouseholdIds.Contains(residence.EntityId)
                ? residence with { SettlementId = MigrationDaughterSettlementState.SettlementId }
                : residence).ToArray(),
            state.StructureOwners.Select(residence => daughterStructureIds.Contains(residence.EntityId)
                ? residence with { SettlementId = MigrationDaughterSettlementState.SettlementId }
                : residence).ToArray(),
            state.FacilityOwners,
            state.WorkOrderOwners, daughter, state.InTransitParties);
        var withDaughter = WithMigrationState(original, daughterState);
        var daughterMigration = MigrationValidation.CreateReadSnapshot(withDaughter);
        var originalMigrationFingerprint = originalMigration.Fingerprint;
        var daughterMigrationFingerprint = daughterMigration.Fingerprint;
        var deceasedId = daughterCitizenIds.Min();

        MarkDead(original, deceasedId);
        MarkDead(withDaughter, deceasedId);
        var originalCounts = MigrationValidation.GetLivingResidentCountsBySettlement(original, originalMigration);
        var daughterCounts = MigrationValidation.GetLivingResidentCountsBySettlement(withDaughter, daughterMigration);

        Assert.Equal(original.Citizens.Count(citizen => citizen.IsAlive), originalCounts[1]);
        Assert.Equal(original.Citizens.Count(citizen => citizen.IsAlive), originalCounts.Values.Sum());
        Assert.Equal(daughterCitizenIds.Count - 1, daughterCounts[MigrationDaughterSettlementState.SettlementId]);
        Assert.Equal(withDaughter.Citizens.Count(citizen => citizen.IsAlive), daughterCounts.Values.Sum());
        Assert.Equal(daughterCitizenIds.Count,
            daughterMigration.Settlements.Single(site => site.Id == MigrationDaughterSettlementState.SettlementId).CitizenIds.Count);
        Assert.Equal(originalMigrationFingerprint, originalMigration.Fingerprint);

        var originalReport = HeadlessReportSerialization.Serialize(new HeadlessReport
        {
            Migration = originalMigration,
            LivingResidentCountsBySettlement = originalCounts
        });
        using var originalDocument = JsonDocument.Parse(originalReport);
        var originalSite = Assert.Single(originalDocument.RootElement.GetProperty("migration").GetProperty("settlements").EnumerateArray());
        Assert.Equal(original.Citizens.Count, originalSite.GetProperty("citizenIds").GetArrayLength());
        Assert.Equal(originalCounts[1], originalSite.GetProperty("livingResidentCount").GetInt32());

        var report = HeadlessReportSerialization.Serialize(new HeadlessReport
        {
            Migration = daughterMigration,
            LivingResidentCountsBySettlement = daughterCounts
        });
        using var document = JsonDocument.Parse(report);
        var reportedSites = document.RootElement.GetProperty("migration").GetProperty("settlements").EnumerateArray().ToArray();
        var daughterReport = reportedSites.Single(site => site.GetProperty("id").GetInt64() == MigrationDaughterSettlementState.SettlementId);
        Assert.Equal(daughterCitizenIds.Count, daughterReport.GetProperty("citizenIds").GetArrayLength());
        Assert.Equal(daughterCitizenIds.Count - 1, daughterReport.GetProperty("livingResidentCount").GetInt32());
        Assert.Equal(daughterMigrationFingerprint, daughterMigration.Fingerprint);
    }

    [Fact]
    public void M14RunReportsValidatedSiteStateAndExplicitOriginalStockScope()
    {
        var options = HeadlessOptions.Default(HeadlessCommand.Run) with
        {
            Rules = SimulationEngine.MigrationSimulationRulesVersion
        };

        var report = HeadlessRunner.Run(options);

        Assert.True(report.MandatoryInvariantsPassed,
            string.Join("; ", report.Invariants.Where(item => !item.Passed).Select(item => $"{item.Name}: {item.Details}")));
        Assert.NotNull(report.Migration);
        Assert.Equal(1, Assert.Single(report.Migration!.Settlements).Id);
        Assert.Equal("FoodStored, WoodStored, and StoneStored describe settlement 1 only; Migration.Settlements reports each site's stocks and local capacity.",
            report.SettlementMetricsScope);
        using var document = JsonDocument.Parse(HeadlessReportSerialization.Serialize(report));
        Assert.True(document.RootElement.TryGetProperty("migration", out _));
    }

    [Fact]
    public async Task M14ShortAcceptanceMatchesAcrossChunkedCheckpointReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Headless-M14Acceptance-Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "checkpoint.db");
        const long targetMinute = 30 * WorldCalendar.MinutesPerDay;
        const long checkpointMinute = 12 * WorldCalendar.MinutesPerDay;
        try
        {
            Directory.CreateDirectory(root);
            var uninterrupted = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            HeadlessRunner.AdvanceEngineForTesting(uninterrupted, targetMinute, chunkMinutes: 17 * WorldCalendar.MinutesPerDay);

            var resumed = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            HeadlessRunner.AdvanceEngineForTesting(resumed, checkpointMinute, chunkMinutes: 5 * WorldCalendar.MinutesPerDay);
            var checkpoint = await HeadlessRunner.PersistAndReloadForTestingAsync(databasePath, resumed.CreatePersistenceSnapshot());
            resumed = SimulationEngine.FromPersistenceSnapshot(checkpoint);
            HeadlessRunner.AdvanceEngineForTesting(resumed, targetMinute, chunkMinutes: 3 * WorldCalendar.MinutesPerDay);

            var uninterruptedSnapshot = uninterrupted.CreatePersistenceSnapshot();
            var resumedSnapshot = resumed.CreatePersistenceSnapshot();
            var comparison = HeadlessSnapshotComparer.Compare(uninterruptedSnapshot, resumedSnapshot);
            Assert.True(comparison.Equivalent, string.Join(", ", comparison.Mismatches));
            Assert.All(HeadlessInvariantValidator.Validate(uninterrupted, uninterruptedSnapshot, targetMinute),
                item => Assert.True(item.Passed, $"{item.Name}: {item.Details}"));
            Assert.All(HeadlessInvariantValidator.Validate(resumed, resumedSnapshot, targetMinute),
                item => Assert.True(item.Passed, $"{item.Name}: {item.Details}"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static SimulationPersistenceSnapshot WithMigrationState(SimulationPersistenceSnapshot snapshot, MigrationWorldState state) =>
        new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion,
            snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents,
            snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates,
            snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion, snapshot.Structures,
            snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households,
            snapshot.HistoryVersion, snapshot.HistoryState, snapshot.HistoricalEvents, snapshot.HistoricalEventCitizens,
            snapshot.HistoricalEventStructures, snapshot.StatisticsSamples, snapshot.Memories, snapshot.Agriculture,
            snapshot.Economy, snapshot.LivingStateJson, state.ToCanonicalJson());

    private static void MarkDead(SimulationPersistenceSnapshot snapshot, long citizenId)
    {
        var citizen = snapshot.Citizens.Single(item => item.Id.Value == citizenId);
        citizen.DeathMinute = snapshot.WorldMinute.Value;
        citizen.DeathCause = "natural";
        citizen.Health = 0;
        citizen.CurrentAction = CitizenAction.Dead;
    }
}
