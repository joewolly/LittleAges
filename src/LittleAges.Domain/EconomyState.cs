using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleAges.Domain;

public enum WorkSpecialization { Farmer = 1, Forager = 2, Woodcutter = 3, Stoneworker = 4, Builder = 5, Hauler = 6 }
public enum BarterStatus { Reserved = 0, Completed = 1, Cancelled = 2 }

public sealed record Goods(long Food = 0, long Wood = 0, long Stone = 0)
{
    public long Total => checked(Food + Wood + Stone);
    public long Value => checked(Food + Wood * 2 + Stone * 3);
    public long Get(ResourceType type) => type switch { ResourceType.Food => Food, ResourceType.Wood => Wood, ResourceType.Stone => Stone, _ => throw new ArgumentOutOfRangeException(nameof(type)) };
    public Goods Add(ResourceType type, long amount) => type switch { ResourceType.Food => this with { Food = checked(Food + amount) }, ResourceType.Wood => this with { Wood = checked(Wood + amount) }, ResourceType.Stone => this with { Stone = checked(Stone + amount) }, _ => throw new ArgumentOutOfRangeException(nameof(type)) };
    public Goods Plus(Goods other) => new(checked(Food + other.Food), checked(Wood + other.Wood), checked(Stone + other.Stone));
    public void Validate() { if (Food < 0 || Wood < 0 || Stone < 0) throw new ArgumentException("Goods cannot be negative."); _ = Value; }
}

public static class EconomyRules
{
    public const int CommunalPercent = 20;
    public const int FoundingStorageCapacity = 4000;
    public const int EligibleBirthChancePercent = 125;
    public const int MarketWood = 80;
    public const int MarketStone = 30;
    public const int MarketWork = 1000;
    public const int PublicWorkFood = 4;
    public static int Weight(ResourceType type) => type switch { ResourceType.Food => 1, ResourceType.Wood => 2, ResourceType.Stone => 3, _ => throw new ArgumentOutOfRangeException(nameof(type)) };
    public static readonly IReadOnlyList<ResourceType> Resources = Array.AsReadOnly(new[] { ResourceType.Food, ResourceType.Wood, ResourceType.Stone });
}

public sealed record HouseholdStock(long HouseholdId, Goods Holdings, Goods ContributionRemainders);
public sealed record EconomicMember(long CitizenId, long HouseholdId);
public sealed record WorkAssignment(long CitizenId, WorkSpecialization Specialization, long AssignedMinute);
public sealed record BarterOffer(long HouseholdId, ResourceType Resource, int Surplus, int Requested, long UpdatedMinute);
public sealed record BarterTrade(long Id, long CreatedMinute, long ExpiresMinute, long MarketId,
    long HouseholdA, long HouseholdB, ResourceType ResourceA, int QuantityA, ResourceType ResourceB, int QuantityB,
    long? CarrierA, long? CarrierB, bool PickedA, bool PickedB, bool DeliveredA, bool DeliveredB,
    BarterStatus Status, long? ClosedMinute);
public sealed record PublicWorkReservation(long CitizenId, long HouseholdId, int Food);
public sealed record PublicSupplyTrade(long Id, long WorldMinute, long CitizenId, long HouseholdId, ResourceType Resource, int Quantity, int FoodPaid);
public sealed record ProductionCargoOwner(long CitizenId, long HouseholdId);
public sealed record RecoverableGoods(long Id, long? HouseholdId, TileCoordinate Location, ResourceType Resource, int Quantity);
public sealed record EconomicEvent(long Id, long WorldMinute, string Kind, long HouseholdId, long? RecipientHouseholdId, Goods Goods);

