using System.Globalization;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed record GrowthHouseholdObservation(string HouseholdId, bool Ready, IReadOnlyList<string> Blockers);
public sealed record GrowthObservation(int Living, int Births, int Deaths, int UnpartneredAdults,
    int Unhoused, int FoodReserveTarget, IReadOnlyDictionary<string, int> DeathCauses,
    IReadOnlyList<GrowthHouseholdObservation> Households);

public sealed partial class SimulationEngine
{
    public static bool GrowthSystemsEnabled(string rulesVersion) => rulesVersion is GrowthSimulationRulesVersion or AgricultureSimulationRulesVersion or BarterSimulationRulesVersion or SpacedSimulationRulesVersion or UnifiedSimulationRulesVersion;
    public static bool UsesSampledShortageRecovery(string rulesVersion) => rulesVersion is M8SimulationRulesVersion or LivingSimulationRulesVersion or Living2SimulationRulesVersion || GrowthSystemsEnabled(rulesVersion);

    private void EnsureIndependentHouseholds()
    {
        foreach (var citizen in _citizens.Values.Where(c => c.IsAlive).OrderBy(c => c.Id.Value))
        {
            var livesWithParent = citizen.HouseholdId is { } home && new[] { citizen.ParentAId, citizen.ParentBId }
                .Any(id => id is { } parentId && _citizens.TryGetValue(parentId.Value, out var parent) && parent.HouseholdId == home);
            if (citizen.HouseholdId is not null && !(citizen.AgeYears(CurrentMinute) >= 18 && citizen.PartnerId is null && livesWithParent)) continue;
            var household = new Household(_counters.AllocateHouseholdId(), CurrentMinute.Value) { DwellingStructureId = citizen.HomeStructureId };
            _households.Add(household.Id.Value, household);
            citizen.HouseholdId = household.Id;
        }
        ReconcileHouseholdsAndHousing();
    }

    private Citizen[] GrowthPartnershipMembers(Citizen first, Citizen second) => _citizens.Values
        .Where(c => c.IsAlive && (c.Id == first.Id || c.Id == second.Id ||
            (c.AgeYears(CurrentMinute) < 18 && (c.ParentAId == first.Id || c.ParentBId == first.Id || c.ParentAId == second.Id || c.ParentBId == second.Id))))
        .OrderBy(c => c.Id.Value).ToArray();

    private int GrowthFoodReserveTarget => checked(LivingPopulation * (EconomyEnabled ? 12 : 60));

    private List<CitizenDecisionEvaluation> EvaluateGrowthDecision(Citizen citizen)
    {
        var result = new List<CitizenDecisionEvaluation>();
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        var random = new DeterministicRandom(Seed);
        var travel = GetTravelCostsCached(citizen.Location);
        var age = citizen.AgeYears(CurrentMinute);
        void Add(CitizenAction action, int score, ulong purpose, long cost = 0)
        {
            if (!IsAgeEligible(action, age)) return;
            score += OccupationBonus(citizen, action);
            var variation = Variation(random, citizen, purpose);
            var penalty = (int)Math.Min(2000, cost / 10);
            result.Add(new(action, score, 0, 0, variation, checked(score + variation - penalty), TravelPenalty: penalty));
        }
        var hasMeal = EconomyEnabled
            ? FoodAvailableTo(citizen) > 0 || citizen.HouseholdId is { } household && HasEmergencyFoodDonor(household.Value, needs.Hunger)
            : Settlement.FoodStored > 0;
        if (hasMeal && needs.Hunger >= 3500 && travel.TryGetValue(World.StartingSite, out var mealCost))
            Add(CitizenAction.Eat, needs.Hunger * 4, 1, mealCost);
        if (needs.Rest >= 4000) Add(CitizenAction.Rest, needs.Rest * 3, 2);
        if (needs.Hunger < 8000 && needs.Rest < 8500 && needs.Social >= 3500 && SelectSocialTarget(citizen) is not null)
            Add(CitizenAction.Socialize, needs.Social * 2 + citizen.Traits.Sociability / 10, 101);

        var project = ActiveConstructionProject;
        var woodMissing = project is null ? 0 : ConstructionMissing(project, ResourceType.Wood);
        var stoneMissing = project is null ? 0 : ConstructionMissing(project, ResourceType.Stone);
        if (project is not null && travel.TryGetValue(project.Location, out var projectCost))
        {
            var urgency = project.Type == StructureType.Shelter && ShelterCapacity < LivingPopulation ? 9000 : 4500;
            if ((woodMissing > 0 && (Settlement.WoodStored > 0 || CanProcure(ResourceType.Wood))) || (stoneMissing > 0 && (Settlement.StoneStored > 0 || CanProcure(ResourceType.Stone))))
                Add(CitizenAction.HaulConstruction, urgency, 9, projectCost);
            else if (woodMissing == 0 && stoneMissing == 0) Add(CitizenAction.Build, urgency, 10, projectCost);
        }
        if (Settlement.StorageUsed < StorageCapacity)
        {
            GatherCandidate(CitizenAction.GatherFood, ResourceType.Food, Settlement.FoodStored < GrowthFoodReserveTarget
                ? 4500 + (GrowthFoodReserveTarget - Settlement.FoodStored) * 4000 / Math.Max(1, GrowthFoodReserveTarget) : 0, 3);
            GatherCandidate(CitizenAction.GatherWood, ResourceType.Wood, Settlement.WoodStored < woodMissing ? 9500 : Settlement.WoodStored < 80 ? 2500 : 0, 4);
            GatherCandidate(CitizenAction.GatherStone, ResourceType.Stone, Settlement.StoneStored < stoneMissing ? 9500 : Settlement.StoneStored < 40 ? 2500 : 0, 5);
        }
        if (AgricultureSystemsEnabled(SimulationRulesVersion))
        {
            if (SelectFarmTarget(citizen, CitizenAction.WorkFarm) is { } field) Add(CitizenAction.WorkFarm, 6500, 201, travel[field.Location]);
            if (SelectFarmTarget(citizen, CitizenAction.HaulHarvest) is { } harvest) Add(CitizenAction.HaulHarvest, 8500, 202, travel[harvest.Location]);
        }
        if (TradeFor(citizen) is not null) Add(CitizenAction.TradeDelivery, 10000, 203);
        // Once supplies and needs are satisfied, rest locally instead of aimless long trips.
        Add(CitizenAction.Idle, 500, 8);
        return result;

        void GatherCandidate(CitizenAction action, ResourceType type, int score, ulong purpose)
        {
            score = EconomicGatherScore(citizen, type, score);
            if (score <= 0 || SelectResourceTarget(citizen, type) is not { } node || !travel.TryGetValue(node.Coordinate, out var cost)) return;
            // Reserve space for outbound workers as well as goods already in transit.
            // Otherwise a full stockpile can trap every gatherer in WaitingForStorage,
            // unable to eat the food they just produced.
            var incoming = _citizens.Values.Where(c => c.IsAlive && c.Id != citizen.Id)
                .Sum(c => c.CarriedResourceQuantity > 0 ? (long)c.CarriedResourceQuantity :
                    c.CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone
                        ? GrowthGatherYield(c, c.CurrentAction) : 0);
            if (incoming + GrowthGatherYield(citizen, action) > AvailableStorage(type)) return;
            if (type == ResourceType.Food && Settlement.FoodStored == 0 && needs.Hunger >= 6000) score += needs.Hunger * 2;
            Add(action, score + citizen.Traits.Industriousness / 20, purpose, cost);
        }
    }

