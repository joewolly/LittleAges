using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Headless;

/// <summary>Observed newcomer outcomes and housing/resources are evidence, separate from mandatory invariants.</summary>
public sealed record HeadlessNewcomerSummary(
    int Appeared, int Guests, int Joined, int Departed, int GuestDeaths, int LivingExternalResidents,
    long InitialProvisions, long RemainingProvisions, long ConsumedProvisions, long ImportedProvisions,
    long ExportedProvisions, long LostProvisions, IReadOnlyDictionary<string, int> ResidentDeathsByCause,
    IReadOnlyList<HeadlessNewcomerSiteSummary> Settlements)
{
    internal static HeadlessNewcomerSummary Build(SimulationPersistenceSnapshot snapshot)
    {
        var visitors = ResidentPopulationHistory.Read(snapshot)?.Visitors ?? [];
        var residents = snapshot.Citizens.ToDictionary(x => x.Id.Value);
        var migration = MigrationValidation.CreateReadSnapshot(snapshot);
        var sites = migration.Settlements.Select(site =>
        {
            var living = site.CitizenIds.Select(id => residents[id]).Where(x => x.IsAlive).ToArray();
            var structures = snapshot.Structures.Where(x => site.StructureIds.Contains(x.Id.Value)).ToArray();
            var shelters = structures.Count(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete);
            return new HeadlessNewcomerSiteSummary(site.Id, living.Length, living.Count(x => x.HomeStructureId is not null),
                living.Count(x => x.HomeStructureId is null), shelters * CitizenSimulationRules.ShelterCapacityPerBuilding,
                visitors.Count(x => x.HostSettlementId == site.Id && x.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting or NewcomerPhase.Leaving),
                structures.Count(x => x.Status == StructureStatus.UnderConstruction), site.CommunalStock.FoodStored,
                site.CommunalStock.WoodStored, site.CommunalStock.StoneStored, site.StorageCapacity);
        }).ToArray();
        return new(visitors.Count,
            visitors.Count(x => x.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting or NewcomerPhase.Leaving),
            visitors.Count(x => x.JoinedMinute is not null), visitors.Count(x => x.Phase == NewcomerPhase.Departed),
            visitors.Count(x => x.Phase == NewcomerPhase.Dead && x.JoinedMinute is null),
            visitors.Count(x => x.JoinedMinute is not null && residents.TryGetValue(x.CitizenId, out var person) && person.IsAlive),
            visitors.Sum(x => (long)x.InitialProvisions), visitors.Sum(x => (long)x.ProvisionsRemaining),
            visitors.Sum(x => (long)x.ProvisionsConsumed), visitors.Sum(x => (long)x.ProvisionsTransferred),
            visitors.Sum(x => (long)x.ProvisionsExported), visitors.Sum(x => (long)x.ProvisionsLost),
            residents.Values.Where(x => !x.IsAlive).GroupBy(x => x.DeathCause ?? "Unknown").OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal), sites);
    }
}

public sealed record HeadlessNewcomerSiteSummary(long SettlementId, int Residents, int HousedResidents, int UnhousedResidents,
    int ShelterCapacity, int Guests, int ConstructionProjects, int Food, int Wood, int Stone, int StorageCapacity);
