using System.Security.Cryptography;
using System.Text;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private readonly SortedDictionary<long, long> _productionCargoOwners = [];
    private readonly SortedDictionary<long, HouseholdStock> _householdStocks = [];
    private readonly SortedDictionary<long, EconomicMember> _economicMembers = [];
    private readonly SortedDictionary<long, WorkAssignment> _workAssignments = [];
    private readonly SortedDictionary<long, BarterTrade> _barterTrades = [];
    private readonly SortedSet<long> _pendingTrades = [];
    private readonly SortedDictionary<long, PublicWorkReservation> _publicWork = [];
    private readonly SortedDictionary<long, RecoverableGoods> _recoverableGoods = [];
    private readonly List<EconomicEvent> _economicEvents = [];
    private readonly List<PublicSupplyTrade> _publicSupplyTrades = [];
    private readonly List<BarterOffer> _barterOffers = [];
    private Goods _producedGoods = new();
    private long _foodConsumed, _emergencyFoodConsumed, _publicWorkPaid;
    private long _nextTradeId = 1, _nextCacheId = 1, _nextEconomicEventId = 1;
    private bool _economyInitialized;
    public static bool EconomySystemsEnabled(string rules) => rules is BarterSimulationRulesVersion or SpacedSimulationRulesVersion || UnifiedSimulationRulesEnabled(rules);
    private bool EconomyEnabled => EconomySystemsEnabled(SimulationRulesVersion);
    private IEnumerable<BarterTrade> OpenTrades => _pendingTrades.Select(id => _barterTrades[id]);

    public EconomyState? CaptureEconomy() => EconomyEnabled ? new(1, EconomyRules.CommunalPercent, _nextTradeId, _nextCacheId, _nextEconomicEventId,
        _producedGoods, _foodConsumed, _emergencyFoodConsumed, _publicWorkPaid,
        Array.AsReadOnly(_householdStocks.Values.ToArray()), Array.AsReadOnly(_economicMembers.Values.ToArray()), Array.AsReadOnly(_workAssignments.Values.ToArray()),
        Array.AsReadOnly(_barterOffers.ToArray()), Array.AsReadOnly(_barterTrades.Values.ToArray()), Array.AsReadOnly(_publicWork.Values.ToArray()),
        Array.AsReadOnly(_recoverableGoods.Values.ToArray()), Array.AsReadOnly(_economicEvents.ToArray()), Array.AsReadOnly(_publicSupplyTrades.ToArray()), Array.AsReadOnly(_productionCargoOwners.Select(p => new ProductionCargoOwner(p.Key, p.Value)).ToArray())) : null;
    public string ComputeEconomyFingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CaptureEconomy()?.ToCanonicalJson() ?? "null"))).ToLowerInvariant();
    private void InitializeEconomy()
    {
        if (!EconomyEnabled) return;
        _economyInitialized = true;
        ReconcileEconomicHouseholds();
        UpdateOccupations();
    }
    private void LoadEconomy(EconomyState? state)
    {
        if (state is null) return;
        _economyInitialized = true;
        _producedGoods = state.Produced; _foodConsumed = state.FoodConsumed; _emergencyFoodConsumed = state.EmergencyFoodConsumed; _publicWorkPaid = state.PublicWorkPaid;
        _nextTradeId = state.NextTradeId; _nextCacheId = state.NextCacheId; _nextEconomicEventId = state.NextEventId;
        foreach (var h in state.Households) _householdStocks.Add(h.HouseholdId, h);
        foreach (var m in state.Members) _economicMembers.Add(m.CitizenId, m);
        foreach (var a in state.Assignments) _workAssignments.Add(a.CitizenId, a);
        foreach (var t in state.Trades) { _barterTrades.Add(t.Id, t); if (t.Status == BarterStatus.Reserved) _pendingTrades.Add(t.Id); }
        foreach (var p in state.PublicWork) _publicWork.Add(p.CitizenId, p);
        foreach (var g in state.Recoverable) _recoverableGoods.Add(g.Id, g);
        foreach (var cargo in state.ProductionCargo) _productionCargoOwners.Add(cargo.CitizenId, cargo.HouseholdId);
        _publicSupplyTrades.AddRange(state.PublicSupplyTrades);
        _barterOffers.AddRange(state.Offers); _economicEvents.AddRange(state.Events);
    }

    private Goods OwnedStoredGoods => _householdStocks.Values.Aggregate(new Goods(), (sum, h) => sum.Plus(h.Holdings))
        .Plus(OpenTrades.Aggregate(new Goods(), (sum, t) => sum.Add(t.ResourceA, t.DeliveredA ? t.QuantityA : 0).Add(t.ResourceB, t.DeliveredB ? t.QuantityB : 0)))
        .Add(ResourceType.Food, _publicWork.Values.Sum(p => (long)p.Food));
    private Goods OwnedStoredGoodsAt(long settlementId)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion)) return OwnedStoredGoods;
        return _householdStocks.Values.Where(h => SiteIdForHousehold(h.HouseholdId) == settlementId)
            .Aggregate(new Goods(), (sum, h) => sum.Plus(h.Holdings))
            .Plus(OpenTrades.Where(t => SiteIdForHousehold(t.HouseholdA) == settlementId)
                .Aggregate(new Goods(), (sum, t) => sum.Add(t.ResourceA, t.DeliveredA ? t.QuantityA : 0)
                    .Add(t.ResourceB, t.DeliveredB ? t.QuantityB : 0)))
            .Add(ResourceType.Food, _publicWork.Values.Where(p => SiteIdForCitizen(p.CitizenId) == settlementId).Sum(p => (long)p.Food));
    }
    public long TotalStoredFood => checked(Settlement.FoodStored + (EconomyEnabled ? OwnedStoredGoods.Food : 0));
    private long TradeCargoReserved => OpenTrades.Sum(t => (t.PickedA && !t.DeliveredA ? (long)t.QuantityA : 0) + (t.PickedB && !t.DeliveredB ? t.QuantityB : 0));
    private long TradeNonFoodReserved => OpenTrades.Sum(t => (t.ResourceA != ResourceType.Food && t.PickedA && !t.DeliveredA ? (long)t.QuantityA : 0) + (t.ResourceB != ResourceType.Food && t.PickedB && !t.DeliveredB ? t.QuantityB : 0));
    private long TradeCargoReservedAt(long settlementId) => !MigrationSystemsEnabled(SimulationRulesVersion) ? TradeCargoReserved :
        OpenTrades.Where(t => SiteIdForHousehold(t.HouseholdA) == settlementId)
            .Sum(t => (t.PickedA && !t.DeliveredA ? (long)t.QuantityA : 0) + (t.PickedB && !t.DeliveredB ? t.QuantityB : 0));
    private long TradeNonFoodReservedAt(long settlementId) => !MigrationSystemsEnabled(SimulationRulesVersion) ? TradeNonFoodReserved :
        OpenTrades.Where(t => SiteIdForHousehold(t.HouseholdA) == settlementId)
            .Sum(t => (t.ResourceA != ResourceType.Food && t.PickedA && !t.DeliveredA ? (long)t.QuantityA : 0) +
                (t.ResourceB != ResourceType.Food && t.PickedB && !t.DeliveredB ? t.QuantityB : 0));
    private long ReservedGoods(long household, ResourceType resource) => OpenTrades.Sum(t =>
        t.HouseholdA == household && t.ResourceA == resource && !t.PickedA ? (long)t.QuantityA :
        t.HouseholdB == household && t.ResourceB == resource && !t.PickedB ? t.QuantityB : 0);
    private long AvailablePrivate(long household, ResourceType resource) => _householdStocks.TryGetValue(household, out var stock) ? Math.Max(0, stock.Holdings.Get(resource) - ReservedGoods(household, resource)) : 0;
    private int FoodAvailableTo(Citizen citizen)
    {
        var siteId = SiteIdForCitizen(citizen);
        return checked(SettlementFor(siteId).FoodStored + (int)(citizen.HouseholdId is { } h ? AvailablePrivate(h.Value, ResourceType.Food) : 0));
    }
    private void AddPrivate(long owner, ResourceType resource, long amount)
    {
        var stock = _householdStocks[owner];
        var goods = stock.Holdings.Add(resource, amount); goods.Validate();
        _householdStocks[owner] = stock with { Holdings = goods };
    }
    private void AddCommons(ResourceType resource, int amount) => AddCommons(1, resource, amount);
    private void AddCommons(long settlementId, ResourceType resource, int amount)
    {
        var settlement = SettlementFor(settlementId);
        if (resource == ResourceType.Food) settlement.FoodStored = checked(settlement.FoodStored + amount);
        else if (resource == ResourceType.Wood) settlement.WoodStored = checked(settlement.WoodStored + amount);
        else settlement.StoneStored = checked(settlement.StoneStored + amount);
    }
    private void RecordProduction(Citizen citizen, ResourceType resource, int amount)
    {
        if (!EconomyEnabled || amount == 0) return;
        _productionCargoOwners[citizen.Id.Value] = citizen.HouseholdId!.Value.Value;
        _producedGoods = _producedGoods.Add(resource, amount);
        if (resource == ResourceType.Food && _historyState is not null) _historyState.FoodProducedSinceSample = checked(_historyState.FoodProducedSinceSample + amount);
    }
    private void RecordCommunalFoodProduction(int amount)
    {
        if (!EconomyEnabled || amount == 0) return;
        _producedGoods = _producedGoods.Add(ResourceType.Food, amount);
        if (_historyState is not null) _historyState.FoodProducedSinceSample = checked(_historyState.FoodProducedSinceSample + amount);
    }
    private void DepositOwnedProduction(Citizen citizen, ResourceType resource, int amount)
    {
        var owner = _productionCargoOwners.GetValueOrDefault(citizen.Id.Value, citizen.HouseholdId!.Value.Value);
        var stock = _householdStocks[owner];
        var numerator = checked(amount * EconomyRules.CommunalPercent + stock.ContributionRemainders.Get(resource));
        var communal = checked((int)(numerator / 100));
        _householdStocks[owner] = stock with { ContributionRemainders = stock.ContributionRemainders.Add(resource, numerator % 100 - stock.ContributionRemainders.Get(resource)) };
        AddCommons(SiteIdForHousehold(owner), resource, communal); AddPrivate(owner, resource, amount - communal);
    }
    private int ConsumeEconomicMeal(Citizen citizen, int portion)
    {
        var owner = citizen.HouseholdId!.Value.Value;
        var siteId = SiteIdForCitizen(citizen);
        var settlement = SettlementFor(siteId);
        if (MigrationSystemsEnabled(SimulationRulesVersion) &&
            FoundingPartyForCitizen(citizen.Id.Value) is { JourneyKind: MigrationJourneyKind.Founding or MigrationJourneyKind.Relocation })
        {
            var provisions = ConsumeFoundingProvisions(citizen, portion);
            _foodConsumed = checked(_foodConsumed + provisions);
            if (_historyState is not null)
                _historyState.FoodConsumedSinceSample = checked(_historyState.FoodConsumedSinceSample + provisions);
            return provisions;
        }
        var transit = MigrationSystemsEnabled(SimulationRulesVersion) ? ConsumeFoundingProvisions(citizen, portion) : 0;
        var remaining = portion - transit;
        var own = checked((int)Math.Min(remaining, AvailablePrivate(owner, ResourceType.Food)));
        AddPrivate(owner, ResourceType.Food, -own);
        var support = Math.Min(remaining - own, settlement.FoodStored);
        settlement.FoodStored -= support;
        _emergencyFoodConsumed = checked(_emergencyFoodConsumed + support);
        var consumed = transit + own + support;
        if (UnifiedSimulationRulesEnabled(SimulationRulesVersion) && citizen.Location == SiteLocation(siteId) && citizen.Needs.Hunger >= 8500 && consumed < portion)
        {
            var aid = TransferEmergencyFood(owner, portion - consumed);
            if (aid > 0)
            {
                // Aid moves through the recipient's ordinary household account before
                // consumption, so stored-goods conservation remains exact.
                AddPrivate(owner, ResourceType.Food, -aid);
                consumed = checked(consumed + aid);
            }
        }
        _foodConsumed = checked(_foodConsumed + consumed);
        if (_historyState is not null) _historyState.FoodConsumedSinceSample = checked(_historyState.FoodConsumedSinceSample + consumed);
        return consumed;
    }

    private int TransferEmergencyFood(long recipient, int requested)
    {
        if (requested <= 0) return 0;

        var donor = SelectEmergencyFoodDonor(recipient);
        if (donor is not { } source) return 0;

        var quantity = checked((int)Math.Min(requested, source.Surplus));
        AddPrivate(source.HouseholdId, ResourceType.Food, -quantity);
        AddPrivate(recipient, ResourceType.Food, quantity);
        _economicEvents.Add(new(_nextEconomicEventId++, CurrentMinute.Value, "EmergencyFoodAid", source.HouseholdId, recipient, new Goods(Food: quantity)));
        return quantity;
    }

    private bool HasEmergencyFoodDonor(long recipient, int hunger) =>
        UnifiedSimulationRulesEnabled(SimulationRulesVersion) && hunger >= 8500 && SelectEmergencyFoodDonor(recipient) is not null;

    private (long HouseholdId, long Surplus)? SelectEmergencyFoodDonor(long recipient)
    {
        var siteId = SiteIdForHousehold(recipient);
        var donor = _householdStocks.Values
            .Where(stock => stock.HouseholdId != recipient && SiteIdForHousehold(stock.HouseholdId) == siteId)
            .Select(stock =>
            {
                var members = _citizens.Values.Count(c => c.IsAlive && c.HouseholdId?.Value == stock.HouseholdId);
                var surplus = AvailablePrivate(stock.HouseholdId, ResourceType.Food) - members * 60L;
                return (stock.HouseholdId, Members: members, Surplus: surplus);
            })
            .Where(candidate => candidate.Members > 0 && candidate.Surplus > 0)
            .OrderByDescending(candidate => candidate.Surplus)
            .ThenBy(candidate => candidate.HouseholdId)
            .FirstOrDefault();
        return donor.Members > 0 && donor.Surplus > 0 ? (donor.HouseholdId, donor.Surplus) : null;
    }

    private void ReconcileEconomicHouseholds()
    {
        if (!_economyInitialized) return;
        foreach (var p in _publicWork.Values.Where(p => _citizens[p.CitizenId].HouseholdId?.Value != p.HouseholdId).ToArray()) ReleasePublicWork(_citizens[p.CitizenId]);
        foreach (var h in _households.Values.Where(h => h.DissolvedMinute is null))
            _householdStocks.TryAdd(h.Id.Value, new(h.Id.Value, new(), new()));
        foreach (var owner in _householdStocks.Keys.Where(id => _households[id].DissolvedMinute is not null).ToArray())
        {
            foreach (var t in OpenTrades.Where(t => t.HouseholdA == owner || t.HouseholdB == owner).ToArray()) CancelTrade(t.Id);
            var formerMembers = _economicMembers.Values.Where(m => m.HouseholdId == owner).Select(m => m.CitizenId).Concat(_citizens.Values.Where(c => c.HouseholdId?.Value == owner).Select(c => c.Id.Value)).ToHashSet();
            var ownerSite = SiteIdForHousehold(owner);
            var livingDestinations = formerMembers.Select(id => _citizens[id]).Where(c => c.IsAlive && c.HouseholdId is not null)
                .Select(c => c.HouseholdId!.Value.Value).Where(id => SiteIdForHousehold(id) == ownerSite).Distinct().Order().ToArray();
            var inherited = livingDestinations.Length == 0;
            var descendants = new HashSet<long>(formerMembers);
            if (inherited)
            {
                bool changed;
                do { changed = false; foreach (var c in _citizens.Values) if (!descendants.Contains(c.Id.Value) && (c.ParentAId is { } a && descendants.Contains(a.Value) || c.ParentBId is { } b && descendants.Contains(b.Value))) changed |= descendants.Add(c.Id.Value); } while (changed);
                livingDestinations = descendants.Select(id => _citizens[id]).Where(c => c.IsAlive && c.HouseholdId is not null && c.HouseholdId.Value.Value != owner)
                    .Select(c => c.HouseholdId!.Value.Value).Where(id => SiteIdForHousehold(id) == ownerSite).Distinct().Order().ToArray();
            }
            foreach (var cargo in _productionCargoOwners.Where(p => p.Value == owner).ToArray())
            {
                if (livingDestinations.Length == 1) { _productionCargoOwners[cargo.Key] = livingDestinations[0]; continue; }
                var carrier = _citizens[cargo.Key];
                RecoverInterruptedCargo(carrier);
                foreach (var item in _scheduledEvents.Where(e => e.Name != CitizenEventNames.SurvivalCheck && IsReservedCitizenEventFor(e, carrier.Id.Value)).ToArray()) _scheduledEvents.Remove(item);
                FinishAction(carrier);
                if (carrier.IsAlive) ScheduleCitizen(carrier, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
            }
            var goods = _householdStocks[owner].Holdings;
            foreach (var resource in EconomyRules.Resources)
            {
                var quantity = goods.Get(resource);
                if (livingDestinations.Length == 0) AddCommons(ownerSite, resource, checked((int)quantity));
                else for (var i = 0; i < livingDestinations.Length; i++) AddPrivate(livingDestinations[i], resource, quantity / livingDestinations.Length + (i < quantity % livingDestinations.Length ? 1 : 0));
            }
            foreach (var cache in _recoverableGoods.Values.Where(g => g.HouseholdId == owner).ToArray())
            {
                _recoverableGoods.Remove(cache.Id);
                if (livingDestinations.Length == 0) AddRecoverable(null, cache.Location, cache.Resource, cache.Quantity);
                else for (var i = 0; i < livingDestinations.Length; i++) AddRecoverable(livingDestinations[i], cache.Location, cache.Resource, cache.Quantity / livingDestinations.Length + (i < cache.Quantity % livingDestinations.Length ? 1 : 0));
            }
            if (goods.Total > 0) _economicEvents.Add(new(_nextEconomicEventId++, CurrentMinute.Value, inherited ? "Inheritance" : "HouseholdMerged", owner, livingDestinations.Length == 1 ? livingDestinations[0] : null, goods));
            _householdStocks.Remove(owner);
        }
        // Household changes cancel reservations before the new membership is published.
        foreach (var t in OpenTrades.ToArray())
            if (t.CarrierA is { } a && _citizens[a].HouseholdId?.Value != t.HouseholdA || t.CarrierB is { } b && _citizens[b].HouseholdId?.Value != t.HouseholdB) CancelTrade(t.Id);
        _economicMembers.Clear();
        foreach (var c in _citizens.Values.Where(c => c.IsAlive)) _economicMembers.Add(c.Id.Value, new(c.Id.Value, c.HouseholdId!.Value.Value));
        _barterOffers.RemoveAll(o => !_householdStocks.ContainsKey(o.HouseholdId));
        foreach (var id in _workAssignments.Keys.Where(id => !_citizens[id].IsAlive).ToArray()) _workAssignments.Remove(id);
    }

    private void UpdateOccupations()
    {
        foreach (var siteId in MigrationSettlementIds)
        {
            var workers = CitizensAt(siteId).Where(c => c.IsAlive && c.AgeYears(CurrentMinute) >= 13).OrderBy(c => c.Id.Value).ToArray();
            var count = workers.Length;
            var quotas = new[] { (WorkSpecialization.Builder, Math.Max(1, count / 10)), (WorkSpecialization.Hauler, Math.Max(1, count / 10)),
                (WorkSpecialization.Farmer, Math.Min(count / 4, FarmsAt(siteId).Count() * 2)), (WorkSpecialization.Woodcutter, Math.Max(1, count / 8)), (WorkSpecialization.Stoneworker, Math.Max(1, count / 10)), (WorkSpecialization.Forager, count) };
            var assigned = new HashSet<long>();
            foreach (var (role, target) in quotas)
            {
                var chosen = workers.Where(c => !assigned.Contains(c.Id.Value)).OrderByDescending(c => _workAssignments.TryGetValue(c.Id.Value, out var prior) && prior.Specialization == role ? 20000 : 0)
                    .ThenByDescending(c => role switch { WorkSpecialization.Builder => c.Skills.Construction, WorkSpecialization.Hauler => c.Skills.Hauling, WorkSpecialization.Woodcutter => c.Skills.Woodcutting, WorkSpecialization.Stoneworker => c.Skills.Stoneworking, _ => c.Skills.Foraging }).ThenBy(c => c.Id.Value).Take(target);
                foreach (var c in chosen) { assigned.Add(c.Id.Value); if (!_workAssignments.TryGetValue(c.Id.Value, out var prior) || prior.Specialization != role) _workAssignments[c.Id.Value] = new(c.Id.Value, role, CurrentMinute.Value); }
            }
        }
    }
    private int OccupationBonus(Citizen citizen, CitizenAction action)
    {
        if (!EconomyEnabled || !_workAssignments.TryGetValue(citizen.Id.Value, out var a)) return 0;
        var preferred = a.Specialization switch { WorkSpecialization.Farmer => action is CitizenAction.WorkFarm or CitizenAction.HaulHarvest, WorkSpecialization.Forager => action == CitizenAction.GatherFood, WorkSpecialization.Woodcutter => action == CitizenAction.GatherWood, WorkSpecialization.Stoneworker => action == CitizenAction.GatherStone, WorkSpecialization.Builder => action == CitizenAction.Build, WorkSpecialization.Hauler => action is CitizenAction.HaulConstruction or CitizenAction.TradeDelivery, _ => false };
        return preferred ? 3500 : 0;
    }
    private int EconomicGatherScore(Citizen citizen, ResourceType resource, int commonScore)
    {
        if (!EconomyEnabled) return commonScore;
        var siteId = SiteIdForCitizen(citizen);
        var settlement = SettlementFor(siteId);
        var members = CitizensAt(siteId).Count(c => c.IsAlive && c.HouseholdId == citizen.HouseholdId);
        var own = AvailablePrivate(citizen.HouseholdId!.Value.Value, resource);
        var target = resource == ResourceType.Food ? members * 120 : resource == ResourceType.Wood ? 160 : 100;
        var action = resource == ResourceType.Food ? CitizenAction.GatherFood : resource == ResourceType.Wood ? CitizenAction.GatherWood : CitizenAction.GatherStone;
        var personal = own < target && OccupationBonus(citizen, action) > 0 ? 4500 : 0;
        if (resource == ResourceType.Food && own < members * 20 && settlement.FoodStored < PopulationAt(siteId) * 10) personal = 8500;
        return Math.Max(commonScore, personal);
    }

    private bool CanProcure(ResourceType resource, bool reserveWage = true) => CanProcure(1, resource, reserveWage);
    private bool CanProcure(long settlementId, ResourceType resource, bool reserveWage = true) => EconomyEnabled &&
        SettlementFor(settlementId).FoodStored > PopulationAt(settlementId) * 10 + EconomyRules.Weight(resource) + (reserveWage ? EconomyRules.PublicWorkFood : 0) &&
        _householdStocks.Keys.Any(id => SiteIdForHousehold(id) == settlementId && AvailablePrivate(id, resource) > (resource == ResourceType.Wood ? 20 : 10));
    private void ProcurePublicMaterial(Citizen citizen, ResourceType resource, int missing)
    {
        var siteId = SiteIdForCitizen(citizen);
        var settlement = SettlementFor(siteId);
        if (!CanProcure(siteId, resource, reserveWage: false) || missing <= 0) return;
        var owner = _householdStocks.Keys.First(id => SiteIdForHousehold(id) == siteId && AvailablePrivate(id, resource) > (resource == ResourceType.Wood ? 20 : 10));
        var quantity = checked((int)Math.Min(Math.Min(40, missing), Math.Min(AvailablePrivate(owner, resource) - (resource == ResourceType.Wood ? 20 : 10), (settlement.FoodStored - PopulationAt(siteId) * 10) / EconomyRules.Weight(resource))));
        if (quantity <= 0) return;
        var food = quantity * EconomyRules.Weight(resource);
        AddPrivate(owner, resource, -quantity); AddPrivate(owner, ResourceType.Food, food);
        settlement.FoodStored -= food; AddCommons(siteId, resource, quantity);
        _publicSupplyTrades.Add(new(_publicSupplyTrades.Count + 1L, CurrentMinute.Value, citizen.Id.Value, owner, resource, quantity, food));
    }

    private void ReservePublicWork(Citizen citizen)
    {
        var settlement = SettlementFor(SiteIdForCitizen(citizen));
        if (!EconomyEnabled || _publicWork.ContainsKey(citizen.Id.Value) || settlement.FoodStored <= PopulationAt(SiteIdForCitizen(citizen)) * 10) return;
        var amount = Math.Min(EconomyRules.PublicWorkFood, settlement.FoodStored - PopulationAt(SiteIdForCitizen(citizen)) * 10);
        settlement.FoodStored -= amount;
        _publicWork.Add(citizen.Id.Value, new(citizen.Id.Value, citizen.HouseholdId!.Value.Value, amount));
    }
    private void CompletePublicWork(Citizen citizen)
    {
        if (!_publicWork.Remove(citizen.Id.Value, out var reservation)) return;
        AddPrivate(reservation.HouseholdId, ResourceType.Food, reservation.Food);
        _publicWorkPaid = checked(_publicWorkPaid + reservation.Food);
    }
    private void ReleasePublicWork(Citizen citizen)
    {
        if (_publicWork.Remove(citizen.Id.Value, out var reservation))
        {
            var settlement = SettlementFor(SiteIdForCitizen(citizen));
            settlement.FoodStored = checked(settlement.FoodStored + reservation.Food);
        }
    }
    private void AddRecoverable(long? owner, TileCoordinate location, ResourceType resource, int quantity)
    {
        if (quantity <= 0) return;
        var cache = new RecoverableGoods(_nextCacheId++, owner, location, resource, quantity);
        _recoverableGoods.Add(cache.Id, cache);
    }
    private void RecoverInterruptedCargo(Citizen citizen)
    {
        if (!EconomyEnabled) return;
        foreach (var trade in OpenTrades.Where(t => t.CarrierA == citizen.Id.Value && !t.DeliveredA || t.CarrierB == citizen.Id.Value && !t.DeliveredB).ToArray()) CancelTrade(trade.Id, citizen.Id.Value);
        ReleasePublicWork(citizen);
        if (citizen.CarriedResourceType is { } resource && citizen.CarriedResourceQuantity > 0)
        {
            // Interrupted goods remain physically at the interruption tile. They are
            // canonical property, not erased or silently teleported into storage.
            AddRecoverable(citizen.CurrentAction == CitizenAction.HaulConstruction ? null : _productionCargoOwners.GetValueOrDefault(citizen.Id.Value, citizen.HouseholdId!.Value.Value), citizen.Location, resource, citizen.CarriedResourceQuantity);
            ClearCarriedState(citizen);
        }
    }

    private void UpdateEconomy()
    {
        if (!EconomyEnabled) return;
        ReconcileEconomicHouseholds(); UpdateOccupations();
        foreach (var trade in OpenTrades.Where(t => CurrentMinute.Value >= t.ExpiresMinute).ToArray()) CancelTrade(trade.Id);
        _barterOffers.Clear();
        foreach (var h in _householdStocks.Values)
        {
            var members = _citizens.Values.Count(c => c.IsAlive && c.HouseholdId?.Value == h.HouseholdId);
            foreach (var resource in EconomyRules.Resources)
            {
                var reserve = resource == ResourceType.Food ? members * 60 : resource == ResourceType.Wood ? 20 : 10;
                var available = AvailablePrivate(h.HouseholdId, resource);
                _barterOffers.Add(new(h.HouseholdId, resource, checked((int)Math.Max(0, available - reserve)), checked((int)Math.Max(0, reserve - available)), CurrentMinute.Value));
            }
        }
        foreach (var settlementId in MigrationSettlementIds)
        {
            var market = StructuresAt(settlementId).FirstOrDefault(s => s.Type == StructureType.Marketplace && s.Status == StructureStatus.Complete);
            if (market is null) continue;
            var siteOffers = _barterOffers.Where(o => SiteIdForHousehold(o.HouseholdId) == settlementId).ToArray();
            var busy = OpenTrades.Where(t => SiteIdForHousehold(t.HouseholdA) == settlementId)
                .SelectMany(t => new[] { t.HouseholdA, t.HouseholdB }).ToHashSet();
            foreach (var offer in siteOffers.Where(o => o.Surplus > 0))
            {
                if (busy.Contains(offer.HouseholdId)) continue;
                foreach (var other in siteOffers.Where(o => o.HouseholdId > offer.HouseholdId && o.Surplus > 0 && o.Resource != offer.Resource))
                {
                    if (busy.Contains(other.HouseholdId)) continue;
                    var wantA = siteOffers.Single(o => o.HouseholdId == offer.HouseholdId && o.Resource == other.Resource).Requested;
                    var wantB = siteOffers.Single(o => o.HouseholdId == other.HouseholdId && o.Resource == offer.Resource).Requested;
                    var unitA = EconomyRules.Weight(other.Resource); var unitB = EconomyRules.Weight(offer.Resource);
                    var multiples = Math.Min(Math.Min(Math.Min(offer.Surplus, wantB) / unitA, Math.Min(other.Surplus, wantA) / unitB), 20);
                    if (multiples <= 0) continue;
                    var id = _nextTradeId++;
                    var trade = new BarterTrade(id, CurrentMinute.Value, CurrentMinute.Value + 2 * WorldCalendar.MinutesPerDay, market.Id.Value, offer.HouseholdId, other.HouseholdId, offer.Resource, multiples * unitA, other.Resource, multiples * unitB, null, null, false, false, false, false, BarterStatus.Reserved, null);
                    _barterTrades.Add(id, trade); _pendingTrades.Add(id); busy.Add(offer.HouseholdId); busy.Add(other.HouseholdId); break;
                }
            }
        }
    }
    private BarterTrade? TradeFor(Citizen citizen) => !EconomyEnabled || citizen.AgeYears(CurrentMinute) < 13 ? null : OpenTrades.FirstOrDefault(t =>
        SiteIdForHousehold(t.HouseholdA) == SiteIdForCitizen(citizen) &&
        SiteIdForHousehold(t.HouseholdB) == SiteIdForCitizen(citizen) &&
        (t.HouseholdA == citizen.HouseholdId?.Value && t.CarrierA is null || t.HouseholdB == citizen.HouseholdId?.Value && t.CarrierB is null));
    private void BeginTrade(Citizen citizen, BarterTrade trade)
    {
        _barterTrades[trade.Id] = trade.HouseholdA == citizen.HouseholdId?.Value ? trade with { CarrierA = citizen.Id.Value } : trade with { CarrierB = citizen.Id.Value };
        var marketSite = SiteIdForStructure(trade.MarketId);
        BeginTravel(citizen, CitizenAction.TradeDelivery, SiteLocation(marketSite), null, CitizenActionPhase.TravelToStockpile, new StructureId(trade.MarketId));
    }
    private void ArriveTrade(Citizen citizen)
    {
        var trade = OpenTrades.FirstOrDefault(t => t.CarrierA == citizen.Id.Value && !t.DeliveredA || t.CarrierB == citizen.Id.Value && !t.DeliveredB);
        if (trade is null) { FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority); return; }
        var first = trade.CarrierA == citizen.Id.Value;
        var resource = first ? trade.ResourceA : trade.ResourceB; var quantity = first ? trade.QuantityA : trade.QuantityB;
        if (citizen.ActionPhase == CitizenActionPhase.TravelToStockpile)
        {
            AddPrivate(first ? trade.HouseholdA : trade.HouseholdB, resource, -quantity);
            _barterTrades[trade.Id] = first ? trade with { PickedA = true } : trade with { PickedB = true };
            citizen.CarriedResourceType = resource; citizen.CarriedResourceQuantity = quantity;
            BeginTravel(citizen, CitizenAction.TradeDelivery, _structures[trade.MarketId].Location, null, CitizenActionPhase.TransportToConstruction, new StructureId(trade.MarketId));
            return;
        }
        ClearCarriedState(citizen);
        trade = first ? trade with { DeliveredA = true } : trade with { DeliveredB = true };
        _barterTrades[trade.Id] = trade;
        if (trade.DeliveredA && trade.DeliveredB)
        {
            AddPrivate(trade.HouseholdB, trade.ResourceA, trade.QuantityA); AddPrivate(trade.HouseholdA, trade.ResourceB, trade.QuantityB);
            _barterTrades[trade.Id] = trade with { Status = BarterStatus.Completed, ClosedMinute = CurrentMinute.Value }; _pendingTrades.Remove(trade.Id);
            if (!_economicEvents.Any(e => e.Kind == "FirstTrade")) _economicEvents.Add(new(_nextEconomicEventId++, CurrentMinute.Value, "FirstTrade", trade.HouseholdA, trade.HouseholdB, new Goods().Add(trade.ResourceA, trade.QuantityA).Add(trade.ResourceB, trade.QuantityB)));
        }
        FinishAction(citizen); ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }
    private void CancelTrade(long id, long? interruptedCarrier = null)
    {
        var trade = _barterTrades[id]; if (trade.Status != BarterStatus.Reserved) return;
        _barterTrades[id] = trade with { Status = BarterStatus.Cancelled, ClosedMinute = CurrentMinute.Value }; _pendingTrades.Remove(id);
        ReturnSide(trade.HouseholdA, trade.ResourceA, trade.QuantityA, trade.CarrierA, trade.PickedA, trade.DeliveredA);
        ReturnSide(trade.HouseholdB, trade.ResourceB, trade.QuantityB, trade.CarrierB, trade.PickedB, trade.DeliveredB);
        void ReturnSide(long owner, ResourceType resource, int quantity, long? carrierId, bool picked, bool delivered)
        {
            if (delivered) { AddPrivate(owner, resource, quantity); return; }
            if (carrierId is not { } citizenId) return;
            var carrier = _citizens[citizenId];
            if (picked) { AddRecoverable(owner, carrier.Location, resource, quantity); ClearCarriedState(carrier); }
            foreach (var item in _scheduledEvents.Where(e => e.Name != CitizenEventNames.SurvivalCheck && IsReservedCitizenEventFor(e, citizenId)).ToArray()) _scheduledEvents.Remove(item);
            FinishAction(carrier);
            if (carrier.IsAlive && citizenId != interruptedCarrier) ScheduleCitizen(carrier, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
        }
    }
}

