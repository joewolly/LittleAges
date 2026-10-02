using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public static class NewcomerValidation
{
    public static void ValidateRetainedEpisodes(string? previousJson, string? nextJson, IReadOnlyList<Citizen>? residentCitizens = null)
    {
        var previous = previousJson is null ? null : LivingWorldCodec.Deserialize(previousJson).Newcomers;
        if (previous is null) return;
        var next = nextJson is null ? null : LivingWorldCodec.Deserialize(nextJson).Newcomers;
        Require(next is not null && next.LastAttemptYear >= previous.LastAttemptYear, "Retained newcomer opportunity state cannot disappear or go backward.");
        foreach (var old in previous.Visitors)
        {
            var current = next!.Visitors.SingleOrDefault(x => x.CitizenId == old.CitizenId);
            Require(current is not null && current.HostSettlementId == old.HostSettlementId && current.ShelterStructureId == old.ShelterStructureId && current.EntryTile == old.EntryTile &&
                current.ArrivalMinute == old.ArrivalMinute && current.InitialProvisions == old.InitialProvisions && (old.JoinedMinute is null || current.JoinedMinute == old.JoinedMinute) &&
                (old.VisitingStartedMinute is null || current.VisitingStartedMinute == old.VisitingStartedMinute) && (old.StayDeadlineMinute is null || current.StayDeadlineMinute == old.StayDeadlineMinute) &&
                current.ProvisionsConsumed >= old.ProvisionsConsumed && current.ProvisionsTransferred >= old.ProvisionsTransferred && current.ProvisionsExported >= old.ProvisionsExported && current.ProvisionsLost >= old.ProvisionsLost,
                "Retained newcomer identity and transition facts cannot be rewritten.");
            if (old.Phase == NewcomerPhase.Departed || old.Phase == NewcomerPhase.Dead && old.JoinedMinute is null)
                Require(JsonSerializer.Serialize(old) == JsonSerializer.Serialize(current), "A departed or dead guest archive is immutable.");
            Require(old.Contacts.All(contact => current!.Contacts.Any(x => x.CitizenAId == contact.CitizenAId && x.CitizenBId == contact.CitizenBId && x.InteractionCount >= contact.InteractionCount && x.LastInteractionMinute >= contact.LastInteractionMinute)), "Retained newcomer contact evidence cannot disappear.");
            if (old.Person is { } previousPerson)
            {
                var nextPerson = current!.Person ?? residentCitizens?.SingleOrDefault(x => x.Id.Value == old.CitizenId);
                Require(nextPerson is not null && nextPerson.Id == previousPerson.Id && nextPerson.GivenName == previousPerson.GivenName && nextPerson.FamilyName == previousPerson.FamilyName &&
                    nextPerson.BirthMinute == previousPerson.BirthMinute && nextPerson.Traits == previousPerson.Traits, "An external person's immutable biography cannot be rewritten during visiting or admission.");
            }
        }
    }

    public static void Validate(SimulationPersistenceSnapshot snapshot, LivingWorldState? living)
    {
        var state = living?.Newcomers;
        var pending = snapshot.ScheduledEvents.Where(x => x.Name == "newcomer.step.v1").ToArray();
        if (!SimulationEngine.NewcomersSystemsEnabled(snapshot.SimulationRulesVersion))
        {
            Require(state is null && pending.Length == 0 && snapshot.HistoricalEvents.All(x => x.EventType < HistoricalEventType.NewcomerAppeared), "Newcomer state and history require M17 rules.");
            return;
        }
        Require(state is not null && state.Version == 1 && state.LastAttemptYear >= -1 && state.LastAttemptYear <= snapshot.WorldMinute.ToCalendar().Year && state.Visitors is not null, "Newcomer state version or opportunity timing is invalid.");
        var visitors = state!.Visitors;
        Require(visitors.All(x => x is not null) && visitors.Select(x => x.CitizenId).SequenceEqual(visitors.Select(x => x.CitizenId).Distinct().Order()), "Newcomer identities must be unique and ordered.");
        Require(visitors.Count(x => x.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting or NewcomerPhase.Leaving) <= 1, "Only one external traveler may be active worldwide.");
        var citizens = snapshot.Citizens.ToDictionary(x => x.Id.Value);
        var residentIds = citizens.Keys.Concat(snapshot.Households.Select(x => x.Id.Value)).Concat(snapshot.Structures.Select(x => x.Id.Value)).ToHashSet();
        var world = snapshot.World!;
        var minute = snapshot.WorldMinute.Value;
        bool Walkable(TileCoordinate tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < world.Width && tile.Y < world.Height && world.GetTile(tile).Walkable;
        foreach (var visitor in visitors)
        {
            Require(visitor.CitizenId > 0 && visitor.CitizenId < snapshot.Counters.NextEntityId && Enum.IsDefined(visitor.Phase) &&
                (visitor.HostSettlementId == 1 || visitor.HostSettlementId == 2 && snapshot.MigrationState?.DaughterSettlement is not null), "Newcomer identity, phase, or host is invalid.");
            Require(Walkable(visitor.EntryTile) && (visitor.EntryTile.X == 0 || visitor.EntryTile.Y == 0 || visitor.EntryTile.X == world.Width - 1 || visitor.EntryTile.Y == world.Height - 1), "Newcomer entry must be a walkable world edge.");
            var shelter = snapshot.Structures.SingleOrDefault(x => x.Id.Value == visitor.ShelterStructureId);
            Require(shelter is { Type: StructureType.Shelter, Status: StructureStatus.Complete } &&
                (snapshot.MigrationState?.StructureOwners.FirstOrDefault(x => x.EntityId == visitor.ShelterStructureId)?.SettlementId ?? 1) == visitor.HostSettlementId, "Newcomer shelter reservation requires a completed shelter at its host.");
            Require(visitor.ArrivalMinute >= 0 && visitor.ArrivalMinute <= minute && visitor.SegmentStartedMinute >= visitor.ArrivalMinute && visitor.SegmentStartedMinute <= minute &&
                new[] { visitor.VisitingStartedMinute, visitor.JoinedMinute, visitor.DepartedMinute, visitor.DeathMinute }.All(x => x is null || x >= visitor.ArrivalMinute && x <= minute) &&
                (visitor.StayDeadlineMinute is null || visitor.VisitingStartedMinute is not null && visitor.StayDeadlineMinute > visitor.VisitingStartedMinute), "Newcomer transition timing is invalid.");
            visitor.ValidateProvisions();
            Require(visitor.ExitReason is null or "deadline" or "choice" or "lost_support" or "extinction", "Newcomer exit reason is invalid.");
            Require((visitor.Phase == NewcomerPhase.Departed) == (visitor.DepartedMinute is not null) &&
                (visitor.Phase != NewcomerPhase.Approaching || visitor.VisitingStartedMinute is null && visitor.StayDeadlineMinute is null) &&
                (visitor.Phase != NewcomerPhase.Visiting || visitor.VisitingStartedMinute is not null && visitor.StayDeadlineMinute is not null) &&
                (visitor.StayDeadlineMinute is null || visitor.StayDeadlineMinute == visitor.VisitingStartedMinute + SimulationEngine.MaximumVisitorStayMinutes), "Newcomer phase deadlines disagree with physical lifecycle.");
            var joined = visitor.JoinedMinute is not null;
            Require(joined == (visitor.Person is null) && (joined ? visitor.Phase is NewcomerPhase.Resident or NewcomerPhase.Dead : visitor.Phase != NewcomerPhase.Resident), "Guest biography ownership disagrees with admission.");
            var person = joined ? citizens.GetValueOrDefault(visitor.CitizenId) : visitor.Person;
            Require(person is not null && person.Id.Value == visitor.CitizenId && person.FounderOrdinal is null && person.ParentAId is null && person.ParentBId is null && person.AgeYears(new WorldMinute(visitor.ArrivalMinute)) >= 18, "Newcomer biography must be an adult external ancestry root.");
            if (joined)
            {
                Require(person!.BirthMinute <= visitor.ArrivalMinute && visitor.VisitingStartedMinute is not null && visitor.JoinedMinute >= visitor.VisitingStartedMinute &&
                    visitor.DepartedMinute is null && (visitor.Phase == NewcomerPhase.Dead) == !person.IsAlive && visitor.DeathMinute == person.DeathMinute, "Admitted newcomer biography or phase is invalid.");
                Require(visitor.JoinedMinute >= visitor.VisitingStartedMinute + WorldCalendar.MinutesPerDay && visitor.Contacts is not null && visitor.Contacts.Any(contact =>
                    contact.Familiarity >= 2000 && contact.Affinity >= 1500 && contact.Trust >= 1000 && contact.Conflict < 2000 &&
                    person.Traits.Cooperativeness + person.Traits.Sociability + contact.Affinity + contact.Trust - contact.Conflict >= 11000),
                    "Admission requires a full day of visiting and retained contact evidence meeting its affiliation and personality thresholds.");
            }
            else
            {
                Require(!residentIds.Contains(visitor.CitizenId) && person!.HouseholdId is null && person.PartnerId is null && person.HomeStructureId is null && person.TargetStructureId is null && person.TargetResourceNodeId is null &&
                    person.CarriedResourceQuantity == 0 && person.CarriedResourceType is null && person.NeedsUpdatedMinute <= minute && person.HealthUpdatedMinute <= minute &&
                    (person.CurrentAction is CitizenAction.None or CitizenAction.Wander or CitizenAction.Idle or CitizenAction.Eat or CitizenAction.Rest or CitizenAction.Socialize or CitizenAction.Dead), "External travelers cannot have resident ownership or production actions.");
                person.Validate(world);
                Require(person.BirthMinute <= visitor.ArrivalMinute && person.NeedsUpdatedMinute >= visitor.ArrivalMinute && person.HealthUpdatedMinute >= visitor.ArrivalMinute &&
                    (person.CurrentAction is CitizenAction.None or CitizenAction.Dead || person.ActionStartedMinute is { } started && started.Value >= visitor.ArrivalMinute && started.Value <= minute && person.ActionCompletesMinute is { } completes && completes.Value >= minute), "External biography action and need timestamps are invalid.");
                Require((visitor.Phase == NewcomerPhase.Dead) == !person.IsAlive && visitor.DeathMinute == person.DeathMinute && person.ActionSequence >= 0, "Guest death facts disagree with biography.");
                Require(visitor.Phase != NewcomerPhase.Departed || visitor.DepartedMinute is not null && person.IsAlive && person.CurrentAction == CitizenAction.None && visitor.ProvisionsRemaining == 0, "Departed travelers remain alive and inactive with exported provisions.");
                Require(visitor.Phase != NewcomerPhase.Dead || visitor.DeathMinute is not null && visitor.ProvisionsRemaining == 0, "Dead travelers cannot retain provisions.");
            }
            Require(visitor.Route is not null && visitor.Route.All(Walkable) && visitor.RouteIndex >= 0 && (visitor.Route.Count == 0 ? visitor.RouteIndex == 0 : visitor.RouteIndex < visitor.Route.Count) &&
                visitor.Route.Zip(visitor.Route.Skip(1)).All(pair => Math.Max(Math.Abs(pair.First.X - pair.Second.X), Math.Abs(pair.First.Y - pair.Second.Y)) == 1 &&
                    (pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y || Walkable(new(pair.First.X, pair.Second.Y)) && Walkable(new(pair.Second.X, pair.First.Y)))), "Newcomer physical route is invalid.");
            Require(visitor.Phase != NewcomerPhase.Approaching || visitor.Route.Count > 0 && visitor.Route[^1] == shelter!.Location, "Approaching route must lead to the reserved shelter.");
            Require(visitor.Phase != NewcomerPhase.Visiting || person!.Location == shelter!.Location && visitor.Route.Count > 0 && visitor.RouteIndex == visitor.Route.Count - 1 && visitor.Route[^1] == shelter.Location && visitor.NextStepMinute is null, "Visiting requires completed physical arrival at the reserved shelter.");
            Require(visitor.Phase != NewcomerPhase.Leaving || visitor.Route.Count > 0 && visitor.Route[^1] == visitor.EntryTile, "Leaving route must return to the observed world edge.");
            Require(visitor.Phase != NewcomerPhase.Departed || person!.Location == visitor.EntryTile && visitor.Route.Count > 0 && visitor.RouteIndex == visitor.Route.Count - 1 && visitor.Route[^1] == visitor.EntryTile, "Departure requires completed physical return to the world edge.");
            Require(visitor.Contacts is not null && visitor.Contacts.Select(x => (x.CitizenAId.Value, x.CitizenBId.Value)).SequenceEqual(visitor.Contacts.Select(x => (x.CitizenAId.Value, x.CitizenBId.Value)).Distinct().Order()), "Newcomer contacts must be unique and ordered.");
            foreach (var contact in visitor.Contacts)
            {
                contact.Validate();
                var other = contact.CitizenAId.Value == visitor.CitizenId ? contact.CitizenBId.Value : contact.CitizenAId.Value;
                Require((contact.CitizenAId.Value == visitor.CitizenId || contact.CitizenBId.Value == visitor.CitizenId) && citizens.ContainsKey(other) && contact.LastInteractionMinute >= visitor.ArrivalMinute && contact.LastInteractionMinute <= minute, "Newcomer contacts require factual known resident interactions.");
            }
            var episodeEvents = pending.Where(x => EventCitizen(x) == visitor.CitizenId).ToArray();
            var active = visitor.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting or NewcomerPhase.Leaving;
            Require(!active || visitor.Route.Count > 0 && visitor.Route[visitor.RouteIndex] == person!.Location, "Active traveler location must agree with persisted route progress.");
            Require(visitor.NextStepMinute is null || active && visitor.Phase is NewcomerPhase.Approaching or NewcomerPhase.Leaving && visitor.RouteIndex + 1 < visitor.Route.Count &&
                visitor.NextStepMinute > minute && visitor.NextStepMinute > visitor.SegmentStartedMinute, "Traveler movement deadline is invalid.");
            Require(active ? episodeEvents.Length == 1 : episodeEvents.Length == 0, "Active travelers require exactly one lifecycle event; archives require none.");
            if (active)
            {
                var scheduled = episodeEvents[0];
                var expectedPayload = $"{{\"citizenId\":\"{visitor.CitizenId.ToString(CultureInfo.InvariantCulture)}\",\"actionSequence\":{person!.ActionSequence.ToString(CultureInfo.InvariantCulture)}}}";
                Require(scheduled.PayloadJson == expectedPayload && scheduled.Order.Priority == 10 && scheduled.Order.EntitySortKey == visitor.CitizenId && scheduled.Order.DueWorldMinute.Value > minute &&
                    (visitor.NextStepMinute is null || scheduled.Order.DueWorldMinute.Value <= visitor.NextStepMinute), "Newcomer lifecycle event timing or stale-action guard is invalid.");
                if (visitor.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting)
                    Require(snapshot.Citizens.Count(x => x.IsAlive && x.HomeStructureId?.Value == visitor.ShelterStructureId) < CitizenSimulationRules.ShelterCapacityPerBuilding, "Guest shelter must retain a spare reserved bed.");
                if (person!.CurrentAction == CitizenAction.Socialize)
                    Require(visitor.Phase == NewcomerPhase.Visiting && person.TargetCitizenId is { } target && citizens.TryGetValue(target.Value, out var resident) && resident.IsAlive, "Guest social action requires a living resident target during its stay.");
            }
        }
        Require(pending.All(x => visitors.Any(v => v.CitizenId == EventCitizen(x))), "Newcomer event references an unknown episode.");
        ValidateHistory(snapshot, visitors);
    }

    private static long EventCitizen(ScheduledEventSnapshot item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            return long.Parse(document.RootElement.GetProperty("citizenId").GetString()!, CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new ArgumentException("Newcomer lifecycle payload is invalid.", nameof(item), error); }
    }

    private static void ValidateHistory(SimulationPersistenceSnapshot snapshot, List<NewcomerState> visitors)
    {
        foreach (var visitor in visitors)
        {
            var facts = snapshot.HistoricalEvents.Where(x => x.EventType >= HistoricalEventType.NewcomerAppeared && PayloadId(x, "citizenId") == visitor.CitizenId).ToArray();
            Require(facts.All(x => PayloadId(x, "hostSettlementId") == visitor.HostSettlementId), "Newcomer history host disagrees with its episode.");
            Require(facts.All(x => x.WorldMinute >= visitor.ArrivalMinute && (visitor.JoinedMinute is null || x.WorldMinute <= visitor.JoinedMinute) && (visitor.DepartedMinute is null || x.WorldMinute <= visitor.DepartedMinute)), "Newcomer factual history is outside its guest episode.");
            Require(facts.Count(x => x.EventType == HistoricalEventType.NewcomerAppeared && x.WorldMinute == visitor.ArrivalMinute) == 1 && facts.Count(x => x.EventType == HistoricalEventType.NewcomerAppeared) == 1, "Every external ancestry root needs exactly one appearance fact.");
            Require(facts.Single(x => x.EventType == HistoricalEventType.NewcomerAppeared).Location == visitor.EntryTile, "Newcomer appearance location disagrees with its world edge.");
            Require(facts.Count(x => x.EventType == HistoricalEventType.VisitorArrived) == (visitor.VisitingStartedMinute is null ? 0 : 1) && facts.Where(x => x.EventType == HistoricalEventType.VisitorArrived).All(x => x.WorldMinute == visitor.VisitingStartedMinute), "Visitor arrival history disagrees with physical arrival.");
            Require(facts.Where(x => x.EventType == HistoricalEventType.VisitorArrived).All(x => x.Location == snapshot.Structures.Single(s => s.Id.Value == visitor.ShelterStructureId).Location), "Visitor arrival facts must observe the reserved shelter.");
            Require(facts.Count(x => x.EventType == HistoricalEventType.NewcomerJoined) == (visitor.JoinedMinute is null ? 0 : 1) && facts.Where(x => x.EventType == HistoricalEventType.NewcomerJoined).All(x => x.WorldMinute == visitor.JoinedMinute && PayloadId(x, "shelterStructureId") == visitor.ShelterStructureId && PayloadInt(x, "provisionsTransferred") == visitor.ProvisionsTransferred), "Newcomer admission history disagrees with its ownership transfer.");
            Require(facts.Count(x => x.EventType == HistoricalEventType.VisitorDeparted) == (visitor.DepartedMinute is null ? 0 : 1) && facts.Where(x => x.EventType == HistoricalEventType.VisitorDeparted).All(x => x.WorldMinute == visitor.DepartedMinute && PayloadInt(x, "remainingProvisions") == visitor.ProvisionsExported), "Visitor departure history disagrees with exports.");
            Require(facts.Count(x => x.EventType == HistoricalEventType.VisitorDied) == (visitor.JoinedMinute is null && visitor.DeathMinute is not null ? 1 : 0) && facts.Where(x => x.EventType == HistoricalEventType.VisitorDied).All(x => x.WorldMinute == visitor.DeathMinute), "Guest death history disagrees with its archive.");
            foreach (var contact in visitor.Contacts)
            {
                var other = contact.CitizenAId.Value == visitor.CitizenId ? contact.CitizenBId.Value : contact.CitizenAId.Value;
                var interactions = facts.Where(x => x.EventType == HistoricalEventType.VisitorContact && PayloadId(x, "contactId") == other).ToArray();
                Require(interactions.LongLength == contact.InteractionCount && interactions.Select(x => PayloadLong(x, "interactionCount")).SequenceEqual(Enumerable.Range(1, interactions.Length).Select(x => (long)x)), "Guest contact history must prove every interaction once.");
                var last = interactions[^1];
                Require(last.WorldMinute == contact.LastInteractionMinute && PayloadInt(last, "familiarity") == contact.Familiarity && PayloadInt(last, "affinity") == contact.Affinity && PayloadInt(last, "trust") == contact.Trust && PayloadInt(last, "conflict") == contact.Conflict &&
                    visitor.VisitingStartedMinute is not null && last.WorldMinute >= visitor.VisitingStartedMinute, "Guest contact strengths disagree with factual history.");
            }
            Require(facts.Where(x => x.EventType == HistoricalEventType.VisitorContact).All(x => visitor.Contacts.Any(c => c.CitizenAId.Value == PayloadId(x, "contactId") || c.CitizenBId.Value == PayloadId(x, "contactId"))), "Guest contact facts require retained contact evidence.");
        }
        Require(snapshot.HistoricalEvents.Where(x => x.EventType >= HistoricalEventType.NewcomerAppeared).All(x => visitors.Any(v => v.CitizenId == PayloadId(x, "citizenId"))), "Newcomer history references an unknown episode.");
    }
    private static long PayloadId(HistoricalEvent item, string name) { using var document = JsonDocument.Parse(item.PayloadJson); return long.Parse(document.RootElement.GetProperty(name).GetString()!, CultureInfo.InvariantCulture); }
    private static int PayloadInt(HistoricalEvent item, string name) { using var document = JsonDocument.Parse(item.PayloadJson); return document.RootElement.GetProperty(name).GetInt32(); }
    private static long PayloadLong(HistoricalEvent item, string name) { using var document = JsonDocument.Parse(item.PayloadJson); return document.RootElement.GetProperty(name).GetInt64(); }
    private static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
