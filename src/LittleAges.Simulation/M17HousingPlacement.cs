using LittleAges.Domain;

namespace LittleAges.Simulation;

internal sealed record M17HousingPlacement(
    IReadOnlyDictionary<long, long?> HouseholdDwellings,
    IReadOnlyDictionary<long, long?> CitizenHomes,
    IReadOnlyDictionary<long, int> ShelterUsage,
    IReadOnlySet<long> RetainedHouseholds,
    IReadOnlySet<long> RetainedSoloCitizens);

internal static class M17HousingPlacementPlanner
{
    internal static M17HousingPlacement Plan(IEnumerable<Structure> structures,
        IEnumerable<Citizen> residents, IEnumerable<Household> households,
        long? incomingHouseholdId = null, int incomingMembers = 0)
    {
        var shelters = structures.Where(s => s.Status == StructureStatus.Complete && s.Type == StructureType.Shelter)
            .OrderBy(s => s.Id.Value).ToArray();
        var citizens = residents.Where(c => c.IsAlive).OrderBy(c => c.Id.Value).ToArray();
        var usage = shelters.ToDictionary(s => s.Id.Value, _ => 0);
        var homes = citizens.ToDictionary(c => c.Id.Value, _ => (long?)null);
        var groups = households.Where(h => h.DissolvedMinute is null && h.Id.Value != incomingHouseholdId)
            .Select(h => new Group(h.Id.Value, h.DwellingStructureId?.Value,
                citizens.Where(c => c.HouseholdId == h.Id).ToArray(), 0))
            .OrderBy(g => g.Id).ToList();
        if (incomingHouseholdId is { } incomingId)
            groups.Add(new Group(incomingId, null, [], incomingMembers));
        var dwellings = groups.ToDictionary(g => g.Id, _ => (long?)null);
        var retainedHouseholds = new HashSet<long>();
        var retainedSolo = new HashSet<long>();
        foreach (var group in groups.OrderBy(g => g.Id))
        {
            if (group.Dwelling is not { } home || !usage.TryGetValue(home, out var used) || group.Count == 0 ||
                group.Members.Any(c => c.HomeStructureId?.Value != home) ||
                used + group.Count > CitizenSimulationRules.ShelterCapacityPerBuilding)
                continue;
            Assign(group, home);
            retainedHouseholds.Add(group.Id);
        }
        var solo = citizens.Where(c => c.HouseholdId is null).ToArray();
        foreach (var citizen in solo)
        {
            if (citizen.HomeStructureId is not { } home || !usage.TryGetValue(home.Value, out var used) ||
                used >= CitizenSimulationRules.ShelterCapacityPerBuilding) continue;
            homes[citizen.Id.Value] = home.Value;
            usage[home.Value]++;
            retainedSolo.Add(citizen.Id.Value);
        }
        foreach (var group in groups.Where(g => dwellings[g.Id] is null)
                     .OrderByDescending(g => g.Count).ThenBy(g => g.Id))
        {
            var shelter = shelters.Where(s => CitizenSimulationRules.ShelterCapacityPerBuilding - usage[s.Id.Value] >= group.Count)
                .OrderBy(s => usage[s.Id.Value]).ThenBy(s => s.Id.Value).FirstOrDefault();
            if (shelter is not null) Assign(group, shelter.Id.Value);
        }
        foreach (var citizen in solo.Where(c => homes[c.Id.Value] is null))
        {
            var shelter = shelters.Where(s => usage[s.Id.Value] < CitizenSimulationRules.ShelterCapacityPerBuilding)
                .OrderBy(s => usage[s.Id.Value]).ThenBy(s => s.Id.Value).FirstOrDefault();
            if (shelter is null) break;
            homes[citizen.Id.Value] = shelter.Id.Value;
            usage[shelter.Id.Value]++;
        }
        return new M17HousingPlacement(dwellings, homes, usage, retainedHouseholds, retainedSolo);

        void Assign(Group group, long home)
        {
            dwellings[group.Id] = home;
            foreach (var citizen in group.Members) homes[citizen.Id.Value] = home;
            usage[home] += group.Count;
        }
    }

    private sealed record Group(long Id, long? Dwelling, Citizen[] Members, int HypotheticalMembers)
    {
        internal int Count => HypotheticalMembers == 0 ? Members.Length : HypotheticalMembers;
    }
}

public sealed partial class SimulationEngine
{
    private M17HousingPlacement PlanM17Housing(long settlementId, long? incomingHouseholdId = null, int incomingMembers = 0)
        => M17HousingPlacementPlanner.Plan(StructuresAt(settlementId), CitizensAt(settlementId), HouseholdsAt(settlementId),
            incomingHouseholdId, incomingMembers);

    private void ReconcileM17MigrationHouseholdsAndHousing()
    {
        foreach (var settlementId in MigrationSettlementIds)
        {
            var plan = PlanM17Housing(settlementId);
            var citizens = CitizensAt(settlementId).Where(c => c.IsAlive).ToArray();
            var groups = HouseholdsAt(settlementId).Where(h => h.DissolvedMinute is null)
                .Select(h => (Household: h, Members: citizens.Where(c => c.HouseholdId == h.Id).OrderBy(c => c.Id.Value).ToArray()))
                .OrderBy(g => g.Household.Id.Value).ToArray();
            // Keep the existing clear-before-assign effects on interrupted home travel.
            foreach (var group in groups.Where(g => !plan.RetainedHouseholds.Contains(g.Household.Id.Value)))
            {
                group.Household.DwellingStructureId = null;
                foreach (var citizen in group.Members) SetHomeStructure(citizen, null);
            }
            var solo = citizens.Where(c => c.HouseholdId is null).OrderBy(c => c.Id.Value).ToArray();
            foreach (var citizen in solo.Where(c => !plan.RetainedSoloCitizens.Contains(c.Id.Value)))
                SetHomeStructure(citizen, null);
            foreach (var group in groups.Where(g => !plan.RetainedHouseholds.Contains(g.Household.Id.Value))
                         .OrderByDescending(g => g.Members.Length).ThenBy(g => g.Household.Id.Value))
            {
                group.Household.DwellingStructureId = plan.HouseholdDwellings[group.Household.Id.Value] is { } home ? new StructureId(home) : null;
                if (group.Household.DwellingStructureId is not { } dwelling) continue;
                foreach (var citizen in group.Members) SetHomeStructure(citizen, dwelling);
            }
            foreach (var citizen in solo.Where(c => !plan.RetainedSoloCitizens.Contains(c.Id.Value)))
                SetHomeStructure(citizen, plan.CitizenHomes[citizen.Id.Value] is { } home ? new StructureId(home) : null);
            foreach (var (shelterId, used) in plan.ShelterUsage)
                if (used + GuestShelterReservations(shelterId) > CitizenSimulationRules.ShelterCapacityPerBuilding)
                    ReleaseGuestShelterForResidents(shelterId);
        }
    }
}
