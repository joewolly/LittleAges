using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class MigrationStateTests
{
    [Fact]
    public void InitialMigrationStateHasStableCanonicalRoundTripWithoutSecondSiteStock()
    {
        var state = new MigrationWorldState(1,
            [new MigrationEntityResidence(1, 1)], [], [], [], []);

        var json = state.ToCanonicalJson();

        Assert.Equal(json, MigrationWorldState.Parse(json).ToCanonicalJson());
        Assert.DoesNotContain("foundingPressure", json, StringComparison.Ordinal);
        Assert.Null(MigrationWorldState.Parse(json).DaughterSettlement);
    }

    [Fact]
    public void FoundingPressureIsCanonicalPerHouseholdAndSurvivesRoundTrip()
    {
        var state = new MigrationWorldState(1, [], [new MigrationEntityResidence(4, 1),
            new MigrationEntityResidence(9, 1)], [], [], [], foundingPressure:
        [
            new MigrationFoundingPressureState(9, 120),
            new MigrationFoundingPressureState(4, 60)
        ]);

        var json = state.ToCanonicalJson();
        var parsed = MigrationWorldState.Parse(json);

        Assert.Equal(new long[] { 4, 9 }, parsed.FoundingPressure!.Select(x => x.HouseholdId));
        Assert.Equal(new long[] { 60, 120 }, parsed.FoundingPressure!.Select(x => x.SinceMinute));
        Assert.Equal(json, parsed.ToCanonicalJson());
    }

    [Fact]
    public void SettlementTwoOwnershipRequiresDaughterSite()
    {
        var state = new MigrationWorldState(1, [], [], [], [], [], inTransitParties: [],
            daughterSettlement: null);
        var missingDaughter = new MigrationWorldState(1,
            [new MigrationEntityResidence(1, 2)], [], [], [], []);

        Assert.Throws<ArgumentException>(() => missingDaughter.Validate());
        Assert.Empty(state.Validate().HouseholdResidences);
    }

    [Fact]
    public void ReturningFoundingAndRelocationPartiesRemainDistinctInCanonicalState()
    {
        var cargo = new[] { new MigrationCargoStackState(12, MigrationCargoGood.Food, 10, MigrationCargoPurpose.Provisions) };
        var foundingReturn = new MigrationTransitPartyState(10, 1, 1, 1, new TileCoordinate(2, 2),
            new TileCoordinate(1, 1), [2], cargo, 10, 0, returning: true);
        var founding = new MigrationWorldState(1, [new MigrationEntityResidence(2, 1)],
            [new MigrationEntityResidence(1, 1)], [], [], [], inTransitParties: [foundingReturn]);
        var foundingJson = founding.ToCanonicalJson();
        Assert.DoesNotContain("journeyKind", foundingJson, StringComparison.Ordinal);
        Assert.Equal(MigrationJourneyKind.Founding,
            MigrationWorldState.Parse(foundingJson).InTransitParties.Single().JourneyKind);

        var relocationReturn = new MigrationTransitPartyState(10, 1, 1, 1, new TileCoordinate(2, 2),
            new TileCoordinate(1, 1), [2], cargo, 10, 0, returning: true,
            journeyKind: MigrationJourneyKind.Relocation);
        var relocation = new MigrationWorldState(1, [new MigrationEntityResidence(2, 1)],
            [new MigrationEntityResidence(1, 1)], [], [], [], inTransitParties: [relocationReturn]);
        var relocationJson = relocation.ToCanonicalJson();
        Assert.Contains("journeyKind", relocationJson, StringComparison.Ordinal);
        Assert.Equal(MigrationJourneyKind.Relocation,
            MigrationWorldState.Parse(relocationJson).InTransitParties.Single().JourneyKind);
    }
}
