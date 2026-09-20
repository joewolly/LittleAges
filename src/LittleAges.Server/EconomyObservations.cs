using System.Globalization;
using LittleAges.Domain;

namespace LittleAges.Server;

public sealed record HouseholdEconomyObservation(string HouseholdId, Goods Inventory, Goods Reserved, Goods InTransitAndEscrow, long Wealth, string Standing);
public sealed record OccupationObservation(string CitizenId, string Specialization, long AssignedMinute);
public sealed record OfferObservation(string HouseholdId, string Resource, int Surplus, int Requested, long UpdatedMinute);
public sealed record TradeObservation(string TradeId, string MarketId, string HouseholdA, string HouseholdB,
    string ResourceA, int QuantityA, string ResourceB, int QuantityB, string? CarrierA, string? CarrierB,
    bool PickedA, bool PickedB, bool DeliveredA, bool DeliveredB, string Status, long CreatedMinute, long? ClosedMinute);
public sealed record EconomicEventObservation(string EventId, long WorldMinute, string Kind, string HouseholdId, string? RecipientHouseholdId, Goods Goods);
public sealed record RecoverableObservation(string CacheId, string? HouseholdId, TileCoordinate Location, string Resource, int Quantity);
public sealed record PublicSupplyObservation(string TransactionId, long WorldMinute, string CitizenId, string HouseholdId, string Resource, int Quantity, int FoodPaid);
public sealed record EconomyObservation(int Version, int CommunalPercent, Goods Commons, Goods Produced, long FoodConsumed,
    long EmergencyFoodConsumed, long PublicWorkPaid, long ReservedPublicFood,
    IReadOnlyList<HouseholdEconomyObservation> Households, IReadOnlyList<OccupationObservation> Occupations,
    IReadOnlyList<OfferObservation> Offers, IReadOnlyList<TradeObservation> Trades,
    IReadOnlyList<EconomicEventObservation> Events, IReadOnlyList<RecoverableObservation> Recoverable, IReadOnlyList<PublicSupplyObservation> PublicSupplyTrades)
{
    public static EconomyObservation? Create(EconomyState? economy, SettlementState? commons, IReadOnlyList<Citizen> citizens)
    {
        if (economy is null || commons is null) return null;
        var pending = economy.Trades.Where(t => t.Status == BarterStatus.Reserved).ToArray();
        var households = economy.Households.Select(h =>
        {
            var reserved = new Goods(); var transit = new Goods();
            foreach (var t in pending)
            {
                if (t.HouseholdA == h.HouseholdId) { if (t.PickedA) transit = transit.Add(t.ResourceA, t.QuantityA); else reserved = reserved.Add(t.ResourceA, t.QuantityA); }
                if (t.HouseholdB == h.HouseholdId) { if (t.PickedB) transit = transit.Add(t.ResourceB, t.QuantityB); else reserved = reserved.Add(t.ResourceB, t.QuantityB); }
            }
            foreach (var c in economy.ProductionCargo.Where(g => g.HouseholdId == h.HouseholdId).Select(g => citizens.Single(c => c.Id.Value == g.CitizenId))) transit = transit.Add(c.CarriedResourceType!.Value, c.CarriedResourceQuantity);
            var ground = economy.Recoverable.Where(g => g.HouseholdId == h.HouseholdId).Aggregate(new Goods(), (sum, g) => sum.Add(g.Resource, g.Quantity));
            var members = citizens.Count(c => c.IsAlive && c.HouseholdId?.Value == h.HouseholdId);
            return new HouseholdEconomyObservation(Id(h.HouseholdId), h.Holdings, reserved, transit, h.Holdings.Plus(transit).Plus(ground).Value,
                h.Holdings.Food < members * 10 ? "Food insecure" : h.Holdings.Food >= members * 60 ? "Food reserve met" : "Building reserves");
        }).ToArray();
        return new(1, economy.CommunalPercent, new(commons.FoodStored, commons.WoodStored, commons.StoneStored), economy.Produced, economy.FoodConsumed, economy.EmergencyFoodConsumed, economy.PublicWorkPaid, economy.PublicWork.Sum(w => (long)w.Food),
            Array.AsReadOnly(households), Array.AsReadOnly(economy.Assignments.Select(a => new OccupationObservation(Id(a.CitizenId), a.Specialization.ToString(), a.AssignedMinute)).ToArray()),
            Array.AsReadOnly(economy.Offers.Select(o => new OfferObservation(Id(o.HouseholdId), o.Resource.ToString(), o.Surplus, o.Requested, o.UpdatedMinute)).ToArray()),
            Array.AsReadOnly(economy.Trades.Select(t => new TradeObservation(Id(t.Id), Id(t.MarketId), Id(t.HouseholdA), Id(t.HouseholdB), t.ResourceA.ToString(), t.QuantityA, t.ResourceB.ToString(), t.QuantityB, NullableId(t.CarrierA), NullableId(t.CarrierB), t.PickedA, t.PickedB, t.DeliveredA, t.DeliveredB, t.Status.ToString(), t.CreatedMinute, t.ClosedMinute)).ToArray()),
            Array.AsReadOnly(economy.Events.Select(e => new EconomicEventObservation(Id(e.Id), e.WorldMinute, e.Kind, Id(e.HouseholdId), NullableId(e.RecipientHouseholdId), e.Goods)).ToArray()),
            Array.AsReadOnly(economy.Recoverable.Select(g => new RecoverableObservation(Id(g.Id), NullableId(g.HouseholdId), g.Location, g.Resource.ToString(), g.Quantity)).ToArray()),
            Array.AsReadOnly(economy.PublicSupplyTrades.Select(t => new PublicSupplyObservation(Id(t.Id), t.WorldMinute, Id(t.CitizenId), Id(t.HouseholdId), t.Resource.ToString(), t.Quantity, t.FoodPaid)).ToArray()));
    }
    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
    private static string? NullableId(long? id) => id is { } value ? Id(value) : null;
}
