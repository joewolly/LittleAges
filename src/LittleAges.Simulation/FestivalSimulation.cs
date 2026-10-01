using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private bool FestivalsEnabled => FestivalSystemsEnabled(SimulationRulesVersion);
    private int FestivalReservedAt(long siteId) => _living?.Festivals?.Where(x => x.SettlementId == siteId).Sum(x => x.ReservedFood) ?? 0;
    private FestivalState? ActiveFestival(long siteId) => _living?.Festivals?.FirstOrDefault(x => x.SettlementId == siteId && x.Started && !x.Finished && CurrentMinute.Value >= x.StartMinute && CurrentMinute.Value < x.EndMinute);

    private bool FestivalEligible(Citizen citizen)
    {
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        var person = LivingPerson(citizen);
        return citizen.IsAlive && citizen.AgeYears(CurrentMinute) >= 6 && needs.Hunger < 3500 && needs.Rest < 5000 && person.Injury < 7000 && person.Illness < 7000;
    }

    private void AdvanceFestivals()
    {
        if (!FestivalsEnabled) return;
        _living!.Festivals ??= [];
        var year = CurrentMinute.ToCalendar().Year;
        foreach (var siteId in MigrationSettlementIds)
        {
            var festival = _living.Festivals.FirstOrDefault(x => x.SettlementId == siteId);
            if (festival is null || festival.Finished && festival.Year < year)
            {
                var scheduledYear = FestivalRules.Start(siteId, year) <= CurrentMinute.Value ? year + 1 : year;
                // At the exact opening minute a newly founded site can participate.
                if (FestivalRules.Start(siteId, year) == CurrentMinute.Value) scheduledYear = year;
                festival = new FestivalState { SettlementId = siteId, Year = scheduledYear, Location = SiteLocation(siteId), StartMinute = FestivalRules.Start(siteId, scheduledYear), EndMinute = FestivalRules.Start(siteId, scheduledYear) + FestivalRules.DurationMinutes };
                _living.Festivals.RemoveAll(x => x.SettlementId == siteId);
                _living.Festivals.Add(festival);
                _living.Festivals.Sort((a, b) => a.SettlementId.CompareTo(b.SettlementId));
            }
            if (!festival.Started && CurrentMinute.Value == festival.StartMinute && PopulationAt(siteId) > 0)
                OpenFestival(festival);
            if (!festival.Started && CurrentMinute.Value >= festival.EndMinute) festival.Finished = true;
            if (festival.Started && !festival.Finished)
            {
                ObserveFestivalVisitor(festival);
                if (CurrentMinute.Value >= festival.EndMinute) CloseFestival(festival);
                else
                    foreach (var citizen in CitizensAt(siteId).Where(x => x.IsAlive && x.AgeYears(CurrentMinute) >= 6 && FoundingPartyForCitizen(x.Id.Value) is null).OrderBy(x => x.Id.Value))
                        RequestLiving(LivingWorkKind.AttendFestival, festival.Location, 10000, citizen.Id.Value, work: FestivalRules.AttendanceMinutes, settlementId: siteId);
            }
        }
        foreach (var party in _migrationState?.InTransitParties.Where(x => x.JourneyKind == MigrationJourneyKind.Visit && x.VisitPhase == MigrationVisitPhase.Dwell).ToArray() ?? [])
            if (_citizens.TryGetValue(party.CitizenIds[0], out var visitor) && visitor.IsAlive)
            {
                if (TryPauseMigrationVisitForMeal(visitor)) continue;
                TryPauseFestivalVisitForRest(visitor, party);
            }
        EvaluateMigrationVisits();
    }

    private void TryPauseFestivalVisitForRest(Citizen visitor, MigrationTransitPartyState party)
    {
        if (party.JourneyKind != MigrationJourneyKind.Visit || visitor.CurrentAction != CitizenAction.Idle || party.VisitDwellEndsMinute <= CurrentMinute.Value || visitor.GetProjectedNeeds(CurrentMinute).Rest < 5000) return;
        foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck && IsReservedCitizenEventFor(x, visitor.Id.Value)).ToArray()) _scheduledEvents.Remove(item);
        visitor.Needs = visitor.GetProjectedNeeds(CurrentMinute);
        visitor.NeedsUpdatedMinute = CurrentMinute.Value;
        visitor.ActionSequence = checked(visitor.ActionSequence + 1);
        visitor.CurrentAction = CitizenAction.Rest;
        visitor.ActionPhase = CitizenActionPhase.Perform;
        visitor.ActionStartedMinute = CurrentMinute;
        visitor.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.RestDurationMinutes);
        ScheduleCitizen(visitor, CitizenEventNames.ActionComplete, visitor.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }

    private void OpenFestival(FestivalState festival)
    {
        festival.Started = true;
        var attendees = CitizensAt(festival.SettlementId).Count(x => FestivalEligible(x) && FoundingPartyForCitizen(x.Id.Value) is null);
        if (_living!.FestivalVisit is { } visit && visit.SettlementId == festival.SettlementId && visit.StartMinute == festival.StartMinute && visit.PartyId is not null) attendees++;
        var food = checked(attendees * CitizenSimulationRules.MealFoodUnits);
        var stock = SettlementFor(festival.SettlementId);
        if (attendees > 0 && (long)stock.FoodStored - food >= (long)PopulationAt(festival.SettlementId) * 20)
        {
            festival.Mode = FestivalMode.Feast;
            festival.InitialFood = festival.ReservedFood = food;
            stock.FoodStored -= food;
        }
        EmitHistory(HistoricalEventType.FestivalStarted, HistoricalImportance.Notable, festival.Location,
            HistoricalEventPayloads.Festival(festival.SettlementId, festival.Year, festival.Mode, 0, 0));
    }

    private void CloseFestival(FestivalState festival)
    {
        foreach (var order in OrdersAt(festival.SettlementId).Where(x => x.Kind == LivingWorkKind.AttendFestival).ToArray())
        {
            if (order.CitizenId is { } id && _citizens.TryGetValue(id, out var citizen))
            {
                if (citizen.ActionPhase == CitizenActionPhase.Perform)
                    AddFestivalAttendance(festival, citizen, checked((int)Math.Max(0, CurrentMinute.Value - (citizen.ActionStartedMinute?.Value ?? CurrentMinute.Value))));
                foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck && IsReservedCitizenEventFor(x, id)).ToArray()) _scheduledEvents.Remove(item);
                _activePaths.Remove((id, citizen.ActionSequence));
                citizen.ActionSequence = checked(citizen.ActionSequence + 1);
                CancelLivingOrder(order);
                EndLivingAction(citizen);
            }
            else CancelLivingOrder(order);
        }
        SettlementFor(festival.SettlementId).FoodStored = checked(SettlementFor(festival.SettlementId).FoodStored + festival.ReservedFood);
        festival.ReservedFood = 0;
        festival.Finished = true;
        EmitHistory(HistoricalEventType.FestivalEnded, HistoricalImportance.Notable, festival.Location,
            HistoricalEventPayloads.Festival(festival.SettlementId, festival.Year, festival.Mode, festival.Attendance.Count(x => x.BenefitsGranted), festival.ConsumedFood));
    }

    private void CompleteFestivalShift(Citizen citizen, LivingWorkOrder order)
    {
        var festival = ActiveFestival(SiteIdForOrder(order));
        if (festival is null) { CancelLivingOrder(order); EndLivingAction(citizen); return; }
        AddFestivalAttendance(festival, citizen, checked((int)(CurrentMinute.Value - (citizen.ActionStartedMinute?.Value ?? CurrentMinute.Value))));
        order.WorkDone = Math.Min(order.RequiredWork, festival.Attendance.FirstOrDefault(x => x.CitizenId == citizen.Id.Value)?.Minutes ?? 0);
        if (!FestivalEligible(citizen)) { EndLivingAction(citizen); return; }
        ScheduleLivingShift(citizen, checked((int)Math.Min(30, festival.EndMinute - CurrentMinute.Value)));
    }

    private void AddFestivalAttendance(FestivalState festival, Citizen citizen, int elapsed)
    {
        if (!citizen.IsAlive || citizen.Location != festival.Location || elapsed <= 0) return;
        var attendance = festival.Attendance.FirstOrDefault(x => x.CitizenId == citizen.Id.Value);
        if (attendance is null)
        {
            attendance = new FestivalAttendance { CitizenId = citizen.Id.Value };
            festival.Attendance.Add(attendance);
            festival.Attendance.Sort((a, b) => a.CitizenId.CompareTo(b.CitizenId));
        }
        attendance.Minutes = Math.Min(FestivalRules.DurationMinutes, attendance.Minutes + elapsed);
        if (attendance.Minutes < FestivalRules.AttendanceMinutes) return;
        if (!attendance.BenefitsGranted)
        {
            attendance.BenefitsGranted = true;
            var person = LivingPerson(citizen);
            person.Stress = Math.Max(0, person.Stress - 1000);
            person.LastLeisureMinute = CurrentMinute.Value;
            Experience(person, LivingExperienceKind.Festival);
            citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute) with { Social = Math.Max(0, citizen.GetProjectedNeeds(CurrentMinute).Social - 1500) };
            if (festival.ReservedFood >= CitizenSimulationRules.MealFoodUnits)
            {
                attendance.PortionConsumed = true;
                festival.ReservedFood -= CitizenSimulationRules.MealFoodUnits;
                festival.ConsumedFood += CitizenSimulationRules.MealFoodUnits;
                _foodConsumed = checked(_foodConsumed + CitizenSimulationRules.MealFoodUnits);
                _historyState!.FoodConsumedSinceSample = checked(_historyState.FoodConsumedSinceSample + CitizenSimulationRules.MealFoodUnits);
                citizen.Needs = citizen.Needs with { Hunger = Math.Max(0, citizen.Needs.Hunger - CitizenSimulationRules.FullHungerReduction) };
            }
            citizen.NeedsUpdatedMinute = CurrentMinute.Value;
            EmitHistory(HistoricalEventType.FestivalAttended, HistoricalImportance.Personal, festival.Location,
                HistoricalEventPayloads.Festival(festival.SettlementId, festival.Year, festival.Mode, 1, attendance.PortionConsumed ? CitizenSimulationRules.MealFoodUnits : 0), [(citizen.Id, "subject")]);
        }
        if (attendance.RelationshipGranted) return;
        var other = festival.Attendance.Where(x => x.CitizenId != citizen.Id.Value && x.BenefitsGranted)
            .Select(x => _citizens[x.CitizenId]).Where(x => x.IsAlive && x.Location == citizen.Location && FestivalCitizenPresent(x, festival))
            .OrderByDescending(x => AreMigrationVisitRelatives(citizen, x) || citizen.PartnerId == x.Id).ThenBy(x => Distance(x.Location, citizen.Location)).ThenBy(x => x.Id.Value).FirstOrDefault();
        if (other is null) return;
        attendance.RelationshipGranted = true;
        var pair = new FestivalPair(Math.Min(citizen.Id.Value, other.Id.Value), Math.Max(citizen.Id.Value, other.Id.Value));
        if (festival.StrengthenedPairs.Contains(pair)) return;
        festival.StrengthenedPairs.Add(pair);
        festival.StrengthenedPairs.Sort((a, b) => a.FirstId != b.FirstId ? a.FirstId.CompareTo(b.FirstId) : a.SecondId.CompareTo(b.SecondId));
        StrengthenLivingRelationship(citizen, other, false);
    }

    private bool FestivalCitizenPresent(Citizen citizen, FestivalState festival) => citizen.Location == festival.Location &&
        (OrderFor(citizen) is { Kind: LivingWorkKind.AttendFestival, Phase: LivingWorkPhase.Work } ||
         FoundingPartyForCitizen(citizen.Id.Value) is { JourneyKind: MigrationJourneyKind.Visit, VisitPhase: MigrationVisitPhase.Dwell, DestinationSettlementId: var destination } && destination == festival.SettlementId && citizen.CurrentAction == CitizenAction.Idle);

    private void ObserveFestivalVisitor(FestivalState festival)
    {
        var visit = _living!.FestivalVisit;
        if (visit is null || visit.SettlementId != festival.SettlementId || visit.StartMinute != festival.StartMinute) return;
        if (_citizens.TryGetValue(visit.CitizenId, out var visitor) && FestivalCitizenPresent(visitor, festival) && FestivalEligible(visitor))
            AddFestivalAttendance(festival, visitor, checked((int)Math.Max(0, Math.Min(CurrentMinute.Value, festival.EndMinute) - Math.Max(visit.LastAttendanceMinute, festival.StartMinute))));
        visit.LastAttendanceMinute = CurrentMinute.Value;
    }
}
