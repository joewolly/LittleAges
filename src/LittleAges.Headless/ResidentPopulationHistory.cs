using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Headless;

/// <summary>Residence starts at admission for external adults, rather than at their off-map birth.</summary>
internal static class ResidentPopulationHistory
{
    internal static NewcomersWorldState? Read(SimulationPersistenceSnapshot snapshot) =>
        SimulationEngine.NewcomersSystemsEnabled(snapshot.SimulationRulesVersion) && snapshot.LivingStateJson is not null
            ? LivingWorldCodec.Deserialize(snapshot.LivingStateJson).Newcomers : null;

    internal static IReadOnlyDictionary<long, long> Introductions(NewcomersWorldState? newcomers) =>
        (newcomers?.Visitors ?? []).Where(x => x.JoinedMinute is not null)
            .ToDictionary(x => x.CitizenId, x => x.JoinedMinute!.Value);

    internal static long StartMinute(Citizen citizen, IReadOnlyDictionary<long, long> introductions) =>
        introductions.TryGetValue(citizen.Id.Value, out var joined) ? joined : citizen.BirthMinute;

    internal static int AtMinute(IEnumerable<Citizen> residents, NewcomersWorldState? newcomers, long minute)
    {
        var introductions = Introductions(newcomers);
        return residents.Count(x => StartMinute(x, introductions) <= minute && (x.DeathMinute is null || x.DeathMinute > minute));
    }
}
