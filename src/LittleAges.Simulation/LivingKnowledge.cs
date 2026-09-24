using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private void PlanLivingLearning(Citizen citizen, LivingPerson person)
    {
        if (citizen.AgeYears(CurrentMinute) < 6 || CurrentMinute.Value - person.LastTeachingMinute < WorldCalendar.MinutesPerDay) return;
        var settlementId = SiteIdForCitizen(citizen);
        var missing = Enum.GetValues<LivingTechnique>().FirstOrDefault(x => !person.Knowledge.Contains(x) && SettlementKnows(settlementId, x));
        if (missing != 0) RequestLiving(LivingWorkKind.Teach, citizen.Location, 2000, citizen.Id.Value, missing, 240, settlementId);
    }

    private bool DiscoveryReady(Citizen citizen, LivingTechnique technique)
    {
        var settlementId = SiteIdForCitizen(citizen);
        var person = LivingPerson(citizen);
        if (person.Knowledge.Contains(technique)) return false;
        var practiced = person.Practice + citizen.Traits.Curiosity / 1000;
        return technique switch
        {
            LivingTechnique.Cultivation => practiced >= 8 && citizen.Skills.Foraging > 0,
            LivingTechnique.Toolmaking => practiced >= 10 && citizen.Skills.Stoneworking + citizen.Skills.Woodcutting > 0,
            LivingTechnique.Preservation => practiced >= 12 &&
                (UnifiedSimulationRulesEnabled(SimulationRulesVersion)
                    ? _living!.FarmFoodHarvested > 0
                    : _living!.FoodHarvested > 0),
            LivingTechnique.Textiles => practiced >= 14 && Good(settlementId, LivingGood.Fiber) > 0,
            LivingTechnique.Care => practiced >= 16 && CitizensAt(settlementId).Any(x => _living!.People.Any(p => p.CitizenId == x.Id.Value && !p.DeathObserved && p.Injury + p.Illness > 0)),
            _ => false
        };
    }

    private void Learn(LivingPerson person, LivingTechnique technique)
    {
        if (person.Knowledge.Contains(technique)) return;
        person.Knowledge.Add(technique);
        person.Knowledge.Sort();
        Experience(person, LivingExperienceKind.Learned);
    }
}