public sealed record EconomyState(int Version, int CommunalPercent, long NextTradeId, long NextCacheId, long NextEventId,
    Goods Produced, long FoodConsumed, long EmergencyFoodConsumed, long PublicWorkPaid,
    IReadOnlyList<HouseholdStock> Households, IReadOnlyList<EconomicMember> Members,
    IReadOnlyList<WorkAssignment> Assignments, IReadOnlyList<BarterOffer> Offers,
    IReadOnlyList<BarterTrade> Trades, IReadOnlyList<PublicWorkReservation> PublicWork,
    IReadOnlyList<RecoverableGoods> Recoverable, IReadOnlyList<EconomicEvent> Events, IReadOnlyList<PublicSupplyTrade> PublicSupplyTrades, IReadOnlyList<ProductionCargoOwner> ProductionCargo)
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, IgnoreReadOnlyProperties = true };
    private static readonly JsonSerializerOptions UnifiedOptions = CreateUnifiedOptions();
    public string ToCanonicalJson() => JsonSerializer.Serialize(this, Options);
    public string ToUnifiedCanonicalJson() => JsonSerializer.Serialize(this, UnifiedOptions);
    public static EconomyState Parse(string json) => Parse(json, Options);
    public static EconomyState ParseUnified(string json) => Parse(json, UnifiedOptions);

    private static EconomyState Parse(string json, JsonSerializerOptions options)
    {
        var state = JsonSerializer.Deserialize<EconomyState>(json, options) ?? throw new InvalidDataException("Missing economy state.");
        if (state.Produced is null || state.Households is null || state.Members is null || state.Assignments is null || state.Offers is null || state.Trades is null || state.PublicWork is null || state.Recoverable is null || state.Events is null || state.PublicSupplyTrades is null || state.ProductionCargo is null || JsonSerializer.Serialize(state, options) != json) throw new InvalidDataException("Economy JSON is not canonical.");
        return state;
    }

    private static JsonSerializerOptions CreateUnifiedOptions()
    {
        var options = new JsonSerializerOptions(Options);
        options.Converters.Add(new TileCoordinateJsonConverter());
        return options;
    }

    public EconomyState Copy() => this with { Households = Array.AsReadOnly(Households.ToArray()), Members = Array.AsReadOnly(Members.ToArray()), Assignments = Array.AsReadOnly(Assignments.ToArray()), Offers = Array.AsReadOnly(Offers.ToArray()), Trades = Array.AsReadOnly(Trades.ToArray()), PublicWork = Array.AsReadOnly(PublicWork.ToArray()), Recoverable = Array.AsReadOnly(Recoverable.ToArray()), Events = Array.AsReadOnly(Events.ToArray()), PublicSupplyTrades = Array.AsReadOnly(PublicSupplyTrades.ToArray()), ProductionCargo = Array.AsReadOnly(ProductionCargo.ToArray()) };

    public Goods StoredGoods(SettlementState commons) => Households.Aggregate(new Goods(commons.FoodStored, commons.WoodStored, commons.StoneStored), (sum, h) => sum.Plus(h.Holdings))
        .Plus(Trades.Where(t => t.Status == BarterStatus.Reserved).Aggregate(new Goods(), (sum, t) => sum.Add(t.ResourceA, t.DeliveredA ? t.QuantityA : 0).Add(t.ResourceB, t.DeliveredB ? t.QuantityB : 0)))
        .Add(ResourceType.Food, PublicWork.Sum(p => (long)p.Food));

    private sealed class TileCoordinateJsonConverter : JsonConverter<TileCoordinate>
    {
        public override TileCoordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A persisted tile coordinate must be an object.");
            int? x = null;
            int? y = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("A persisted tile coordinate must contain named coordinates.");
                var name = reader.GetString();
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var coordinate) || coordinate < 0)
                    throw new JsonException("A persisted tile coordinate component must be a non-negative integer.");
                if (name == nameof(TileCoordinate.X) && x is null) x = coordinate;
                else if (name == nameof(TileCoordinate.Y) && y is null) y = coordinate;
                else throw new JsonException("A persisted tile coordinate contains an unknown or duplicate component.");
            }
            if (reader.TokenType != JsonTokenType.EndObject || x is null || y is null)
                throw new JsonException("A persisted tile coordinate must contain exactly one X and one Y component.");
            return new TileCoordinate(x.Value, y.Value);
        }

        public override void Write(Utf8JsonWriter writer, TileCoordinate value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(TileCoordinate.X), value.X);
            writer.WriteNumber(nameof(TileCoordinate.Y), value.Y);
            writer.WriteEndObject();
        }
    }

    public void Validate(IReadOnlyList<Citizen> citizens, IReadOnlyList<Household> households, IReadOnlyList<Structure> structures,
        SettlementState commons, WorldMap world, long minute, int capacity, int dedicatedFoodCapacity, Goods? additionalAccounted = null)
    {
        if (Version != 1 || CommunalPercent != EconomyRules.CommunalPercent || FoodConsumed < 0 || EmergencyFoodConsumed < 0 || EmergencyFoodConsumed > FoodConsumed || PublicWorkPaid < 0)
            throw new ArgumentException("Unsupported economy version or accounting totals.");
        Produced.Validate();
        Ordered(Households.Select(h => h.HouseholdId)); Ordered(Members.Select(m => m.CitizenId)); Ordered(Assignments.Select(a => a.CitizenId)); Ordered(Trades.Select(t => t.Id)); Ordered(PublicWork.Select(w => w.CitizenId)); Ordered(Recoverable.Select(g => g.Id)); Ordered(Events.Select(e => e.Id));
        if (!Households.Select(h => h.HouseholdId).SequenceEqual(households.Where(h => h.DissolvedMinute is null).Select(h => h.Id.Value).Order()) ||
            !Members.SequenceEqual(citizens.Where(c => c.IsAlive).OrderBy(c => c.Id.Value).Select(c => new EconomicMember(c.Id.Value, c.HouseholdId?.Value ?? 0)))) throw new ArgumentException("Every living citizen must have exactly one active economic household.");
        var owners = Households.ToDictionary(h => h.HouseholdId);
        Ordered(ProductionCargo.Select(g => g.CitizenId));
        if (!ProductionCargo.Select(g => g.CitizenId).SequenceEqual(citizens.Where(c => c.CarriedResourceQuantity > 0 && c.CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone or CitizenAction.HaulHarvest).Select(c => c.Id.Value).Order()) || ProductionCargo.Any(g => !owners.ContainsKey(g.HouseholdId))) throw new ArgumentException("Every production cargo must retain exactly one active household owner.");
        foreach (var h in Households) { h.Holdings.Validate(); h.ContributionRemainders.Validate(); if (EconomyRules.Resources.Any(r => h.ContributionRemainders.Get(r) >= 100)) throw new ArgumentException("Invalid production contribution remainder."); }
        if (citizens.Any(c => c.IsAlive && c.AgeYears(new WorldMinute(minute)) >= 18 && !Assignments.Any(a => a.CitizenId == c.Id.Value)) || Assignments.Any(a => !citizens.Any(c => c.Id.Value == a.CitizenId && c.IsAlive && c.AgeYears(new WorldMinute(minute)) >= 13))) throw new ArgumentException("Every working-age citizen needs an occupation.");
        foreach (var a in Assignments) if (!Enum.IsDefined(a.Specialization) || a.AssignedMinute < 0 || a.AssignedMinute > minute) throw new ArgumentException("Invalid occupation.");
        if (!Offers.SequenceEqual(Offers.OrderBy(o => o.HouseholdId).ThenBy(o => o.Resource)) || Offers.Select(o => (o.HouseholdId, o.Resource)).Distinct().Count() != Offers.Count) throw new ArgumentException("Offers must be unique and ordered.");
        foreach (var o in Offers) if (!owners.ContainsKey(o.HouseholdId) || !EconomyRules.Resources.Contains(o.Resource) || o.Surplus < 0 || o.Requested < 0 || o.Surplus > 0 && o.Requested > 0 || o.UpdatedMinute < 0 || o.UpdatedMinute > minute) throw new ArgumentException("Invalid household offer.");
        var reserved = new Dictionary<(long, ResourceType), long>();
        var carriers = new HashSet<long>();
        foreach (var t in Trades)
        {
            if (t.HouseholdA >= t.HouseholdB || t.DeliveredA && !t.PickedA || t.DeliveredB && !t.PickedB ||
                t.PickedA && t.CarrierA is null || t.PickedB && t.CarrierB is null ||
                t.CarrierA is { } carrierA && !citizens.Any(c => c.Id.Value == carrierA) ||
                t.CarrierB is { } carrierB && !citizens.Any(c => c.Id.Value == carrierB) ||
                t.CarrierA is not null && t.CarrierA == t.CarrierB) throw new ArgumentException("Trade history must retain valid, distinct physical carriers and delivery facts.");
            if (!Enum.IsDefined(t.Status) || t.HouseholdA == t.HouseholdB || t.QuantityA <= 0 || t.QuantityB <= 0 || t.QuantityA > 60 || t.QuantityB > 60 || t.ResourceA == t.ResourceB ||
                (long)t.QuantityA * EconomyRules.Weight(t.ResourceA) != (long)t.QuantityB * EconomyRules.Weight(t.ResourceB) || t.CreatedMinute < 0 || t.CreatedMinute > minute || t.ExpiresMinute != t.CreatedMinute + 2 * WorldCalendar.MinutesPerDay ||
                !structures.Any(s => s.Id.Value == t.MarketId && s.Type == StructureType.Marketplace && s.Status == StructureStatus.Complete) || !households.Any(h => h.Id.Value == t.HouseholdA) || !households.Any(h => h.Id.Value == t.HouseholdB)) throw new ArgumentException("Invalid barter transaction.");
            if (t.Status == BarterStatus.Reserved)
            {
                if (t.ClosedMinute is not null || !owners.ContainsKey(t.HouseholdA) || !owners.ContainsKey(t.HouseholdB) || t.DeliveredA && t.DeliveredB) throw new ArgumentException("Invalid open escrow.");
                CheckSide(t.HouseholdA, t.ResourceA, t.QuantityA, t.CarrierA, t.PickedA, t.DeliveredA, t);
                CheckSide(t.HouseholdB, t.ResourceB, t.QuantityB, t.CarrierB, t.PickedB, t.DeliveredB, t);
            }
            else if (t.ClosedMinute is null || t.ClosedMinute < t.CreatedMinute || t.ClosedMinute > minute || t.Status == BarterStatus.Completed && (!t.DeliveredA || !t.DeliveredB)) throw new ArgumentException("Invalid closed trade.");
        }
        foreach (var ((owner, resource), amount) in reserved) if (owners[owner].Holdings.Get(resource) < amount) throw new ArgumentException("Offers reserve unavailable goods.");
        foreach (var citizen in citizens.Where(c => c.CurrentAction == CitizenAction.TradeDelivery)) if (!carriers.Contains(citizen.Id.Value)) throw new ArgumentException("Trade carrier has no pending reservation.");
        foreach (var p in PublicWork)
            if (p.Food is <= 0 or > EconomyRules.PublicWorkFood || !owners.ContainsKey(p.HouseholdId) || !citizens.Any(c => c.Id.Value == p.CitizenId && c.IsAlive && c.HouseholdId?.Value == p.HouseholdId && c.CurrentAction is CitizenAction.Build or CitizenAction.HaulConstruction)) throw new ArgumentException("Public compensation must be reserved against actual work.");
        Ordered(PublicSupplyTrades.Select(t => t.Id));
        foreach (var t in PublicSupplyTrades) if (t.Id > PublicSupplyTrades.Count || t.WorldMinute < 0 || t.WorldMinute > minute || t.Resource is not (ResourceType.Wood or ResourceType.Stone) || t.Quantity <= 0 || t.FoodPaid != t.Quantity * EconomyRules.Weight(t.Resource) || !citizens.Any(c => c.Id.Value == t.CitizenId) || !households.Any(h => h.Id.Value == t.HouseholdId)) throw new ArgumentException("Invalid public supply transaction.");
        foreach (var g in Recoverable) if (g.Quantity <= 0 || g.HouseholdId is { } owner && !owners.ContainsKey(owner) || !EconomyRules.Resources.Contains(g.Resource) || !world.GetTile(g.Location).Walkable) throw new ArgumentException("Invalid recoverable cargo.");
        foreach (var e in Events) { e.Goods.Validate(); if (e.WorldMinute < 0 || e.WorldMinute > minute || e.Kind is not ("Inheritance" or "HouseholdMerged" or "FirstTrade" or "EmergencyFoodAid") || !households.Any(h => h.Id.Value == e.HouseholdId) || e.RecipientHouseholdId is { } recipient && !households.Any(h => h.Id.Value == recipient) || e.Kind == "EmergencyFoodAid" && (e.HouseholdId == e.RecipientHouseholdId || e.RecipientHouseholdId is null || e.Goods.Food <= 0 || e.Goods.Wood != 0 || e.Goods.Stone != 0)) throw new ArgumentException("Invalid economic event."); }
        if (NextTradeId <= (Trades.Count == 0 ? 0 : Trades[^1].Id) || NextCacheId <= (Recoverable.Count == 0 ? 0 : Recoverable[^1].Id) || NextEventId <= (Events.Count == 0 ? 0 : Events[^1].Id)) throw new ArgumentException("Economic counters must exceed recorded identities.");
        var stored = StoredGoods(commons); stored.Validate();
        var transit = Trades.Where(t => t.Status == BarterStatus.Reserved).Aggregate(new Goods(), (sum, t) => sum.Add(t.ResourceA, t.PickedA && !t.DeliveredA ? t.QuantityA : 0).Add(t.ResourceB, t.PickedB && !t.DeliveredB ? t.QuantityB : 0));
        if (stored.Total + transit.Total > capacity || stored.Wood + stored.Stone + transit.Wood + transit.Stone > capacity - dedicatedFoodCapacity) throw new ArgumentException("Owned storage exceeds shared or dedicated capacity.");
        var accounted = citizens.Aggregate(stored, (sum, c) => c.CarriedResourceType is { } resource ? sum.Add(resource, c.CarriedResourceQuantity) : sum);
        accounted = Recoverable.Aggregate(accounted, (sum, g) => sum.Add(g.Resource, g.Quantity));
        accounted = accounted.Plus(new Goods(FoodConsumed, structures.Sum(s => (long)s.DeliveredWood), structures.Sum(s => (long)s.DeliveredStone)));
        if (additionalAccounted is not null) { additionalAccounted.Validate(); accounted = accounted.Plus(additionalAccounted); }
        if (accounted != Produced.Plus(new Goods(400))) throw new ArgumentException($"Goods conservation failed: accounted {accounted}; produced plus founding supplies {Produced.Plus(new Goods(400))}.");

        void CheckSide(long owner, ResourceType resource, int quantity, long? carrierId, bool picked, bool delivered, BarterTrade trade)
        {
            if (delivered && !picked || picked && carrierId is null) throw new ArgumentException("Escrow cannot precede physical delivery.");
            if (!picked) reserved[(owner, resource)] = reserved.GetValueOrDefault((owner, resource)) + quantity;
            if (carrierId is not { } id || delivered) return;
            var c = citizens.SingleOrDefault(c => c.Id.Value == id);
            if (!carriers.Add(id) || c is null || !c.IsAlive || c.HouseholdId?.Value != owner || c.CurrentAction != CitizenAction.TradeDelivery || c.TargetStructureId?.Value != trade.MarketId ||
                c.CarriedResourceQuantity != (picked ? quantity : 0) || picked && c.CarriedResourceType != resource || c.ActionPhase != (picked ? CitizenActionPhase.TransportToConstruction : CitizenActionPhase.TravelToStockpile) ||
                c.ActionTarget != (picked ? structures.Single(s => s.Id.Value == trade.MarketId).Location : world.StartingSite)) throw new ArgumentException("Trade cargo and its carrier disagree.");
        }
        static void Ordered(IEnumerable<long> values) { long prior = 0; foreach (var id in values) { if (id <= prior) throw new ArgumentException("Economic rows must have unique ascending positive identities."); prior = id; } }
    }
}

