using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    public const string LivingPulseEvent = "living.pulse.v1";
    public const int LivingPulseMinutes = 360;
    private LivingWorldState? _living;
    private bool LivingEnabled => LivingSystemsEnabled(SimulationRulesVersion);
    public string? LivingStateJson => _living is null ? null : LivingWorldCodec.Serialize(_living);
    public System.Text.Json.JsonElement? CreateLivingObservation() => _living is null ? null : LivingWorldCodec.Observe(_living, CurrentMinute.Value, SimulationRulesVersion);
    private int LivingStoredQuantity => _living is null ? 0 : checked(_living.Stock.Sum(x => x.Quantity) + _living.Orders.Sum(x => x.Cargo.Sum(y => y.Quantity) + (x.Reserved && !x.Produced ? x.Ingredients.Sum(y => y.Quantity) : 0)));
    private int LivingStoredQuantityAt(long settlementId) => checked(LivingGoodsFor(settlementId).Sum(x => x.Quantity) + OrdersAt(settlementId)
        .Sum(x => x.Cargo.Sum(y => y.Quantity) + (x.Reserved && !x.Produced ? x.Ingredients.Sum(y => y.Quantity) : 0)));
    private int LivingFreeStorage => checked((int)Math.Max(0L, (long)StorageCapacity - Settlement.StorageUsed - LivingStoredQuantity - (EconomyEnabled ? OwnedStoredGoods.Total + TradeCargoReserved : 0L)));
    private int LivingFreeStorageAt(long settlementId) => checked((int)Math.Max(0L, (long)StorageCapacityAt(settlementId) -
        SettlementFor(settlementId).StorageUsed - LivingStoredQuantityAt(settlementId) -
        (EconomyEnabled ? OwnedStoredGoodsAt(settlementId).Total + TradeCargoReservedAt(settlementId) : 0L)));
    private LivingPerson LivingPerson(Citizen citizen) => _living!.People.Single(x => x.CitizenId == citizen.Id.Value);
    private int Good(LivingGood good) => _living!.Stock.Single(x => x.Good == good).Quantity;
    private int Good(long settlementId, LivingGood good) => GoodAt(settlementId, good);
    private void ChangeGood(LivingGood good, int quantity)
    {
        ChangeGoodAt(1, good, quantity);
    }
    private void InitializeLiving()
    {
        _living = new LivingWorldState { Stock = Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToList() };
        SynchronizeLivingPeople();
        var tiles = World.Tiles.Where(x => x.Walkable && GetTravelCostsCached(World.StartingSite).ContainsKey(x.Coordinate))
            .OrderBy(x => Distance(x.Coordinate, World.StartingSite)).ThenBy(x => x.Coordinate).Take(160).ToArray();
        for (var i = 0; i < Math.Min(8, tiles.Length); i++)
            _living.Animals.Add(new LivingAnimal { Id = _living.NextId++, Predator = i == 7, Location = tiles[(i * 19) % tiles.Length].Coordinate });
        ScheduleLivingPulse();
    }
    private void ScheduleLivingPulse()
    {
        var sequence = _counters.AllocateScheduledEventSequence();
        var due = new WorldMinute(checked((CurrentMinute.Value / LivingPulseMinutes + 1) * LivingPulseMinutes));
        AddPending(new ScheduledEventId(sequence), new ScheduledEventOrder(due, 5, 0, sequence), LivingPulseEvent, "{\"version\":1}");
    }
    private void PulseLiving()
    {
        SynchronizeLivingPeople();
        _living!.UpdatedMinute = CurrentMinute.Value;
        if (CurrentMinute.Value / WorldCalendar.MinutesPerDay > _living.LastDailyMinute / WorldCalendar.MinutesPerDay)
        {
            AdvanceLivingEnvironment();
            AdvanceLivingPeople();
            _living.LastDailyMinute = CurrentMinute.Value;
        }
        ReconcileLivingOrders();
        PlanLivingEconomy();
        ScheduleLivingPulse();
    }
    private void SynchronizeLivingPeople()
    {
        foreach (var citizen in _citizens.Values.OrderBy(x => x.Id.Value))
        {
            var person = _living!.People.FirstOrDefault(x => x.CitizenId == citizen.Id.Value);
            if (person is null)
            {
                person = new LivingPerson { CitizenId = citizen.Id.Value, Goal = citizen.Traits.Curiosity > 7000 ? LivingGoal.Exploration : citizen.Traits.Industriousness > 6500 ? LivingGoal.Mastery : citizen.Traits.Cooperativeness > 5000 ? LivingGoal.FamilySecurity : LivingGoal.Comfort };
                _living.People.Add(person);
            }
            if (citizen.IsAlive || person.DeathObserved) continue;
            person.DeathObserved = true;
            ReleaseLivingClaim(citizen);
            foreach (var relative in _citizens.Values.Where(x => x.IsAlive && (x.PartnerId == citizen.Id || x.ParentAId == citizen.Id || x.ParentBId == citizen.Id)).OrderBy(x => x.Id.Value))
            {
                var survivor = _living.People.Single(x => x.CitizenId == relative.Id.Value);
                Experience(survivor, LivingExperienceKind.Bereavement, citizen.Id.Value);
                Fact(LivingFactKind.Bereavement, relative.Id.Value, citizen.Id.Value, citizen.Location);
            }
        }
    }
    private void Fact(LivingFactKind kind, long? citizenId = null, long? relatedId = null, TileCoordinate? location = null, int value = 0) =>
        _living!.Facts.Add(new LivingFact(_living.NextFactId++, CurrentMinute.Value, kind, citizenId, relatedId, location, value));
    private static int Distance(TileCoordinate a, TileCoordinate b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private LivingWorkOrder? OrderFor(Citizen citizen) => _living!.Orders.FirstOrDefault(x => x.CitizenId == citizen.Id.Value);
    private bool Knows(Citizen citizen, LivingTechnique technique) => LivingPerson(citizen).Knowledge.Contains(technique);
    private bool SettlementKnows(LivingTechnique technique) => _living!.People.Any(x => !x.DeathObserved && x.Knowledge.Contains(technique));
    private bool SettlementKnows(long settlementId, LivingTechnique technique) => !MigrationSystemsEnabled(SimulationRulesVersion)
        ? SettlementKnows(technique)
        : CitizensAt(settlementId).Any(c => _living!.People.Any(p => p.CitizenId == c.Id.Value && !p.DeathObserved && p.Knowledge.Contains(technique)));

    private bool TryStartLivingWork(Citizen citizen, int ordinaryScore)
    {
        var needs = citizen.Needs;
        var siteId = SiteIdForCitizen(citizen);
        var settlement = SettlementFor(siteId);
        var person = LivingPerson(citizen);
        // M13 retains M12's meal candidate and food ownership. Do not replace an
        // available M12 meal with discretionary Living work at the same hunger
        // threshold where Growth first offers Eat.
        if (UnifiedSimulationRulesEnabled(SimulationRulesVersion) && needs.Hunger >= 3500 && FoodAvailableTo(citizen) > 0) return false;
        if (needs.Hunger >= 8500 && settlement.FoodStored > 0 || needs.Rest >= 7500 || person.Injury >= 7000 || person.Illness >= 7000 || citizen.AgeYears(CurrentMinute) < 6) return false;
        var choices = OrdersAt(siteId).Where(x => x.CitizenId is null && CanWork(citizen, x))
            .Where(x => needs.Hunger < 4000 && needs.Rest < 5000 || IsEmergencyFoodWork(x.Kind) && settlement.FoodStored == 0 && needs.Rest < 6000)
            .Select(x => (Order: x, Score: WorkScore(citizen, x))).Where(x => x.Score > ordinaryScore)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Order.Id).ToArray();
        foreach (var choice in choices)
        {
            var order = choice.Order;
            if (!order.Reserved && !Reserve(order)) continue;
            order.CitizenId = citizen.Id.Value;
            order.ClaimedMinute = CurrentMinute.Value;
            order.BlockedReason = "";
            if (order.Produced) { order.Phase = LivingWorkPhase.Deliver; order.CargoInTransit = false; }
            else order.Phase = order.SuppliesDelivered ? LivingWorkPhase.Travel : LivingWorkPhase.Collect;
            BeginTravel(citizen, CitizenAction.LivingWork, order.Produced ? order.SupplyLocation : order.SuppliesDelivered ? order.Location : order.SupplyLocation, null);
            return true;
        }
        return false;
    }
    private bool CanWork(Citizen citizen, LivingWorkOrder order)
    {
        var age = citizen.AgeYears(CurrentMinute);
        if (MigrationSystemsEnabled(SimulationRulesVersion) && SiteIdForCitizen(citizen) != SiteIdForOrder(order)) return false;
        if (MigrationSystemsEnabled(SimulationRulesVersion) && order.SubjectId is { } subjectId && _citizens.TryGetValue(subjectId, out var targetCitizen) && SiteIdForCitizen(targetCitizen) != SiteIdForOrder(order)) return false;
        if (order.Kind is LivingWorkKind.Recreate or LivingWorkKind.EquipTool or LivingWorkKind.EquipClothing && order.SubjectId != citizen.Id.Value) return false;
        if (age < 13 && order.Kind is not (LivingWorkKind.Recreate or LivingWorkKind.EquipClothing)) return false;
        if (order.Kind == LivingWorkKind.Teach && (order.SubjectId == citizen.Id.Value || order.Technique is not { } taught || !Knows(citizen, taught))) return false;
        if (order.Kind is LivingWorkKind.Care or LivingWorkKind.RepairRelationship && order.SubjectId == citizen.Id.Value) return false;
        if (order.Kind == LivingWorkKind.Experiment && (order.Technique is not { } discovery || Knows(citizen, discovery))) return false;
        if (!order.Produced && RequiredTechnique(order.Kind) is { } technique && !Knows(citizen, technique)) return false;
        if (MigrationSystemsEnabled(SimulationRulesVersion) && SiteIdForOrder(order) != SiteIdForCitizen(citizen)) return false;
        if (!order.Produced && order.Kind == LivingWorkKind.MakeTool && !StructuresAt(SiteIdForOrder(order)).Any(x => x.Type == StructureType.Workshop && x.Status == StructureStatus.Complete)) return false;
        if (order.Kind == LivingWorkKind.Care && order.Ingredients.Any(x => x.Resource == "Medicine") && !Knows(citizen, LivingTechnique.Care)) return false;
        if (!GetTravelCostsCached(citizen.Location).ContainsKey(order.Location) || !GetTravelCostsCached(citizen.Location).ContainsKey(order.SupplyLocation)) return false;
        if (!HasOutputSpace(order)) return false;
        return order.Reserved || order.Ingredients.All(x => AvailableIngredient(SiteIdForOrder(order), x.Resource) >= x.Quantity);
    }
    private int WorkScore(Citizen citizen, LivingWorkOrder order)
    {
        var person = LivingPerson(citizen);
        var related = order.Kind is LivingWorkKind.Care or LivingWorkKind.Teach or LivingWorkKind.RepairRelationship && order.SubjectId is { } id && _citizens.TryGetValue(id, out var other) && (citizen.PartnerId == other.Id || other.ParentAId == citizen.Id || other.ParentBId == citizen.Id);
        var preference = person.Goal switch
        {
            LivingGoal.FamilySecurity when order.Kind is LivingWorkKind.Care or LivingWorkKind.Cook or LivingWorkKind.Harvest => 1800,
            LivingGoal.Comfort when order.Kind is LivingWorkKind.Recreate or LivingWorkKind.Weave or LivingWorkKind.CutFuel => 1600,
            LivingGoal.Mastery when order.Kind is LivingWorkKind.MakeTool or LivingWorkKind.Teach => 1800,
            LivingGoal.Exploration when order.Kind is LivingWorkKind.Experiment or LivingWorkKind.Hunt => 1800,
            _ => 0
        };
        var experience = person.Experiences.Sum(x => x.Kind switch
        {
            LivingExperienceKind.Scarcity when order.Kind is LivingWorkKind.Harvest or LivingWorkKind.Preserve or LivingWorkKind.Cook => 100,
            LivingExperienceKind.Helped when x.OtherCitizenId == order.SubjectId && order.Kind is LivingWorkKind.Care or LivingWorkKind.Teach or LivingWorkKind.RepairRelationship => 400,
            LivingExperienceKind.Bereavement when order.Kind == LivingWorkKind.Recreate => 300,
            _ => 0
        });
        var relationship = order.SubjectId is { } subject && subject != citizen.Id.Value && _citizens.ContainsKey(subject)
            && order.Kind is LivingWorkKind.Care or LivingWorkKind.Teach or LivingWorkKind.RepairRelationship ? GetRelationship(citizen.Id, new CitizenId(subject)) : null;
        var emergency = 0;
        var siteId = SiteIdForCitizen(citizen);
        if (SettlementFor(siteId).FoodStored < PopulationAt(siteId) * 10)
        {
            emergency = order.Kind switch
            {
                LivingWorkKind.Cook => 60000,
                LivingWorkKind.CutFuel when Good(siteId, LivingGood.Fuel) == 0 => 60000,
                LivingWorkKind.Harvest when Good(siteId, LivingGood.Grain) < 20 => 60000,
                _ => 0
            };
        }
        var preparation = (MigrationSystemsEnabled(SimulationRulesVersion)
            ? Good(siteId, LivingGood.PreservedFood) < PopulationAt(siteId) * 1000
            : LivingNeedsSeasonalReserves) && order.Kind is LivingWorkKind.Harvest or LivingWorkKind.Preserve or LivingWorkKind.Sow or LivingWorkKind.Tend ? 8000 : 0;
        return 6500 + emergency + preparation + order.Priority + preference + experience + (relationship?.Affinity ?? 0) / 10 + citizen.Traits.Industriousness / 5 + citizen.Traits.Cooperativeness / 8
            + Math.Min(1500, citizen.Skills.Domestic / 100) + (related ? 2500 : 0) + (order.Kind == LivingWorkKind.Recreate ? person.Stress : 0)
            - person.Injury / 2 - person.Illness / 2 - person.Stress / 5 - Distance(citizen.Location, order.Location) * 60;
    }
    private static bool IsEmergencyFoodWork(LivingWorkKind kind) => kind is LivingWorkKind.Cook or LivingWorkKind.Harvest or LivingWorkKind.CutFuel;
    private static LivingTechnique? RequiredTechnique(LivingWorkKind kind) => kind switch
    {
        LivingWorkKind.EstablishField or LivingWorkKind.Sow or LivingWorkKind.Tend or LivingWorkKind.Harvest => LivingTechnique.Cultivation,
        LivingWorkKind.Preserve => LivingTechnique.Preservation,
        LivingWorkKind.MakeTool => LivingTechnique.Toolmaking,
        LivingWorkKind.Weave or LivingWorkKind.BuildLoom => LivingTechnique.Textiles,
        LivingWorkKind.PrepareMedicine or LivingWorkKind.BuildCareHouse => LivingTechnique.Care,
        _ => null
    };
    private int AvailableIngredient(string resource) => AvailableIngredient(1, resource);
    private int AvailableIngredient(long settlementId, string resource) => resource switch
    {
        "Food" => SettlementFor(settlementId).FoodStored, "Wood" => SettlementFor(settlementId).WoodStored, "Stone" => SettlementFor(settlementId).StoneStored,
        _ => Good(settlementId, Enum.Parse<LivingGood>(resource))
    };
    private void ChangeIngredient(string resource, int delta)
        => ChangeIngredient(1, resource, delta);
    private void ChangeIngredient(long settlementId, string resource, int delta)
    {
        switch (resource)
        {
            case "Food": SettlementFor(settlementId).FoodStored = checked(SettlementFor(settlementId).FoodStored + delta); break;
            case "Wood": SettlementFor(settlementId).WoodStored = checked(SettlementFor(settlementId).WoodStored + delta); break;
            case "Stone": SettlementFor(settlementId).StoneStored = checked(SettlementFor(settlementId).StoneStored + delta); break;
            default: ChangeGoodAt(settlementId, Enum.Parse<LivingGood>(resource), delta); break;
        }
    }
    private bool Reserve(LivingWorkOrder order)
    {
        var siteId = SiteIdForOrder(order);
        if (order.Ingredients.Any(x => AvailableIngredient(siteId, x.Resource) < x.Quantity)) return false;
        foreach (var ingredient in order.Ingredients) ChangeIngredient(siteId, ingredient.Resource, -ingredient.Quantity);
        order.Reserved = true;
        return true;
    }
    private void ArriveLiving(Citizen citizen)
    {
        var order = OrderFor(citizen) ?? throw new InvalidOperationException("Living action lost its work claim.");
        if (order.Produced)
        {
            if (!order.CargoInTransit)
            {
                order.CargoInTransit = true;
                BeginTravel(citizen, CitizenAction.LivingWork, SiteLocation(SiteIdForOrder(order)), null);
                return;
            }
            DepositLiving(citizen, order);
            return;
        }
        if (order.Phase == LivingWorkPhase.Collect)
        {
            order.Phase = LivingWorkPhase.Travel;
            BeginTravel(citizen, CitizenAction.LivingWork, order.Location, null);
            return;
        }
        order.SuppliesDelivered = true;
        order.SupplyLocation = order.Location;
        order.Phase = LivingWorkPhase.Work;
        ScheduleLivingShift(citizen, 120);
    }
    private void ScheduleLivingShift(Citizen citizen, int duration)
    {
        citizen.CurrentAction = CitizenAction.LivingWork;
        citizen.ActionTarget = null;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionStartedMinute = CurrentMinute;
        citizen.ActionCompletesMinute = CurrentMinute.Add(duration);
        ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }
    private bool InterruptUnifiedLivingWorkForFood(Citizen citizen)
    {
        if (!UnifiedSimulationRulesEnabled(SimulationRulesVersion) || citizen.CurrentAction != CitizenAction.LivingWork) return false;
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        if (needs.Hunger < 3500 || FoodAvailableTo(citizen) <= 0) return false;
        citizen.Needs = needs;
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        EndLivingAction(citizen);
        return true;
    }
    private void CompleteLivingShift(Citizen citizen)
    {
        var order = OrderFor(citizen) ?? throw new InvalidOperationException("Living completion lost its work claim.");
        citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        if (order.Produced) { DepositLiving(citizen, order); return; }
        if (!OrderStillUseful(order)) { CancelLivingOrder(order); EndLivingAction(citizen); return; }
        var person = LivingPerson(citizen);
        var work = Math.Max(20, 120 - (person.Injury + person.Illness) / 200 + (person.ToolCondition > 0 ? 60 : 0) - (citizen.AgeYears(CurrentMinute) >= 65 ? 30 : 0));
        order.WorkDone = Math.Min(order.RequiredWork, order.WorkDone + work);
        person.ToolCondition = Math.Max(0, person.ToolCondition - 20);
        person.Practice = Math.Min(100000, person.Practice + 1);
        citizen.Skills.Domestic = checked(citizen.Skills.Domestic + 10);
        if (order.WorkDone >= order.RequiredWork)
        {
            if (!HasOutputSpace(order)) { order.BlockedReason = "Waiting for storage capacity"; EndLivingAction(citizen); return; }
            ProduceLiving(citizen, order);
            order.Produced = true;
            order.Phase = LivingWorkPhase.Deliver;
            if (order.Cargo.Count > 0) { order.CargoInTransit = true; BeginTravel(citizen, CitizenAction.LivingWork, SiteLocation(SiteIdForOrder(order)), null); return; }
            FinishLivingOrder(citizen, order);
            return;
        }
        var unifiedMealAvailable = UnifiedSimulationRulesEnabled(SimulationRulesVersion) && citizen.Needs.Hunger >= 3500 && FoodAvailableTo(citizen) > 0;
        if (unifiedMealAvailable || citizen.Needs.Hunger >= 6000 || citizen.Needs.Rest >= 6000 || person.Injury >= 7000) { EndLivingAction(citizen); return; }
        ScheduleLivingShift(citizen, 120);
    }
    private void DepositLiving(Citizen citizen, LivingWorkOrder order)
    {
        var siteId = SiteIdForOrder(order);
        if (citizen.Location != SiteLocation(siteId)) throw new InvalidOperationException("Production cargo must reach its local communal storage before deposit.");
        // Cargo already owns storage capacity while in transit; depositing is a transfer.
        foreach (var item in order.Cargo)
        {
            if (item.Good == LivingGood.Meal)
            {
                if (UnifiedSimulationRulesEnabled(SimulationRulesVersion)) RecordCommunalFoodProduction(item.Quantity);
                SettlementFor(siteId).FoodStored = checked(SettlementFor(siteId).FoodStored + item.Quantity);
            }
            else ChangeGoodAt(siteId, item.Good, item.Quantity);
        }
        order.Cargo.Clear();
        FinishLivingOrder(citizen, order);
    }
    private void FinishLivingOrder(Citizen citizen, LivingWorkOrder order)
    {
        _living!.CompletedOrders++;
        _living.Orders.Remove(order);
        EndLivingAction(citizen);
    }
    private void EndLivingAction(Citizen citizen)
    {
        ReleaseLivingClaim(citizen);
        FinishAction(citizen);
        ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }
    private void ReleaseLivingClaim(Citizen citizen)
    {
        var order = OrderFor(citizen);
        if (order is null) return;
        if (UnifiedSimulationRulesEnabled(SimulationRulesVersion) && order.Kind == LivingWorkKind.Harvest && order.Produced && order.CargoInTransit && citizen.CurrentAction == CitizenAction.HaulHarvest)
            order.SupplyLocation = citizen.Location;
        else if (citizen.ActionPhase == CitizenActionPhase.TravelToTarget && (order.CargoInTransit || !order.SuppliesDelivered && order.Phase == LivingWorkPhase.Travel))
            order.SupplyLocation = citizen.Location;
        order.CargoInTransit = false;
        order.CitizenId = null;
        order.BlockedReason = citizen.IsAlive ? "Worker is meeting personal needs" : "Waiting for a replacement worker";
    }
    private void CancelLivingOrder(LivingWorkOrder order)
    {
        var siteId = SiteIdForOrder(order);
        if (order.Reserved && !order.Produced) foreach (var ingredient in order.Ingredients) ChangeIngredient(siteId, ingredient.Resource, ingredient.Quantity);
        foreach (var cargo in order.Cargo)
        {
            if (cargo.Good == LivingGood.Meal)
            {
                if (UnifiedSimulationRulesEnabled(SimulationRulesVersion)) RecordCommunalFoodProduction(cargo.Quantity);
                SettlementFor(siteId).FoodStored = checked(SettlementFor(siteId).FoodStored + cargo.Quantity);
            }
            else ChangeGoodAt(siteId, cargo.Good, cargo.Quantity);
        }
        _living!.Orders.Remove(order);
    }
    private void ReconcileLivingOrders()
    {
        foreach (var order in _living!.Orders.ToArray())
        {
            if (order.CitizenId is { } workerId)
            {
                var worker = _citizens[workerId];
                var unifiedFarmCarrier = UnifiedSimulationRulesEnabled(SimulationRulesVersion) && order.Kind == LivingWorkKind.Harvest && order.Produced && worker.IsAlive && worker.CurrentAction == CitizenAction.HaulHarvest;
                if (!worker.IsAlive || worker.CurrentAction != CitizenAction.LivingWork && !unifiedFarmCarrier) ReleaseLivingClaim(worker);
            }
            if (order.CitizenId is not null) continue;
            if (!OrderStillUseful(order)) { CancelLivingOrder(order); continue; }
            var siteId = SiteIdForOrder(order);
            var missing = order.Reserved ? [] : order.Ingredients.Where(x => AvailableIngredient(siteId, x.Resource) < x.Quantity).Select(x => x.Resource).ToArray();
            order.BlockedReason = missing.Length > 0 ? "Waiting for " + string.Join(", ", missing)
                : !HasOutputSpace(order) ? "Waiting for storage capacity"
                : order.Kind == LivingWorkKind.MakeTool && !StructuresAt(siteId).Any(x => x.Type == StructureType.Workshop && x.Status == StructureStatus.Complete) ? "Needs a completed workshop"
                : RequiredTechnique(order.Kind) is { } required && !SettlementKnows(siteId, required) ? $"Needs {required} knowledge"
                : "Waiting for an available qualified worker";
        }
    }
    private bool OrderStillUseful(LivingWorkOrder order)
    {
        if (order.Produced) return true;
        var siteId = SiteIdForOrder(order);
        if (order.Kind == LivingWorkKind.Experiment) return order.Technique is { } technique && !SettlementKnows(siteId, technique);
        if (order.Kind is LivingWorkKind.Care or LivingWorkKind.Teach or LivingWorkKind.Recreate or LivingWorkKind.RepairRelationship or LivingWorkKind.EquipTool or LivingWorkKind.EquipClothing)
        {
            if (order.SubjectId is not { } citizen || !_citizens.TryGetValue(citizen, out var target) || !target.IsAlive ||
                MigrationSystemsEnabled(SimulationRulesVersion) && SiteIdForCitizen(target) != siteId) return false;
            var person = LivingPerson(target);
            return order.Kind switch
            {
                LivingWorkKind.Teach => order.Technique is { } taught && !person.Knowledge.Contains(taught),
                LivingWorkKind.Care => person.Injury > 0 || person.Illness > 500 || target.AgeYears(CurrentMinute) < 6,
                LivingWorkKind.EquipTool => person.ToolCondition == 0,
                LivingWorkKind.EquipClothing => person.ClothingCondition == 0,
                LivingWorkKind.Recreate => target.AgeYears(CurrentMinute) >= 6 && person.LastLeisureMinute < order.CreatedMinute,
                LivingWorkKind.RepairRelationship => person.Stress > 1000,
                _ => true
            };
        }
        if (order.Kind == LivingWorkKind.Hunt) return _living!.Animals.Any(x => x.Id == order.SubjectId && (!MigrationSystemsEnabled(SimulationRulesVersion) || SiteIdForLocation(x.Location) == siteId));
        if (order.Kind is LivingWorkKind.Sow or LivingWorkKind.Tend or LivingWorkKind.Harvest)
        {
            var field = _living!.Fields.SingleOrDefault(x => x.Id == order.SubjectId && (!MigrationSystemsEnabled(SimulationRulesVersion) || SiteIdForLocation(x.Location) == siteId));
            return field is not null && (order.Kind != LivingWorkKind.Harvest || field.YieldRemaining > 0) && (order.Kind != LivingWorkKind.Sow || field.SownMinute < 0);
        }
        return true;
    }
}
