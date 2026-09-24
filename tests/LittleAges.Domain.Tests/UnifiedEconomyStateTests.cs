using System.Text.Json;
using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class UnifiedEconomyStateTests
{
    [Fact]
    public void UnifiedEconomyJsonPersistsCoordinatesWithoutChangingLegacyCanonicalJson()
    {
        var location = new TileCoordinate(3, 7);
        var state = CreateState(location);

        var legacyJson = state.ToCanonicalJson();
        Assert.Equal("{\"Version\":1,\"CommunalPercent\":20,\"NextTradeId\":1,\"NextCacheId\":1,\"NextEventId\":1,\"Produced\":{\"Food\":0,\"Wood\":0,\"Stone\":0},\"FoodConsumed\":0,\"EmergencyFoodConsumed\":0,\"PublicWorkPaid\":0,\"Households\":[],\"Members\":[],\"Assignments\":[],\"Offers\":[],\"Trades\":[],\"PublicWork\":[],\"Recoverable\":[{\"Id\":1,\"HouseholdId\":null,\"Location\":{},\"Resource\":1,\"Quantity\":2}],\"Events\":[],\"PublicSupplyTrades\":[],\"ProductionCargo\":[]}", legacyJson);

        var unifiedJson = state.ToUnifiedCanonicalJson();
        Assert.Contains("\"Location\":{\"X\":3,\"Y\":7}", unifiedJson, StringComparison.Ordinal);
        var parsed = EconomyState.ParseUnified(unifiedJson);
        Assert.Equal(location, Assert.Single(parsed.Recoverable).Location);

        var missingX = unifiedJson.Replace("\"X\":3,", string.Empty, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EconomyState.ParseUnified(missingX));
    }

    private static EconomyState CreateState(TileCoordinate location) => new(
        1, EconomyRules.CommunalPercent, 1, 1, 1, new Goods(), 0, 0, 0,
        Array.Empty<HouseholdStock>(), Array.Empty<EconomicMember>(), Array.Empty<WorkAssignment>(), Array.Empty<BarterOffer>(),
        Array.Empty<BarterTrade>(), Array.Empty<PublicWorkReservation>(),
        [new RecoverableGoods(1, null, location, ResourceType.Food, 2)],
        Array.Empty<EconomicEvent>(), Array.Empty<PublicSupplyTrade>(), Array.Empty<ProductionCargoOwner>());
}