    private int GrowthGatherYield(Citizen citizen, CitizenAction action)
    {
        var baseYield = action == CitizenAction.GatherFood ? CitizenSimulationRules.FoodBaseYield : action == CitizenAction.GatherWood ? CitizenSimulationRules.WoodBaseYield : CitizenSimulationRules.StoneBaseYield;
        return checked((baseYield + RelevantSkill(action, citizen) / 1000) * LifeStages.ProductivityBasisPoints(citizen.AgeYears(CurrentMinute)) / 10000);
    }

    public GrowthObservation CreateGrowthObservation()
    {
        var citizens = _citizens.Values;
        var households = _households.Values.Where(h => h.DissolvedMinute is null).OrderBy(h => h.Id.Value).Select(h =>
        {
            var readiness = EvaluateReproductionReadiness(h);
            var reasons = new List<string>();
            var paired = readiness.Parents.Length == 2 && readiness.Parents[0].PartnerId == readiness.Parents[1].Id;
            if (!paired) reasons.Add("No partnered pair");
            if (readiness.AgesBlocked) reasons.Add("Outside reproductive age");
            if (readiness.HungerOrHealthBlocked) reasons.Add("Health or hunger");
            if (readiness.FoodBlocked) reasons.Add("Insufficient food reserve");
            if (paired && readiness.CooldownBlocked) reasons.Add("Birth spacing");
            if (citizens.Count(c => c.IsAlive && c.HouseholdId == h.Id) >= CitizenSimulationRules.MaximumHouseholdSize) reasons.Add("Household full");
            if (h.DwellingStructureId is not { } home || citizens.Count(c => c.IsAlive && c.HomeStructureId == home) >= CitizenSimulationRules.ShelterCapacityPerBuilding) reasons.Add("No room at home");
            if (LivingPopulation >= CitizenSimulationRules.MaximumPopulation) reasons.Add("Population limit");
            return new GrowthHouseholdObservation(h.Id.Value.ToString(CultureInfo.InvariantCulture), readiness.HasDwellingCapacity, reasons.AsReadOnly());
        }).ToArray();
        return new(LivingPopulation, citizens.Count(c => c.ParentAId is not null), DeadPopulation,
            citizens.Count(c => c.IsAlive && c.AgeYears(CurrentMinute) >= 18 && c.PartnerId is null),
            citizens.Count(c => c.IsAlive && c.HomeStructureId is null), GrowthSystemsEnabled(SimulationRulesVersion) ? GrowthFoodReserveTarget : CitizenSimulationRules.FoodTarget,
            citizens.Where(c => !c.IsAlive).GroupBy(c => c.DeathCause ?? "unknown").OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal), Array.AsReadOnly(households));
    }
}

public static class GrowthKinship
{
    public static bool AreCloseKin(CitizenId first, CitizenId second, IReadOnlyDictionary<long, Citizen> citizens)
    {
        ArgumentNullException.ThrowIfNull(citizens);
        if (first == second) return true;
        var left = Ancestors(first, citizens, int.MaxValue);
        var right = Ancestors(second, citizens, int.MaxValue);
        if (left.Contains(second.Value) || right.Contains(first.Value)) return true;
        return Ancestors(first, citizens, 2).Overlaps(Ancestors(second, citizens, 2));
    }

    private static HashSet<long> Ancestors(CitizenId start, IReadOnlyDictionary<long, Citizen> citizens, int maximumDepth)
    {
        var found = new HashSet<long>();
        var pending = new Queue<(long Id, int Depth)>();
        pending.Enqueue((start.Value, 0));
        while (pending.TryDequeue(out var item))
        {
            if (item.Depth >= maximumDepth || !citizens.TryGetValue(item.Id, out var citizen)) continue;
            foreach (var parent in new[] { citizen.ParentAId, citizen.ParentBId })
                if (parent is { } id && found.Add(id.Value)) pending.Enqueue((id.Value, item.Depth + 1));
        }
        return found;
    }
}
