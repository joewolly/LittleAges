using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private const int FoundingStartYear = 5;
    private const int FoundingProvisionDays = 14;
    // Match the existing household food target while a daughter lacks food-only storage.
    private const int DaughterFoodReservePerResident = 120;
    private const int MinimumReachableFoundingFoodNodes = 2;
    private const int FoundingFoodAccessRadius = 6;
    private static readonly long MinutesPerMigrationSeason = (long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay;

    private void ObserveMigrationFoundingPressure()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) || _migrationState is not { DaughterSettlement: null } state)
            return;

        var prior = (state.FoundingPressure ?? Array.Empty<MigrationFoundingPressureState>())
            .ToDictionary(x => x.HouseholdId, x => x.SinceMinute);
        var noUsableLand = !HasLocalUsableLand();
        var pressure = new List<MigrationFoundingPressureState>();
        foreach (var household in _households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value))
        {
            var members = _citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
                .OrderBy(x => x.Id.Value).ToArray();
            if (members.Length == 0) continue;

            var hasHousingPressure = household.DwellingStructureId is not { } dwellingId ||
                !_structures.TryGetValue(dwellingId.Value, out var dwelling) ||
                dwelling.Type != StructureType.Shelter || dwelling.Status != StructureStatus.Complete ||
                SiteIdForStructure(dwellingId.Value) != 1 ||
                members.Length > CitizenSimulationRules.ShelterCapacityPerBuilding ||
                members.Any(x => x.HomeStructureId != dwellingId);
            if (!hasHousingPressure && !noUsableLand) continue;

            pressure.Add(new MigrationFoundingPressureState(household.Id.Value,
                prior.GetValueOrDefault(household.Id.Value, CurrentMinute.Value)));
        }

        _migrationState = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            state.InTransitParties, pressure, state.LastRelocations, state.LastVisitAttemptYear);
    }

    private void EvaluateMigrationFounding()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) || _migrationState is not { DaughterSettlement: null } state ||
            state.InTransitParties.Count != 0 || CurrentMinute.ToCalendar().Year < FoundingStartYear ||
            CurrentMinute.Value % MinutesPerMigrationSeason != 0)
            return;

        var pressuredSince = (state.FoundingPressure ?? Array.Empty<MigrationFoundingPressureState>())
            .ToDictionary(x => x.HouseholdId, x => x.SinceMinute);
        foreach (var household in _households.Values.Where(x => x.DissolvedMinute is null && SiteIdForHousehold(x.Id.Value) == 1)
                     .OrderBy(x => x.Id.Value))
        {
            var members = _citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
                .OrderBy(x => x.Id.Value).ToArray();
            var adults = members.Where(x => x.AgeYears(CurrentMinute) >= 18).ToArray();
            if (adults.Length < 2) continue;
            var curiousAdult = adults.Any(x => x.Traits.Curiosity > 7500);
            var hadFullSeasonPressure = pressuredSince.TryGetValue(household.Id.Value, out var since) &&
                since <= CurrentMinute.Value - MinutesPerMigrationSeason;
            if (!curiousAdult && !hadFullSeasonPressure) continue;

            var site = FindFoundingSite(members);
            if (site is not { } destination) continue;

            var provisionPerTraveler = FoundingProvisionFood(CurrentMinute.Value);
            var provisionQuantity = checked(provisionPerTraveler * members.Length);
            if (provisionQuantity > int.MaxValue || !TryWithdrawFoundingFood(household.Id.Value, provisionQuantity))
                continue;

            var representative = members[0];
            var path = FindPathCached(representative.Location, destination);
            if (path is null || path.Count < 2)
            {
                // The candidate selector checks every traveler before any stock is moved.
                throw new InvalidDataException("A selected founding site became unreachable during departure.");
            }

            var partyId = _counters.AllocateMigrationPartyId();
            var cargo = new[]
            {
                new MigrationCargoStackState(_counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Food,
                    provisionQuantity, MigrationCargoPurpose.Provisions)
            };
            var party = new MigrationTransitPartyState(partyId, household.Id.Value, 1, null,
                representative.Location, destination, members.Select(x => x.Id.Value).ToArray(), cargo,
                checked((int)TravelPathCost(path)), CurrentMinute.Value);
            SetMigrationParty(party);
            EmitHistory(HistoricalEventType.ExpeditionDeparted, HistoricalImportance.Notable, party.Location,
                HistoricalEventPayloads.ExpeditionDeparted(party.Id, party.HouseholdId, party.OriginSettlementId,
                    party.DestinationSite.X, party.DestinationSite.Y, party.CitizenIds.Count),
                party.CitizenIds.Select(id => (new CitizenId(id), "participant")));

            foreach (var member in members) StartFoundingTravel(member, destination);
            return;
        }
    }

    private TileCoordinate? FindFoundingSite(IReadOnlyList<Citizen> members)
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) || _migrationState?.DaughterSettlement is not null)
            return null;

        var originCosts = GetTravelCostsCached(World.StartingSite);
        var foodNodes = World.Resources.Where(x => x.Type == ResourceType.Food).OrderBy(x => x.Id.Value).ToArray();
        var farmTiles = AvailableFoundingTiles().Where(AgricultureRules.Suitable).ToArray();
        var candidates = AvailableFoundingTiles()
            .Where(x => originCosts.TryGetValue(x.Coordinate, out var cost) && cost >= 32)
            .Select(x => (x.Coordinate, Cost: originCosts[x.Coordinate],
                Distance: Math.Abs(x.Coordinate.X - World.StartingSite.X) + Math.Abs(x.Coordinate.Y - World.StartingSite.Y),
                NearbyFoodNodes: foodNodes.Count(node => Math.Abs(node.Coordinate.X - x.Coordinate.X) +
                    Math.Abs(node.Coordinate.Y - x.Coordinate.Y) <= FoundingFoodAccessRadius),
                NearbyFarmTiles: farmTiles.Count(tile => tile.Coordinate != x.Coordinate &&
                    Math.Abs(tile.Coordinate.X - x.Coordinate.X) + Math.Abs(tile.Coordinate.Y - x.Coordinate.Y) <= FoundingFoodAccessRadius)))
            .Where(x => x.NearbyFoodNodes >= MinimumReachableFoundingFoodNodes && x.NearbyFarmTiles > 0)
            .OrderBy(x => x.Cost).ThenBy(x => x.Distance)
            .ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X);
        foreach (var candidate in candidates)
        {
            var daughterCosts = GetTravelCostsCached(candidate.Coordinate);
            var ownedFoodNodes = foodNodes.Count(node =>
                Math.Abs(node.Coordinate.X - candidate.Coordinate.X) +
                    Math.Abs(node.Coordinate.Y - candidate.Coordinate.Y) <= FoundingFoodAccessRadius &&
                originCosts.TryGetValue(node.Coordinate, out var originCost) &&
                daughterCosts.TryGetValue(node.Coordinate, out var daughterCost) && daughterCost < originCost);
            if (ownedFoodNodes < MinimumReachableFoundingFoodNodes) continue;
            var hasOwnedFarmland = farmTiles.Any(tile => tile.Coordinate != candidate.Coordinate &&
                originCosts.TryGetValue(tile.Coordinate, out var originCost) &&
                daughterCosts.TryGetValue(tile.Coordinate, out var daughterCost) && daughterCost < originCost);
            if (!hasOwnedFarmland) continue;
            if (members.All(x => FindPathCached(x.Location, candidate.Coordinate) is { Count: >= 2 }))
                return candidate.Coordinate;
        }
        return null;
    }

    private bool HasLocalUsableLand()
    {
        var originCosts = GetTravelCostsCached(World.StartingSite);
        return AvailableFoundingTiles().Any(x => originCosts.TryGetValue(x.Coordinate, out var cost) && cost is > 0 and < 32);
    }

    private IEnumerable<WorldTile> AvailableFoundingTiles()
    {
        var settlementSites = _migrationState?.DaughterSettlement is { } daughter
            ? new HashSet<TileCoordinate> { World.StartingSite, daughter.Site }
            : new HashSet<TileCoordinate> { World.StartingSite };
        var occupied = _structures.Values.Select(x => x.Location)
            .Concat(_living?.Fields.Select(x => x.Location) ?? [])
            .Concat(_living?.Facilities.Select(x => x.Location) ?? [])
            .Concat(_living?.Orders.Where(x => x.Kind is LivingWorkKind.EstablishField or LivingWorkKind.BuildHearth or
                LivingWorkKind.BuildLoom or LivingWorkKind.BuildCareHouse).Select(x => x.Location) ?? [])
            .ToHashSet();
        var resources = World.Resources.Select(x => x.Coordinate).ToHashSet();
        return World.Tiles.Where(x => x.Walkable && x.Buildable && x.Terrain != TerrainType.Freshwater &&
            !settlementSites.Contains(x.Coordinate) && !occupied.Contains(x.Coordinate) && !resources.Contains(x.Coordinate));
    }

    private static long FoundingProvisionFood(long departureMinute)
    {
        var seasonOffset = departureMinute % WorldCalendar.MinutesPerYear;
        var seasonIndex = seasonOffset / MinutesPerMigrationSeason;
        var rate = seasonIndex == 3 ? NeedsProjection.WinterHungerRatePerMinute : NeedsProjection.HungerRatePerMinute;
        var hungerPoints = checked((long)rate * FoundingProvisionDays * WorldCalendar.MinutesPerDay);
        return checked((hungerPoints * CitizenSimulationRules.MealFoodUnits +
            CitizenSimulationRules.FullHungerReduction - 1) / CitizenSimulationRules.FullHungerReduction);
    }

    private bool TryWithdrawFoundingFood(long householdId, long quantity)
        => TryWithdrawFoundingFood(householdId, 1, quantity);

    private bool TryWithdrawFoundingFood(long householdId, long settlementId, long quantity)
    {
        var privateFood = Math.Min(quantity, AvailablePrivate(householdId, ResourceType.Food));
        var commonFood = quantity - privateFood;
        var settlement = SettlementFor(settlementId);
        if (commonFood > settlement.FoodStored) return false;
        if (privateFood > 0) AddPrivate(householdId, ResourceType.Food, -privateFood);
        settlement.FoodStored = checked(settlement.FoodStored - checked((int)commonFood));
        return true;
    }

    private void StartFoundingTravel(Citizen citizen, TileCoordinate destination)
    {
        var previousSequence = citizen.ActionSequence;
        RecoverInterruptedCargo(citizen);
        if (LivingEnabled) ReleaseLivingClaim(citizen);
        foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck &&
                     IsReservedCitizenEventFor(x, citizen.Id.Value)).ToArray())
            _scheduledEvents.Remove(item);
        _activePaths.Remove((citizen.Id.Value, previousSequence));
        FinishAction(citizen);
        citizen.ActionSequence = checked(previousSequence + 1);
        citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        BeginTravel(citizen, CitizenAction.Explore, destination, null);
    }

    private MigrationTransitPartyState? FoundingPartyForCitizen(long citizenId) =>
        _migrationState?.InTransitParties.FirstOrDefault(x => x.CitizenIds.Contains(citizenId));

    private void SetMigrationParty(MigrationTransitPartyState party)
    {
        var state = _migrationState ?? throw new InvalidDataException("M14 migration state is unavailable.");
        var parties = state.InTransitParties.Where(x => x.Id != party.Id).Append(party).OrderBy(x => x.Id).ToArray();
        _migrationState = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            parties, state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear);
    }

    private void RemoveMigrationParty(MigrationTransitPartyState party)
    {
        var state = _migrationState ?? throw new InvalidDataException("M14 migration state is unavailable.");
        _migrationState = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            state.InTransitParties.Where(x => x.Id != party.Id).ToArray(), state.FoundingPressure,
            state.LastRelocations, state.LastVisitAttemptYear);
    }

    private void UpdateFoundingPartyPosition(Citizen moved)
    {
        var party = FoundingPartyForCitizen(moved.Id.Value);
        if (party is null) return;
        var representative = party.CitizenIds.Select(id => _citizens.GetValueOrDefault(id))
            .FirstOrDefault(x => x is { IsAlive: true });
        if (representative is null) return;
        var route = FindPathCached(representative.Location, party.DestinationSite);
        var remaining = route is null || route.Count < 2 ? 0 : checked((int)TravelPathCost(route));
        SetMigrationParty(new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
            party.DestinationSettlementId, representative.Location, party.DestinationSite, party.CitizenIds,
            party.Cargo, remaining, party.DepartedMinute, party.Returning, party.FoundingAdultArrived,
            party.JourneyKind, party.VisitRelativeId, party.VisitPhase, party.VisitDwellEndsMinute));
    }

    private bool TryPauseFoundingTravelForMeal(Citizen citizen)
    {
        var party = FoundingPartyForCitizen(citizen.Id.Value);
        if (party is null ||
            citizen.CurrentAction != CitizenAction.Explore &&
            (citizen.CurrentAction != CitizenAction.None || party.JourneyKind == MigrationJourneyKind.Visit) ||
            !MigrationPartyHasProvision(party, 1)) return false;
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        if (needs.Hunger < 8500) return false;

        BeginFoundingTravelNeedAction(citizen, needs, CitizenAction.Eat,
            CitizenSimulationRules.EatDurationMinutes);
        return true;
    }

    private bool TryPauseFoundingTravelForRest(Citizen citizen)
    {
        var party = FoundingPartyForCitizen(citizen.Id.Value);
        if (party is null || party.JourneyKind == MigrationJourneyKind.Visit ||
            citizen.CurrentAction is not (CitizenAction.Explore or CitizenAction.None)) return false;
        var needs = citizen.GetProjectedNeeds(CurrentMinute);
        if (needs.Rest < 8500) return false;

        BeginFoundingTravelNeedAction(citizen, needs, CitizenAction.Rest,
            CitizenSimulationRules.RestDurationMinutes);
        return true;
    }

    private void BeginFoundingTravelNeedAction(Citizen citizen, CitizenNeeds needs, CitizenAction action,
        int durationMinutes)
    {
        citizen.Needs = needs;
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        _activePaths.Remove((citizen.Id.Value, citizen.ActionSequence));
        foreach (var movement in _scheduledEvents.Where(x => x.Name == CitizenEventNames.MoveStep &&
                     IsReservedCitizenEventFor(x, citizen.Id.Value)).ToArray())
            _scheduledEvents.Remove(movement);
        citizen.ActionSequence = checked(citizen.ActionSequence + 1);
        citizen.CurrentAction = action;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionTarget = null;
        citizen.ActionStartedMinute = CurrentMinute;
        citizen.ActionCompletesMinute = CurrentMinute.Add(durationMinutes);
        ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value,
            CitizenEventNames.CompletionPriority);
    }

    private static bool MigrationPartyHasProvision(MigrationTransitPartyState party, long quantity) =>
        party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Provisions && x.Good == MigrationCargoGood.Food)
            .Sum(x => x.Quantity) >= quantity;

    private int ConsumeFoundingProvisions(Citizen citizen, int requested)
    {
        var party = FoundingPartyForCitizen(citizen.Id.Value);
        if (party is null || requested <= 0) return 0;

        var remaining = requested;
        var consumed = 0;
        var cargo = new List<MigrationCargoStackState>();
        foreach (var stack in party.Cargo)
        {
            if (remaining > 0 && stack.Purpose == MigrationCargoPurpose.Provisions &&
                stack.Good == MigrationCargoGood.Food)
            {
                var taken = Math.Min((long)remaining, stack.Quantity);
                remaining -= checked((int)taken);
                consumed += checked((int)taken);
                if (taken < stack.Quantity) cargo.Add(stack with { Quantity = stack.Quantity - taken });
            }
            else cargo.Add(stack);
        }

        if (consumed > 0)
            SetMigrationParty(new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
                party.DestinationSettlementId, party.Location, party.DestinationSite, party.CitizenIds, cargo,
                party.RemainingPathCost, party.DepartedMinute, party.Returning, party.FoundingAdultArrived,
                party.JourneyKind, party.VisitRelativeId, party.VisitPhase, party.VisitDwellEndsMinute));
        return consumed;
    }

    private void ResumeFoundingTravel(Citizen citizen)
    {
        var party = FoundingPartyForCitizen(citizen.Id.Value);
        if (party is null) return;
        if (party.JourneyKind == MigrationJourneyKind.Visit)
        {
            ResumeMigrationVisit(citizen, party);
            return;
        }
        citizen.ActionSequence = checked(citizen.ActionSequence + 1);
        citizen.Needs = citizen.GetProjectedNeeds(CurrentMinute);
        citizen.NeedsUpdatedMinute = CurrentMinute.Value;
        BeginTravel(citizen, CitizenAction.Explore, party.DestinationSite, null);
    }

    private bool HandleFoundingPartyArrival(Citizen citizen)
    {
        var party = FoundingPartyForCitizen(citizen.Id.Value);
        if (party is null || citizen.CurrentAction != CitizenAction.Explore ||
            citizen.Location != party.DestinationSite) return false;

        if (party.JourneyKind == MigrationJourneyKind.Visit)
            return HandleMigrationVisitArrival(citizen, party);

        if (!party.Returning && citizen.IsAlive && citizen.AgeYears(CurrentMinute) >= 18 && !party.FoundingAdultArrived)
        {
            party = new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
                party.DestinationSettlementId, party.Location, party.DestinationSite, party.CitizenIds, party.Cargo,
                party.RemainingPathCost, party.DepartedMinute, party.Returning, foundingAdultArrived: true,
                journeyKind: party.JourneyKind);
            SetMigrationParty(party);
        }

        if (party.JourneyKind == MigrationJourneyKind.Relocation)
            return HandleRelocationPartyArrival(citizen, party);

        var survivors = party.CitizenIds.Select(id => _citizens.GetValueOrDefault(id))
            .Where(x => x is { IsAlive: true }).Cast<Citizen>().OrderBy(x => x.Id.Value).ToArray();
        if (survivors.Any(x => x.Location != party.DestinationSite))
        {
            WaitForFoundingParty(citizen);
            return true;
        }

        if (party.Returning)
        {
            RecoverFoundingCargoAtOrigin(party);
            RecordFoundingExpeditionReturned(party, survivors);
            foreach (var survivor in survivors) FinishFoundingTravel(survivor);
            RemoveMigrationParty(party);
            return true;
        }

        var adultAtSite = survivors.Any(x => x.AgeYears(CurrentMinute) >= 18 && x.Location == party.DestinationSite);
        if (!party.FoundingAdultArrived || !adultAtSite)
        {
            BeginFoundingPartyReturn(party);
            return true;
        }

        CompleteFoundingSettlement(party, survivors);
        return true;
    }

    private void WaitForFoundingParty(Citizen citizen)
    {
        foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck &&
                     IsReservedCitizenEventFor(x, citizen.Id.Value)).ToArray())
            _scheduledEvents.Remove(item);
        _activePaths.Remove((citizen.Id.Value, citizen.ActionSequence));
        citizen.ActionSequence = checked(citizen.ActionSequence + 1);
        citizen.CurrentAction = CitizenAction.Idle;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionTarget = null;
        citizen.ActionStartedMinute = CurrentMinute;
        citizen.ActionCompletesMinute = CurrentMinute.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes);
        ScheduleCitizen(citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value,
            CitizenEventNames.CompletionPriority);
    }

    private void CompleteFoundingSettlement(MigrationTransitPartyState party, Citizen[] survivors)
    {
        var household = _households.GetValueOrDefault(party.HouseholdId)
            ?? throw new InvalidDataException("Founding party household is missing.");
        if (!_householdStocks.TryGetValue(party.HouseholdId, out var founderStock))
            throw new InvalidDataException("Founding party household is missing its private stock account.");

        var cargoGoods = party.Cargo.Aggregate(new Goods(), (goods, stack) => stack.Good switch
        {
            MigrationCargoGood.Food or MigrationCargoGood.Meal => goods.Add(ResourceType.Food, stack.Quantity),
            MigrationCargoGood.Wood => goods.Add(ResourceType.Wood, stack.Quantity),
            MigrationCargoGood.Stone => goods.Add(ResourceType.Stone, stack.Quantity),
            _ => goods
        });
        var foundingGoods = founderStock.Holdings.Plus(cargoGoods);
        var foodReserve = checked((long)survivors.Length * DaughterFoodReservePerResident);
        var capacity = checked((int)Math.Max(EconomyRules.FoundingStorageCapacity,
            checked(foundingGoods.Total + foodReserve)));
        var emptyStock = new SettlementState(0, 0, 0, capacity, CurrentMinute.Value,
            CurrentMinute.Add(CitizenSimulationRules.ExposureGraceDurationMinutes).Value);
        var state = _migrationState ?? throw new InvalidDataException("M14 migration state is unavailable.");
        _migrationState = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(party.DestinationSite, MigrationSettlementStockState.From(emptyStock),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()),
            state.InTransitParties.Where(x => x.Id != party.Id).ToArray(), state.FoundingPressure,
            state.LastRelocations, state.LastVisitAttemptYear);
        _daughterSettlementRuntime = emptyStock;
        _daughterLivingGoodsRuntime = Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToList();

        household.DwellingStructureId = null;
        foreach (var member in _citizens.Values.Where(x => x.HouseholdId == household.Id).OrderBy(x => x.Id.Value))
        {
            SetHomeStructure(member, null);
            RecordMigrationOwner(MigrationEntityKind.Citizen, member.Id.Value, MigrationDaughterSettlementState.SettlementId);
        }
        RecordMigrationOwner(MigrationEntityKind.Household, household.Id.Value, MigrationDaughterSettlementState.SettlementId);
        DepositFoundingCargo(party, MigrationDaughterSettlementState.SettlementId);
        ReconcileHouseholdsAndHousing();
        EmitHistory(HistoricalEventType.DaughterSettlementFounded, HistoricalImportance.Historic,
            party.DestinationSite,
            HistoricalEventPayloads.DaughterSettlementFounded(MigrationDaughterSettlementState.SettlementId,
                party.Id, party.HouseholdId, survivors.Length),
            survivors.Select(x => (x.Id, "founder")));
        foreach (var survivor in survivors) FinishFoundingTravel(survivor);
    }

    private void FinishFoundingTravel(Citizen citizen)
    {
        foreach (var item in _scheduledEvents.Where(x => x.Name != CitizenEventNames.SurvivalCheck &&
                     IsReservedCitizenEventFor(x, citizen.Id.Value)).ToArray())
            _scheduledEvents.Remove(item);
        _activePaths.Remove((citizen.Id.Value, citizen.ActionSequence));
        FinishAction(citizen);
        ScheduleCitizen(citizen, CitizenEventNames.Decision, CurrentMinute, CitizenEventNames.DecisionPriority);
    }

    private void RecordFoundingExpeditionReturned(MigrationTransitPartyState party, Citizen[] survivors)
    {
        if (party.JourneyKind != MigrationJourneyKind.Founding) return;
        var destination = ExpeditionDestinationForHistory(party.Id, party.DestinationSite);
        EmitHistory(HistoricalEventType.ExpeditionReturned, HistoricalImportance.Notable,
            SiteLocation(party.OriginSettlementId),
            HistoricalEventPayloads.ExpeditionReturned(party.Id, party.HouseholdId, party.OriginSettlementId,
                destination.X, destination.Y, survivors.Length),
            survivors.Select(x => (x.Id, "participant")));
    }

    private void RecordFoundingExpeditionLost(MigrationTransitPartyState party)
    {
        if (party.JourneyKind != MigrationJourneyKind.Founding) return;
        var destination = ExpeditionDestinationForHistory(party.Id, party.DestinationSite);
        EmitHistory(HistoricalEventType.ExpeditionLost, HistoricalImportance.Notable, party.Location,
            HistoricalEventPayloads.ExpeditionLost(party.Id, party.HouseholdId, party.OriginSettlementId,
                destination.X, destination.Y, party.CitizenIds.Count),
            party.CitizenIds.Select(id => (new CitizenId(id), "participant")));
    }

    private void BeginFoundingPartyReturn(MigrationTransitPartyState party)
    {
        var destination = SiteLocation(party.OriginSettlementId);
        var survivors = party.CitizenIds.Select(id => _citizens.GetValueOrDefault(id))
            .Where(x => x is { IsAlive: true }).Cast<Citizen>().OrderBy(x => x.Id.Value).ToArray();
        var representative = survivors.FirstOrDefault();
        var path = representative is null ? null : FindPathCached(representative.Location, destination);
        var updated = new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
            party.OriginSettlementId, representative?.Location ?? party.Location, destination, party.CitizenIds,
            party.Cargo, path is null || path.Count < 2 ? 0 : checked((int)TravelPathCost(path)),
            party.DepartedMinute, returning: true, foundingAdultArrived: party.FoundingAdultArrived,
            journeyKind: party.JourneyKind);
        SetMigrationParty(updated);
        if (representative is null)
        {
            RecoverFoundingCargoAtPartyLocation(updated);
            RemoveMigrationParty(updated);
            RecordFoundingExpeditionLost(updated);
            return;
        }
        if (path is null || survivors.Any(x => FindPathCached(x.Location, destination) is null))
        {
            RecoverFoundingCargoAtPartyLocation(updated);
            foreach (var survivor in survivors) FinishFoundingTravel(survivor);
            RemoveMigrationParty(updated);
            RecordFoundingExpeditionLost(updated);
            return;
        }
        if (survivors.All(x => x.Location == destination))
        {
            RecoverFoundingCargoAtOrigin(updated);
            RecordFoundingExpeditionReturned(updated, survivors);
            foreach (var survivor in survivors) FinishFoundingTravel(survivor);
            RemoveMigrationParty(updated);
            return;
        }
        foreach (var survivor in survivors) StartFoundingTravel(survivor, destination);
    }

    private void CheckFoundingPartyAfterSurvival(Citizen citizen)
    {
        var party = FoundingPartyForCitizen(citizen.Id.Value);
        if (party is null) return;
        if (party.JourneyKind == MigrationJourneyKind.Visit)
        {
            HandleMigrationVisitAfterSurvival(citizen, party);
            return;
        }
        UpdateFoundingPartyPosition(citizen);
        party = FoundingPartyForCitizen(citizen.Id.Value)!;
        if (!party.CitizenIds.Any(id => _citizens.TryGetValue(id, out var member) && member.IsAlive))
        {
            RecoverFoundingCargoAtPartyLocation(party);
            RemoveMigrationParty(party);
            RecordFoundingExpeditionLost(party);
            return;
        }
        if (party.Returning) return;
        var adultSurvives = party.CitizenIds.Select(id => _citizens.GetValueOrDefault(id))
            .Any(x => x is { IsAlive: true } && x.AgeYears(CurrentMinute) >= 18);
        if (!adultSurvives) BeginFoundingPartyReturn(party);
    }

    private void RecoverFoundingCargoAtOrigin(MigrationTransitPartyState party)
    {
        if (party.JourneyKind == MigrationJourneyKind.Relocation)
            DepositRelocationCargoAtSite(party, party.OriginSettlementId);
        else
            DepositFoundingCargo(party, party.OriginSettlementId);
    }

    private void RecoverFoundingCargoAtPartyLocation(MigrationTransitPartyState party)
    {
        var owner = _households.TryGetValue(party.HouseholdId, out var household) && household.DissolvedMinute is null
            ? party.HouseholdId
            : (long?)null;
        foreach (var stack in party.Cargo.OrderBy(x => x.Id))
        {
            var resource = stack.Good switch
            {
                MigrationCargoGood.Food or MigrationCargoGood.Meal => ResourceType.Food,
                MigrationCargoGood.Wood => ResourceType.Wood,
                MigrationCargoGood.Stone => ResourceType.Stone,
                _ => (ResourceType?)null
            };
            if (resource is { } recoverable && stack.Quantity > 0)
                AddRecoverable(owner, party.Location, recoverable, checked((int)stack.Quantity));
        }
    }

    private void DepositFoundingCargo(MigrationTransitPartyState party, long settlementId)
    {
        foreach (var stack in party.Cargo.OrderBy(x => x.Id))
        {
            switch (stack.Good)
            {
                case MigrationCargoGood.Food:
                case MigrationCargoGood.Meal:
                    SettlementFor(settlementId).FoodStored = checked(SettlementFor(settlementId).FoodStored +
                        checked((int)stack.Quantity));
                    break;
                case MigrationCargoGood.Wood:
                    SettlementFor(settlementId).WoodStored = checked(SettlementFor(settlementId).WoodStored +
                        checked((int)stack.Quantity));
                    break;
                case MigrationCargoGood.Stone:
                    SettlementFor(settlementId).StoneStored = checked(SettlementFor(settlementId).StoneStored +
                        checked((int)stack.Quantity));
                    break;
                default:
                    ChangeGoodAt(settlementId, Enum.Parse<LivingGood>(stack.Good.ToString()), checked((int)stack.Quantity));
                    break;
            }
        }
    }
}
