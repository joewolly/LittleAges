using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class M15HistoryContractTests
{
    [Fact]
    public void TradeAndRoadPayloadsUseFixedOrderCanonicalJson()
    {
        Assert.Equal("{\"partyId\":\"9\",\"traderId\":\"4\",\"originSettlementId\":\"1\",\"destinationSettlementId\":\"2\",\"outboundGood\":\"Food\",\"outboundQuantity\":30,\"returnGood\":\"Tool\"}",
            HistoricalEventPayloads.TradeDeparted(9, 4, 1, 2, MigrationCargoGood.Food, 30, MigrationCargoGood.Tool));
        Assert.Equal("{\"partyId\":\"9\",\"traderId\":\"4\",\"originSettlementId\":\"1\",\"destinationSettlementId\":\"2\",\"outboundGood\":\"Food\",\"outboundQuantity\":30,\"returnGood\":\"Tool\",\"returnQuantity\":2}",
            HistoricalEventPayloads.TradeCompleted(9, 4, 1, 2, MigrationCargoGood.Food, 30, MigrationCargoGood.Tool, 2));
        Assert.Equal("{\"partyId\":\"9\",\"traderId\":\"4\",\"originSettlementId\":\"2\",\"destinationSettlementId\":\"1\",\"exchanged\":false}",
            HistoricalEventPayloads.TradeReturned(9, 4, 2, 1, exchanged: false));
        Assert.Equal("{\"partyId\":\"9\",\"traderId\":\"4\",\"originSettlementId\":\"1\",\"destinationSettlementId\":\"2\"}",
            HistoricalEventPayloads.TradeLost(9, 4, 1, 2));
        Assert.Equal("{\"settlementId\":\"2\",\"tilesBuilt\":3,\"season\":\"Autumn\",\"year\":4}",
            HistoricalEventPayloads.RoadWorkSeason(2, 3, WorldSeason.Autumn, 4));
        Assert.Equal("{\"grade\":\"Road\"}", HistoricalEventPayloads.RouteConnected(RoadGrade.Road));

        foreach (var (type, payload) in new[]
        {
            (HistoricalEventType.TradeDeparted, HistoricalEventPayloads.TradeDeparted(9, 4, 1, 2, MigrationCargoGood.Food, 30, MigrationCargoGood.Tool)),
            (HistoricalEventType.TradeCompleted, HistoricalEventPayloads.TradeCompleted(9, 4, 1, 2, MigrationCargoGood.Food, 30, MigrationCargoGood.Tool, 2)),
            (HistoricalEventType.TradeReturned, HistoricalEventPayloads.TradeReturned(9, 4, 1, 2, exchanged: true)),
            (HistoricalEventType.TradeLost, HistoricalEventPayloads.TradeLost(9, 4, 1, 2)),
            (HistoricalEventType.RoadWorkSeason, HistoricalEventPayloads.RoadWorkSeason(1, 1, WorldSeason.Spring, 0)),
            (HistoricalEventType.RouteConnected, HistoricalEventPayloads.RouteConnected(RoadGrade.Trail))
        })
        {
            Assert.True(HistoricalEventPayloads.IsCanonical(type, payload));
            Assert.False(HistoricalEventPayloads.IsCanonical(type, payload + " "));
        }
    }

    [Fact]
    public void TradeAndRoadPayloadsRejectImpossibleValues()
    {
        Assert.ThrowsAny<ArgumentException>(() => HistoricalEventPayloads.TradeDeparted(9, 4, 1, 1, MigrationCargoGood.Food, 30, MigrationCargoGood.Tool));
        Assert.ThrowsAny<ArgumentException>(() => HistoricalEventPayloads.TradeDeparted(9, 4, 1, 2, MigrationCargoGood.Tool, 30, MigrationCargoGood.Tool));
        Assert.ThrowsAny<ArgumentException>(() => HistoricalEventPayloads.TradeCompleted(9, 4, 1, 2, MigrationCargoGood.Food, 0, MigrationCargoGood.Tool, 2));
        Assert.ThrowsAny<ArgumentException>(() => HistoricalEventPayloads.RoadWorkSeason(1, 0, WorldSeason.Spring, 0));
        Assert.ThrowsAny<ArgumentException>(() => HistoricalEventPayloads.RouteConnected(RoadGrade.Track));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.TradeDeparted,
            "{\"partyId\":\"9\",\"traderId\":\"4\",\"originSettlementId\":\"1\",\"destinationSettlementId\":\"2\",\"outboundGood\":\"4\",\"outboundQuantity\":30,\"returnGood\":\"Tool\"}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.RouteConnected, "{\"grade\":\"None\"}"));
    }

    [Fact]
    public void TradeEventsLinkOnlyTheirTraderAndRoadEventsLinkNothing()
    {
        var id = new HistoricalEventId(5);
        var departed = new HistoricalEvent(id, 10, HistoricalEventType.TradeDeparted, HistoricalImportance.Routine, HistoricalEventOrigin.Live,
            new TileCoordinate(1, 1), HistoricalEventPayloads.TradeDeparted(9, 4, 1, 2, MigrationCargoGood.Food, 30, MigrationCargoGood.Tool)).Validate();
        departed.ValidateLinks([new HistoricalEventCitizenLink(id, new CitizenId(4), "subject")], []);
        Assert.Throws<ArgumentException>(() => departed.ValidateLinks([new HistoricalEventCitizenLink(id, new CitizenId(5), "subject")], []));
        Assert.Throws<ArgumentException>(() => departed.ValidateLinks([], []));

        var connected = new HistoricalEvent(id, 10, HistoricalEventType.RouteConnected, HistoricalImportance.Notable, HistoricalEventOrigin.Live,
            new TileCoordinate(1, 1), HistoricalEventPayloads.RouteConnected(RoadGrade.Trail)).Validate();
        connected.ValidateLinks([], []);
        Assert.Throws<ArgumentException>(() => connected.ValidateLinks([new HistoricalEventCitizenLink(id, new CitizenId(4), "subject")], []));
    }
}
