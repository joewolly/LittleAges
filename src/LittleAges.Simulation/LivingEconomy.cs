using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    // Ten food units restore 5000 hunger. Winter adds 4320 hunger per day;
    // 1000 units per person cover its 90 days plus spoilage and a poor spring.
    private int LivingSeasonalFoodTarget => checked(LivingPopulation * 1000);
    private bool LivingNeedsSeasonalReserves => Good(LivingGood.PreservedFood) < LivingSeasonalFoodTarget;
    private void RequestLiving(LivingWorkKind kind, TileCoordinate location, int priority, long? subject = null,
        LivingTechnique? technique = null, int work = 120, params LivingIngredient[] ingredients)
    {
        var existing = _living!.Orders.FirstOrDefault(x => x.Kind == kind && x.SubjectId == subject && x.Technique == technique);
        if (existing is not null) { existing.Priority = priority; return; }
        if (_living.Orders.Count >= Math.Max(32, LivingPopulation * 4))
        {
            // An old optional queue must not prevent a new essential opportunity.
            // Claimed work and cargo retain their ownership; only untouched work can yield.
            var deferred = _living.Orders.Where(x => x.CitizenId is null && !x.Reserved && x.WorkDone == 0 && x.Priority < priority)
                .OrderBy(x => x.Priority).ThenByDescending(x => x.Id).FirstOrDefault();
            // Essential work has its own finite keys (hearth, field, or patient).
            // Preserve partial work without letting a full board block its recovery chain.
            if (deferred is null && kind is not (LivingWorkKind.Cook or LivingWorkKind.CutFuel or LivingWorkKind.Harvest or LivingWorkKind.Care)) return;
            if (deferred is not null) CancelLivingOrder(deferred);
        }
        _living.Orders.Add(new LivingWorkOrder { Id = _living.NextId++, Kind = kind, Location = location, SupplyLocation = World.StartingSite,
            SubjectId = subject, Technique = technique, CreatedMinute = CurrentMinute.Value, Priority = priority, RequiredWork = work, Ingredients = ingredients.ToList() });
    }
    private void PlanLivingEconomy()
    {
        if (LivingPopulation == 0) return;
        var state = _living!;
        var site = World.StartingSite;
        var foodPressure = Settlement.FoodStored < LivingPopulation * 20;
        foreach (var technique in Enum.GetValues<LivingTechnique>())
        {
            if (!SettlementKnows(technique)) RequestLiving(LivingWorkKind.Experiment, site, 2200, technique: technique, work: 480);
        }
        if (SimulationRulesVersion != UnifiedSimulationRulesVersion)
        {
            var desiredFields = Math.Min(64, Math.Max(2, (LivingPopulation + 1) / 2));
            if (SettlementKnows(LivingTechnique.Cultivation) && state.Fields.Count < desiredFields && SelectLivingSite(true) is { } fieldSite)
                RequestLiving(LivingWorkKind.EstablishField, fieldSite, 4000, work: 360, ingredients: [new("Wood", 4)]);
            foreach (var field in state.Fields)
            {
                if (field.YieldRemaining > 0 && Good(LivingGood.Grain) < LivingPopulation * 200) RequestLiving(LivingWorkKind.Harvest, field.Location, foodPressure ? 6500 : 3500, field.Id);
                else if (field.SownMinute < 0 && CurrentMinute.ToCalendar().Season != WorldSeason.Winter)
                    RequestLiving(LivingWorkKind.Sow, field.Location, 4500, field.Id, work: 240);
                else if (field.SownMinute >= 0 && CurrentMinute.Value - field.LastTendedMinute >= 3L * WorldCalendar.MinutesPerDay)
                    RequestLiving(LivingWorkKind.Tend, field.Location, 3200, field.Id);
            }
        }
        RequestFacility(LivingFacilityKind.Hearth, LivingWorkKind.BuildHearth, 4500, null);
        RequestFacility(LivingFacilityKind.Loom, LivingWorkKind.BuildLoom, 2200, LivingTechnique.Textiles);
        RequestFacility(LivingFacilityKind.CareHouse, LivingWorkKind.BuildCareHouse, 2600, LivingTechnique.Care);
        foreach (var hearth in state.Facilities.Where(x => x.Kind == LivingFacilityKind.Hearth))
        {
            if (Good(LivingGood.Grain) >= 20 && (foodPressure || Settlement.FoodStored < LivingPopulation * 60))
                RequestLiving(LivingWorkKind.Cook, hearth.Location, foodPressure ? 6500 : 3000, hearth.Id, ingredients: [new("Grain", 20), new("Fuel", 1)]);
            if (Good(LivingGood.Grain) >= 20 && LivingNeedsSeasonalReserves && SettlementKnows(LivingTechnique.Preservation))
                RequestLiving(LivingWorkKind.Preserve, hearth.Location, 5000, hearth.Id, ingredients: [new("Grain", 20), new("Fuel", 2)]);
        }
        if (Good(LivingGood.Fuel) < LivingPopulation * (state.Temperature < 5 ? 8 : 3))
            RequestLiving(LivingWorkKind.CutFuel, site, state.Temperature < 5 ? 5000 : 3000, ingredients: [new("Wood", 10)]);
        if (Good(LivingGood.Tool) < Math.Max(2, LivingPopulation / 4) && SettlementKnows(LivingTechnique.Toolmaking))
            RequestLiving(LivingWorkKind.MakeTool, site, 2400, work: 240, ingredients: [new("Wood", 4), new("Stone", 2)]);
        var loom = state.Facilities.FirstOrDefault(x => x.Kind == LivingFacilityKind.Loom);
        if (loom is not null && Good(LivingGood.Clothing) < Math.Max(2, LivingPopulation / 4))
            RequestLiving(LivingWorkKind.Weave, loom.Location, 3000, work: 240, ingredients: [new("Fiber", 8)]);
        var careHouse = state.Facilities.FirstOrDefault(x => x.Kind == LivingFacilityKind.CareHouse);
        if (careHouse is not null && SettlementKnows(LivingTechnique.Care) && Good(LivingGood.Medicine) < LivingPopulation)
            RequestLiving(LivingWorkKind.PrepareMedicine, careHouse.Location, 2600, ingredients: [new("Food", 5), new("Fiber", 2)]);
        var hunt = state.Animals.Where(x => !x.Predator && !state.Orders.Any(o => o.Kind == LivingWorkKind.Hunt && o.SubjectId == x.Id))
            .OrderBy(x => Distance(x.Location, site)).ThenBy(x => x.Id).FirstOrDefault();
        if (foodPressure && hunt is not null && !state.Orders.Any(x => x.Kind == LivingWorkKind.Hunt))
            RequestLiving(LivingWorkKind.Hunt, hunt.Location, 4000, hunt.Id, work: 240);
        PlanLivingPersonalWork();
    }
    private void RequestFacility(LivingFacilityKind kind, LivingWorkKind workKind, int priority, LivingTechnique? technique)
    {
        var needed = kind == LivingFacilityKind.Hearth ? Math.Min(16, Math.Max(1, (LivingPopulation + 3) / 4)) : 1;
        if (_living!.Facilities.Count(x => x.Kind == kind) >= needed || technique is { } required && !SettlementKnows(required)) return;
        if (SelectLivingSite(false) is { } site) RequestLiving(workKind, site, priority, work: 480, ingredients: [new("Wood", 12), new("Stone", 6)]);
    }
    private TileCoordinate? SelectLivingSite(bool field)
    {
        var used = _living!.Fields.Select(x => x.Location).Concat(_living.Facilities.Select(x => x.Location))
            .Concat(_structures.Values.Select(x => x.Location)).Concat(_living.Orders.Where(x => x.Kind is LivingWorkKind.EstablishField or LivingWorkKind.BuildHearth or LivingWorkKind.BuildLoom or LivingWorkKind.BuildCareHouse).Select(x => x.Location)).ToHashSet();
        var costs = GetTravelCostsCached(World.StartingSite);
        var resourceSites = World.Resources.Select(x => x.Coordinate).ToHashSet();
        return World.Tiles.Where(x => x.Buildable && x.Coordinate != World.StartingSite && !used.Contains(x.Coordinate) && costs.ContainsKey(x.Coordinate)
                && !resourceSites.Contains(x.Coordinate) && (!field || x.Fertility >= 2000))
            .OrderBy(x => costs[x.Coordinate] * 10L - (field ? x.Fertility / 10 : 0)).ThenBy(x => x.Coordinate)
            .Select(x => (TileCoordinate?)x.Coordinate).FirstOrDefault();
    }
    private int OutputQuantity(LivingWorkKind kind) => LivingWorkDefinitions.OutputQuantity(kind, SimulationRulesVersion);
    private bool HasOutputSpace(LivingWorkOrder order) => order.Produced || OutputQuantity(order.Kind) <= LivingFreeStorage + order.Ingredients.Sum(x => x.Quantity);
    private void ProduceLiving(Citizen citizen, LivingWorkOrder order)
    {
        var state = _living!;
        if (SimulationRulesVersion == UnifiedSimulationRulesVersion)
        {
            var consumed = state.UnifiedM12InputsConsumed ?? new Goods();
            foreach (var ingredient in order.Ingredients)
                consumed = ingredient.Resource switch
                {
                    "Food" => consumed.Add(ResourceType.Food, ingredient.Quantity),
                    "Wood" => consumed.Add(ResourceType.Wood, ingredient.Quantity),
                    "Stone" => consumed.Add(ResourceType.Stone, ingredient.Quantity),
                    _ => consumed
                };
            if (consumed != new Goods()) state.UnifiedM12InputsConsumed = consumed;
        }
        if (SimulationRulesVersion == UnifiedSimulationRulesVersion && order.Kind is LivingWorkKind.Cook or LivingWorkKind.Preserve)
            state.CommunalGrainConsumed = checked(state.CommunalGrainConsumed + order.Ingredients.Where(x => x.Resource == nameof(LivingGood.Grain)).Sum(x => (long)x.Quantity));
        switch (order.Kind)
        {
            case LivingWorkKind.EstablishField:
                var field = new LivingField { Id = state.NextId++, Location = order.Location };
                state.Fields.Add(field);
                Fact(LivingFactKind.FieldEstablished, citizen.Id.Value, field.Id, field.Location);
                break;
            case LivingWorkKind.Sow:
                var planted = state.Fields.Single(x => x.Id == order.SubjectId);
                planted.SownMinute = CurrentMinute.Value; planted.Growth = 0; planted.Condition = 10000; planted.LastTendedMinute = CurrentMinute.Value;
                break;
            case LivingWorkKind.Tend:
                var tended = state.Fields.Single(x => x.Id == order.SubjectId);
                tended.Moisture = Math.Min(10000, tended.Moisture + 1800);
                tended.Condition = Math.Min(10000, tended.Condition + 1000);
                tended.LastTendedMinute = CurrentMinute.Value;
                break;
            case LivingWorkKind.Harvest:
                var harvested = state.Fields.Single(x => x.Id == order.SubjectId);
                var yield = Math.Min(60, harvested.YieldRemaining);
                order.Cargo.Add(new(LivingGood.Grain, yield));
                order.Cargo.Add(new(LivingGood.Fiber, 5));
                harvested.YieldRemaining -= yield;
                state.FoodHarvested += yield;
                if (harvested.YieldRemaining == 0)
                {
                    harvested.Harvests++; harvested.SownMinute = -1; harvested.Growth = 0;
                    if (harvested.Harvests == 1) Fact(LivingFactKind.FirstHarvest, citizen.Id.Value, harvested.Id, harvested.Location);
                }
                break;
            case LivingWorkKind.Cook: order.Cargo.Add(new(LivingGood.Meal, 30)); state.FoodPrepared += 30; break;
            case LivingWorkKind.Preserve: order.Cargo.Add(new(LivingGood.PreservedFood, 20)); break;
            case LivingWorkKind.CutFuel: order.Cargo.Add(new(LivingGood.Fuel, OutputQuantity(order.Kind))); break;
            case LivingWorkKind.MakeTool: order.Cargo.Add(new(LivingGood.Tool, 1)); break;
            case LivingWorkKind.Weave: order.Cargo.Add(new(LivingGood.Clothing, 1)); break;
            case LivingWorkKind.PrepareMedicine: order.Cargo.Add(new(LivingGood.Medicine, 5)); break;
            case LivingWorkKind.BuildHearth: CompleteLivingFacility(citizen, order, LivingFacilityKind.Hearth); break;
            case LivingWorkKind.BuildLoom: CompleteLivingFacility(citizen, order, LivingFacilityKind.Loom); break;
            case LivingWorkKind.BuildCareHouse: CompleteLivingFacility(citizen, order, LivingFacilityKind.CareHouse); break;
            case LivingWorkKind.Hunt:
                var animal = state.Animals.Single(x => x.Id == order.SubjectId);
                // Wildlife moves independently; a missed hunt costs time, not an invented yield.
                if (Distance(animal.Location, citizen.Location) > 3) break;
                state.Animals.Remove(animal);
                order.Cargo.Add(new(LivingGood.Meal, 40)); order.Cargo.Add(new(LivingGood.Hide, 5));
                if (LivingRandom(citizen.Id.Value, 74) % 10 == 0) InjureLiving(citizen, 1800);
                break;
            default: CompleteLivingPersonalWork(citizen, order); break;
        }
    }
    private void CompleteLivingFacility(Citizen citizen, LivingWorkOrder order, LivingFacilityKind kind)
    {
        var facility = new LivingFacility(_living!.NextId++, kind, order.Location, CurrentMinute.Value);
        _living.Facilities.Add(facility);
        Fact(LivingFactKind.FacilityCompleted, citizen.Id.Value, facility.Id, facility.Location, (int)kind);
    }
}
