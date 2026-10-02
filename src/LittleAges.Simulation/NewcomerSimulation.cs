using System.Globalization;
using System.Text.Json;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    public const string NewcomerStepEvent = "newcomer.step.v1";
    public const int NewcomerStepPriority = 10;
    public const int MaximumVisitorStayMinutes = 7 * WorldCalendar.MinutesPerDay;
    private bool NewcomersEnabled => NewcomersSystemsEnabled(SimulationRulesVersion);
    private static bool ActiveGuest(NewcomerState visitor) => visitor.JoinedMinute is null && visitor.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting or NewcomerPhase.Leaving;
    private int GuestShelterReservations(long shelterId) => !NewcomersEnabled ? 0 : _living?.Newcomers?.Visitors.Count(x => x.ShelterStructureId == shelterId && x.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting) ?? 0;
    private ulong NewcomerRandom(long yearOrId, ulong purpose, ulong sequence = 0) => new DeterministicRandom(Seed).NextUInt64(RandomDomain.Newcomers, (ulong)yearOrId, purpose, sequence);

    private void AdvanceNewcomers()
    {
        if (!NewcomersEnabled) return;
        var state = _living!.Newcomers ??= new();
        foreach (var visitor in state.Visitors.Where(x => x.JoinedMinute is not null && x.Phase == NewcomerPhase.Resident))
            if (_citizens.TryGetValue(visitor.CitizenId, out var citizen) && !citizen.IsAlive) { visitor.Phase = NewcomerPhase.Dead; visitor.DeathMinute = citizen.DeathMinute; }
        var calendar = CurrentMinute.ToCalendar();
        // One keyed opportunity each summer, after the village has had five full years.
        if (calendar.Year <= 5 || calendar.Month != 4 || calendar.Day != 1 || state.LastAttemptYear >= calendar.Year) return;
        state.LastAttemptYear = calendar.Year;
        if (state.Visitors.Any(ActiveGuest) || _citizens.Values.All(x => !x.IsAlive) || NewcomerRandom(calendar.Year, 1) % 4 != 0) return;
        TryCreateNewcomer(calendar.Year);
    }

    private bool TryCreateNewcomer(long year)
    {
        if (!NewcomersEnabled || _living!.Newcomers!.Visitors.Any(ActiveGuest)) return false;
        var candidates = new List<(long Site, Structure Shelter, TileCoordinate Edge, ulong Rank)>();
        foreach (var site in MigrationSettlementIds.Where(x => PopulationAt(x) > 0))
        foreach (var shelter in StructuresAt(site).Where(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete &&
                     CitizensAt(site).Count(c => c.IsAlive && c.HomeStructureId == x.Id) + GuestShelterReservations(x.Id.Value) < CitizenSimulationRules.ShelterCapacityPerBuilding).OrderBy(x => x.Id.Value).Take(1))
        {
            var costs = GetTravelCostsCached(shelter.Location);
            foreach (var tile in World.Tiles.Where(x => x.Walkable && (x.Coordinate.X == 0 || x.Coordinate.Y == 0 || x.Coordinate.X == World.Width - 1 || x.Coordinate.Y == World.Height - 1)).OrderBy(x => x.Coordinate))
            {
                if (tile.Coordinate == shelter.Location || !costs.TryGetValue(tile.Coordinate, out var cost) || cost > 2L * WorldCalendar.MinutesPerDay) continue;
                candidates.Add((site, shelter, tile.Coordinate, NewcomerRandom(year, 2, (ulong)shelter.Id.Value ^ ((ulong)(uint)tile.Coordinate.X << 32) ^ (uint)tile.Coordinate.Y)));
            }
        }
        if (candidates.Count == 0) return false;
        var choice = candidates.OrderBy(x => x.Rank).ThenBy(x => x.Site).ThenBy(x => x.Shelter.Id.Value).ThenBy(x => x.Edge).First();
        var route = FindPathCached(choice.Edge, choice.Shelter.Location);
        if (route is not { Count: >= 2 } || TravelPathCost(route) > 2L * WorldCalendar.MinutesPerDay) return false;
        var id = _counters.AllocateCitizenId();
        int Trait(ulong purpose) => (int)(NewcomerRandom(id.Value, purpose) % 10001);
        int Skill(ulong purpose) => 500 + (int)(NewcomerRandom(id.Value, purpose) % 5501);
        string[] names = ["Ari", "Bea", "Cora", "Dane", "Evan", "Fara", "Galen", "Hana"];
        string[] families = ["Ash", "Birch", "Cliff", "Fern", "Hill", "Lake", "Rowan", "Willow"];
        var age = 18 + (long)(NewcomerRandom(id.Value, 3) % 23);
        var person = new Citizen(id, (int?)null, names[NewcomerRandom(id.Value, 4) % (ulong)names.Length], families[NewcomerRandom(id.Value, 5) % (ulong)families.Length],
            checked(CurrentMinute.Value - age * WorldCalendar.MinutesPerYear), choice.Edge,
            new(Trait(10), Trait(11), Trait(12), Trait(13), Trait(14), Trait(15)), new(Skill(20), Skill(21), Skill(22), Skill(23), Skill(24), Skill(25)))
        { NeedsUpdatedMinute = CurrentMinute.Value, HealthUpdatedMinute = CurrentMinute.Value };
        // Covers the bounded approach, seven-day stay, and return even at the winter hunger rate.
        const int provisions = 120;
        var visitor = new NewcomerState { CitizenId = id.Value, HostSettlementId = choice.Site, ShelterStructureId = choice.Shelter.Id.Value, EntryTile = choice.Edge,
            ArrivalMinute = CurrentMinute.Value, Person = person, InitialProvisions = provisions, ProvisionsRemaining = provisions };
        _living.Newcomers.Visitors.Add(visitor);
        EmitHistory(HistoricalEventType.NewcomerAppeared, HistoricalImportance.Routine, person.Location, HistoricalEventPayloads.NewcomerAppeared(id.Value, choice.Site));
        BeginNewcomerRoute(visitor, route);
        ScheduleNewcomer(visitor);
        return true;
    }

    private void BeginNewcomerRoute(NewcomerState visitor, IReadOnlyList<TileCoordinate> route)
    {
        var person = visitor.Person!;
        visitor.Route = route.ToList(); visitor.RouteIndex = 0; visitor.SegmentStartedMinute = CurrentMinute.Value;
        visitor.NextStepMinute = route.Count > 1 ? checked(CurrentMinute.Value + TravelStepCost(route[0], route[1])) : null;
        person.CurrentAction = CitizenAction.Wander; person.ActionPhase = CitizenActionPhase.TravelToTarget; person.ActionTarget = route[^1];
        person.TargetCitizenId = null; person.ActionStartedMinute = CurrentMinute;
        person.ActionCompletesMinute = new(checked(CurrentMinute.Value + TravelPathCost(route)));
    }

    private void ScheduleNewcomer(NewcomerState visitor)
    {
        if (!ActiveGuest(visitor)) return;
        var person = visitor.Person!;
        var due = checked(person.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes);
        if (visitor.NextStepMinute is { } movement) due = Math.Min(due, movement);
        if (person.ActionCompletesMinute is { } action) due = Math.Min(due, action.Value);
        if (visitor.Phase == NewcomerPhase.Visiting && visitor.StayDeadlineMinute is { } deadline)
        {
            var path = FindPathCached(person.Location, visitor.EntryTile);
            if (path is not null) due = Math.Min(due, deadline - TravelPathCost(path) - WorldCalendar.MinutesPerDay);
        }
        due = Math.Max(CurrentMinute.Value + 1, due);
        person.ActionSequence = checked(person.ActionSequence + 1);
        var sequence = _counters.AllocateScheduledEventSequence();
        AddPending(new(sequence), new(new(due), NewcomerStepPriority, visitor.CitizenId, sequence), NewcomerStepEvent,
            $"{{\"citizenId\":\"{visitor.CitizenId.ToString(CultureInfo.InvariantCulture)}\",\"actionSequence\":{person.ActionSequence.ToString(CultureInfo.InvariantCulture)}}}");
    }

    private void ProcessNewcomerStep(PendingEvent item)
    {
        using var payload = JsonDocument.Parse(item.PayloadJson);
        var id = long.Parse(payload.RootElement.GetProperty("citizenId").GetString()!, CultureInfo.InvariantCulture);
        var visitor = _living!.Newcomers!.Visitors.FirstOrDefault(x => x.CitizenId == id);
        if (visitor is null || !ActiveGuest(visitor) || visitor.Person!.ActionSequence != payload.RootElement.GetProperty("actionSequence").GetInt64()) return;
        var person = visitor.Person;
        person.Needs = person.GetProjectedNeeds(CurrentMinute); person.NeedsUpdatedMinute = CurrentMinute.Value;
        if (visitor.Phase == NewcomerPhase.Visiting) person.Needs = person.Needs with { Shelter = 0 };
        if (CurrentMinute.Value - person.HealthUpdatedMinute >= CitizenSimulationRules.SurvivalCheckIntervalMinutes)
        {
            var hunger = person.Needs.Hunger >= CitizenSimulationRules.StarvationThreshold;
            var rest = person.Needs.Rest >= CitizenSimulationRules.ExhaustionThreshold;
            var exposure = visitor.Phase != NewcomerPhase.Visiting && person.Needs.Shelter >= CitizenSimulationRules.ShelterCriticalThreshold;
            var damage = (hunger ? CitizenSimulationRules.StarvationDamagePerCheck : 0) + (rest ? CitizenSimulationRules.ExhaustionDamagePerCheck : 0) + (exposure ? CitizenSimulationRules.ExposureDamagePerCheck : 0);
            person.Health = Math.Clamp(person.Health - damage * (10000 - person.Traits.Resilience / 4) / 10000 + (damage == 0 ? CitizenSimulationRules.HealthRecoveryPerCheck : 0), 0, 10000);
            person.HealthUpdatedMinute = CurrentMinute.Value;
            if (person.Health == 0) { DieNewcomer(visitor, hunger && rest ? "deprivation" : hunger ? "starvation" : rest ? "exhaustion" : "exposure"); return; }
        }
        if (visitor.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting && !GuestHostSupported(visitor)) BeginNewcomerExit(visitor, PopulationAt(visitor.HostSettlementId) == 0 ? "extinction" : "lost_support");
        if (!ActiveGuest(visitor)) return;
        if (visitor.Phase == NewcomerPhase.Visiting && visitor.StayDeadlineMinute is { } limit && FindPathCached(person.Location, visitor.EntryTile) is { } returnPath &&
            CurrentMinute.Value + TravelPathCost(returnPath) + WorldCalendar.MinutesPerDay >= limit) BeginNewcomerExit(visitor, "deadline");
        if (visitor.NextStepMinute is { } nextStep && nextStep <= CurrentMinute.Value)
        {
            var previous = person.Location; visitor.RouteIndex++; person.Location = visitor.Route[visitor.RouteIndex];
            person.LifetimeMovementSteps++; person.LifetimeMovementCost = checked(person.LifetimeMovementCost + TravelStepCost(previous, person.Location));
            visitor.SegmentStartedMinute = CurrentMinute.Value;
            visitor.NextStepMinute = visitor.RouteIndex + 1 < visitor.Route.Count ? checked(CurrentMinute.Value + TravelStepCost(person.Location, visitor.Route[visitor.RouteIndex + 1])) : null;
            if (visitor.NextStepMinute is null)
            {
                if (visitor.Phase == NewcomerPhase.Leaving) { DepartNewcomer(visitor); return; }
                visitor.Phase = NewcomerPhase.Visiting; visitor.VisitingStartedMinute = CurrentMinute.Value; visitor.StayDeadlineMinute = CurrentMinute.Value + MaximumVisitorStayMinutes;
                person.Needs = person.Needs with { Shelter = 0 };
                EmitHistory(HistoricalEventType.VisitorArrived, HistoricalImportance.Personal, person.Location, HistoricalEventPayloads.VisitorArrived(id, visitor.HostSettlementId));
                SetGuestAction(person, CitizenAction.Idle, 60);
            }
        }
        else if (visitor.NextStepMinute is null && person.ActionCompletesMinute?.Value <= CurrentMinute.Value)
        {
            if (person.CurrentAction == CitizenAction.Eat)
            {
                var consumed = Math.Min(CitizenSimulationRules.MealFoodUnits, visitor.ProvisionsRemaining); visitor.ProvisionsRemaining -= consumed; visitor.ProvisionsConsumed += consumed;
                person.Needs = person.Needs with { Hunger = Math.Max(0, person.Needs.Hunger - CitizenSimulationRules.FullHungerReduction * consumed / CitizenSimulationRules.MealFoodUnits) };
            }
            else if (person.CurrentAction == CitizenAction.Rest) person.Needs = person.Needs with { Rest = Math.Max(0, person.Needs.Rest - CitizenSimulationRules.RestNeedReduction), Shelter = visitor.Phase == NewcomerPhase.Visiting ? 0 : person.Needs.Shelter };
            else if (person.CurrentAction == CitizenAction.Socialize) CompleteGuestContact(visitor);
            if (visitor.Phase == NewcomerPhase.Visiting && TryJoinNewcomer(visitor)) return;
            if (visitor.Phase is NewcomerPhase.Approaching or NewcomerPhase.Leaving)
            {
                var destination = visitor.Phase == NewcomerPhase.Leaving ? visitor.EntryTile : _structures[visitor.ShelterStructureId].Location;
                if (FindPathCached(person.Location, destination) is { Count: >= 2 } resume) BeginNewcomerRoute(visitor, resume);
            }
            else SetGuestAction(person, CitizenAction.Idle, 60);
        }
        if (person.CurrentAction is CitizenAction.Idle or CitizenAction.Wander)
        {
            if (person.Needs.Hunger >= 3500 && visitor.ProvisionsRemaining > 0) PauseGuestRoute(visitor, CitizenAction.Eat, CitizenSimulationRules.EatDurationMinutes);
            else if (person.Needs.Rest >= 4000) PauseGuestRoute(visitor, CitizenAction.Rest, CitizenSimulationRules.RestDurationMinutes);
            else if (visitor.Phase == NewcomerPhase.Visiting)
            {
                var target = CitizensAt(visitor.HostSettlementId).Where(x => x.IsAlive && FoundingPartyForCitizen(x.Id.Value) is null && ChebyshevDistance(x.Location, person.Location) <= CitizenSimulationRules.SocialRadius)
                    .OrderBy(x => visitor.Contacts.FirstOrDefault(r => r.CitizenAId == x.Id || r.CitizenBId == x.Id)?.LastInteractionMinute ?? -1).ThenBy(x => x.Id.Value).FirstOrDefault();
                if (target is not null) { SetGuestAction(person, CitizenAction.Socialize, CitizenSimulationRules.SocializeDurationMinutes); person.TargetCitizenId = target.Id; }
            }
        }
        ScheduleNewcomer(visitor);
    }

    private static void ClearGuestAction(Citizen person)
    { person.CurrentAction = CitizenAction.None; person.ActionPhase = CitizenActionPhase.None; person.ActionTarget = null; person.TargetCitizenId = null; person.ActionStartedMinute = null; person.ActionCompletesMinute = null; }
    private void SetGuestAction(Citizen person, CitizenAction action, int duration)
    { ClearGuestAction(person); person.CurrentAction = action; person.ActionPhase = CitizenActionPhase.Perform; person.ActionStartedMinute = CurrentMinute; person.ActionCompletesMinute = CurrentMinute.Add(duration); }
    private void PauseGuestRoute(NewcomerState visitor, CitizenAction action, int duration)
    { visitor.NextStepMinute = null; SetGuestAction(visitor.Person!, action, duration); }
    private bool GuestHostSupported(NewcomerState visitor) => PopulationAt(visitor.HostSettlementId) > 0 && _structures.TryGetValue(visitor.ShelterStructureId, out var shelter) && shelter.Type == StructureType.Shelter && shelter.Status == StructureStatus.Complete &&
        CitizensAt(visitor.HostSettlementId).Count(x => x.IsAlive && x.HomeStructureId == shelter.Id) + GuestShelterReservations(shelter.Id.Value) <= CitizenSimulationRules.ShelterCapacityPerBuilding;

    private void CompleteGuestContact(NewcomerState visitor)
    {
        var person = visitor.Person!;
        if (visitor.Phase != NewcomerPhase.Visiting) return;
        if (person.TargetCitizenId is not { } targetId || !_citizens.TryGetValue(targetId.Value, out var target) || !target.IsAlive || SiteIdForCitizen(target) != visitor.HostSettlementId || ChebyshevDistance(person.Location, target.Location) > CitizenSimulationRules.SocialRadius) return;
        var pair = RelationshipState.Normalize(person.Id, target.Id);
        var previous = visitor.Contacts.FirstOrDefault(x => x.CitizenAId == pair.A && x.CitizenBId == pair.B);
        var count = (previous?.InteractionCount ?? 0) + 1;
        var negative = NewcomerRandom(person.Id.Value, (ulong)target.Id.Value, (ulong)count) % 10000 < (ulong)(500 + (10000 - (person.Traits.Cooperativeness + target.Traits.Cooperativeness) / 2) / 10);
        var next = new RelationshipState(pair.A, pair.B, Math.Min(10000, (previous?.Familiarity ?? 0) + (negative ? 350 : 500)), Math.Clamp((previous?.Affinity ?? 0) + (negative ? -500 : 400), -10000, 10000),
            Math.Clamp((previous?.Trust ?? 0) + (negative ? -250 : 300), 0, 10000), Math.Clamp((previous?.Conflict ?? 0) + (negative ? 600 : -150), 0, 10000), CurrentMinute.Value, count);
        if (previous is not null) visitor.Contacts.Remove(previous); visitor.Contacts.Add(next); visitor.Contacts.Sort((a, b) => a.CitizenAId.Value != b.CitizenAId.Value ? a.CitizenAId.Value.CompareTo(b.CitizenAId.Value) : a.CitizenBId.Value.CompareTo(b.CitizenBId.Value));
        person.Needs = person.Needs with { Social = Math.Max(0, person.Needs.Social - 5000) };
        target.Needs = target.GetProjectedNeeds(CurrentMinute) with { Social = Math.Max(0, target.GetProjectedNeeds(CurrentMinute).Social - 2500) }; target.NeedsUpdatedMinute = CurrentMinute.Value;
        EmitHistory(HistoricalEventType.VisitorContact, HistoricalImportance.Routine, person.Location, HistoricalEventPayloads.VisitorContact(person.Id.Value, visitor.HostSettlementId, target.Id.Value, next.Familiarity, next.Affinity, next.Trust, next.Conflict, count));
    }

    private bool TryJoinNewcomer(NewcomerState visitor)
    {
        var person = visitor.Person!;
        if (visitor.Phase != NewcomerPhase.Visiting || !GuestHostSupported(visitor) || person.Location != _structures[visitor.ShelterStructureId].Location || CurrentMinute.Value - visitor.VisitingStartedMinute < WorldCalendar.MinutesPerDay || visitor.Contacts.Count == 0) return false;
        var strongest = visitor.Contacts.Where(r => _citizens.TryGetValue(r.CitizenAId == person.Id ? r.CitizenBId.Value : r.CitizenAId.Value, out var other) && other.IsAlive && SiteIdForCitizen(other) == visitor.HostSettlementId)
            .OrderByDescending(r => r.Affinity + r.Trust - r.Conflict).ThenBy(r => r.CitizenAId.Value).ThenBy(r => r.CitizenBId.Value).FirstOrDefault();
        if (strongest is null || strongest.Familiarity < 2000 || strongest.Affinity < 1500 || strongest.Trust < 1000 || strongest.Conflict >= 2000 ||
            person.Traits.Cooperativeness + person.Traits.Sociability + strongest.Affinity + strongest.Trust - strongest.Conflict < 11000) return false;
        var site = visitor.HostSettlementId;
        if (PopulationAt(site) >= CitizenSimulationRules.MaximumPopulation || SettlementFor(site).FoodStored < 20L * (PopulationAt(site) + 1) || LivingFreeStorageAt(site) < visitor.ProvisionsRemaining) return false;
        var household = new Household(_counters.AllocateHouseholdId(), CurrentMinute.Value) { DwellingStructureId = new(visitor.ShelterStructureId) };
        ClearGuestAction(person); person.HouseholdId = household.Id; person.HomeStructureId = household.DwellingStructureId;
        _citizens.Add(person.Id.Value, person); _households.Add(household.Id.Value, household);
        RecordMigrationOwner(MigrationEntityKind.Citizen, person.Id.Value, site); RecordMigrationOwner(MigrationEntityKind.Household, household.Id.Value, site);
        visitor.ProvisionsTransferred = visitor.ProvisionsRemaining; visitor.ProvisionsRemaining = 0; visitor.JoinedMinute = CurrentMinute.Value; visitor.Phase = NewcomerPhase.Resident; visitor.Person = null; visitor.NextStepMinute = null;
        ReconcileEconomicHouseholds(); AddPrivate(household.Id.Value, ResourceType.Food, visitor.ProvisionsTransferred); UpdateOccupations();
        SynchronizeLivingPeople();
        // Native births can receive later IDs while this visitor is outside the roster.
        _living!.People.Sort((first, second) => first.CitizenId.CompareTo(second.CitizenId));
        if (person.Skills.Woodcutting + person.Skills.Stoneworking >= 9000) Learn(LivingPerson(person), LivingTechnique.Toolmaking);
        EmitHistory(HistoricalEventType.NewcomerJoined, HistoricalImportance.Notable, person.Location, HistoricalEventPayloads.NewcomerJoined(person.Id.Value, site, household.Id.Value, visitor.ShelterStructureId, visitor.ProvisionsTransferred), [(person.Id, "subject")]);
        RecordPopulationMilestones();
        foreach (var relationship in visitor.Contacts) { _relationships[(relationship.CitizenAId.Value, relationship.CitizenBId.Value)] = relationship; RecordSocialInteractionHistory(null, relationship); }
        // Admission changes ownership, not the person's last authoritative survival boundary.
        ScheduleCitizen(person, CitizenEventNames.SurvivalCheck, new WorldMinute(checked(person.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes)), CitizenEventNames.SurvivalPriority);
        ScheduleCitizen(person, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
        return true;
    }

    private void BeginNewcomerExit(NewcomerState visitor, string reason)
    {
        if (visitor.Phase == NewcomerPhase.Leaving) return;
        visitor.Phase = NewcomerPhase.Leaving;
        visitor.ExitReason = reason;
        // Geography is immutable; the validated inbound route always has a physical reverse route.
        var path = FindPathCached(visitor.Person!.Location, visitor.EntryTile);
        if (path is { Count: >= 2 }) BeginNewcomerRoute(visitor, path);
        else if (visitor.Person.Location == visitor.EntryTile) DepartNewcomer(visitor);
    }
    private void ReleaseGuestShelterForResidents(long shelterId)
    {
        if (!NewcomersEnabled || _living?.Newcomers is not { } state) return;
        foreach (var visitor in state.Visitors.Where(x => x.ShelterStructureId == shelterId && x.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting))
        {
            // Native housing reconciliation can release support between the guest's own events.
            foreach (var pending in _scheduledEvents.Where(x => x.Name == NewcomerStepEvent && x.Order.EntitySortKey == visitor.CitizenId).ToArray()) _scheduledEvents.Remove(pending);
            var person = visitor.Person!;
            person.Needs = person.GetProjectedNeeds(CurrentMinute); person.NeedsUpdatedMinute = CurrentMinute.Value;
            BeginNewcomerExit(visitor, "lost_support");
            if (ActiveGuest(visitor)) ScheduleNewcomer(visitor);
        }
    }
    private void DepartNewcomer(NewcomerState visitor)
    {
        var person = visitor.Person!; person.Needs = person.GetProjectedNeeds(CurrentMinute); person.NeedsUpdatedMinute = CurrentMinute.Value;
        ClearGuestAction(person); visitor.Phase = NewcomerPhase.Departed; visitor.DepartedMinute = CurrentMinute.Value; visitor.NextStepMinute = null;
        visitor.ProvisionsExported = visitor.ProvisionsRemaining; visitor.ProvisionsRemaining = 0;
        EmitHistory(HistoricalEventType.VisitorDeparted, HistoricalImportance.Routine, person.Location, HistoricalEventPayloads.VisitorDeparted(person.Id.Value, visitor.HostSettlementId, visitor.ExitReason ?? "deadline", visitor.ProvisionsExported));
    }
    private void DieNewcomer(NewcomerState visitor, string cause)
    {
        var person = visitor.Person!; ClearGuestAction(person); person.CurrentAction = CitizenAction.Dead; person.Health = 0; person.DeathMinute = CurrentMinute.Value; person.DeathCause = cause;
        visitor.Phase = NewcomerPhase.Dead; visitor.DeathMinute = CurrentMinute.Value; visitor.NextStepMinute = null; visitor.ProvisionsLost = visitor.ProvisionsRemaining; visitor.ProvisionsRemaining = 0;
        EmitHistory(HistoricalEventType.VisitorDied, HistoricalImportance.Notable, person.Location, HistoricalEventPayloads.VisitorDied(person.Id.Value, visitor.HostSettlementId, cause, person.AgeYears(CurrentMinute)));
    }
    private void ObserveNewcomerResidentDeath(Citizen citizen)
    {
        if (!NewcomersEnabled) return;
        var visitor = _living!.Newcomers?.Visitors.FirstOrDefault(x => x.CitizenId == citizen.Id.Value && x.JoinedMinute is not null);
        if (visitor is not null) { visitor.Phase = NewcomerPhase.Dead; visitor.DeathMinute = citizen.DeathMinute; }
    }
    private void CancelGuestContactsTargeting(CitizenId targetId)
    {
        if (!NewcomersEnabled || _living?.Newcomers is not { } state) return;
        foreach (var visitor in state.Visitors.Where(x => ActiveGuest(x) && x.Person!.CurrentAction == CitizenAction.Socialize && x.Person.TargetCitizenId == targetId))
        {
            foreach (var pending in _scheduledEvents.Where(x => x.Name == NewcomerStepEvent && x.Order.EntitySortKey == visitor.CitizenId).ToArray()) _scheduledEvents.Remove(pending);
            var person = visitor.Person!;
            person.Needs = person.GetProjectedNeeds(CurrentMinute); person.NeedsUpdatedMinute = CurrentMinute.Value;
            SetGuestAction(person, CitizenAction.Idle, CitizenSimulationRules.IdleMinimumMinutes);
            ScheduleNewcomer(visitor);
        }
    }
}
