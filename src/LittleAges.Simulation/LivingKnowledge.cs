using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private void PlanLivingLearning(Citizen citizen, LivingPerson person)
    {
        if (citizen.AgeYears(CurrentMinute) < 6 || CurrentMinute.Value - person.LastTeachingMinute < WorldCalendar.MinutesPerDay) return;
        var missing = Enum.GetValues<LivingTechnique>().FirstOrDefault(x => !person.Knowledge.Contains(x) && SettlementKnows(x));
        if (missing != 0) RequestLiving(LivingWorkKind.Teach, citizen.Location, 2000, citizen.Id.Value, missing, 240);
    }

    private bool DiscoveryReady(Citizen citizen, LivingTechnique technique)
    {
        var person = LivingPerson(citizen);
        if (person.Knowledge.Contains(technique)) return false;
        var practiced = person.Practice + citizen.Traits.Curiosity / 1000;
        return technique switch
        {
            LivingTechnique.Cultivation => practiced >= 8 && citizen.Skills.Foraging > 0,
            LivingTechnique.Toolmaking => practiced >= 10 && citizen.Skills.Stoneworking + citizen.Skills.Woodcutting > 0,
            LivingTechnique.Preservation => practiced >= 12 && _living!.FoodHarvested > 0,
            LivingTechnique.Textiles => practiced >= 14 && Good(LivingGood.Fiber) > 0,
            LivingTechnique.Care => practiced >= 16 && _living!.People.Any(x => !x.DeathObserved && x.Injury + x.Illness > 0),
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
