using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class M14MigrationHistoryContractTests
{
    [Fact]
    public void MigrationEventTypesAppendWithoutChangingExistingValuesOrSchemaVersion()
    {
        Assert.Equal(Enumerable.Range(1, 22), Enum.GetValues<HistoricalEventType>().Select(value => (int)value));
        Assert.Equal(2, (int)HistoricalEventType.SettlementFounded);
        Assert.Equal(15, (int)HistoricalEventType.SeasonStarted);
        Assert.Equal(16, (int)HistoricalEventType.ExpeditionDeparted);
        Assert.Equal(17, (int)HistoricalEventType.ExpeditionReturned);
        Assert.Equal(18, (int)HistoricalEventType.ExpeditionLost);
        Assert.Equal(19, (int)HistoricalEventType.DaughterSettlementFounded);
        Assert.Equal(20, (int)HistoricalEventType.HouseholdRelocated);
        Assert.Equal(21, (int)HistoricalEventType.FamilyVisitDeparted);
        Assert.Equal(22, (int)HistoricalEventType.FamilyVisitReturned);
        Assert.Equal(1, HistoricalEvent.CurrentSchemaVersion);

        Assert.Equal("{\"founderCount\":2}", HistoricalEventPayloads.SettlementFounded(2));
        Assert.True(HistoricalEventPayloads.IsCanonical(HistoricalEventType.SettlementFounded,
            "{\"founderCount\":2}"));
    }

    [Fact]
    public void MigrationPayloadBuildersProduceFixedOrderCanonicalJson()
    {
        var expedition = "{\"partyId\":\"7\",\"householdId\":\"8\",\"originSettlementId\":\"1\",\"destinationX\":12,\"destinationY\":34,\"travelerCount\":5}";
        var daughterFounded = "{\"settlementId\":\"2\",\"partyId\":\"7\",\"householdId\":\"8\",\"founderCount\":3}";
        var relocation = "{\"householdId\":\"8\",\"originSettlementId\":\"1\",\"destinationSettlementId\":\"2\",\"travelerCount\":5}";
        var familyVisit = "{\"visitorId\":\"15\",\"relativeId\":\"16\",\"originSettlementId\":\"1\",\"destinationSettlementId\":\"2\"}";

        Assert.Equal(expedition, HistoricalEventPayloads.ExpeditionDeparted(7, 8, 1, 12, 34, 5));
        Assert.Equal(expedition, HistoricalEventPayloads.ExpeditionReturned(7, 8, 1, 12, 34, 5));
        Assert.Equal(expedition, HistoricalEventPayloads.ExpeditionLost(7, 8, 1, 12, 34, 5));
        Assert.Equal(daughterFounded, HistoricalEventPayloads.DaughterSettlementFounded(2, 7, 8, 3));
        Assert.Equal(relocation, HistoricalEventPayloads.HouseholdRelocated(8, 1, 2, 5));
        Assert.Equal(familyVisit, HistoricalEventPayloads.FamilyVisitDeparted(15, 16, 1, 2));
        Assert.Equal(familyVisit, HistoricalEventPayloads.FamilyVisitReturned(15, 16, 1, 2));

        var payloads = new (HistoricalEventType Type, string Payload)[]
        {
            (HistoricalEventType.ExpeditionDeparted, expedition),
            (HistoricalEventType.ExpeditionReturned, expedition),
            (HistoricalEventType.ExpeditionLost, expedition),
            (HistoricalEventType.DaughterSettlementFounded, daughterFounded),
            (HistoricalEventType.HouseholdRelocated, relocation),
            (HistoricalEventType.FamilyVisitDeparted, familyVisit),
            (HistoricalEventType.FamilyVisitReturned, familyVisit)
        };

        for (var index = 0; index < payloads.Length; index++)
        {
            var (type, payload) = payloads[index];
            Assert.True(HistoricalEventPayloads.IsCanonical(type, payload), $"{type}: {payload}");
            new HistoricalEvent(new HistoricalEventId(index + 1), 0, type, HistoricalImportance.Notable,
                HistoricalEventOrigin.Live, null, payload).Validate(0);
        }
    }

    [Fact]
    public void MigrationPayloadValidationRejectsMalformedOrderIdsCoordinatesCountsAndSettlementPairs()
    {
        var expedition = HistoricalEventPayloads.ExpeditionDeparted(7, 8, 1, 12, 34, 5);
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.ExpeditionDeparted,
            "{\"householdId\":\"8\",\"partyId\":\"7\",\"originSettlementId\":\"1\",\"destinationX\":12,\"destinationY\":34,\"travelerCount\":5}"));
        Assert.False(IsCanonical(HistoricalEventType.ExpeditionDeparted, expedition, "\"partyId\":\"7\"", "\"partyId\":\"07\""));
        Assert.False(IsCanonical(HistoricalEventType.ExpeditionDeparted, expedition, "\"householdId\":\"8\"", "\"householdId\":8"));
        Assert.False(IsCanonical(HistoricalEventType.ExpeditionDeparted, expedition, "\"originSettlementId\":\"1\"", "\"originSettlementId\":\"0\""));
        Assert.False(IsCanonical(HistoricalEventType.ExpeditionDeparted, expedition, "\"destinationX\":12", "\"destinationX\":-1"));
        Assert.False(IsCanonical(HistoricalEventType.ExpeditionDeparted, expedition, "\"destinationY\":34", "\"destinationY\":2147483648"));
        Assert.False(IsCanonical(HistoricalEventType.ExpeditionDeparted, expedition, "\"travelerCount\":5", "\"travelerCount\":0"));

        var daughterFounded = HistoricalEventPayloads.DaughterSettlementFounded(2, 7, 8, 3);
        Assert.False(IsCanonical(HistoricalEventType.DaughterSettlementFounded, daughterFounded, "\"settlementId\":\"2\"", "\"settlementId\":\"02\""));
        Assert.False(IsCanonical(HistoricalEventType.DaughterSettlementFounded, daughterFounded, "\"partyId\":\"7\"", "\"partyId\":\"0\""));
        Assert.False(IsCanonical(HistoricalEventType.DaughterSettlementFounded, daughterFounded, "\"householdId\":\"8\"", "\"householdId\":\"9223372036854775808\""));
        Assert.False(IsCanonical(HistoricalEventType.DaughterSettlementFounded, daughterFounded, "\"founderCount\":3", "\"founderCount\":0"));

        var relocation = HistoricalEventPayloads.HouseholdRelocated(8, 1, 2, 5);
        Assert.False(IsCanonical(HistoricalEventType.HouseholdRelocated, relocation, "\"destinationSettlementId\":\"2\"", "\"destinationSettlementId\":\"1\""));
        Assert.False(IsCanonical(HistoricalEventType.HouseholdRelocated, relocation, "\"householdId\":\"8\"", "\"householdId\":\"-8\""));
        Assert.False(IsCanonical(HistoricalEventType.HouseholdRelocated, relocation, "\"travelerCount\":5", "\"travelerCount\":0"));

        var familyVisit = HistoricalEventPayloads.FamilyVisitDeparted(15, 16, 1, 2);
        Assert.False(IsCanonical(HistoricalEventType.FamilyVisitDeparted, familyVisit, "\"visitorId\":\"15\"", "\"visitorId\":\"+15\""));
        Assert.False(IsCanonical(HistoricalEventType.FamilyVisitDeparted, familyVisit, "\"relativeId\":\"16\"", "\"relativeId\":\"16.0\""));
        Assert.False(IsCanonical(HistoricalEventType.FamilyVisitDeparted, familyVisit, "\"destinationSettlementId\":\"2\"", "\"destinationSettlementId\":\"1\""));

        Assert.Throws<ArgumentOutOfRangeException>(() => HistoricalEventPayloads.ExpeditionDeparted(0, 8, 1, 12, 34, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => HistoricalEventPayloads.ExpeditionDeparted(7, 8, 1, -1, 34, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => HistoricalEventPayloads.DaughterSettlementFounded(2, 7, 8, 0));
        Assert.Throws<ArgumentException>(() => HistoricalEventPayloads.HouseholdRelocated(8, 1, 1, 5));
        Assert.Throws<ArgumentException>(() => HistoricalEventPayloads.FamilyVisitDeparted(15, 16, 2, 2));
    }

    [Fact]
    public void MigrationEventLinksRequireSpecifiedRolesCountsAndNoStructures()
    {
        var expeditionPayload = HistoricalEventPayloads.ExpeditionDeparted(7, 8, 1, 12, 34, 5);
        var daughterPayload = HistoricalEventPayloads.DaughterSettlementFounded(2, 7, 8, 2);
        var relocationPayload = HistoricalEventPayloads.HouseholdRelocated(8, 1, 2, 2);
        var familyVisitPayload = HistoricalEventPayloads.FamilyVisitDeparted(15, 16, 1, 2);
        var cases = new (HistoricalEventType Type, string Payload, (long CitizenId, string Role)[] Links)[]
        {
            (HistoricalEventType.ExpeditionDeparted, expeditionPayload, [(10, "participant")]),
            (HistoricalEventType.ExpeditionReturned, expeditionPayload, [(10, "participant")]),
            (HistoricalEventType.ExpeditionLost, expeditionPayload, [(10, "participant")]),
            (HistoricalEventType.DaughterSettlementFounded, daughterPayload, [(10, "founder"), (11, "founder")]),
            (HistoricalEventType.HouseholdRelocated, relocationPayload, [(10, "member"), (11, "member")]),
            (HistoricalEventType.FamilyVisitDeparted, familyVisitPayload, [(15, "subject"), (16, "participant")]),
            (HistoricalEventType.FamilyVisitReturned, familyVisitPayload, [(15, "subject"), (16, "participant")])
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var item = cases[index];
            var historicalEvent = MigrationEvent(item.Type, item.Payload, 50 + index);
            var links = item.Links.Select(link => CitizenLink(historicalEvent, link.CitizenId, link.Role)).ToArray();

            historicalEvent.ValidateLinks(links, []);
            Assert.Throws<ArgumentException>(() => historicalEvent.ValidateLinks(links,
                [new HistoricalEventStructureLink(historicalEvent.Id, new StructureId(1))]));
        }

        var departed = MigrationEvent(HistoricalEventType.ExpeditionDeparted, expeditionPayload, 70);
        Assert.Throws<ArgumentException>(() => departed.ValidateLinks([], []));
        Assert.Throws<ArgumentException>(() => departed.ValidateLinks([CitizenLink(departed, 10, "subject")], []));

        var daughter = MigrationEvent(HistoricalEventType.DaughterSettlementFounded, daughterPayload, 71);
        Assert.Throws<ArgumentException>(() => daughter.ValidateLinks([CitizenLink(daughter, 10, "founder")], []));
        Assert.Throws<ArgumentException>(() => daughter.ValidateLinks(
            [CitizenLink(daughter, 10, "founder"), CitizenLink(daughter, 11, "member")], []));

        var relocation = MigrationEvent(HistoricalEventType.HouseholdRelocated, relocationPayload, 72);
        Assert.Throws<ArgumentException>(() => relocation.ValidateLinks([CitizenLink(relocation, 10, "member")], []));
        Assert.Throws<ArgumentException>(() => relocation.ValidateLinks(
            [CitizenLink(relocation, 10, "participant"), CitizenLink(relocation, 11, "participant")], []));

        var visit = MigrationEvent(HistoricalEventType.FamilyVisitDeparted, familyVisitPayload, 73);
        Assert.Throws<ArgumentException>(() => visit.ValidateLinks(
            [CitizenLink(visit, 15, "subject"), CitizenLink(visit, 16, "subject")], []));
        Assert.Throws<ArgumentException>(() => visit.ValidateLinks(
            [CitizenLink(visit, 15, "subject"), CitizenLink(visit, 15, "participant")], []));
        Assert.Throws<ArgumentException>(() => visit.ValidateLinks(
            [CitizenLink(visit, 15, "subject"), CitizenLink(visit, 16, "participant"), CitizenLink(visit, 17, "participant")], []));
    }

    private static HistoricalEvent MigrationEvent(HistoricalEventType type, string payload, long eventId) =>
        new(new HistoricalEventId(eventId), 0, type, HistoricalImportance.Notable, HistoricalEventOrigin.Live, null, payload);

    private static HistoricalEventCitizenLink CitizenLink(HistoricalEvent historicalEvent, long citizenId, string role) =>
        new(historicalEvent.Id, new CitizenId(citizenId), role);

    private static bool IsCanonical(HistoricalEventType type, string payload, string oldValue, string newValue) =>
        HistoricalEventPayloads.IsCanonical(type, payload.Replace(oldValue, newValue, StringComparison.Ordinal));
}
