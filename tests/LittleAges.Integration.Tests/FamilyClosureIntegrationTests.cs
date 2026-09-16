using LittleAges.Domain;
using LittleAges.Server;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class FamilyClosureIntegrationTests
{
    [Fact]
    public void FamilyClosureIncludesOnlyRootBoundedRelationsAcrossThreeGenerations()
    {
        var citizens = new[]
        {
            Citizen("1"), Citizen("2"), Citizen("3"), Citizen("4"), Citizen("5"), Citizen("6"),
            Citizen("7", "1", "2"), Citizen("8", "3", "4"), Citizen("9", "1", "5"),
            Citizen("10", "7", "8", partnerId: "12"), Citizen("11", "7", "8", partnerId: "13"),
            Citizen("12", "5", "6", partnerId: "10"), Citizen("13", "5", "6", partnerId: "11"),
            Citizen("14", "10", "12"), Citizen("15", "11", "13"),
            Citizen("16", "14", "3"), Citizen("17", "15", "2")
        };

        var closure = global::FamilyClosureRules.Closure("10", citizens);

        var included = new[] { "1", "2", "3", "4", "7", "8", "10", "11", "12", "14", "16" };
        var excluded = new[] { "5", "6", "9", "13", "15", "17" };
        Assert.True(included.ToHashSet(StringComparer.Ordinal).SetEquals(closure));
        Assert.Empty(excluded.Intersect(closure, StringComparer.Ordinal));
    }

    private static ServerCitizenSnapshot Citizen(string id, string? parentAId = null, string? parentBId = null, string? partnerId = null)
    {
        static CitizenId? ParseId(string? value) => value is null ? null : new CitizenId(long.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        var snapshot = new CitizenReadSnapshot(
            id,
            null,
            "Given",
            "Family",
            $"Citizen {id}",
            20,
            "Adult",
            new TileCoordinate(0, 0),
            10_000,
            new CitizenNeeds(),
            new CitizenTraits(5_000, 5_000, 5_000, 5_000, 5_000, 5_000),
            new CitizenSkills(0, 0, 0, 0, 0, 0),
            CitizenAction.None,
            null,
            null,
            null,
            0,
            ParentAId: ParseId(parentAId),
            ParentBId: ParseId(parentBId),
            PartnerId: ParseId(partnerId));
        return new ServerCitizenSnapshot(snapshot);
    }
}
