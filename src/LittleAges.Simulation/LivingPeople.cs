using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private void Experience(LivingPerson person, LivingExperienceKind kind, long? other = null)
    {
        if (person.Experiences.Any(x => x.Kind == kind && x.OtherCitizenId == other && CurrentMinute.Value - x.Minute < WorldCalendar.MinutesPerDay)) return;
        person.Experiences.Add(new(kind, CurrentMinute.Value, other));
        if (person.Experiences.Count > 24) person.Experiences.RemoveAt(0);
    }
    private void AdvanceLivingPeople()
    {
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive).OrderBy(x => x.Id.Value))
        {
            var person = LivingPerson(citizen);
            var settlementId = SiteIdForCitizen(citizen);
            var settlement = SettlementFor(settlementId);
            var needs = citizen.GetProjectedNeeds(CurrentMinute);
            person.Experiences.RemoveAll(x => CurrentMinute.Value - x.Minute > 30L * WorldCalendar.MinutesPerDay);
            if (settlement.FoodStored < PopulationAt(settlementId) * 5) Experience(person, LivingExperienceKind.Scarcity);
            var feelings = person.Experiences.Sum(x => x.Kind switch { LivingExperienceKind.Bereavement => -800, LivingExperienceKind.Scarcity => -300, LivingExperienceKind.Helped => 500, LivingExperienceKind.SharedWork => 150, LivingExperienceKind.Recreation => 400, LivingExperienceKind.Learned => 300, _ => 0 });
            person.Mood = Math.Clamp(6500 + feelings - (needs.Hunger + needs.Rest) / 5 - (person.Injury + person.Illness) / 4, 0, 10000);
            person.Stress = Math.Clamp(person.Stress + (5000 - person.Mood) / 5 - citizen.Traits.Resilience / 100, 0, 10000);
            person.ClothingCondition = Math.Max(0, person.ClothingCondition - 30);
            var hasWarmth = _living!.Temperature >= 5 || person.ClothingCondition > 0 || Good(settlementId, LivingGood.Fuel) > 0 && citizen.HomeStructureId is not null;
            if (!hasWarmth) person.Illness = Math.Min(10000, person.Illness + 300);
            else if (needs.Hunger < 6000) person.Illness = Math.Max(0, person.Illness - 100);
            if (person.Injury > 0 && needs.Hunger < 6000) person.Injury = Math.Max(0, person.Injury - 100);
            if (person.Injury + person.Illness > 10000)
            {
                citizen.Health = Math.Max(1, citizen.Health - 100);
                // Existing survival/lifecycle mortality remains authoritative; debility reduces its resilience.
            }
            if (citizen.CurrentAction is CitizenAction.GatherWood or CitizenAction.GatherStone or CitizenAction.Build && LivingRandom(citizen.Id.Value, 81) % 1200 == 0)
                InjureLiving(citizen, 2000);
        }
    }
    private void InjureLiving(Citizen citizen, int severity)
    {
        var person = LivingPerson(citizen);
        var wasHealthy = person.Injury == 0;
        person.Injury = Math.Min(10000, person.Injury + severity);
        if (wasHealthy) Fact(LivingFactKind.Injury, citizen.Id.Value, location: citizen.Location, value: severity);
    }
    private void RecoverLivingRest(Citizen citizen)
    {
        var person = LivingPerson(citizen);
        var before = person.Injury + person.Illness;
        person.Injury = Math.Max(0, person.Injury - 80);
        person.Illness = Math.Max(0, person.Illness - 100);
        if (before > 0 && person.Injury + person.Illness == 0) Fact(LivingFactKind.Recovery, citizen.Id.Value, location: citizen.Location);
    }
    private int LivingUtilityAdjustment(Citizen citizen, CitizenAction action)
    {
        if (_living is null) return 0;
        var person = LivingPerson(citizen);
        var predatorNearby = _living.Animals.Any(x => x.Predator && Distance(x.Location, citizen.Location) <= 3);
        return action switch
        {
            CitizenAction.Eat when citizen.GetProjectedNeeds(CurrentMinute).Hunger >= 4000 => 60000,
            CitizenAction.Rest when citizen.GetProjectedNeeds(CurrentMinute).Rest >= 5000 => 60000,
            CitizenAction.Build or CitizenAction.HaulConstruction => 11000,
            CitizenAction.Rest => (person.Injury + person.Illness) / 2 + (predatorNearby ? 2500 : 0),
            CitizenAction.Socialize => person.Stress / 3 + (10000 - person.Mood) / 5,
            CitizenAction.GatherWood when Settlement.FoodStored == 0 && Good(LivingGood.Grain) >= 20 && Good(LivingGood.Fuel) == 0 && Settlement.WoodStored < 10 => 60000,
            CitizenAction.GatherWood => Settlement.WoodStored < 80 ? 3500 : -20000,
            CitizenAction.GatherStone => Settlement.StoneStored < 40 ? 3500 : -20000,
            CitizenAction.Explore when person.Goal == LivingGoal.Exploration => 1000 - (predatorNearby ? 4000 : 0),
            CitizenAction.GatherFood => Settlement.FoodStored < LivingPopulation * 20 ? 7000 : -5000,
            _ => 0
        };
    }
    private void PlanLivingPersonalWork()
    {
        foreach (var citizen in _citizens.Values.Where(x => x.IsAlive).OrderBy(x => x.Id.Value))
        {
            var settlementId = SiteIdForCitizen(citizen);
            var person = LivingPerson(citizen);
            if (person.ToolCondition == 0 && citizen.AgeYears(CurrentMinute) >= 13 && Good(settlementId, LivingGood.Tool) > 0)
                RequestLiving(LivingWorkKind.EquipTool, SiteLocation(settlementId), 4000, citizen.Id.Value, settlementId: settlementId, ingredients: [new("Tool", 1)]);
            if (person.ClothingCondition == 0 && citizen.AgeYears(CurrentMinute) >= 6 && Good(settlementId, LivingGood.Clothing) > 0)
                RequestLiving(LivingWorkKind.EquipClothing, SiteLocation(settlementId), 4500, citizen.Id.Value, settlementId: settlementId, ingredients: [new("Clothing", 1)]);
            if ((person.Injury > 0 || person.Illness > 500 || citizen.AgeYears(CurrentMinute) < 6) && CurrentMinute.Value - person.LastCareMinute >= WorldCalendar.MinutesPerDay)
            {
                var supplies = new List<LivingIngredient>();
                if (Good(settlementId, LivingGood.Medicine) > 0 && SettlementKnows(settlementId, LivingTechnique.Care)) supplies.Add(new("Medicine", 1));
                if (citizen.AgeYears(CurrentMinute) < 6 && SettlementFor(settlementId).FoodStored >= 2) supplies.Add(new("Food", 2));
                RequestLiving(LivingWorkKind.Care, citizen.Location, 6500, citizen.Id.Value, settlementId: settlementId, ingredients: supplies.ToArray());
            }
            if (citizen.AgeYears(CurrentMinute) >= 6 && (person.Stress > 2500 || CurrentMinute.Value - person.LastLeisureMinute >= 2L * WorldCalendar.MinutesPerDay))
                RequestLiving(LivingWorkKind.Recreate, citizen.Location, 1500, citizen.Id.Value, settlementId: settlementId);
            PlanLivingLearning(citizen, person);
            if (person.Stress > 3000)
                RequestLiving(LivingWorkKind.RepairRelationship, citizen.Location, 1800, citizen.Id.Value, settlementId: settlementId);
        }
        foreach (var order in _living!.Orders.Where(x => x.CitizenId is null && x.Kind is LivingWorkKind.Care or LivingWorkKind.Teach or LivingWorkKind.Recreate or LivingWorkKind.RepairRelationship))
            if (order.SubjectId is { } id && _citizens.TryGetValue(id, out var person)) order.Location = person.Location;
    }
    private void CompleteLivingPersonalWork(Citizen citizen, LivingWorkOrder order)
    {
        var person = LivingPerson(citizen);
        switch (order.Kind)
        {
            case LivingWorkKind.EquipTool: person.ToolCondition = 10000; break;
            case LivingWorkKind.EquipClothing: person.ClothingCondition = 10000; break;
            case LivingWorkKind.Experiment:
                if (order.Technique is { } technique && DiscoveryReady(citizen, technique))
                {
                    Learn(person, technique);
                    Fact(LivingFactKind.TechniqueDiscovered, citizen.Id.Value, location: citizen.Location, value: (int)technique);
                }
                break;
            case LivingWorkKind.Recreate:
                person.Stress = Math.Max(0, person.Stress - 2200);
                person.LastLeisureMinute = CurrentMinute.Value;
                Experience(person, LivingExperienceKind.Recreation);
                citizen.Needs = citizen.Needs with { Social = Math.Max(0, citizen.Needs.Social - 1500), Rest = Math.Max(0, citizen.Needs.Rest - 500) };
                break;
            case LivingWorkKind.Care:
            case LivingWorkKind.Teach:
            case LivingWorkKind.RepairRelationship:
                if (order.SubjectId is not { } id || !_citizens.TryGetValue(id, out var target) || !target.IsAlive ||
                    MigrationSystemsEnabled(SimulationRulesVersion) && SiteIdForCitizen(citizen) != SiteIdForCitizen(target) || Distance(citizen.Location, target.Location) > 3) break;
                var recipient = LivingPerson(target);
                if (order.Kind == LivingWorkKind.Teach && order.Technique is { } taught && !recipient.Knowledge.Contains(taught))
                {
                    Learn(recipient, taught); recipient.LastTeachingMinute = CurrentMinute.Value;
                    Fact(LivingFactKind.TechniqueTaught, target.Id.Value, citizen.Id.Value, citizen.Location, (int)taught);
                }
                else if (order.Kind == LivingWorkKind.Care)
                {
                    var before = recipient.Injury + recipient.Illness;
                    var medicine = order.Ingredients.Any(x => x.Resource == "Medicine");
                    var treatment = medicine ? 2500 : 900;
                    if (FacilitiesAt(SiteIdForCitizen(target)).Any(x => x.Kind == LivingFacilityKind.CareHouse && Distance(x.Location, target.Location) <= 8)) treatment += 500;
                    recipient.Injury = Math.Max(0, recipient.Injury - treatment);
                    recipient.Illness = Math.Max(0, recipient.Illness - treatment);
                    recipient.LastCareMinute = CurrentMinute.Value;
                    var needs = target.GetProjectedNeeds(CurrentMinute);
                    target.Needs = needs with { Social = Math.Max(0, needs.Social - 2500), Hunger = Math.Max(0, needs.Hunger - (order.Ingredients.Any(x => x.Resource == "Food") ? 3000 : 0)) };
                    target.NeedsUpdatedMinute = CurrentMinute.Value;
                    _living!.CareGiven++;
                    Experience(recipient, LivingExperienceKind.Helped, citizen.Id.Value);
                    if (before > 0 && recipient.Injury + recipient.Illness == 0) Fact(LivingFactKind.Recovery, target.Id.Value, citizen.Id.Value, target.Location);
                }
                else if (order.Kind == LivingWorkKind.RepairRelationship)
                {
                    recipient.Stress = Math.Max(0, recipient.Stress - 1000);
                    Experience(recipient, LivingExperienceKind.SharedWork, citizen.Id.Value);
                }
                StrengthenLivingRelationship(citizen, target, order.Kind == LivingWorkKind.RepairRelationship);
                Experience(person, LivingExperienceKind.SharedWork, target.Id.Value);
                break;
        }
    }
    private void StrengthenLivingRelationship(Citizen citizen, Citizen target, bool repair)
    {
        var pair = RelationshipState.Normalize(citizen.Id, target.Id);
        var previous = GetRelationship(citizen.Id, target.Id);
        var next = new RelationshipState(pair.A, pair.B,
            Math.Min(10000, (previous?.Familiarity ?? 0) + 100), Math.Min(10000, (previous?.Affinity ?? 0) + 100),
            Math.Min(10000, (previous?.Trust ?? 0) + 150), Math.Max(0, (previous?.Conflict ?? 0) - (repair ? 600 : 100)),
            CurrentMinute.Value, checked((previous?.InteractionCount ?? 0) + 1));
        _relationships[(pair.A.Value, pair.B.Value)] = next;
        RecordSocialInteractionHistory(previous, next);
    }
}
