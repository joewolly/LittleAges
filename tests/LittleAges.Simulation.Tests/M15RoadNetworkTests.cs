using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M15RoadNetworkTests
{
    private const string Roads = SimulationEngine.RoadsSimulationRulesVersion;
    private const long Season = 90L * WorldCalendar.MinutesPerDay;

    [Theory]
    [InlineData(RoadGrade.None, 23, RoadGrade.None)]
    [InlineData(RoadGrade.None, 24, RoadGrade.Track)]
    [InlineData(RoadGrade.None, 72, RoadGrade.Trail)]
    [InlineData(RoadGrade.Track, 12, RoadGrade.Track)]
    [InlineData(RoadGrade.Track, 11, RoadGrade.None)]
    [InlineData(RoadGrade.Track, 72, RoadGrade.Trail)]
    [InlineData(RoadGrade.Trail, 36, RoadGrade.Trail)]
    [InlineData(RoadGrade.Trail, 35, RoadGrade.Track)]
    [InlineData(RoadGrade.Trail, 11, RoadGrade.None)]
    [InlineData(RoadGrade.Road, 0, RoadGrade.Road)]
    public void GradesFollowWearWithHysteresis(RoadGrade grade, int wear, RoadGrade expected) =>
        Assert.Equal(expected, SimulationEngine.NextRoadGrade(grade, wear));

    [Fact]
    public void M14CanonicalStateCarriesNoRoads()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(Season + WorldCalendar.MinutesPerDay));
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Null(snapshot.MigrationState!.Roads);
        Assert.DoesNotContain("\"roads\"", snapshot.MigrationStateJson!, StringComparison.Ordinal);
        Assert.Empty(engine.RoadGrades);
    }

    [Fact]
    public void FootTrafficWearsGradedTracksOnWalkableTilesAtSeasonBoundaries()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: Roads);
        engine.AdvanceUntil(new WorldMinute(Season - 1));
        var beforeGrading = engine.CreatePersistenceSnapshot().MigrationState!.Roads!;
        Assert.NotEmpty(beforeGrading.Tiles);
        Assert.All(beforeGrading.Tiles, x => Assert.Equal(RoadGrade.None, x.Grade));

        engine.AdvanceUntil(new WorldMinute(Season + 1));
        var graded = engine.CreatePersistenceSnapshot().MigrationState!.Roads!;
        Assert.Contains(graded.Tiles, x => x.Grade >= RoadGrade.Track);
        Assert.All(graded.Tiles, x => Assert.True(engine.World.GetTile(x.Coordinate).Walkable));
        Assert.Equal(graded.Tiles.Where(x => x.Grade != RoadGrade.None).Select(x => (x.Coordinate, x.Grade)), engine.RoadGrades);

        // Grading fades wear by one eighth, rounding the reduction up.
        var before = beforeGrading.Tiles.ToDictionary(x => x.Coordinate, x => x.Wear);
        var after = graded.Tiles.ToDictionary(x => x.Coordinate, x => x.Wear);
        var quiet = before.Keys.Where(x => !after.ContainsKey(x) || after[x] <= before[x] - (before[x] + 7) / 8).ToArray();
        Assert.NotEmpty(quiet);
    }

    [Theory]
    [InlineData(-180L)]
    [InlineData(45L)]
    public void ReopenedRunsMatchUninterruptedRunsAcrossGradeChanges(long offsetFromSeason)
    {
        var seed = new WorldSeed(42);
        var checkpointMinute = new WorldMinute(Season + offsetFromSeason);
        var target = new WorldMinute(Season + 3L * WorldCalendar.MinutesPerDay);
        var uninterrupted = new SimulationEngine(seed, simulationRulesVersion: Roads);
        uninterrupted.AdvanceUntil(target);

        var interrupted = new SimulationEngine(seed, simulationRulesVersion: Roads);
        interrupted.AdvanceUntil(checkpointMinute);
        var checkpoint = interrupted.CreatePersistenceSnapshot();
        Assert.NotEmpty(checkpoint.MigrationState!.Roads!.ActiveRoutes);
        MigrationValidation.Validate(checkpoint);
        var reopened = new SimulationEngine(checkpoint);
        Assert.Equal(checkpoint.MigrationStateJson, reopened.CreatePersistenceSnapshot().MigrationStateJson);
        reopened.AdvanceUntil(target);

        AssertEquivalent(uninterrupted, reopened);
    }

    [Fact]
    public void ChunkSizeDoesNotChangeRoadState()
    {
        var seed = new WorldSeed(17);
        var target = new WorldMinute(Season + 2L * WorldCalendar.MinutesPerDay);
        var single = new SimulationEngine(seed, simulationRulesVersion: Roads);
        single.AdvanceUntil(target);
        var chunked = new SimulationEngine(seed, simulationRulesVersion: Roads);
        for (var minute = 997L; minute < target.Value; minute += 997L) chunked.AdvanceUntil(new WorldMinute(minute));
        chunked.AdvanceUntil(target);
        AssertEquivalent(single, chunked);
    }

    internal static void AssertEquivalent(SimulationEngine expected, SimulationEngine actual)
    {
        var left = expected.CreatePersistenceSnapshot();
        var right = actual.CreatePersistenceSnapshot();
        Assert.Equal(left.WorldMinute, right.WorldMinute);
        Assert.Equal(left.MigrationStateJson, right.MigrationStateJson);
        Assert.Equal(left.LivingStateJson, right.LivingStateJson);
        Assert.Equal(left.Citizens, right.Citizens);
        Assert.Equal(left.Counters, right.Counters);
        Assert.Equal(expected.SurvivalFingerprint, actual.SurvivalFingerprint);
        Assert.Equal(expected.SettlementFingerprint, actual.SettlementFingerprint);
        Assert.Equal(expected.SocialFingerprint, actual.SocialFingerprint);
        Assert.Equal(expected.HistoryFingerprint, actual.HistoryFingerprint);
    }
}
