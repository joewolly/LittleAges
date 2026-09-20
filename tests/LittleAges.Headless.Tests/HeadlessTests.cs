using System.Globalization;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Headless;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Headless.Tests;

public sealed class HeadlessTests
{
    [Fact]
    public void GrowthAcceptanceAllowsRecordedBereavementButRejectsBrokenLivingPartnership()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.GrowthSimulationRulesVersion);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var people = ((Dictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", flags)!.GetValue(engine)!).Values.OrderBy(c => c.Id.Value).Take(2).ToArray();
        void Invoke(string name, params object[] args) => typeof(SimulationEngine).GetMethod(name, flags)!.Invoke(engine, args);
        Invoke("SetRelationship", people[0].Id, people[1].Id, 9000, 9000, 9000, 0);
        Invoke("TryFormPartnership", people[0], people[1], Assert.Single(engine.Relationships));
        Invoke("RecordPartnershipHistory", people[0], people[1]);
        var valid = engine.CreatePersistenceSnapshot();
        Assert.True(Assert.Single(HeadlessInvariantValidator.Validate(engine, valid, 0), i => i.Name == "partnership-symmetry").Passed);
        valid.Citizens.Single(c => c.Id == people[1].Id).PartnerId = null;
        Assert.False(Assert.Single(HeadlessInvariantValidator.Validate(engine, valid, 0), i => i.Name == "partnership-symmetry").Passed);
        Invoke("KillNatural", people[0]);
        var bereaved = engine.CreatePersistenceSnapshot();
        Assert.All(HeadlessInvariantValidator.Validate(engine, bereaved, 0), i => Assert.True(i.Passed, i.Details));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void SupportedHorizonsParseWithoutRunningTheHorizon(int years)
    {
        var result = HeadlessCommandLine.Parse(["run", "--years", years.ToString(CultureInfo.InvariantCulture)]);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Options);
        Assert.Equal(years, result.Options!.Years);
        Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, result.Options.Rules);
    }

    [Fact]
    public void InvalidArgumentsAreDeterministicAndRulesAreBounded()
    {
        var first = HeadlessCommandLine.Parse(["run", "--years", "2"]);
        var second = HeadlessCommandLine.Parse(["run", "--years", "2"]);
        var unsupported = HeadlessCommandLine.Parse(["run", "--rules", SimulationEngine.M5SimulationRulesVersion]);

        Assert.False(first.Succeeded);
        Assert.Equal(first.Error, second.Error);
        Assert.Contains("one of: 1, 10, 100, 500", first.Error, StringComparison.Ordinal);
        Assert.False(unsupported.Succeeded);
        Assert.Contains("supports", unsupported.Error, StringComparison.Ordinal);
        var m6 = HeadlessCommandLine.Parse(["run", "--rules", SimulationEngine.M6SimulationRulesVersion]);
        Assert.True(m6.Succeeded);
        Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, HeadlessOptions.Default(HeadlessCommand.Run).Rules);
    }

    [Fact]
    public void AcceptanceCheckpointParserAcceptsFinalContractAndRejectsInvalidPlacement()
    {
        var missingCheckpoint = HeadlessCommandLine.Parse(["acceptance", "--years", "100"]);
        var accepted = HeadlessCommandLine.Parse(["acceptance", "--years", "100", "--checkpoint-year", "37"]);
        var callerSelectedDatabase = HeadlessCommandLine.Parse(["acceptance", "--years", "100", "--checkpoint-year", "37", "--database", "checkpoint.db"]);

        Assert.False(missingCheckpoint.Succeeded);
        Assert.Equal("acceptance requires --checkpoint-year.", missingCheckpoint.Error);
        Assert.True(accepted.Succeeded);
        Assert.Equal(37, accepted.Options!.CheckpointYear);
        Assert.Null(accepted.Options.DatabasePath);
        Assert.True(callerSelectedDatabase.Succeeded);
        Assert.Equal("checkpoint.db", callerSelectedDatabase.Options!.DatabasePath);
        Assert.False(HeadlessCommandLine.Parse(["run", "--years", "100", "--checkpoint-year", "37"]).Succeeded);
        Assert.False(HeadlessCommandLine.Parse(["acceptance", "--years", "10", "--checkpoint-year", "10"]).Succeeded);
    }

    [Fact]
    public async Task SqliteCheckpointReloadMatchesUninterruptedMinuteSeam()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Headless-Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "checkpoint.db");
        try
        {
            var uninterrupted = HeadlessRunner.RunEngineForTesting(42, 720, 240);
            var staged = HeadlessRunner.RunEngineForTesting(42, 240, 120);
            var reloadedSnapshot = await HeadlessRunner.PersistAndReloadForTestingAsync(databasePath, staged.CreatePersistenceSnapshot());
            var resumed = SimulationEngine.FromPersistenceSnapshot(reloadedSnapshot);
            HeadlessRunner.AdvanceEngineForTesting(resumed, 720, 240);
            var comparison = HeadlessSnapshotComparer.Compare(uninterrupted.CreatePersistenceSnapshot(), resumed.CreatePersistenceSnapshot());
            Assert.True(comparison.Equivalent, string.Join("; ", comparison.Mismatches));
            Assert.Equal(uninterrupted.HistoryFingerprint, resumed.HistoryFingerprint);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExactPeakUsesMinuteZeroFoundersAndTrajectoryAndShortageRatesAreDeterministic()
    {
        var first = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var second = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        Assert.Equal(20, HeadlessRunner.ComputeExactPeakForTesting(first));

        HeadlessRunner.AdvanceEngineForTesting(first, 720, 240);
        HeadlessRunner.AdvanceEngineForTesting(second, 720, 240);
        var firstSnapshot = first.CreatePersistenceSnapshot();
        var secondSnapshot = second.CreatePersistenceSnapshot();
        var firstTrajectory = HeadlessRunner.BuildPopulationTrajectoryForTesting(firstSnapshot, 1);
        var secondTrajectory = HeadlessRunner.BuildPopulationTrajectoryForTesting(secondSnapshot, 1);
        var firstShortage = HeadlessRunner.BuildShortageMetricsForTesting(firstSnapshot);
        var secondShortage = HeadlessRunner.BuildShortageMetricsForTesting(secondSnapshot);

        Assert.True(HeadlessRunner.ComputeExactPeakForTesting(first) >= 20);
        Assert.Equal(2, firstTrajectory.Length);
        Assert.Equal(0, firstTrajectory[0].Year);
        Assert.Equal(1, firstTrajectory[1].Year);
        Assert.Equal(20, firstTrajectory[0].LivingCitizens);
        Assert.Equal(firstTrajectory, secondTrajectory);
        Assert.Equal(firstShortage.StartsByYear.OrderBy(item => item.Key), secondShortage.StartsByYear.OrderBy(item => item.Key));
        Assert.Equal(firstShortage.EndsByYear.OrderBy(item => item.Key), secondShortage.EndsByYear.OrderBy(item => item.Key));
        Assert.Equal(firstShortage.TransitionPercentageOfHistory, secondShortage.TransitionPercentageOfHistory);
        Assert.InRange(firstShortage.TransitionPercentageOfHistory, 0d, 100d);
    }

    [Fact]
    public void AcceptanceReportProjectionCarriesEquivalenceWithoutTimingInDeterministicJson()
    {
        var report = new HeadlessReport
        {
            Command = "acceptance",
            Seed = 42,
            Rules = SimulationEngine.CurrentSimulationRulesVersion,
            RequestedYears = 1,
            FinalMinute = 720,
            RealElapsedMilliseconds = 123,
            Acceptance = new HeadlessAcceptanceResult(1, 360, "C:/temp/checkpoint.db", "caller-selected", new HeadlessRunSummary(720, 100, 7, 600, 10, "a", "b", "c", "d", "e", true), new HeadlessRunSummary(720, 200, 3, 300, 10, "a", "b", "c", "d", "f", true), "whole-a", "whole-b", true, Array.Empty<string>())
        };
        var deterministic = report.ToDeterministicReportJson();
        Assert.DoesNotContain("realElapsedMilliseconds", deterministic, StringComparison.Ordinal);
        Assert.Contains("whole-a", deterministic, StringComparison.Ordinal);
        Assert.Contains("equivalent", deterministic, StringComparison.Ordinal);
        Assert.DoesNotContain("C:/temp", deterministic, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(HeadlessReportSerialization.Serialize(report));
        Assert.True(parsed.RootElement.GetProperty("acceptance").GetProperty("equivalent").GetBoolean());
    }

    [Fact]
    public void OneYearReportHasStableDeterministicReportOutputAndOperationalTiming()
    {
        var options = new HeadlessOptions(HeadlessCommand.Run, 42, 1, SimulationEngine.CurrentSimulationRulesVersion, null, WorldCalendar.MinutesPerYear);
        var first = HeadlessRunner.Run(options);
        var second = HeadlessRunner.Run(options);

        Assert.True(first.MandatoryInvariantsPassed);
        Assert.Equal(first.ToDeterministicReportJson(), first.ToDeterministicReportJson());
        Assert.Equal(first.DeterministicReportFingerprint, second.DeterministicReportFingerprint);
        Assert.Equal(WorldCalendar.MinutesPerYear, first.FinalMinute);
        Assert.True(first.PeakLivingCitizens >= 20);
        Assert.NotEmpty(first.HistoryFingerprint);
        Assert.DoesNotContain("realElapsedMilliseconds", first.ToDeterministicReportJson(), StringComparison.Ordinal);
        using var json = JsonDocument.Parse(HeadlessReportSerialization.Serialize(first));
        Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, json.RootElement.GetProperty("rules").GetString());
        Assert.True(json.RootElement.GetProperty("mandatoryInvariantsPassed").GetBoolean());
    }

    [Fact]
    public void DifferentChunkSizesReachIdenticalEngineCanonicalFingerprints()
    {
        var singleChunk = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var splitChunks = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);

        singleChunk.AdvanceUntil(new WorldMinute(720));
        splitChunks.AdvanceUntil(new WorldMinute(240));
        splitChunks.AdvanceUntil(new WorldMinute(720));

        Assert.Equal(singleChunk.SurvivalFingerprint, splitChunks.SurvivalFingerprint);
        Assert.Equal(singleChunk.SettlementFingerprint, splitChunks.SettlementFingerprint);
        Assert.Equal(singleChunk.SocialFingerprint, splitChunks.SocialFingerprint);
        Assert.Equal(singleChunk.HistoryFingerprint, splitChunks.HistoryFingerprint);
    }
}
