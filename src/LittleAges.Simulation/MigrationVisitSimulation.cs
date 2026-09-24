using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private const int MigrationVisitProvisionDays = 14;
    private const int MigrationVisitDwellMinutes = WorldCalendar.MinutesPerHour;

    private void EvaluateMigrationVisits()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) ||
            _migrationState is not { DaughterSettlement: not null } state ||
            CurrentMinute.Value % WorldCalendar.MinutesPerYear != 0)
            return;

        var year = CurrentMinute.ToCalendar().Year;
        if (state.LastVisitAttemptYear is { } attemptedYear && attemptedYear >= year) return;

        state = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            state.InTransitParties, state.FoundingPressure, state.LastRelocations, year);
        _migrationState = state;
        if (state.InTransitParties.Count != 0) return;

        var citizenSites = state.CitizenResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var householdSites = state.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        foreach (var visitor in _citizens.Values.Where(x => x.IsAlive && x.AgeYears(CurrentMinute) >= 18)
                     .OrderBy(x => x.Id.Value))
        {
            if (!citizenSites.TryGetValue(visitor.Id.Value, out var originSite) ||
                visitor.HouseholdId is not { } householdId ||
                !householdSites.TryGetValue(householdId.Value, out var householdSite) || householdSite != originSite ||
                !_households.TryGetValue(householdId.Value, out var household) || household.DissolvedMinute is not null ||
                HasMigrationWorkOrBarterObligations(householdId.Value, [visitor]))
                continue;

            foreach (var relative in _citizens.Values.Where(x => x.IsAlive && x.Id != visitor.Id &&
                         AreMigrationVisitRelatives(visitor, x) && citizenSites.TryGetValue(x.Id.Value, out var site) && site != originSite)
                     .OrderBy(x => x.Id.Value))
            {
                var destinationSiteId = citizenSites[relative.Id.Value];
                var destinationSite = SiteLocation(destinationSiteId);
                var originSiteLocation = SiteLocation(originSite);
                var outboundPath = FindPathCached(visitor.Location, destinationSite);
                var returnPath = FindPathCached(destinationSite, originSiteLocation);
                if (outboundPath is not { Count: >= 2 } || returnPath is not { Count: >= 2 }) continue;

                var provisions = FoundingProvisionFood(CurrentMinute.Value);
                if (provisions > int.MaxValue || !TryWithdrawFoundingFood(householdId.Value, originSite, provisions))
                    continue;

                var cargo = new[]
                {
                    new MigrationCargoStackState(_counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Food,
                        provisions, MigrationCargoPurpose.Provisions)
                };
                var party = new MigrationTransitPartyState(_counters.AllocateMigrationPartyId(), householdId.Value,
                    originSite, destinationSiteId, visitor.Location, destinationSite, [visitor.Id.Value], cargo,
                    checked((int)SimulationEngine.RemainingPathCost(outboundPath, World)), CurrentMinute.Value,
                    journeyKind: MigrationJourneyKind.Visit, visitRelativeId: relative.Id.Value,
                    visitPhase: MigrationVisitPhase.Outbound);
                SetMigrationParty(party);
                EmitHistory(HistoricalEventType.FamilyVisitDeparted, HistoricalImportance.Personal, party.Location,
                    HistoricalEventPayloads.FamilyVisitDeparted(visitor.Id.Value, relative.Id.Value,
                        party.OriginSettlementId, destinationSiteId),
                    [(visitor.Id, "subject"), (relative.Id, "participant")]);
                StartFoundingTravel(visitor, destinationSite);
                return;
            }
        }
    }

    private static bool AreMigrationVisitRelatives(Citizen first, Citizen second)
    {
        if (first.ParentAId == second.Id || first.ParentBId == second.Id ||
            second.ParentAId == first.Id || second.ParentBId == first.Id)
            return true;

        return first.ParentAId is { } parentA &&
               (parentA == second.ParentAId || parentA == second.ParentBId) ||
               first.ParentBId is { } parentB &&
               (parentB == second.ParentAId || parentB == second.ParentBId);
    }

    private bool HandleMigrationVisitArrival(Citizen visitor, MigrationTransitPartyState party)
    {
        if (party.JourneyKind != MigrationJourneyKind.Visit || party.VisitPhase is null) return false;
        if (party.VisitPhase == MigrationVisitPhase.Outbound)
        {
            if (VisitRelativeStillAtDestination(party)) BeginMigrationVisitDwell(visitor, party);
            else BeginMigrationVisitReturn(visitor, party);
            return true;
        }

        if (party.VisitPhase == MigrationVisitPhase.Returning && party.Returning &&
            visitor.Location == SiteLocation(party.OriginSettlementId))
        {
            var remaining = party.Cargo.Where(x => x.Good == MigrationCargoGood.Food &&
                x.Purpose == MigrationCargoPurpose.Provisions).Sum(x => x.Quantity);
            if (remaining > 0)
                SettlementFor(party.OriginSettlementId).FoodStored = checked(
                    SettlementFor(party.OriginSettlementId).FoodStored + checked((int)remaining));
            if (party.VisitRelativeId is { } relativeId)
            {
                var destinationId = FamilyVisitDestinationForHistory(visitor.Id.Value, relativeId,
                    party.OriginSettlementId,
                    party.DestinationSettlementId ?? party.OriginSettlementId);
                EmitHistory(HistoricalEventType.FamilyVisitReturned, HistoricalImportance.Personal,
                    SiteLocation(party.OriginSettlementId),
                    HistoricalEventPayloads.FamilyVisitReturned(visitor.Id.Value, relativeId,
                        party.OriginSettlementId, destinationId),
                    [(visitor.Id, "subject"), (new CitizenId(relativeId), "participant")]);
            }
            FinishFoundingTravel(visitor);
            RemoveMigrationParty(party);
            return true;
        }

        return false;
    }

    private bool VisitRelativeStillAtDestination(MigrationTransitPartyState party)
    {
        if (party.VisitRelativeId is not { } relativeId || party.DestinationSettlementId is not { } destinationId ||
            !_citizens.TryGetValue(relativeId, out var relative) || !relative.IsAlive)
            return false;
        return SiteIdForCitizen(relative) == destinationId;
    }

    private void BeginMigrationVisitDwell(Citizen visitor, MigrationTransitPartyState party)
    {
        var endMinute = CurrentMinute.Add(MigrationVisitDwellMinutes).Value;
        var dwelling = new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
            party.DestinationSettlementId, visitor.Location, party.DestinationSite, party.CitizenIds, party.Cargo,
            0, party.DepartedMinute, journeyKind: MigrationJourneyKind.Visit,
            visitRelativeId: party.VisitRelativeId, visitPhase: MigrationVisitPhase.Dwell,
            visitDwellEndsMinute: endMinute);
        SetMigrationParty(dwelling);
        BeginMigrationVisitDwellWait(visitor, endMinute);
    }

    private void BeginMigrationVisitDwellWait(Citizen visitor, long endMinute)
    {
        var previousSequence = visitor.ActionSequence;
        foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck &&
                     IsReservedCitizenEventFor(x, visitor.Id.Value)).ToArray())
            _scheduledEvents.Remove(item);
        _activePaths.Remove((visitor.Id.Value, previousSequence));
        FinishAction(visitor);
        visitor.ActionSequence = checked(previousSequence + 1);
        visitor.CurrentAction = CitizenAction.Idle;
        visitor.ActionPhase = CitizenActionPhase.Perform;
        visitor.ActionTarget = null;
        visitor.ActionStartedMinute = CurrentMinute;
        visitor.ActionCompletesMinute = new WorldMinute(endMinute);
        ScheduleCitizen(visitor, CitizenEventNames.ActionComplete, visitor.ActionCompletesMinute.Value,
            CitizenEventNames.CompletionPriority);
    }

    private bool TryCompleteMigrationVisitDwell(Citizen visitor)
    {
        var party = FoundingPartyForCitizen(visitor.Id.Value);
        if (party is not { JourneyKind: MigrationJourneyKind.Visit, VisitPhase: MigrationVisitPhase.Dwell } ||
            visitor.CurrentAction != CitizenAction.Idle || visitor.ActionPhase != CitizenActionPhase.Perform ||
            party.VisitDwellEndsMinute is not { } endMinute || CurrentMinute.Value < endMinute)
            return false;

        BeginMigrationVisitReturn(visitor, party);
        return true;
    }

    private void BeginMigrationVisitReturn(Citizen visitor, MigrationTransitPartyState party)
    {
        var originSite = SiteLocation(party.OriginSettlementId);
        var returnPath = FindPathCached(visitor.Location, originSite);
        if (returnPath is not { Count: >= 2 })
        {
            AbortMigrationVisit(visitor, party);
            return;
        }

        var returning = new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
            party.OriginSettlementId, visitor.Location, originSite, party.CitizenIds, party.Cargo,
            checked((int)SimulationEngine.RemainingPathCost(returnPath, World)), party.DepartedMinute,
            returning: true, journeyKind: MigrationJourneyKind.Visit, visitRelativeId: party.VisitRelativeId,
            visitPhase: MigrationVisitPhase.Returning);
        SetMigrationParty(returning);
        StartFoundingTravel(visitor, originSite);
    }

    private void HandleMigrationVisitAfterSurvival(Citizen visitor, MigrationTransitPartyState party)
    {
        UpdateFoundingPartyPosition(visitor);
        party = FoundingPartyForCitizen(visitor.Id.Value) ?? party;
        if (!visitor.IsAlive)
        {
            RecoverFoundingCargoAtPartyLocation(party);
            RemoveMigrationParty(party);
            return;
        }

        if (party.VisitPhase is MigrationVisitPhase.Outbound or MigrationVisitPhase.Returning &&
            FindPathCached(visitor.Location, party.DestinationSite) is not { Count: >= 2 } &&
            visitor.Location != party.DestinationSite)
            AbortMigrationVisit(visitor, party);
    }

    private bool TryPauseMigrationVisitForMeal(Citizen visitor)
    {
        var party = FoundingPartyForCitizen(visitor.Id.Value);
        if (party is not { JourneyKind: MigrationJourneyKind.Visit, VisitPhase: MigrationVisitPhase.Dwell } ||
            visitor.CurrentAction != CitizenAction.Idle || visitor.ActionPhase != CitizenActionPhase.Perform ||
            party.VisitDwellEndsMinute is not { } dwellEnd || CurrentMinute.Value >= dwellEnd ||
            !MigrationPartyHasProvision(party, 1))
            return false;

        var needs = visitor.GetProjectedNeeds(CurrentMinute);
        if (needs.Hunger < 8500) return false;
        visitor.Needs = needs;
        visitor.NeedsUpdatedMinute = CurrentMinute.Value;
        var previousSequence = visitor.ActionSequence;
        foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck &&
                     IsReservedCitizenEventFor(x, visitor.Id.Value)).ToArray())
            _scheduledEvents.Remove(item);
        _activePaths.Remove((visitor.Id.Value, previousSequence));
        visitor.ActionSequence = checked(previousSequence + 1);
        visitor.CurrentAction = CitizenAction.Eat;
        visitor.ActionPhase = CitizenActionPhase.Perform;
        visitor.ActionTarget = null;
        visitor.ActionStartedMinute = CurrentMinute;
        visitor.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.EatDurationMinutes);
        ScheduleCitizen(visitor, CitizenEventNames.ActionComplete, visitor.ActionCompletesMinute.Value,
            CitizenEventNames.CompletionPriority);
        return true;
    }

    private void ResumeMigrationVisit(Citizen visitor, MigrationTransitPartyState party)
    {
        visitor.Needs = visitor.GetProjectedNeeds(CurrentMinute);
        visitor.NeedsUpdatedMinute = CurrentMinute.Value;
        if (party.VisitPhase == MigrationVisitPhase.Dwell && party.VisitDwellEndsMinute is { } endMinute)
        {
            if (CurrentMinute.Value >= endMinute) BeginMigrationVisitReturn(visitor, party);
            else BeginMigrationVisitDwellWait(visitor, endMinute);
            return;
        }

        if (visitor.Location == party.DestinationSite)
        {
            HandleMigrationVisitArrival(visitor, party);
            return;
        }
        if (FindPathCached(visitor.Location, party.DestinationSite) is not { Count: >= 2 })
        {
            AbortMigrationVisit(visitor, party);
            return;
        }
        visitor.ActionSequence = checked(visitor.ActionSequence + 1);
        BeginTravel(visitor, CitizenAction.Explore, party.DestinationSite, null);
    }

    private bool AbortMigrationVisitForUnavailableRoute(Citizen visitor)
    {
        var party = FoundingPartyForCitizen(visitor.Id.Value);
        if (party is not { JourneyKind: MigrationJourneyKind.Visit }) return false;
        AbortMigrationVisit(visitor, party);
        return true;
    }

    private void AbortMigrationVisit(Citizen visitor, MigrationTransitPartyState party)
    {
        RecoverFoundingCargoAtPartyLocation(party);
        RemoveMigrationParty(party);
        if (visitor.IsAlive) FinishFoundingTravel(visitor);
    }
}
