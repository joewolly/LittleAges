using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private const int RelocationCooldownSeasons = 4;

    private void EvaluateMigrationRelocation()
    {
        if (!MigrationSystemsEnabled(SimulationRulesVersion) ||
            _migrationState?.DaughterSettlement is null ||
            _migrationState.InTransitParties.Count != 0 ||
            CurrentMinute.Value % MinutesPerMigrationSeason != 0)
            return;

        var sites = MigrationSettlementIds;
        if (sites.Length != 2) return;
        var opportunity = sites.ToDictionary(x => x, MigrationOpportunityAt);
        var lastMoves = (_migrationState.LastRelocations ?? Array.Empty<MigrationHouseholdRelocationState>())
            .ToDictionary(x => x.HouseholdId, x => x.LastCompletedMinute);
        var cooldown = checked((long)RelocationCooldownSeasons * MinutesPerMigrationSeason);

        foreach (var household in _households.Values
                     .Where(x => x.DissolvedMinute is null)
                     .OrderBy(x => x.Id.Value))
        {
            var origin = SiteIdForHousehold(household.Id.Value);
            var destination = origin == 1 ? MigrationDaughterSettlementState.SettlementId : 1;
            if (!opportunity.ContainsKey(origin) || !opportunity.ContainsKey(destination)) continue;
            if (lastMoves.TryGetValue(household.Id.Value, out var lastMove) &&
                CurrentMinute.Value - lastMove < cooldown)
                continue;

            var members = _citizens.Values
                .Where(x => x.IsAlive && x.HouseholdId == household.Id)
                .OrderBy(x => x.Id.Value).ToArray();
            if (members.Length == 0 || !members.Any(x => x.AgeYears(CurrentMinute) >= 18) ||
                HasMigrationWorkOrBarterObligations(household.Id.Value, members))
                continue;
            var provisionPerTraveler = FoundingProvisionFood(CurrentMinute.Value);
            var provisionQuantity = checked(provisionPerTraveler * members.Length);
            if (provisionQuantity > int.MaxValue) continue;
            var incomingGoods = RelocationGoodsForDeparture(household.Id.Value, provisionQuantity);
            if (opportunity[destination] - opportunity[origin] < 5 ||
                !HasRelocationSupport(destination, members.Length, incomingGoods))
                continue;

            var destinationSite = SiteLocation(destination);
            var paths = members.Select(x => FindPathCached(x.Location, destinationSite)).ToArray();
            if (paths.Any(x => x is not { Count: >= 2 })) continue;
            var representativePath = paths[0]!;

            if (!TryWithdrawFoundingFood(household.Id.Value, origin, provisionQuantity))
                continue;

            var cargo = new List<MigrationCargoStackState>
            {
                new(_counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Food,
                    provisionQuantity, MigrationCargoPurpose.Provisions)
            };
            WithdrawRelocatingHouseholdGoods(household.Id.Value, cargo);
            var party = new MigrationTransitPartyState(_counters.AllocateMigrationPartyId(), household.Id.Value,
                origin, destination, members[0].Location, destinationSite, members.Select(x => x.Id.Value).ToArray(),
                cargo, checked((int)TravelPathCost(representativePath)),
                CurrentMinute.Value, journeyKind: MigrationJourneyKind.Relocation);
            SetMigrationParty(party);
            foreach (var member in members) StartFoundingTravel(member, destinationSite);
            return;
        }
    }

    private int MigrationOpportunityAt(long settlementId)
    {
        var activeHouseholds = Math.Max(1, HouseholdsAt(settlementId).Count(x => x.DissolvedMinute is null));
        var livingPopulation = Math.Max(1, PopulationAt(settlementId));
        var completedShelters = StructuresAt(settlementId)
            .Where(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete)
            .Select(x => x.Id.Value).ToHashSet();
        var assignedShelterResidents = CitizensAt(settlementId)
            .Count(x => x.IsAlive && x.HomeStructureId is { } home && completedShelters.Contains(home.Value));
        var freeShelterSlots = Math.Max(0, ShelterCapacityAt(settlementId) - assignedShelterResidents);
        var communalFood = SettlementFor(settlementId).FoodStored;
        var localTravelCosts = GetTravelCostsCached(SiteLocation(settlementId));
        var reachableBuildableTiles = AvailableFoundingTiles()
            .Count(tile => localTravelCosts.TryGetValue(tile.Coordinate, out var cost) && cost <= 31);

        return checked(10 * Math.Min(4, freeShelterSlots / activeHouseholds) +
            Math.Min(40, communalFood / livingPopulation) +
            Math.Min(20, reachableBuildableTiles / activeHouseholds));
    }

    private bool HasRelocationSupport(long settlementId, int incomingMembers)
        => HasRelocationSupport(settlementId, incomingMembers, new Goods());

    private bool HasRelocationSupport(long settlementId, int incomingMembers, Goods incomingGoods)
    {
        var localFood = SettlementFor(settlementId).FoodStored;
        var requiredFood = checked(20L * checked(PopulationAt(settlementId) + incomingMembers));
        var completedShelters = StructuresAt(settlementId)
            .Where(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete)
            .Select(x => x.Id.Value).ToHashSet();
        var assignedResidents = CitizensAt(settlementId)
            .Count(x => x.IsAlive && x.HomeStructureId is { } home && completedShelters.Contains(home.Value));
        var freeShelterSlots = Math.Max(0, ShelterCapacityAt(settlementId) - assignedResidents);
        return localFood >= requiredFood && freeShelterSlots >= incomingMembers &&
            CanStoreRelocationGoodsAt(settlementId, incomingGoods);
    }

    private Goods RelocationGoodsForDeparture(long householdId, long provisionQuantity)
    {
        if (!_householdStocks.TryGetValue(householdId, out var stock))
            throw new InvalidDataException("A relocating household is missing its private stock account.");

        // Provisions are deposited as communal food, while every remaining private good
        // travels with the household. When private food covers provisions, count it once.
        return new Goods(Math.Max(stock.Holdings.Food, provisionQuantity), stock.Holdings.Wood,
            stock.Holdings.Stone);
    }

    private bool CanStoreRelocationGoodsAt(long settlementId, Goods incomingGoods)
    {
        var communal = SettlementFor(settlementId);
        var migration = _migrationState ?? throw new InvalidDataException("M14 migration state is unavailable.");
        var economy = CaptureEconomy() ?? throw new InvalidDataException("M14 economy state is unavailable.");
        var householdSites = migration.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var stored = economy.StoredGoodsAt(communal, migration, settlementId);
        var reserved = economy.Trades.Where(x => x.Status == BarterStatus.Reserved)
            .Aggregate(new Goods(), (sum, trade) => sum
                .Add(trade.ResourceA, householdSites.GetValueOrDefault(trade.HouseholdA, 1) == settlementId &&
                    trade.PickedA && !trade.DeliveredA ? trade.QuantityA : 0)
                .Add(trade.ResourceB, householdSites.GetValueOrDefault(trade.HouseholdB, 1) == settlementId &&
                    trade.PickedB && !trade.DeliveredB ? trade.QuantityB : 0));
        var capacity = StorageCapacityAt(settlementId);
        var currentStorage = checked(stored.Total + reserved.Total + LivingStoredQuantityAt(settlementId));
        if (checked(currentStorage + incomingGoods.Total) > capacity) return false;

        var nonFoodCapacity = Math.Max(0, capacity - GranaryCapacityAt(settlementId));
        var currentNonFood = checked(stored.Wood + stored.Stone + reserved.Wood + reserved.Stone);
        return checked(currentNonFood + incomingGoods.Wood + incomingGoods.Stone) <= nonFoodCapacity;
    }

    private bool HasMigrationWorkOrBarterObligations(long householdId, IReadOnlyList<Citizen> members)
    {
        if (OpenTrades.Any(x => x.HouseholdA == householdId || x.HouseholdB == householdId) ||
            _publicWork.Values.Any(x => x.HouseholdId == householdId))
            return true;

        var memberIds = members.Select(x => x.Id.Value).ToHashSet();
        if (members.Any(x => x.CarriedResourceQuantity > 0 || x.CarriedResourceType is not null ||
                _productionCargoOwners.ContainsKey(x.Id.Value)))
            return true;

        return (_living?.Orders ?? []).Any(x => x.CitizenId is { } citizenId && memberIds.Contains(citizenId) &&
            (x.CargoInTransit || x.Cargo.Count > 0));
    }

    private void WithdrawRelocatingHouseholdGoods(long householdId, List<MigrationCargoStackState> cargo)
    {
        if (!_householdStocks.TryGetValue(householdId, out var stock))
            throw new InvalidDataException("A relocating household is missing its private stock account.");

        foreach (var (resource, good) in new[]
                 {
                     (ResourceType.Food, MigrationCargoGood.Food),
                     (ResourceType.Wood, MigrationCargoGood.Wood),
                     (ResourceType.Stone, MigrationCargoGood.Stone)
                 })
        {
            var quantity = stock.Holdings.Get(resource);
            if (quantity <= 0) continue;
            AddPrivate(householdId, resource, -quantity);
            cargo.Add(new MigrationCargoStackState(_counters.AllocateMigrationCargoStackId(), good,
                quantity, MigrationCargoPurpose.Cargo));
        }
    }

    private bool HandleRelocationPartyArrival(Citizen citizen, MigrationTransitPartyState party)
    {
        var survivors = party.CitizenIds.Select(id => _citizens.GetValueOrDefault(id))
            .Where(x => x is { IsAlive: true }).Cast<Citizen>().OrderBy(x => x.Id.Value).ToArray();
        if (survivors.Any(x => x.Location != party.DestinationSite))
        {
            WaitForFoundingParty(citizen);
            return true;
        }

        if (party.Returning)
        {
            var cargoGoods = RelocationCargoGoods(party.Cargo);
            if (CanStoreRelocationGoodsAt(party.OriginSettlementId, cargoGoods))
                DepositRelocationCargoAtSite(party, party.OriginSettlementId);
            else
                RecoverFoundingCargoAtPartyLocation(party);
            foreach (var survivor in survivors) FinishFoundingTravel(survivor);
            RemoveMigrationParty(party);
            return true;
        }

        var destination = party.DestinationSettlementId ??
            throw new InvalidDataException("A relocation party has no destination settlement.");
        var adultAtDestination = survivors.Any(x => x.AgeYears(CurrentMinute) >= 18 &&
            x.Location == party.DestinationSite);
        if (!party.FoundingAdultArrived || !adultAtDestination ||
            !HasRelocationSupport(destination, survivors.Length, RelocationCargoGoods(party.Cargo)))
        {
            BeginFoundingPartyReturn(party);
            return true;
        }

        CompleteHouseholdRelocation(party, destination, survivors);
        return true;
    }

    private void CompleteHouseholdRelocation(MigrationTransitPartyState party, long destination,
        Citizen[] survivors)
    {
        var household = _households.GetValueOrDefault(party.HouseholdId)
            ?? throw new InvalidDataException("A relocation party household is missing.");
        var state = _migrationState ?? throw new InvalidDataException("M14 migration state is unavailable.");
        var householdMembers = _citizens.Values.Where(x => x.HouseholdId == household.Id)
            .OrderBy(x => x.Id.Value).ToArray();

        household.DwellingStructureId = null;
        foreach (var member in householdMembers) SetHomeStructure(member, null);

        var memberIds = householdMembers.Select(x => x.Id.Value).ToHashSet();
        var citizenResidences = state.CitizenResidences.Select(x => memberIds.Contains(x.EntityId)
            ? new MigrationEntityResidence(x.EntityId, destination)
            : x).ToArray();
        var householdResidences = state.HouseholdResidences.Select(x => x.EntityId == household.Id.Value
            ? new MigrationEntityResidence(x.EntityId, destination)
            : x).ToArray();
        var lastMoves = (state.LastRelocations ?? Array.Empty<MigrationHouseholdRelocationState>())
            .Where(x => x.HouseholdId != household.Id.Value)
            .Append(new MigrationHouseholdRelocationState(household.Id.Value, CurrentMinute.Value))
            .OrderBy(x => x.HouseholdId).ToArray();

        _migrationState = new MigrationWorldState(state.Version, citizenResidences, householdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            state.InTransitParties.Where(x => x.Id != party.Id).ToArray(), state.FoundingPressure, lastMoves,
            state.LastVisitAttemptYear);
        DepositRelocationCargoAtSite(party, destination);
        ReconcileHouseholdsAndHousing();
        EmitHistory(HistoricalEventType.HouseholdRelocated, HistoricalImportance.Notable,
            party.DestinationSite,
            HistoricalEventPayloads.HouseholdRelocated(party.HouseholdId, party.OriginSettlementId,
                destination, survivors.Length),
            survivors.Select(x => (x.Id, "member")));
        foreach (var survivor in survivors) FinishFoundingTravel(survivor);
    }

    private void DepositRelocationCargoAtSite(MigrationTransitPartyState party, long settlementId)
    {
        foreach (var stack in party.Cargo.OrderBy(x => x.Id))
        {
            if (stack.Purpose == MigrationCargoPurpose.Provisions)
            {
                if (stack.Good != MigrationCargoGood.Food)
                    throw new InvalidDataException("Migration provisions must be food.");
                var settlement = SettlementFor(settlementId);
                settlement.FoodStored = checked(settlement.FoodStored + checked((int)stack.Quantity));
                continue;
            }

            var resource = stack.Good switch
            {
                MigrationCargoGood.Food => ResourceType.Food,
                MigrationCargoGood.Wood => ResourceType.Wood,
                MigrationCargoGood.Stone => ResourceType.Stone,
                _ => throw new InvalidDataException("Relocation cargo contains an unsupported good.")
            };
            AddPrivate(party.HouseholdId, resource, stack.Quantity);
        }
    }

    private static Goods RelocationCargoGoods(IReadOnlyList<MigrationCargoStackState> cargo)
    {
        var goods = new Goods();
        foreach (var stack in cargo.OrderBy(x => x.Id))
        {
            if (stack.Purpose == MigrationCargoPurpose.Provisions && stack.Good != MigrationCargoGood.Food)
                throw new InvalidDataException("Migration provisions must be food.");
            var resource = stack.Good switch
            {
                MigrationCargoGood.Food => ResourceType.Food,
                MigrationCargoGood.Wood => ResourceType.Wood,
                MigrationCargoGood.Stone => ResourceType.Stone,
                _ => throw new InvalidDataException("Relocation cargo contains an unsupported good.")
            };
            goods = goods.Add(resource, stack.Quantity);
        }
        return goods;
    }
}
