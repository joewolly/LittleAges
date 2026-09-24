using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class UnifiedRulesTests
{
    [Fact]
    public void M14IsCurrentDefaultWhileM13RemainsExplicitlyAvailable()
    {
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, SimulationEngine.CurrentSimulationRulesVersion);

        var m13 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);

        Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, m13.SimulationRulesVersion);
        Assert.NotNull(m13.CreatePersistenceSnapshot().LivingStateJson);
    }

    [Fact]
    public void M13ComposesM12AndLivingWithoutDuplicateFieldsAndCarriesCanonicalGrain()
    {
        var m12 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.SpacedSimulationRulesVersion);
        Assert.Equal(SimulationEngine.SpacedSimulationRulesVersion, m12.SimulationRulesVersion);
        Assert.Null(m12.LivingStateJson);

        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var fresh = engine.CreatePersistenceSnapshot();
        Assert.NotNull(fresh.Agriculture);
        Assert.NotNull(fresh.Economy);
        Assert.NotNull(fresh.LivingStateJson);

        var deadline = new WorldMinute(190L * WorldCalendar.MinutesPerDay);
        engine.AdvanceUntil(new WorldMinute(180L * WorldCalendar.MinutesPerDay));
        LivingWorkOrder[] harvestCargo = [];
        while (engine.CurrentMinute < deadline)
        {
            var state = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
            Assert.Empty(state.Fields);
            Assert.DoesNotContain(state.Orders, x => x.Kind is LivingWorkKind.EstablishField or LivingWorkKind.Sow or LivingWorkKind.Tend);
            harvestCargo = state.Orders.Where(x => x.Kind == LivingWorkKind.Harvest && x.CargoInTransit && x.CitizenId is not null).ToArray();
            if (harvestCargo.Length > 0) break;

            var nextEvent = engine.NextScheduledEventMinute ?? throw new InvalidOperationException("The simulation stopped scheduling events before M13 harvest.");
            engine.AdvanceUntil(nextEvent);
        }

        Assert.NotEmpty(harvestCargo);
        var checkpoint = engine.CreatePersistenceSnapshot();
        var living = LivingWorldCodec.Deserialize(checkpoint.LivingStateJson!);
        Assert.Empty(living.Fields);
        Assert.Equal(0, living.FoodHarvested);
        Assert.All(harvestCargo, order =>
        {
            Assert.True(order.Produced);
            Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, checkpoint.SimulationRulesVersion);
            Assert.True(LivingWorkDefinitions.ValidCargo(order, checkpoint.SimulationRulesVersion));
            var carrier = checkpoint.Citizens.Single(x => x.Id.Value == order.CitizenId);
            Assert.Equal(CitizenAction.HaulHarvest, carrier.CurrentAction);
            Assert.Equal(ResourceType.Food, carrier.CarriedResourceType);
            Assert.True(carrier.CarriedResourceQuantity >= 0);
            Assert.Equal(0, living.Stock.Single(x => x.Good == LivingGood.Grain).Quantity);
        });

        var agriculture = checkpoint.Agriculture!;
        var expectedGrain = agriculture.Harvests.Sum(x => (long)(x.Harvested / 10)) + agriculture.Farms.Sum(x => (long)(x.Harvested / 10));
        Assert.Equal(expectedGrain, living.CommunalGrainHarvested);
        Assert.Equal(agriculture.Harvests.Sum(x => (long)x.Harvested) + agriculture.Farms.Sum(x => (long)x.Harvested),
            living.FarmFoodHarvested + living.CommunalGrainHarvested);

        var interrupted = GetPrivateField<LivingWorldState>(engine, "_living").Orders.Single(x => x.Id == harvestCargo[0].Id);
        var interruptedGrainQuantity = interrupted.Cargo.Single().Quantity;
        var carrier = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens")[interrupted.CitizenId!.Value];
        var carrierLocation = carrier.Location;
        carrier.Health = 0;
        typeof(SimulationEngine).GetMethod("Kill", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(engine, [carrier, false, false, true]);
        var nextPulseMinute = checked((engine.CurrentMinute.Value / SimulationEngine.LivingPulseMinutes + 1) * SimulationEngine.LivingPulseMinutes);
        engine.AdvanceUntil(new WorldMinute(nextPulseMinute));

        var afterDeath = engine.CreatePersistenceSnapshot();
        LivingValidation.Validate(afterDeath);
        var afterDeathLiving = LivingWorldCodec.Deserialize(afterDeath.LivingStateJson!);
        var recoveredGrainOrder = afterDeathLiving.Orders.SingleOrDefault(x => x.Id == interrupted.Id);
        if (recoveredGrainOrder is not null)
        {
            Assert.True(recoveredGrainOrder.Produced);
            Assert.Equal(carrierLocation, recoveredGrainOrder.SupplyLocation);
            Assert.Equal(interruptedGrainQuantity, recoveredGrainOrder.Cargo.Single().Quantity);
        }
        Assert.Equal(afterDeathLiving.CommunalGrainHarvested,
            afterDeathLiving.Stock.Single(x => x.Good == LivingGood.Grain).Quantity +
            afterDeathLiving.Orders.Sum(x => x.Cargo.Where(y => y.Good == LivingGood.Grain).Sum(y => y.Quantity)) +
            afterDeathLiving.CommunalGrainConsumed + afterDeathLiving.CommunalGrainSpoiled);
    }

    [Theory]
    [InlineData(SimulationEngine.SpacedSimulationRulesVersion)]
    [InlineData(SimulationEngine.Living2SimulationRulesVersion)]
    public void RestoringExplicitLegacySnapshotsPreservesTheirRecordedRules(string rulesVersion)
    {
        var snapshot = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rulesVersion)
            .CreatePersistenceSnapshot();

        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);

        Assert.Equal(rulesVersion, snapshot.SimulationRulesVersion);
        Assert.Equal(rulesVersion, restored.SimulationRulesVersion);
        Assert.Equal(snapshot.LivingStateJson, restored.LivingStateJson);
        Assert.Equal(rulesVersion == SimulationEngine.Living2SimulationRulesVersion, snapshot.LivingStateJson is not null);
    }

    [Fact]
    public void M13FarmWorkUsesDeterministicWeatherAndCultivationWhileM12RemainsUnchanged()
    {
        var m13 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var living = GetPrivateField<LivingWorldState>(m13, "_living");
        var farmer = m13.Citizens[0];
        var farmWorkPerShift = typeof(SimulationEngine).GetMethod("FarmWorkPerShift", BindingFlags.Instance | BindingFlags.NonPublic)!;

        living.Weather = LivingWeatherKind.Rain;
        Assert.Equal(120, (int)farmWorkPerShift.Invoke(m13, [farmer])!);
        living.Weather = LivingWeatherKind.Drought;
        Assert.Equal(80, (int)farmWorkPerShift.Invoke(m13, [farmer])!);
        living.People.Single(x => x.CitizenId == farmer.Id.Value).Knowledge.Add(LivingTechnique.Cultivation);
        Assert.Equal(100, (int)farmWorkPerShift.Invoke(m13, [farmer])!);
        living.Weather = LivingWeatherKind.Rain;
        Assert.Equal(140, (int)farmWorkPerShift.Invoke(m13, [farmer])!);

        var m12 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.SpacedSimulationRulesVersion);
        Assert.Equal(100, (int)farmWorkPerShift.Invoke(m12, [m12.Citizens[0]])!);
    }

    [Fact]
    public void M13LivingWorkYieldsToM12FoodAtTheGrowthMealThreshold()
    {
        var unified = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var unifiedCitizen = GetPrivateField<Dictionary<long, Citizen>>(unified, "_citizens").Values.First(x => x.IsAlive);
        unifiedCitizen.Needs = new CitizenNeeds(hunger: 3750);
        unifiedCitizen.NeedsUpdatedMinute = unified.CurrentMinute.Value;
        unified.Settlement.FoodStored = 100;
        var state = GetPrivateField<LivingWorldState>(unified, "_living");
        var order = new LivingWorkOrder
        {
            Id = state.NextId++,
            Kind = LivingWorkKind.Experiment,
            Location = unified.World.StartingSite,
            SupplyLocation = unified.World.StartingSite,
            Technique = LivingTechnique.Cultivation,
            CreatedMinute = unified.CurrentMinute.Value,
            RequiredWork = 480,
            Priority = 2200
        };
        state.Orders.Add(order);

        var startWork = typeof(SimulationEngine).GetMethod("TryStartLivingWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)startWork.Invoke(unified, [unifiedCitizen, 0])!);
        Assert.Null(order.CitizenId);

        order.CitizenId = unifiedCitizen.Id.Value;
        order.ClaimedMinute = unified.CurrentMinute.Value;
        order.Reserved = true;
        order.SuppliesDelivered = true;
        order.Phase = LivingWorkPhase.Work;
        unifiedCitizen.CurrentAction = CitizenAction.LivingWork;
        unifiedCitizen.ActionPhase = CitizenActionPhase.Perform;
        unifiedCitizen.ActionStartedMinute = unified.CurrentMinute;
        unifiedCitizen.ActionCompletesMinute = unified.CurrentMinute.Add(120);

        var completeShift = typeof(SimulationEngine).GetMethod("CompleteLivingShift", BindingFlags.Instance | BindingFlags.NonPublic)!;
        completeShift.Invoke(unified, [unifiedCitizen]);
        Assert.Equal(CitizenAction.None, unifiedCitizen.CurrentAction);
        Assert.Null(order.CitizenId);
        Assert.Equal(120, order.WorkDone);
        Assert.Contains(order, state.Orders);

        unifiedCitizen.CurrentAction = CitizenAction.LivingWork;
        unifiedCitizen.ActionPhase = CitizenActionPhase.TravelToTarget;
        unifiedCitizen.ActionTarget = unified.World.StartingSite;
        var interruptTravel = typeof(SimulationEngine).GetMethod("InterruptUnifiedLivingWorkForFood", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)interruptTravel.Invoke(unified, [unifiedCitizen])!);
        Assert.Equal(CitizenAction.None, unifiedCitizen.CurrentAction);
        Assert.Null(order.CitizenId);
        Assert.Contains(order, state.Orders);
    }

    [Fact]
    public void M13PrioritizesTheOrdinaryGatherFoodWinnerOverLivingWork()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        engine.Settlement.FoodStored = 0;
        var order = AddCompetitiveExperiment(engine);
        var citizen = FindCitizenWithOrdinaryWinner(engine, CitizenAction.GatherFood, hunger: 3000, rest: 0);

        Assert.NotNull(citizen);
        Assert.Equal(CitizenAction.GatherFood, SimulationEngine.SelectDecision(engine.EvaluateDecision(citizen.Id)));
        InvokeDecide(engine, citizen);

        Assert.Equal(CitizenAction.GatherFood, citizen.CurrentAction);
        Assert.Null(order.CitizenId);
    }

    [Fact]
    public void M13StillStartsLivingWorkForANonFoodWinnerAndPreviousLivingRulesKeepTheirPriority()
    {
        var unified = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        unified.Settlement.FoodStored = 0;
        var unifiedOrder = AddCompetitiveExperiment(unified);
        var unifiedCitizen = FindCitizenWithOrdinaryWinner(unified, CitizenAction.Rest, hunger: 0, rest: 4990);

        Assert.NotNull(unifiedCitizen);
        Assert.Equal(CitizenAction.Rest, SimulationEngine.SelectDecision(unified.EvaluateDecision(unifiedCitizen.Id)));
        InvokeDecide(unified, unifiedCitizen);

        Assert.Equal(CitizenAction.LivingWork, unifiedCitizen.CurrentAction);
        Assert.Equal(unifiedCitizen.Id.Value, unifiedOrder.CitizenId);

        var living2 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.Living2SimulationRulesVersion);
        living2.Settlement.FoodStored = 0;
        var living2Order = AddCompetitiveExperiment(living2);
        var living2Citizen = FindCitizenWithOrdinaryWinner(living2, CitizenAction.GatherFood, hunger: 3000, rest: 0);

        Assert.NotNull(living2Citizen);
        Assert.Equal(CitizenAction.GatherFood, SimulationEngine.SelectDecision(living2.EvaluateDecision(living2Citizen.Id)));
        InvokeDecide(living2, living2Citizen);

        Assert.Equal(CitizenAction.LivingWork, living2Citizen.CurrentAction);
        Assert.Equal(living2Citizen.Id.Value, living2Order.CitizenId);
    }

    [Fact]
    public void M13PartnershipsRejectLargeAgeGapsWhileM12AndM13PeerPairsRemainEligible()
    {
        var m13 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var m13Citizens = GetPrivateField<Dictionary<long, Citizen>>(m13, "_citizens");
        var founders = m13.Citizens.Where(x => x.FounderOrdinal is not null).OrderBy(x => x.Id.Value).ToArray();
        var ageGapPair = founders.SelectMany((first, index) => founders.Skip(index + 1).Select(second => (first, second)))
            .First(pair => Math.Abs(pair.first.AgeYears(m13.CurrentMinute) - pair.second.AgeYears(m13.CurrentMinute)) > 20);
        var peerPair = founders.SelectMany((first, index) => founders.Skip(index + 1).Select(second => (first, second)))
            .First(pair => Math.Abs(pair.first.AgeYears(m13.CurrentMinute) - pair.second.AgeYears(m13.CurrentMinute)) <= 20);
        var tryForm = typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!;

        InvokePartnership(tryForm, m13, m13Citizens[ageGapPair.first.Id.Value], m13Citizens[ageGapPair.second.Id.Value]);
        Assert.Null(m13Citizens[ageGapPair.first.Id.Value].PartnerId);
        Assert.Null(m13Citizens[ageGapPair.second.Id.Value].PartnerId);

        InvokePartnership(tryForm, m13, m13Citizens[peerPair.first.Id.Value], m13Citizens[peerPair.second.Id.Value]);
        Assert.Equal(m13Citizens[peerPair.second.Id.Value].Id, m13Citizens[peerPair.first.Id.Value].PartnerId);
        Assert.Equal(m13Citizens[peerPair.first.Id.Value].Id, m13Citizens[peerPair.second.Id.Value].PartnerId);

        foreach (var rules in new[]
        {
            SimulationEngine.SpacedSimulationRulesVersion,
            SimulationEngine.LivingSimulationRulesVersion,
            SimulationEngine.Living2SimulationRulesVersion
        })
        {
            var legacy = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
            var legacyCitizens = GetPrivateField<Dictionary<long, Citizen>>(legacy, "_citizens");
            InvokePartnership(tryForm, legacy, legacyCitizens[ageGapPair.first.Id.Value], legacyCitizens[ageGapPair.second.Id.Value]);
            Assert.Equal(legacyCitizens[ageGapPair.second.Id.Value].Id, legacyCitizens[ageGapPair.first.Id.Value].PartnerId);
            Assert.Equal(legacyCitizens[ageGapPair.first.Id.Value].Id, legacyCitizens[ageGapPair.second.Id.Value].PartnerId);
        }
    }

    [Fact]
    public void M13HarvestValidationDoesNotDoubleCountTheWinterCropHistoryRecord()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var winterBoundary = 3L * WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay;
        var foundRecordedCrop = false;
        for (var year = 0; year < 5 && !foundRecordedCrop; year++)
        {
            engine.AdvanceUntil(new WorldMinute(winterBoundary + year * WorldCalendar.MinutesPerYear));
            var agriculture = engine.CaptureAgriculture()!;
            foundRecordedCrop = agriculture.Harvests.Any(harvest => agriculture.Farms.Any(farm =>
                farm.StructureId == harvest.StructureId && farm.Year == harvest.Year));
        }

        Assert.True(foundRecordedCrop, "The winter checkpoint must include a historical harvest and its still-current dormant crop.");
        var snapshot = engine.CreatePersistenceSnapshot();
        LivingValidation.Validate(snapshot);
    }

    private static T GetPrivateField<T>(SimulationEngine engine, string name) =>
        (T)typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;

    private static LivingWorkOrder AddCompetitiveExperiment(SimulationEngine engine)
    {
        var state = GetPrivateField<LivingWorldState>(engine, "_living");
        var order = new LivingWorkOrder
        {
            Id = state.NextId++,
            Kind = LivingWorkKind.Experiment,
            Location = engine.World.StartingSite,
            SupplyLocation = engine.World.StartingSite,
            Technique = LivingTechnique.Care,
            CreatedMinute = engine.CurrentMinute.Value,
            RequiredWork = 480,
            Priority = 20_000
        };
        state.Orders.Add(order);
        return order;
    }

    private static Citizen? FindCitizenWithOrdinaryWinner(SimulationEngine engine, CitizenAction expected, int hunger, int rest)
    {
        foreach (var citizen in GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
                     .Where(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 13))
        {
            citizen.Needs = new CitizenNeeds(hunger: hunger, rest: rest);
            citizen.NeedsUpdatedMinute = engine.CurrentMinute.Value;
            if (SimulationEngine.SelectDecision(engine.EvaluateDecision(citizen.Id)) == expected) return citizen;
        }
        return null;
    }

    private static void InvokeDecide(SimulationEngine engine, Citizen citizen) =>
        typeof(SimulationEngine).GetMethod("Decide", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [citizen]);

    private static void InvokePartnership(System.Reflection.MethodInfo method, SimulationEngine engine, Citizen first, Citizen second)
    {
        var pair = RelationshipState.Normalize(first.Id, second.Id);
        var relationship = new RelationshipState(pair.A, pair.B, 7000, 6000, 5000, 0, engine.CurrentMinute.Value, 1);
        method.Invoke(engine, [first, second, relationship]);
    }
}
