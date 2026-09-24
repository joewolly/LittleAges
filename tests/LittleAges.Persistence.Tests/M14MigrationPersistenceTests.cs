using System.Reflection;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M14MigrationPersistenceTests
{
    [Fact]
    public async Task DaughterSiteStocksAndOwnershipSurviveSqliteCheckpointReopen()
    {
        var initial = new SimulationEngine(new WorldSeed(913),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var world = initial.World!;
        var daughterSite = world.Tiles.First(x => x.Coordinate != world.StartingSite && x.Walkable).Coordinate;
        var daughterHousehold = initial.Households.First(x => x.DissolvedMinute is null);
        var daughterCitizens = initial.Citizens.Where(x => x.HouseholdId == daughterHousehold.Id).ToArray();
        Assert.NotEmpty(daughterCitizens);
        foreach (var citizen in daughterCitizens)
        {
            citizen.Location = daughterSite;
            citizen.HomeStructureId = null;
        }

        var state = initial.MigrationState!;
        var daughterCitizenIds = daughterCitizens.Select(x => x.Id.Value).ToHashSet();
        var migration = new MigrationWorldState(state.Version,
            initial.Citizens.Select(x => new MigrationEntityResidence(x.Id.Value, daughterCitizenIds.Contains(x.Id.Value) ? 2 : 1)).ToArray(),
            initial.Households.Select(x => new MigrationEntityResidence(x.Id.Value, x.Id == daughterHousehold.Id ? 2 : 1)).ToArray(),
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(100, 0, 0, 4000, initial.WorldMinute.Value, initial.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()), state.InTransitParties);
        var siteOne = new SettlementState(300, initial.Settlement!.WoodStored, initial.Settlement.StoneStored,
            initial.Settlement.BaseStorageCapacity, initial.Settlement.DemandUpdatedMinute,
            initial.Settlement.ExposureConsequencesStartMinute);
        var crafted = CopySnapshot(initial, siteOne, migration.ToCanonicalJson(), initial.Citizens);

        var living = LivingWorldCodec.Deserialize(crafted.LivingStateJson!);
        var cargoOrderId = living.NextId++;
        living.Orders.Add(new LivingWorkOrder
        {
            Id = cargoOrderId,
            Kind = LivingWorkKind.CutFuel,
            Location = daughterSite,
            SupplyLocation = daughterSite,
            CreatedMinute = crafted.WorldMinute.Value,
            ClaimedMinute = crafted.WorldMinute.Value,
            RequiredWork = LivingWorkDefinitions.Work(LivingWorkKind.CutFuel),
            WorkDone = LivingWorkDefinitions.Work(LivingWorkKind.CutFuel),
            Phase = LivingWorkPhase.Deliver,
            Ingredients = LivingWorkDefinitions.Ingredients(LivingWorkKind.CutFuel).ToList(),
            Reserved = true,
            SuppliesDelivered = true,
            Produced = true,
            Cargo = [new LivingStock(LivingGood.Fuel, LivingWorkDefinitions.OutputQuantity(
                LivingWorkKind.CutFuel, SimulationEngine.MigrationSimulationRulesVersion))]
        });
        var migrationWithCargo = new MigrationWorldState(migration.Version, migration.CitizenResidences,
            migration.HouseholdResidences, migration.StructureOwners, migration.FacilityOwners,
            migration.WorkOrderOwners.Append(new MigrationEntityResidence(cargoOrderId, 2)).ToArray(),
            migration.DaughterSettlement, migration.InTransitParties);
        crafted = CopySnapshot(crafted, siteOne, migrationWithCargo.ToCanonicalJson(), initial.Citizens,
            LivingWorldCodec.Serialize(living));
        MigrationValidation.Validate(crafted);
        var expectedProjection = MigrationValidation.CreateReadSnapshot(crafted);
        var expectedJson = crafted.MigrationStateJson;
        var uninterrupted = SimulationEngine.FromPersistenceSnapshot(crafted);

        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-daughter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(crafted);

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                MigrationValidation.Validate(loaded);
                Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, loaded.SimulationRulesVersion);
                Assert.Equal(expectedJson, loaded.MigrationStateJson);
                var reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                var projection = reopened.CreateMigrationReadSnapshot();
                Assert.Equal(expectedProjection.Fingerprint, projection.Fingerprint);
                Assert.Equal(300, projection.Settlements.Single(x => x.Id == 1).CommunalStock.FoodStored);
                Assert.Equal(100, projection.Settlements.Single(x => x.Id == 2).CommunalStock.FoodStored);
                Assert.Contains(daughterHousehold.Id.Value, projection.Settlements.Single(x => x.Id == 2).HouseholdIds);
                Assert.All(daughterCitizens, citizen =>
                    Assert.Contains(citizen.Id.Value, projection.Settlements.Single(x => x.Id == 2).CitizenIds));
                Assert.Equal(expectedProjection.Settlements.SelectMany(x => x.FarmStructureIds),
                    projection.Settlements.SelectMany(x => x.FarmStructureIds));

                var beforeContinuation = reopened.CreatePersistenceSnapshot();
                var livingBeforeContinuation = LivingWorldCodec.Deserialize(beforeContinuation.LivingStateJson!);
                Assert.Contains(livingBeforeContinuation.Orders, x => x.Id == cargoOrderId &&
                    x.Cargo.SequenceEqual([new LivingStock(LivingGood.Fuel, LivingWorkDefinitions.OutputQuantity(
                        LivingWorkKind.CutFuel, SimulationEngine.MigrationSimulationRulesVersion))]));
                Assert.Contains(beforeContinuation.MigrationState!.WorkOrderOwners,
                    x => x.EntityId == cargoOrderId && x.SettlementId == 2);

                var continuationTarget = reopened.CurrentMinute.Add(120);
                uninterrupted.AdvanceUntil(continuationTarget);
                reopened.AdvanceUntil(continuationTarget);
                Assert.Equal(uninterrupted.LivingStateJson, reopened.LivingStateJson);
                Assert.Equal(uninterrupted.CreateMigrationReadSnapshot().Fingerprint,
                    reopened.CreateMigrationReadSnapshot().Fingerprint);
                var continued = reopened.CreatePersistenceSnapshot();
                MigrationValidation.Validate(continued);
                var livingAfterContinuation = LivingWorldCodec.Deserialize(continued.LivingStateJson!);
                var workOrderOwners = continued.MigrationState!.WorkOrderOwners.ToDictionary(x => x.EntityId, x => x.SettlementId);
                var siteTwoFuel = continued.MigrationState.DaughterSettlement!.LivingGoods
                    .Single(x => x.Good == LivingGood.Fuel).Quantity + livingAfterContinuation.Orders
                    .Where(x => workOrderOwners.GetValueOrDefault(x.Id, 1) == 2)
                    .Sum(x => x.Cargo.Where(y => y.Good == LivingGood.Fuel).Sum(y => y.Quantity));
                Assert.Equal(30, siteTwoFuel);
                Assert.Equal(0, livingAfterContinuation.Stock.Single(x => x.Good == LivingGood.Fuel).Quantity);
                await database.CreateCheckpointStore().CheckpointAsync(continued);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task M14AndM13InitialWorldsRoundTripWithoutChangingRulesOrLegacyFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-foundation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var migrationPath = Path.Combine(root, "m14.db");
        var legacyPath = Path.Combine(root, "m13.db");
        try
        {
            var m14 = new SimulationEngine(new WorldSeed(314),
                simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            var expectedMigration = m14.CreateMigrationReadSnapshot();
            var expectedStateJson = m14.CreatePersistenceSnapshot().MigrationStateJson;
            await using (var database = await WorldDatabase.OpenAsync(migrationPath))
                await database.CreateCheckpointStore().CheckpointAsync(m14.CreatePersistenceSnapshot());

            await using (var database = await WorldDatabase.OpenAsync(migrationPath))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                var reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, loaded.SimulationRulesVersion);
                Assert.Equal(expectedStateJson, loaded.MigrationStateJson);
                Assert.Equal(expectedMigration.Fingerprint, reopened.CreateMigrationReadSnapshot().Fingerprint);
            }

            var m13 = new SimulationEngine(new WorldSeed(315),
                simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
            var expectedLegacy = m13.CreatePersistenceSnapshot();
            await using (var database = await WorldDatabase.OpenAsync(legacyPath))
                await database.CreateCheckpointStore().CheckpointAsync(expectedLegacy);

            await using (var database = await WorldDatabase.OpenAsync(legacyPath))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                var reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, loaded.SimulationRulesVersion);
                Assert.Null(loaded.MigrationStateJson);
                Assert.Equal(expectedLegacy.World!.Fingerprint, loaded.World!.Fingerprint);
                Assert.Equal(m13.SurvivalFingerprint, reopened.SurvivalFingerprint);
                Assert.Equal(m13.SettlementFingerprint, reopened.SettlementFingerprint);
                Assert.Equal(m13.SocialFingerprint, reopened.SocialFingerprint);
                Assert.Equal(m13.HistoryFingerprint, reopened.HistoryFingerprint);
                Assert.Equal(m13.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
                Assert.Equal(m13.ComputeAgricultureFingerprint(), reopened.ComputeAgricultureFingerprint());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FoundingPartyMidTravelAndArrivalOrReturnReplayAcrossSqliteReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-founding-party-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var returning in new[] { false, true })
            {
                var fixture = CreateFoundingPartyFixture(new WorldSeed(returning ? 914UL : 913UL), returning);
                var path = Path.Combine(root, returning ? "return.db" : "arrival.db");
                var reference = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);

                await using (var database = await WorldDatabase.OpenAsync(path))
                    await database.CreateCheckpointStore().CheckpointAsync(fixture.Snapshot);

                SimulationEngine reopenedAtDeparture;
                await using (var database = await WorldDatabase.OpenAsync(path))
                {
                    var loaded = await database.CreateCheckpointStore().LoadAsync();
                    reopenedAtDeparture = SimulationEngine.FromPersistenceSnapshot(loaded);
                    AssertEquivalent(reference, reopenedAtDeparture);
                    var persistedParty = Assert.Single(loaded.MigrationState!.InTransitParties);
                    Assert.Equal(fixture.Returning, persistedParty.Returning);
                    Assert.Equal(MigrationJourneyKind.Founding, persistedParty.JourneyKind);
                    if (fixture.Returning) Assert.Equal(persistedParty.OriginSettlementId, persistedParty.DestinationSettlementId);

                    reference.AdvanceUntil(fixture.MidTravelMinute);
                    reopenedAtDeparture.AdvanceUntil(fixture.MidTravelMinute);
                    var midTravel = reopenedAtDeparture.CreatePersistenceSnapshot();
                    Assert.NotEmpty(midTravel.MigrationState!.InTransitParties);
                    Assert.NotEqual(fixture.OriginLocation, midTravel.MigrationState.InTransitParties.Single().Location);
                    Assert.NotEqual(fixture.DestinationSite, midTravel.MigrationState.InTransitParties.Single().Location);
                    Assert.Equal(fixture.CargoQuantity,
                        midTravel.MigrationState.InTransitParties.Single().Cargo.Sum(x => x.Quantity));
                    MigrationValidation.Validate(midTravel);
                    AssertEquivalent(reference, reopenedAtDeparture);
                    await database.CreateCheckpointStore().CheckpointAsync(midTravel);
                }

                SimulationEngine reopenedAtMidTravel;
                await using (var database = await WorldDatabase.OpenAsync(path))
                {
                    var loaded = await database.CreateCheckpointStore().LoadAsync();
                    MigrationValidation.Validate(loaded);
                    reopenedAtMidTravel = SimulationEngine.FromPersistenceSnapshot(loaded);
                    AssertEquivalent(reference, reopenedAtMidTravel);
                }

                reference.AdvanceUntil(fixture.ArrivalMinute);
                reopenedAtMidTravel.AdvanceUntil(new WorldMinute(fixture.MidTravelMinute.Value + 180));
                reopenedAtMidTravel.AdvanceUntil(fixture.ArrivalMinute);
                AssertEquivalent(reference, reopenedAtMidTravel);
                var arrived = reopenedAtMidTravel.CreatePersistenceSnapshot();
                Assert.Empty(arrived.MigrationState!.InTransitParties);
                Assert.Equal(!fixture.Returning, arrived.MigrationState.DaughterSettlement is not null);
                MigrationValidation.Validate(arrived);

                await using (var database = await WorldDatabase.OpenAsync(path))
                    await database.CreateCheckpointStore().CheckpointAsync(arrived);

                await using (var database = await WorldDatabase.OpenAsync(path))
                {
                    var loaded = await database.CreateCheckpointStore().LoadAsync();
                    var reopenedAfterResolution = SimulationEngine.FromPersistenceSnapshot(loaded);
                    AssertEquivalent(reopenedAtMidTravel, reopenedAfterResolution);
                    var later = arrived.WorldMinute.Add(360);
                    reference.AdvanceUntil(later);
                    reopenedAfterResolution.AdvanceUntil(later);
                    AssertEquivalent(reference, reopenedAfterResolution);
                    if (fixture.Returning)
                    {
                        Assert.Null(loaded.MigrationState!.DaughterSettlement);
                        Assert.True(loaded.Settlement!.FoodStored >= fixture.OriginFoodAfterWithdrawal + fixture.CargoQuantity);
                    }
                    else
                    {
                        Assert.NotNull(loaded.MigrationState!.DaughterSettlement);
                        Assert.Equal(fixture.CargoQuantity,
                            loaded.MigrationState.DaughterSettlement.CommunalStock.FoodStored);
                    }
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FoundingPartyNeedPauseAndTravelResumeReplayAcrossSqliteReopen(bool pauseForRest)
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-travel-needs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fixture = CreateFoundingPartyFixture(new WorldSeed(pauseForRest ? 1403UL : 1402UL), returning: false);
            var partyAtDeparture = Assert.Single(fixture.Snapshot.MigrationState!.InTransitParties);
            var travelerId = partyAtDeparture.CitizenIds[0];
            var travelerAtDeparture = fixture.Snapshot.Citizens.Single(x => x.Id.Value == travelerId);
            travelerAtDeparture.Needs = new CitizenNeeds(pauseForRest ? 100 : 9000,
                pauseForRest ? 9000 : 100, travelerAtDeparture.Needs.Shelter, travelerAtDeparture.Needs.Social);
            travelerAtDeparture.NeedsUpdatedMinute = fixture.Snapshot.WorldMinute.Value;

            var reference = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);
            reference.AdvanceUntil(fixture.MidTravelMinute);
            var activeNeedAction = pauseForRest ? CitizenAction.Rest : CitizenAction.Eat;
            var pausedTraveler = Assert.IsType<Citizen>(reference.GetCitizen(new CitizenId(travelerId)));
            Assert.Equal(activeNeedAction, pausedTraveler.CurrentAction);
            Assert.Equal(fixture.OriginLocation, pausedTraveler.Location);
            var pausedSnapshot = reference.CreatePersistenceSnapshot();
            var pausedParty = Assert.Single(pausedSnapshot.MigrationState!.InTransitParties);
            Assert.Equal(partyAtDeparture.Id, pausedParty.Id);
            Assert.Equal(partyAtDeparture.DestinationSite, pausedParty.DestinationSite);
            MigrationValidation.Validate(pausedSnapshot);

            var path = Path.Combine(root, "world.db");
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(pausedSnapshot);

            SimulationEngine reopenedDuringNeedAction;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                reopenedDuringNeedAction = SimulationEngine.FromPersistenceSnapshot(loaded);
                AssertEquivalent(reference, reopenedDuringNeedAction);
            }

            var actionEnds = pausedTraveler.ActionCompletesMinute!.Value;
            reference.AdvanceUntil(actionEnds);
            reopenedDuringNeedAction.AdvanceUntil(actionEnds);
            AssertEquivalent(reference, reopenedDuringNeedAction);
            var resumedTraveler = Assert.IsType<Citizen>(reference.GetCitizen(new CitizenId(travelerId)));
            Assert.Equal(CitizenAction.Explore, resumedTraveler.CurrentAction);
            Assert.Equal(fixture.DestinationSite, resumedTraveler.ActionTarget);
            Assert.Equal(fixture.OriginLocation, resumedTraveler.Location);
            if (pauseForRest)
                Assert.True(resumedTraveler.Needs.Rest < pausedTraveler.Needs.Rest);
            else
            {
                Assert.True(resumedTraveler.Needs.Hunger < pausedTraveler.Needs.Hunger);
                var resumedParty = Assert.Single(reference.CreatePersistenceSnapshot().MigrationState!.InTransitParties);
                Assert.True(pausedParty.Cargo.Sum(x => x.Quantity) > resumedParty.Cargo.Sum(x => x.Quantity));
            }

            var nextTravel = reference.CreatePersistenceSnapshot().ScheduledEvents
                .Where(x => x.Name == CitizenEventNames.MoveStep &&
                    IsForCitizen(x, travelerId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .OrderBy(x => x.Order).First();
            reference.AdvanceUntil(nextTravel.Order.DueWorldMinute);
            reopenedDuringNeedAction.AdvanceUntil(nextTravel.Order.DueWorldMinute);
            AssertEquivalent(reference, reopenedDuringNeedAction);
            var travelingSnapshot = reference.CreatePersistenceSnapshot();
            var travelingParty = Assert.Single(travelingSnapshot.MigrationState!.InTransitParties);
            var physicallyMoved = reference.GetCitizen(new CitizenId(travelerId))!;
            Assert.NotEqual(fixture.OriginLocation, physicallyMoved.Location);
            Assert.NotEqual(fixture.DestinationSite, physicallyMoved.Location);
            Assert.Equal(partyAtDeparture.Id, travelingParty.Id);
            Assert.Equal(partyAtDeparture.DestinationSite, travelingParty.DestinationSite);
            MigrationValidation.Validate(travelingSnapshot);
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(travelingSnapshot);
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                AssertEquivalent(reference, SimulationEngine.FromPersistenceSnapshot(loaded));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VisitOutboundDwellAndReturnReplayAcrossSqliteReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-visit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fixture = CreateVisitFixture(new WorldSeed(1401));
            var path = Path.Combine(root, "world.db");
            var reference = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);

            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(fixture.Snapshot);

            SimulationEngine reopenedAtDeparture;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                MigrationValidation.Validate(loaded);
                reopenedAtDeparture = SimulationEngine.FromPersistenceSnapshot(loaded);
                AssertEquivalent(reference, reopenedAtDeparture);
                Assert.Single(loaded.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitDeparted);
                Assert.DoesNotContain(loaded.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
                var party = Assert.Single(loaded.MigrationState!.InTransitParties);
                Assert.Equal(MigrationJourneyKind.Visit, party.JourneyKind);
                Assert.Equal(MigrationVisitPhase.Outbound, party.VisitPhase);
                Assert.Equal(fixture.RelativeId, party.VisitRelativeId);
                Assert.All(party.Cargo, stack =>
                {
                    Assert.Equal(MigrationCargoGood.Food, stack.Good);
                    Assert.Equal(MigrationCargoPurpose.Provisions, stack.Purpose);
                });

                var arrivalMinute = checked(party.DepartedMinute + party.RemainingPathCost);
                var outboundMidpoint = new WorldMinute(party.DepartedMinute + party.RemainingPathCost / 2);
                Assert.InRange(arrivalMinute - fixture.Snapshot.WorldMinute.Value, 0, 180);
                AdvanceVisitUntil(reference, new WorldMinute(arrivalMinute), fixture.Snapshot.WorldMinute);
                AdvanceVisitUntil(reopenedAtDeparture, outboundMidpoint, fixture.Snapshot.WorldMinute);
                AdvanceVisitUntil(reopenedAtDeparture, new WorldMinute(arrivalMinute), fixture.Snapshot.WorldMinute);
                var midTravel = reopenedAtDeparture.CreatePersistenceSnapshot();
                Assert.Equal(MigrationVisitPhase.Dwell, Assert.Single(midTravel.MigrationState!.InTransitParties).VisitPhase);
                Assert.Single(midTravel.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitDeparted);
                Assert.DoesNotContain(midTravel.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
                AssertEquivalent(reference, reopenedAtDeparture);
                await database.CreateCheckpointStore().CheckpointAsync(midTravel);
            }

            SimulationEngine reopenedAtDwell;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                MigrationValidation.Validate(loaded);
                reopenedAtDwell = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Single(loaded.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitDeparted);
                Assert.DoesNotContain(loaded.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
                Assert.Equal(MigrationVisitPhase.Dwell, Assert.Single(loaded.MigrationState!.InTransitParties).VisitPhase);
                AssertEquivalent(reference, reopenedAtDwell);

                var dwellEnd = new WorldMinute(Assert.Single(loaded.MigrationState.InTransitParties).VisitDwellEndsMinute!.Value);
                Assert.InRange(dwellEnd.Value - fixture.Snapshot.WorldMinute.Value, 0, 180);
                AdvanceVisitUntil(reference, dwellEnd, fixture.Snapshot.WorldMinute);
                AdvanceVisitUntil(reopenedAtDwell, dwellEnd, fixture.Snapshot.WorldMinute);
                var returning = Assert.Single(reopenedAtDwell.CreatePersistenceSnapshot().MigrationState!.InTransitParties);
                Assert.Equal(MigrationVisitPhase.Returning, returning.VisitPhase);
                Assert.True(returning.Returning);
                AssertEquivalent(reference, reopenedAtDwell);
                await database.CreateCheckpointStore().CheckpointAsync(reopenedAtDwell.CreatePersistenceSnapshot());
            }

            SimulationEngine reopenedAtReturn;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                MigrationValidation.Validate(loaded);
                reopenedAtReturn = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Equal(MigrationVisitPhase.Returning, Assert.Single(loaded.MigrationState!.InTransitParties).VisitPhase);
                AssertEquivalent(reference, reopenedAtReturn);

                var party = Assert.Single(loaded.MigrationState.InTransitParties);
                var returnArrival = new WorldMinute(checked(reopenedAtReturn.CurrentMinute.Value + party.RemainingPathCost));
                var returnMidpoint = new WorldMinute(reopenedAtReturn.CurrentMinute.Value + party.RemainingPathCost / 2);
                Assert.InRange(returnArrival.Value - fixture.Snapshot.WorldMinute.Value, 0, 180);
                AdvanceVisitUntil(reference, returnArrival, fixture.Snapshot.WorldMinute);
                AdvanceVisitUntil(reopenedAtReturn, returnMidpoint, fixture.Snapshot.WorldMinute);
                AdvanceVisitUntil(reopenedAtReturn, returnArrival, fixture.Snapshot.WorldMinute);
                var completed = reopenedAtReturn.CreatePersistenceSnapshot();
                Assert.Empty(completed.MigrationState!.InTransitParties);
                Assert.Single(completed.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitDeparted);
                Assert.Single(completed.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
                Assert.Equal(fixture.LastAttemptYear, completed.MigrationState.LastVisitAttemptYear);
                Assert.Equal(fixture.OriginFoodBeforeVisit, completed.Settlement!.FoodStored);
                AssertEquivalent(reference, reopenedAtReturn);
                await database.CreateCheckpointStore().CheckpointAsync(completed);
            }

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                MigrationValidation.Validate(loaded);
                var afterReturn = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Empty(loaded.MigrationState!.InTransitParties);
                Assert.Single(loaded.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitDeparted);
                Assert.Single(loaded.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
                AssertEquivalent(reopenedAtReturn, afterReturn);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RelocationPartyMidReturnRestoresPrivateCargoAcrossSqliteReopen()
    {
        var snapshot = CreateNearOriginRelocationReturningSnapshot(new WorldSeed(921));
        var partyAtDeparture = Assert.Single(snapshot.MigrationState!.InTransitParties);
        Assert.Equal(MigrationJourneyKind.Relocation, partyAtDeparture.JourneyKind);
        Assert.True(partyAtDeparture.Returning);
        Assert.Equal(partyAtDeparture.OriginSettlementId, partyAtDeparture.DestinationSettlementId);
        var householdId = partyAtDeparture.HouseholdId;
        var initialStock = snapshot.Economy!.Households.Single(x => x.HouseholdId == householdId).Holdings;
        var reference = SimulationEngine.FromPersistenceSnapshot(snapshot);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-relocation-return-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(snapshot);

            SimulationEngine reopenedAtDeparture;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                reopenedAtDeparture = SimulationEngine.FromPersistenceSnapshot(await database.CreateCheckpointStore().LoadAsync());
                AssertEquivalent(reference, reopenedAtDeparture);
                var midreturn = reopenedAtDeparture.CreatePersistenceSnapshot();
                var inTransit = Assert.Single(midreturn.MigrationState!.InTransitParties);
                Assert.Equal(MigrationJourneyKind.Relocation, inTransit.JourneyKind);
                Assert.True(inTransit.Returning);
                Assert.Equal(new Goods(0, 5, 3), inTransit.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Cargo)
                    .Aggregate(new Goods(), (sum, stack) => stack.Good switch
                    {
                        MigrationCargoGood.Food => sum.Add(ResourceType.Food, stack.Quantity),
                        MigrationCargoGood.Wood => sum.Add(ResourceType.Wood, stack.Quantity),
                        MigrationCargoGood.Stone => sum.Add(ResourceType.Stone, stack.Quantity),
                        _ => sum
                    }));
                Assert.NotEqual(midreturn.World!.StartingSite, inTransit.Location);
                AssertEquivalent(reference, reopenedAtDeparture);
                await database.CreateCheckpointStore().CheckpointAsync(midreturn);
            }

            SimulationEngine reopenedAtMidreturn;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                MigrationValidation.Validate(loaded);
                reopenedAtMidreturn = SimulationEngine.FromPersistenceSnapshot(loaded);
                AssertEquivalent(reference, reopenedAtMidreturn);
            }

            ResolveRelocationReturn(reference);
            ResolveRelocationReturn(reopenedAtMidreturn);
            var arrived = reopenedAtMidreturn.CreatePersistenceSnapshot();
            Assert.Empty(arrived.MigrationState!.InTransitParties);
            var restored = arrived.Economy!.Households.Single(x => x.HouseholdId == householdId).Holdings;
            Assert.Equal(initialStock.Wood + 5, restored.Wood);
            Assert.Equal(initialStock.Stone + 3, restored.Stone);
            AssertEquivalent(reference, reopenedAtMidreturn);

            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(arrived);
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                var afterResolution = SimulationEngine.FromPersistenceSnapshot(loaded);
                AssertEquivalent(reopenedAtMidreturn, afterResolution);
                var reopenedStock = loaded.Economy!.Households.Single(x => x.HouseholdId == householdId).Holdings;
                Assert.Equal(initialStock.Wood + 5, reopenedStock.Wood);
                Assert.Equal(initialStock.Stone + 3, reopenedStock.Stone);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ResolveRelocationReturn(SimulationEngine engine)
    {
        var party = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        foreach (var id in party.CitizenIds)
        {
            var member = citizens[id];
            if (!member.IsAlive) continue;
            member.Location = party.DestinationSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = party.DestinationSite;
        }
        var survivor = party.CitizenIds.Select(id => citizens[id]).First(x => x.IsAlive);
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", survivor)!);
    }

    private static SimulationPersistenceSnapshot CreateNearOriginRelocationReturningSnapshot(WorldSeed seed)
    {
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var state = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var households = GetPrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var (household, members) = households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .Select(h => (Household: h, Members: citizens.Values.Where(c => c.IsAlive && c.HouseholdId == h.Id)
                .OrderBy(c => c.Id.Value).ToArray())).First(x => x.Members.Length > 0);
        var neighbor = engine.World.Tiles.Where(x => x.Walkable && x.Coordinate != engine.World.StartingSite)
            .OrderBy(x => Math.Abs(x.Coordinate.X - engine.World.StartingSite.X) + Math.Abs(x.Coordinate.Y - engine.World.StartingSite.Y))
            .ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .Select(x => (Tile: x, Path: FindRoute(engine, x.Coordinate, engine.World.StartingSite)))
            .First(x => x.Path is { Count: >= 2 });
        foreach (var member in members) member.Location = neighbor.Tile.Coordinate;
        var daughterSite = engine.World.Tiles.Where(x => x.Coordinate != engine.World.StartingSite &&
                x.Coordinate != neighbor.Tile.Coordinate && x.Walkable)
            .Select(x => x.Coordinate).First();
        var daughter = new MigrationDaughterSettlementState(daughterSite,
            new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                engine.CurrentMinute.Value, engine.CurrentMinute.Value),
            Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray());
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(state.Version, state.CitizenResidences,
            state.HouseholdResidences, state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            daughter, state.InTransitParties, state.FoundingPressure, state.LastRelocations));
        var stocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        stocks[household.Id.Value] = stocks[household.Id.Value] with
        {
            Holdings = stocks[household.Id.Value].Holdings.Add(ResourceType.Wood, 5).Add(ResourceType.Stone, 3)
        };
        var produced = GetPrivateField<Goods>(engine, "_producedGoods").Add(ResourceType.Food, 10)
            .Add(ResourceType.Wood, 5).Add(ResourceType.Stone, 3);
        SetPrivateField(engine, "_producedGoods", produced);
        InvokePrivate(engine, "AddPrivate", household.Id.Value, ResourceType.Wood, -5L);
        InvokePrivate(engine, "AddPrivate", household.Id.Value, ResourceType.Stone, -3L);

        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var partyId = counters.AllocateMigrationPartyId();
        var cargo = new[]
        {
            new MigrationCargoStackState(counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Food, 10,
                MigrationCargoPurpose.Provisions),
            new MigrationCargoStackState(counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Wood, 5,
                MigrationCargoPurpose.Cargo),
            new MigrationCargoStackState(counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Stone, 3,
                MigrationCargoPurpose.Cargo)
        };
        var party = new MigrationTransitPartyState(partyId, household.Id.Value, 1, 1,
            neighbor.Tile.Coordinate, engine.World.StartingSite, members.Select(x => x.Id.Value).ToArray(), cargo,
            checked((int)PathCost(engine.World, neighbor.Path!)), engine.CurrentMinute.Value, returning: true,
            journeyKind: MigrationJourneyKind.Relocation);
        InvokePrivate(engine, "SetMigrationParty", party);
        foreach (var member in members) InvokePrivate(engine, "StartFoundingTravel", member, engine.World.StartingSite);
        return engine.CreatePersistenceSnapshot();
    }

    private sealed record VisitFixture(SimulationPersistenceSnapshot Snapshot, long VisitorId, long RelativeId,
        long LastAttemptYear, int OriginFoodBeforeVisit);

    private static void AdvanceVisitUntil(SimulationEngine engine, WorldMinute target, WorldMinute visitStart)
    {
        if (target.Value - visitStart.Value > 180)
            throw new InvalidOperationException("The SQLite visit replay exceeded its bounded near-site horizon.");
        engine.AdvanceUntil(target);
    }

    private static VisitFixture CreateVisitFixture(WorldSeed seed)
    {
        var baseline = new SimulationEngine(seed,
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var world = baseline.World!;
        var households = baseline.Households.Where(x => x.DissolvedMinute is null)
            .Select(household => (Household: household, Member: baseline.Citizens.Where(x => x.IsAlive && x.HouseholdId == household.Id)
                .OrderBy(x => x.Id.Value).FirstOrDefault()))
            .Where(x => x.Member is not null).OrderBy(x => x.Member!.Id.Value).ToArray();
        var pair = households.SelectMany(visitor => households.Where(relative => relative.Household.Id != visitor.Household.Id &&
                visitor.Member!.Id.Value < relative.Member!.Id.Value)
            .Select(relative => (Visitor: visitor, Relative: relative))).First();
        var visitorId = pair.Visitor.Member!.Id.Value;
        var relativeId = pair.Relative.Member!.Id.Value;
        baseline.Citizens.Single(x => x.Id.Value == visitorId).Location = world.StartingSite;
        var parentId = baseline.Citizens.Where(x => x.Id.Value > visitorId && x.Id.Value != relativeId && x.IsAlive)
            .OrderBy(x => x.Id.Value).First(x => x.HouseholdId != pair.Relative.Household.Id).Id.Value;
        var daughterSite = world.Tiles.Where(x => x.Coordinate != world.StartingSite && x.Walkable &&
                Math.Abs(x.Coordinate.X - world.StartingSite.X) <= 1 &&
                Math.Abs(x.Coordinate.Y - world.StartingSite.Y) <= 1)
            .Select(x => (x.Coordinate, Route: DeterministicPathfinder.Find(world, world.StartingSite, x.Coordinate)))
            .Where(x => x.Route is { Count: >= 2 })
            .OrderBy(x => PathCost(world, x.Route!)).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .First().Coordinate;
        var relativeHouseholdId = pair.Relative.Household.Id.Value;
        foreach (var citizen in baseline.Citizens.Where(x => x.HouseholdId?.Value == relativeHouseholdId))
        {
            citizen.Location = daughterSite;
            citizen.HomeStructureId = null;
        }

        var state = baseline.MigrationState!;
        var migration = new MigrationWorldState(state.Version,
            baseline.Citizens.Select(x => new MigrationEntityResidence(x.Id.Value,
                x.HouseholdId?.Value == relativeHouseholdId ? 2 : 1)).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value,
                x.Id.Value == relativeHouseholdId ? 2 : 1)).ToArray(),
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                    baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()));
        var snapshot = CopySnapshot(baseline, baseline.Settlement!, migration.ToCanonicalJson(), baseline.Citizens);
        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        citizens[visitorId].ParentAId = new CitizenId(parentId);
        citizens[relativeId].ParentAId = new CitizenId(parentId);
        var originFoodBeforeVisit = engine.Settlement.FoodStored;
        InvokePrivate(engine, "EvaluateMigrationVisits");
        citizens[visitorId].ParentAId = null;
        citizens[relativeId].ParentAId = null;
        var visitSnapshot = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(visitSnapshot);
        var party = Assert.Single(visitSnapshot.MigrationState!.InTransitParties);
        Assert.Equal(MigrationJourneyKind.Visit, party.JourneyKind);
        return new VisitFixture(visitSnapshot, visitorId, relativeId,
            visitSnapshot.MigrationState.LastVisitAttemptYear!.Value, originFoodBeforeVisit);
    }

    private sealed record FoundingPartyFixture(SimulationPersistenceSnapshot Snapshot, WorldMinute MidTravelMinute,
        WorldMinute ArrivalMinute, TileCoordinate OriginLocation, TileCoordinate DestinationSite, long CargoQuantity, int OriginFoodAfterWithdrawal,
        bool Returning);

    private static FoundingPartyFixture CreateFoundingPartyFixture(WorldSeed seed, bool returning)
    {
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var households = GetPrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var choices = households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .Select(household => (Household: household, Members: citizens.Values
                .Where(x => x.IsAlive && x.HouseholdId == household.Id).OrderBy(x => x.Id.Value).ToArray()))
            .Where(x => x.Members.Length > 0 && x.Members.Any(c => c.AgeYears(engine.CurrentMinute) >= 18))
            .OrderBy(x => x.Members.Length).ThenBy(x => x.Household.Id.Value).ToArray();
        var (household, members) = choices.First();
        var representative = members[0];
        var foodPerTraveler = checked(((long)NeedsProjection.HungerRatePerMinute * 14 * WorldCalendar.MinutesPerDay *
            CitizenSimulationRules.MealFoodUnits + CitizenSimulationRules.FullHungerReduction - 1) /
            CitizenSimulationRules.FullHungerReduction);
        var cargoQuantity = checked(foodPerTraveler * members.Length);
        if ((long)engine.Settlement.FoodStored + (long)InvokePrivate(engine, "AvailablePrivate", household.Id.Value, ResourceType.Food)! < cargoQuantity)
        {
            var shortage = checked((int)(cargoQuantity - engine.Settlement.FoodStored -
                (long)InvokePrivate(engine, "AvailablePrivate", household.Id.Value, ResourceType.Food)!));
            engine.Settlement.FoodStored = checked(engine.Settlement.FoodStored + shortage);
            InvokePrivate(engine, "RecordCommunalFoodProduction", shortage);
        }
        Assert.True((bool)InvokePrivate(engine, "TryWithdrawFoundingFood", household.Id.Value, cargoQuantity)!);
        var originFoodAfterWithdrawal = engine.Settlement.FoodStored;

        TileCoordinate destination;
        IReadOnlyList<TileCoordinate> route;
        if (returning)
        {
            destination = engine.World.StartingSite;
            var far = SelectFarWalkableTile(engine, representative.Location);
            foreach (var member in members) member.Location = far.Location;
            route = AssertRoute(engine, representative.Location, destination);
        }
        else
        {
            (destination, route) = SelectFoundingTravelSite(engine, members);
        }

        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var partyId = counters.AllocateMigrationPartyId();
        var cargoId = counters.AllocateMigrationCargoStackId();
        var party = new MigrationTransitPartyState(partyId, household.Id.Value, 1, returning ? 1 : null,
            representative.Location, destination, members.Select(x => x.Id.Value).ToArray(),
            [new MigrationCargoStackState(cargoId, MigrationCargoGood.Food, cargoQuantity, MigrationCargoPurpose.Provisions)],
            checked((int)PathCost(engine.World, route)), engine.CurrentMinute.Value, returning);
        InvokePrivate(engine, "SetMigrationParty", party);
        foreach (var member in members) InvokePrivate(engine, "StartFoundingTravel", member, destination);
        var snapshot = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(snapshot);
        var representativeId = representative.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var firstMovement = snapshot.ScheduledEvents.Where(x => x.Name == CitizenEventNames.MoveStep &&
                IsForCitizen(x, representativeId))
            .OrderBy(x => x.Order).First();
        var maxRouteCost = members.Max(member => PathCost(engine.World,
            AssertRoute(engine, member.Location, destination)));
        var arrival = new WorldMinute(checked(engine.CurrentMinute.Value + maxRouteCost + 720));
        return new FoundingPartyFixture(snapshot, firstMovement.Order.DueWorldMinute, arrival,
            returning ? route[0] : representative.Location, destination, cargoQuantity, originFoodAfterWithdrawal, returning);
    }

    private static (TileCoordinate Destination, IReadOnlyList<TileCoordinate> Route) SelectFoundingTravelSite(
        SimulationEngine engine, IReadOnlyList<Citizen> members)
    {
        var living = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
        var occupied = engine.Structures.Select(x => x.Location).Concat(living.Fields.Select(x => x.Location))
            .Concat(living.Facilities.Select(x => x.Location))
            .Concat(living.Orders.Where(x => x.Kind is LivingWorkKind.EstablishField or LivingWorkKind.BuildHearth or
                LivingWorkKind.BuildLoom or LivingWorkKind.BuildCareHouse).Select(x => x.Location)).ToHashSet();
        var resources = engine.World.Resources.Select(x => x.Coordinate).ToHashSet();
        var candidates = engine.World.Tiles.Where(x => x.Walkable && x.Buildable && x.Terrain != TerrainType.Freshwater &&
                x.Coordinate != engine.World.StartingSite && !occupied.Contains(x.Coordinate) && !resources.Contains(x.Coordinate))
            .Select(x => (x.Coordinate, Route: FindRoute(engine, engine.World.StartingSite, x.Coordinate)))
            .Where(x => x.Route is { Count: >= 2 } && PathCost(engine.World, x.Route) >= 32)
            .Select(x => (x.Coordinate, x.Route!, Cost: PathCost(engine.World, x.Route!),
                Distance: Math.Abs(x.Coordinate.X - engine.World.StartingSite.X) + Math.Abs(x.Coordinate.Y - engine.World.StartingSite.Y)))
            .OrderBy(x => x.Cost).ThenBy(x => x.Distance).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X);
        foreach (var candidate in candidates)
        {
            var routes = members.Select(member => FindRoute(engine, member.Location, candidate.Coordinate)).ToArray();
            if (routes.All(x => x is { Count: >= 3 })) return (candidate.Coordinate, routes[0]!);
        }
        throw new InvalidOperationException("No reachable M14 travel fixture site has multiple movement steps.");
    }

    private static (TileCoordinate Location, IReadOnlyList<TileCoordinate> Route) SelectFarWalkableTile(
        SimulationEngine engine, TileCoordinate from)
    {
        var tile = engine.World.Tiles.Where(x => x.Walkable && x.Coordinate != engine.World.StartingSite)
            .Select(x => (Tile: x, Route: FindRoute(engine, engine.World.StartingSite, x.Coordinate)))
            .Where(x => x.Route is { Count: >= 2 } && PathCost(engine.World, x.Route) >= 32)
            .OrderBy(x => PathCost(engine.World, x.Route!)).ThenBy(x => x.Tile.Coordinate.Y).ThenBy(x => x.Tile.Coordinate.X)
            .First(x => FindRoute(engine, from, x.Tile.Coordinate) is { Count: >= 3 });
        return (tile.Tile.Coordinate, AssertRoute(engine, from, tile.Tile.Coordinate));
    }

    private static IReadOnlyList<TileCoordinate> AssertRoute(SimulationEngine engine, TileCoordinate from, TileCoordinate to) =>
        FindRoute(engine, from, to) ?? throw new InvalidOperationException("Founding test route is not walkable.");

    private static IReadOnlyList<TileCoordinate>? FindRoute(SimulationEngine engine, TileCoordinate from, TileCoordinate to) =>
        InvokePrivate(engine, "FindPathCached", from, to) as IReadOnlyList<TileCoordinate>;

    private static long PathCost(WorldMap world, IReadOnlyList<TileCoordinate> path)
    {
        long result = 0;
        for (var index = 1; index < path.Count; index++)
        {
            var from = path[index - 1];
            var to = path[index];
            result = checked(result + (long)(from.X == to.X || from.Y == to.Y ? 10 : 14) * world.GetTile(to).MovementCost);
        }
        return result;
    }

    private static bool IsForCitizen(ScheduledEventSnapshot item, string citizenId)
    {
        using var document = JsonDocument.Parse(item.PayloadJson);
        return document.RootElement.TryGetProperty("citizenId", out var value) &&
            value.GetString() == citizenId;
    }

    private static void AssertEquivalent(SimulationEngine expected, SimulationEngine actual)
    {
        var a = expected.CreatePersistenceSnapshot();
        var b = actual.CreatePersistenceSnapshot();
        Assert.Equal(a.WorldMinute, b.WorldMinute);
        Assert.Equal(a.Citizens, b.Citizens);
        Assert.Equal(a.ScheduledEvents, b.ScheduledEvents);
        Assert.Equal(a.MigrationStateJson, b.MigrationStateJson);
        Assert.Equal(a.Economy!.ToCanonicalJson(), b.Economy!.ToCanonicalJson());
        Assert.Equal(a.LivingStateJson, b.LivingStateJson);
        Assert.Equal(expected.SurvivalFingerprint, actual.SurvivalFingerprint);
        Assert.Equal(expected.HistoryFingerprint, actual.HistoryFingerprint);
        Assert.Equal(expected.CreateMigrationReadSnapshot().Fingerprint, actual.CreateMigrationReadSnapshot().Fingerprint);
    }

    private static T GetPrivateField<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Could not read private field '{name}'."));

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(instance, value);

    private static object? InvokePrivate(object instance, string name, params object?[] arguments)
    {
        var method = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length &&
                candidate.GetParameters().Select((parameter, index) =>
                    arguments[index] is null ? !parameter.ParameterType.IsValueType :
                    parameter.ParameterType.IsInstanceOfType(arguments[index])).All(x => x));
        if (method is null) throw new InvalidOperationException($"Could not invoke private method '{name}'.");
        return method.Invoke(instance, arguments);
    }

    private static SimulationPersistenceSnapshot CopySnapshot(SimulationPersistenceSnapshot snapshot,
        SettlementState settlement, string migrationStateJson, IReadOnlyList<Citizen> citizens,
        string? livingStateJson = null, EconomyState? economy = null,
        DeterministicCountersSnapshot? counters = null) =>
        new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion,
            snapshot.ApplicationVersion, snapshot.WorldConfiguration, counters ?? snapshot.Counters, snapshot.ScheduledEvents,
            snapshot.World, citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, settlement,
            snapshot.SurvivalVersion, snapshot.SettlementVersion, snapshot.Structures,
            snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households,
            snapshot.HistoryVersion, snapshot.HistoryState, snapshot.HistoricalEvents,
            snapshot.HistoricalEventCitizens, snapshot.HistoricalEventStructures, snapshot.StatisticsSamples,
            snapshot.Memories, snapshot.Agriculture, economy ?? snapshot.Economy, livingStateJson ?? snapshot.LivingStateJson,
            migrationStateJson);
}
