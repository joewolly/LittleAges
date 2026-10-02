using System.Globalization;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed record NewcomerReadSnapshot(
    string CitizenId, NewcomerPhase Phase, string HostSettlementId, string ShelterStructureId, TileCoordinate EntryTile,
    long FirstSeenMinute, long? VisitingStartedMinute, long? StayDeadlineMinute, long? JoinedMinute, long? DepartedMinute,
    long? DeathMinute, int ProvisionsRemaining, CitizenReadSnapshot? Person, IReadOnlyList<RelationshipState> Contacts);

public sealed partial class SimulationEngine
{
    /// <summary>Copies guest observations without adding outsiders to the resident simulation roster.</summary>
    public IReadOnlyList<NewcomerReadSnapshot> CreateNewcomerObservations() => Array.AsReadOnly(
        (_living?.Newcomers?.Visitors ?? []).OrderBy(x => x.CitizenId).Select(episode =>
        {
            CitizenReadSnapshot? person = null;
            if (episode.Person is { } guest)
            {
                var observedMinute = new WorldMinute(episode.DepartedMinute ?? episode.DeathMinute ?? CurrentMinute.Value);
                person = CreateCitizenSnapshot(guest) with
                {
                    Age = guest.AgeYears(observedMinute), LifeStage = guest.LifeStage(observedMinute),
                    ProjectedNeeds = guest.IsAlive ? guest.GetProjectedNeeds(observedMinute) : guest.Needs,
                    MovementPlan = CreateNewcomerMovementPlan(episode),
                };
            }
            return new NewcomerReadSnapshot(episode.CitizenId.ToString(CultureInfo.InvariantCulture), episode.Phase,
                episode.HostSettlementId.ToString(CultureInfo.InvariantCulture), episode.ShelterStructureId.ToString(CultureInfo.InvariantCulture),
                episode.EntryTile, episode.ArrivalMinute, episode.VisitingStartedMinute, episode.StayDeadlineMinute, episode.JoinedMinute,
                episode.DepartedMinute, episode.DeathMinute, episode.ProvisionsRemaining, person,
                Array.AsReadOnly(episode.Contacts.OrderBy(x => x.CitizenAId.Value).ThenBy(x => x.CitizenBId.Value).ToArray()));
        }).ToArray());

    private CitizenMovementPlanSnapshot? CreateNewcomerMovementPlan(NewcomerState episode)
    {
        if (episode.Person is not { IsAlive: true } guest || episode.Phase is not (NewcomerPhase.Approaching or NewcomerPhase.Leaving)
            || episode.NextStepMinute is not { } next || next <= CurrentMinute.Value) return null;
        var route = episode.Route.Skip(episode.RouteIndex).ToArray();
        if (route.Length < 2 || route[0] != guest.Location) return null;
        var waypoints = new List<CitizenMovementWaypointSnapshot> { new(route[0], CurrentMinute) };
        for (var index = 1; index < route.Length; index++)
        {
            if (index > 1) next = checked(next + TravelStepCost(route[index - 1], route[index]));
            waypoints.Add(new(route[index], new WorldMinute(next)));
        }
        return new(guest.ActionSequence, CurrentMinute, waypoints, new WorldMinute(episode.SegmentStartedMinute));
    }
}
