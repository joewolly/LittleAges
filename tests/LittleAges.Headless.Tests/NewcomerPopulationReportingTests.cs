using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Headless.Tests;

public sealed class NewcomerPopulationReportingTests
{
    [Fact]
    public void ExternalAdultEntersResidentHistoryAtAdmissionAndGuestArchivesNeverEnterIt()
    {
        var year = WorldCalendar.MinutesPerYear;
        var founder = Person(1, -20L * year, 0);
        var external = Person(2, -20L * year);
        external.DeathMinute = 8L * year;
        var child = Person(3, 7L * year);
        var newcomers = new NewcomersWorldState
        {
            Visitors = [
                new() { CitizenId = 2, JoinedMinute = 6L * year, Phase = NewcomerPhase.Dead },
                new() { CitizenId = 4, Person = Person(4, -20L * year), Phase = NewcomerPhase.Visiting },
                new() { CitizenId = 5, Person = Person(5, -20L * year), Phase = NewcomerPhase.Departed }
            ]
        };
        Citizen[] residents = [founder, external, child];

        Assert.Equal(1, ResidentPopulationHistory.AtMinute(residents, newcomers, 0));
        Assert.Equal(1, ResidentPopulationHistory.AtMinute(residents, newcomers, 6L * year - 1));
        Assert.Equal(2, ResidentPopulationHistory.AtMinute(residents, newcomers, 6L * year));
        Assert.Equal(3, ResidentPopulationHistory.AtMinute(residents, newcomers, 7L * year));
        Assert.Equal(2, ResidentPopulationHistory.AtMinute(residents, newcomers, 8L * year));
    }

    [Fact]
    public void LegacyPopulationStillUsesBirthAndDeathMinutes()
    {
        var founder = Person(1, -100, 0);
        founder.DeathMinute = 200;
        var child = Person(2, 100);
        Assert.Equal(1, ResidentPopulationHistory.AtMinute([founder, child], null, 0));
        Assert.Equal(2, ResidentPopulationHistory.AtMinute([founder, child], null, 100));
        Assert.Equal(1, ResidentPopulationHistory.AtMinute([founder, child], null, 200));
    }

    [Fact]
    public void NewcomerOutcomeEvidenceIsDeterministicAndAbsentFromLegacyReports()
    {
        var legacy = new HeadlessReport { Rules = SimulationEngine.FestivalsSimulationRulesVersion };
        Assert.DoesNotContain("newcomers", HeadlessReportSerialization.Serialize(legacy), StringComparison.Ordinal);
        Assert.DoesNotContain("newcomers", legacy.ToDeterministicReportJson(), StringComparison.Ordinal);

        var report = legacy with
        {
            Rules = SimulationEngine.NewcomersRulesVersion,
            Migration = new MigrationReadSnapshot(SimulationEngine.NewcomersRulesVersion, "7", 0, new string('a', 64), [], []),
            Newcomers = new HeadlessNewcomerSummary(3, 0, 1, 1, 1, 1, 3000, 0, 100, 900, 1000, 1000,
                new Dictionary<string, int>(), [])
        };
        using var normal = JsonDocument.Parse(HeadlessReportSerialization.Serialize(report));
        using var deterministic = JsonDocument.Parse(report.ToDeterministicReportJson());
        Assert.Equal(normal.RootElement.GetProperty("newcomers").GetRawText(), deterministic.RootElement.GetProperty("newcomers").GetRawText());
        Assert.Equal(1, deterministic.RootElement.GetProperty("newcomers").GetProperty("joined").GetInt32());
    }

    private static Citizen Person(long id, long birthMinute, int? ordinal = null) => new(new CitizenId(id), ordinal,
        "Ari", "Birch", birthMinute, new TileCoordinate(0, 0), new CitizenTraits(5000, 5000, 5000, 5000, 5000, 5000),
        new CitizenSkills(1000, 1000, 1000, 1000, 1000, 1000));
}
