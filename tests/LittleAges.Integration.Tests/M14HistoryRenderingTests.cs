using LittleAges.Domain;
using LittleAges.Server;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class M14HistoryRenderingTests
{
    [Fact]
    public void MigrationSummariesUseNamesAndPreserveEventDirectionAndSettlementIds()
    {
        var expedition = HistoricalEventPayloads.ExpeditionDeparted(7, 8, 1, 12, 34, 2);
        Assert.Equal("Expedition 7 departed Settlement 1 for site (12, 34) with 2 travelers: Lina and Orin.",
            Render(HistoricalEventType.ExpeditionDeparted, expedition, (10, "participant", "Lina"), (11, "participant", "Orin")));
        Assert.Equal("Expedition 7 returned to Settlement 1 from site (12, 34) with 2 travelers: Lina and Orin.",
            Render(HistoricalEventType.ExpeditionReturned, HistoricalEventPayloads.ExpeditionReturned(7, 8, 1, 12, 34, 2), (10, "participant", "Lina"), (11, "participant", "Orin")));
        Assert.Equal("Expedition 7 ended without founding a settlement at site (12, 34); its party included 2 travelers: Lina and Orin.",
            Render(HistoricalEventType.ExpeditionLost, HistoricalEventPayloads.ExpeditionLost(7, 8, 1, 12, 34, 2), (10, "participant", "Lina"), (11, "participant", "Orin")));
        Assert.Equal("Settlement 2 was founded by 2 founders from expedition 7: Lina and Orin.",
            Render(HistoricalEventType.DaughterSettlementFounded, HistoricalEventPayloads.DaughterSettlementFounded(2, 7, 8, 2), (10, "founder", "Lina"), (11, "founder", "Orin")));
        Assert.Equal("Household 8 relocated 2 members from Settlement 1 to Settlement 2: Lina and Orin.",
            Render(HistoricalEventType.HouseholdRelocated, HistoricalEventPayloads.HouseholdRelocated(8, 1, 2, 2), (10, "member", "Lina"), (11, "member", "Orin")));
        Assert.Equal("Lina departed Settlement 1 to visit Orin in Settlement 2.",
            Render(HistoricalEventType.FamilyVisitDeparted, HistoricalEventPayloads.FamilyVisitDeparted(10, 11, 1, 2), (10, "subject", "Lina"), (11, "participant", "Orin")));
        Assert.Equal("Lina returned to Settlement 1 from a trip to see Orin in Settlement 2.",
            Render(HistoricalEventType.FamilyVisitReturned, HistoricalEventPayloads.FamilyVisitReturned(10, 11, 1, 2), (10, "subject", "Lina"), (11, "participant", "Orin")));
    }

    [Fact]
    public void ExistingHistoricalSummariesKeepTheirEstablishedText()
    {
        Assert.Equal("The world was created with seed 42.",
            Render(HistoricalEventType.WorldCreated, HistoricalEventPayloads.WorldCreated(new WorldSeed(42))));
        Assert.Equal("Lina was born.",
            Render(HistoricalEventType.CitizenBorn, HistoricalEventPayloads.CitizenBorn(null), (10, "subject", "Lina")));
        Assert.Equal("The settlement was founded by 2 founders.",
            Render(HistoricalEventType.SettlementFounded, HistoricalEventPayloads.SettlementFounded(2)));
    }

    private static string Render(HistoricalEventType type, string payload, params (long CitizenId, string Role, string Name)[] namedLinks)
    {
        var historicalEvent = new HistoricalEvent(new HistoricalEventId(1), 0, type,
            HistoricalImportance.Notable, HistoricalEventOrigin.Live, null, payload);
        var links = namedLinks.Select(link => new HistoricalEventCitizenLink(historicalEvent.Id,
            new CitizenId(link.CitizenId), link.Role));
        var names = namedLinks.ToDictionary(link => link.CitizenId, link => link.Name);
        return new ServerHistoricalEventSnapshot(historicalEvent, links, [], names).Summary;
    }
}
