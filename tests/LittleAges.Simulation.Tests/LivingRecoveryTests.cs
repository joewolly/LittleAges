using System.Reflection;
using LittleAges.Domain;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class LivingRecoveryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void WorkerLossDropsOnlySuppliesActuallyBeingCarried(bool produced, bool pickedUp, bool shouldMove)
    {
        var engine = Create();
        var state = State(engine);
        var worker = Citizens(engine)[0];
        var site = engine.World.StartingSite;
        worker.Location = engine.World.Tiles.First(x => x.Walkable && x.Coordinate != site).Coordinate;
        worker.CurrentAction = CitizenAction.LivingWork;
        worker.ActionPhase = CitizenActionPhase.TravelToTarget;
        var order = new LivingWorkOrder { Id = state.NextId++, Kind = LivingWorkKind.CutFuel, Location = site, SupplyLocation = site,
            CitizenId = worker.Id.Value, Reserved = true, Produced = produced, CargoInTransit = produced && pickedUp,
            Phase = produced ? LivingWorkPhase.Deliver : pickedUp ? LivingWorkPhase.Travel : LivingWorkPhase.Collect,
            Ingredients = [new("Wood", 10)], Cargo = produced ? [new(LivingGood.Fuel, 10)] : [] };
        state.Orders.Add(order);
        Call(engine, "ReleaseLivingClaim", worker);
        Assert.Equal(shouldMove ? worker.Location : site, order.SupplyLocation);
        Assert.Null(order.CitizenId);
        Assert.False(order.CargoInTransit);
        Assert.True(order.Reserved);
    }

    [Fact]
    public void RememberingAHelperDoesNotTreatAFieldWithTheSameIdAsThatPerson()
    {
        var engine = Create();
        var citizen = Citizens(engine)[0];
        var person = State(engine).People[0];
        var harvest = new LivingWorkOrder { Kind = LivingWorkKind.Harvest, SubjectId = 2, Location = citizen.Location };
        var before = (int)Call(engine, "WorkScore", citizen, harvest)!;
        person.Experiences.Add(new(LivingExperienceKind.Helped, 0, 2));
        Assert.Equal(before, (int)Call(engine, "WorkScore", citizen, harvest)!);
    }
    [Fact]
    public void ResourceIndexPreservesCostQuantityAndIdentityTieOrdering()
    {
        var engine = Create();
        var states = (IDictionary<long, ResourceState>)typeof(SimulationEngine).GetField("_resourceStates", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!;
        foreach (var state in states.Values.Where(x => x.ResourceNodeId.Value % 3 == 0)) state.CurrentQuantity = 0;
        foreach (var citizen in Citizens(engine).Take(3))
        {
            var costs = DeterministicPathfinder.ComputeTravelCosts(engine.World, citizen.Location);
            foreach (var type in Enum.GetValues<ResourceType>())
            {
                var expected = engine.World.Resources.Where(x => x.Type == type && states[x.Id.Value].CurrentQuantity > 0 && costs.ContainsKey(x.Coordinate))
                    .OrderBy(x => costs[x.Coordinate]).ThenByDescending(x => states[x.Id.Value].CurrentQuantity).ThenBy(x => x.Id.Value).FirstOrDefault();
                Assert.Equal(expected, (ResourceNode?)Call(engine, "SelectResourceTarget", citizen, type));
            }
        }
    }
    [Fact]
    public void EssentialOpportunityCanReplaceUntouchedOptionalWorkOnAFullBoard()
    {
        var engine = Create();
        var state = State(engine);
        foreach (var citizen in Citizens(engine))
            foreach (var kind in new[] { LivingWorkKind.Recreate, LivingWorkKind.Care, LivingWorkKind.Teach, LivingWorkKind.RepairRelationship })
                state.Orders.Add(new LivingWorkOrder { Id = state.NextId++, Kind = kind, SubjectId = citizen.Id.Value, Priority = 1000, Location = citizen.Location });
        var retained = state.Orders[0];
        retained.Reserved = true;
        retained.WorkDone = 30;
        Call(engine, "RequestLiving", LivingWorkKind.CutFuel, engine.World.StartingSite, 5000, null!, null!, 120, new[] { new LivingIngredient("Wood", 10) });
        Assert.Equal(80, state.Orders.Count);
        Assert.Contains(retained, state.Orders);
        Assert.Single(state.Orders, x => x.Kind == LivingWorkKind.CutFuel);
        foreach (var order in state.Orders) { order.Reserved = true; order.WorkDone = 30; }
        Call(engine, "RequestLiving", LivingWorkKind.Cook, engine.World.StartingSite, 6500, 123L, null!, 120, new[] { new LivingIngredient("Grain", 20), new LivingIngredient("Fuel", 1) });
        Assert.Equal(81, state.Orders.Count);
        Assert.Single(state.Orders, x => x.Kind == LivingWorkKind.Cook);
    }

    [Fact]
    public void FullStorageAllowsConversionAndEscrowIsTransferredExactlyOnce()
    {
        var engine = Create();
        var state = State(engine);
        engine.Settlement.FoodStored = 0;
        state.Stock[0] = new(LivingGood.Grain, 20);
        state.Stock[3] = new(LivingGood.Fuel, 2);
        state.Stock[7] = new(LivingGood.Fiber, 778);
        var order = new LivingWorkOrder { Id = state.NextId++, Kind = LivingWorkKind.Preserve, Location = engine.World.StartingSite,
            Ingredients = [new("Grain", 20), new("Fuel", 2)] };
        state.Orders.Add(order);
        Assert.True((bool)Call(engine, "HasOutputSpace", order)!);
        Assert.True((bool)Call(engine, "Reserve", order)!);
        Assert.Equal(778, state.Stock.Sum(x => x.Quantity));
        Call(engine, "ProduceLiving", Citizens(engine)[0], order);
        order.Produced = true;
        Assert.Equal(new LivingStock(LivingGood.PreservedFood, 20), Assert.Single(order.Cargo));
        Call(engine, "CancelLivingOrder", order);
        Assert.Equal(798, state.Stock.Sum(x => x.Quantity));
        Assert.Equal(0, state.Stock[0].Quantity);
        Assert.Equal(0, state.Stock[3].Quantity);
        Assert.Equal(20, state.Stock[2].Quantity);
    }
    [Fact]
    public void InterruptedShiftImmediatelyReleasesItsClaimAndRetainsProgress()
    {
        var engine = Create();
        var state = State(engine);
        var worker = Citizens(engine)[0];
        worker.Needs = new CitizenNeeds(6500, 6500);
        worker.CurrentAction = CitizenAction.LivingWork;
        worker.ActionPhase = CitizenActionPhase.Perform;
        var order = new LivingWorkOrder { Id = state.NextId++, Kind = LivingWorkKind.Experiment, Technique = LivingTechnique.Cultivation,
            CitizenId = worker.Id.Value, Reserved = true, SuppliesDelivered = true, RequiredWork = 480,
            Location = worker.Location, SupplyLocation = worker.Location, Phase = LivingWorkPhase.Work };
        state.Orders.Add(order);
        Call(engine, "CompleteLivingShift", worker);
        Assert.Null(order.CitizenId);
        Assert.True(order.Reserved);
        Assert.InRange(order.WorkDone, 1, 479);
        Assert.Equal(CitizenAction.None, worker.CurrentAction);
    }
    [Fact]
    public void LivingMealsUseOnlyTheNeededPortionAndPreparationContinuesBeyondDailyReserves()
    {
        var engine = Create();
        var state = State(engine);
        var citizen = Citizens(engine)[0];
        engine.Settlement.FoodStored = 50;
        citizen.Needs = new CitizenNeeds(1250);
        Call(engine, "Eat", citizen);
        Assert.Equal(47, engine.Settlement.FoodStored);
        Assert.Equal(0, citizen.Needs.Hunger);
        state.Stock[state.Stock.FindIndex(x => x.Good == LivingGood.PreservedFood)] = new(LivingGood.PreservedFood, 2000);
        Assert.True((bool)typeof(SimulationEngine).GetProperty("LivingNeedsSeasonalReserves", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!);
        var order = new LivingWorkOrder { Kind = LivingWorkKind.Preserve, Priority = 5000, Location = citizen.Location };
        var preparationScore = (int)Call(engine, "WorkScore", citizen, order)!;
        state.Stock[state.Stock.FindIndex(x => x.Good == LivingGood.PreservedFood)] = new(LivingGood.PreservedFood, 20000);
        Assert.False((bool)typeof(SimulationEngine).GetProperty("LivingNeedsSeasonalReserves", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!);
        Assert.True(preparationScore > (int)Call(engine, "WorkScore", citizen, order)!);
    }
    private static SimulationEngine Create() => new(new WorldSeed(42), simulationRulesVersion: SimulationEngine.LivingSimulationRulesVersion);
    private static Citizen[] Citizens(SimulationEngine engine) => ((IDictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!).Values.OrderBy(x => x.Id.Value).ToArray();
    private static LivingWorldState State(SimulationEngine engine) => (LivingWorldState)typeof(SimulationEngine).GetField("_living", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!;
    private static object? Call(SimulationEngine engine, string name, params object[] arguments) => typeof(SimulationEngine).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(engine, arguments);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerDeathReleasesClaimWithoutLosingReservedInputsOrProgress(bool natural)
    {
        var engine = Create();
        engine.AdvanceUntil(new WorldMinute(4777));
        var state = State(engine);
        var order = state.Orders.First(x => x.CitizenId is not null && x.Reserved);
        var worker = Citizens(engine).Single(x => x.Id.Value == order.CitizenId);
        var progress = order.WorkDone;
        var inputs = order.Ingredients.ToArray();
        var supplies = state.Stock.ToArray();
        worker.Health = 0;
        if (natural) Call(engine, "KillNatural", worker);
        else Call(engine, "Kill", worker, true, false, false);
        Call(engine, "SynchronizeLivingPeople");
        Assert.Null(order.CitizenId);
        Assert.True(order.Reserved);
        Assert.Equal(progress, order.WorkDone);
        Assert.Equal(inputs, order.Ingredients);
        Assert.Equal(supplies, state.Stock);
        Assert.DoesNotContain(engine.CreatePersistenceSnapshot().ScheduledEvents, e => e.Order.EntitySortKey == worker.Id.Value && e.Name.StartsWith("citizen.", StringComparison.Ordinal));
        var replaced = false;
        for (var minute = 4897; minute < 20000 && state.Orders.Contains(order); minute += 120)
        {
            engine.AdvanceUntil(new WorldMinute(minute));
            replaced |= order.CitizenId is { } id && id != worker.Id.Value;
        }
        Assert.True(replaced || !state.Orders.Contains(order));
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public void TreatmentAndTeachingHavePersistentConsequencesAndKnowledgeSurvivesTeacher()
    {
        var engine = Create();
        engine.AdvanceUntil(new WorldMinute(1000));
        var state = State(engine);
        var pair = Citizens(engine).Where(x => x.IsAlive).GroupBy(x => x.Location).First(x => x.Count() >= 2).Take(2).ToArray();
        var teacher = pair[0];
        var recipient = pair[1];
        var learner = state.People.Single(x => x.CitizenId == recipient.Id.Value);
        var expert = state.People.Single(x => x.CitizenId == teacher.Id.Value);
        expert.Knowledge = [LivingTechnique.Cultivation, LivingTechnique.Care];
        learner.Knowledge.Clear();
        var lesson = new LivingWorkOrder { Kind = LivingWorkKind.Teach, SubjectId = recipient.Id.Value, Technique = LivingTechnique.Cultivation, Location = teacher.Location, SupplyLocation = teacher.Location };
        Assert.True((bool)Call(engine, "CanWork", teacher, lesson)!);
        Call(engine, "CompleteLivingPersonalWork", teacher, lesson);
        Assert.Contains(LivingTechnique.Cultivation, learner.Knowledge);
        learner.Injury = 2400;
        learner.Illness = 1400;
        Call(engine, "CompleteLivingPersonalWork", teacher, new LivingWorkOrder { Kind = LivingWorkKind.Care, SubjectId = recipient.Id.Value, Ingredients = [new("Medicine", 1)] });
        Assert.Equal(0, learner.Injury + learner.Illness);
        Assert.Contains(learner.Experiences, x => x.Kind == LivingExperienceKind.Helped && x.OtherCitizenId == teacher.Id.Value);
        Assert.Contains(state.Facts, x => x.Kind == LivingFactKind.Recovery && x.CitizenId == recipient.Id.Value);
        teacher.Health = 0;
        Call(engine, "Kill", teacher, false, false, true);
        Call(engine, "SynchronizeLivingPeople");
        Assert.True((bool)Call(engine, "SettlementKnows", LivingTechnique.Cultivation)!);
        Assert.False((bool)Call(engine, "SettlementKnows", LivingTechnique.Care)!);
        var resumed = new SimulationEngine(engine.CreatePersistenceSnapshot());
        Assert.Contains(LivingTechnique.Cultivation, State(resumed).People.Single(x => x.CitizenId == recipient.Id.Value).Knowledge);
    }

    [Fact]
    public void GoalsExperiencesRelationshipsAndUrgentNeedsChangeChoices()
    {
        var engine = Create();
        var citizen = Citizens(engine)[0];
        var person = State(engine).People.Single(x => x.CitizenId == citizen.Id.Value);
        var job = new LivingWorkOrder { Kind = LivingWorkKind.Cook, Priority = 3000, Location = citizen.Location };
        person.Goal = LivingGoal.Comfort;
        var baseline = (int)Call(engine, "WorkScore", citizen, job)!;
        person.Goal = LivingGoal.FamilySecurity;
        Assert.True((int)Call(engine, "WorkScore", citizen, job)! > baseline);
        person.Experiences.Add(new(LivingExperienceKind.Scarcity, 0));
        Assert.True((int)Call(engine, "WorkScore", citizen, job)! > baseline + 1800);
        citizen.Needs = citizen.Needs with { Hunger = 9000 };
        engine.Settlement.FoodStored = 20;
        Assert.False((bool)Call(engine, "TryStartLivingWork", citizen, -100000)!);
        Assert.Equal(CitizenAction.Eat, engine.EvaluateDecision(citizen.Id).MaxBy(x => x.FinalScore)!.Action);
        citizen.Needs = new CitizenNeeds(0, 9000, 0, 0);
        Assert.Equal(CitizenAction.Rest, engine.EvaluateDecision(citizen.Id).MaxBy(x => x.FinalScore)!.Action);
        var target = Citizens(engine).Skip(1).First();
        target.Location = citizen.Location;
        var pair = RelationshipState.Normalize(citizen.Id, target.Id);
        Call(engine, "CompleteLivingPersonalWork", citizen, new LivingWorkOrder { Kind = LivingWorkKind.RepairRelationship, SubjectId = target.Id.Value });
        var relationship = engine.Relationships.Single(x => x.CitizenAId == pair.A && x.CitizenBId == pair.B);
        Assert.True(relationship.Affinity > 0 && relationship.Trust > 0);
    }

    [Fact]
    public void HungryCitizensCanCookAvailableIngredientsWhenThereIsNothingToEat()
    {
        var engine = Create();
        var state = State(engine);
        var citizen = Citizens(engine)[0];
        citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
        engine.Settlement.FoodStored = 0;
        state.Stock[state.Stock.FindIndex(x => x.Good == LivingGood.Grain)] = new(LivingGood.Grain, 20);
        state.Stock[state.Stock.FindIndex(x => x.Good == LivingGood.Fuel)] = new(LivingGood.Fuel, 1);
        var hearth = new LivingFacility(state.NextId++, LivingFacilityKind.Hearth, engine.World.StartingSite, 0);
        state.Facilities.Add(hearth);
        Call(engine, "PlanLivingEconomy");
        Assert.True((bool)Call(engine, "TryStartLivingWork", citizen, engine.EvaluateDecision(citizen.Id).Max(x => x.FinalScore))!);
        Assert.Contains(state.Orders, x => x.Kind == LivingWorkKind.Cook && x.CitizenId == citizen.Id.Value && x.Reserved);
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("cargo")]
    [InlineData("capacity")]
    [InlineData("claim")]
    public void CorruptCanonicalStateIsRejected(string corruption)
    {
        var engine = Create();
        engine.AdvanceUntil(new WorldMinute(4777));
        var state = State(engine);
        var order = state.Orders.First(x => x.CitizenId is not null);
        switch (corruption)
        {
            case "recipe": order.RequiredWork++; break;
            case "cargo": order.Cargo.Add(new(LivingGood.Tool, 100)); break;
            case "capacity": state.Stock[0] = state.Stock[0] with { Quantity = int.MaxValue }; break;
            case "claim": order.CitizenId = long.MaxValue; break;
        }
        Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void ColdExposureAndRecoveryAffectHealthStressAndFuelWork()
    {
        var engine = Create();
        var state = State(engine);
        var citizen = Citizens(engine)[0];
        var person = state.People.Single(x => x.CitizenId == citizen.Id.Value);
        person.ClothingCondition = 0;
        state.Temperature = -15;
        Call(engine, "AdvanceLivingPeople");
        Assert.True(person.Illness > 0);
        Call(engine, "AdvanceLivingPeople");
        Call(engine, "PlanLivingEconomy");
        Assert.Contains(state.Orders, x => x.Kind == LivingWorkKind.CutFuel && x.Priority == 5000);
        Assert.Contains(state.Orders, x => x.Kind == LivingWorkKind.Care);
        person.ClothingCondition = 10000;
        citizen.Needs = new CitizenNeeds();
        var illness = person.Illness;
        Call(engine, "AdvanceLivingPeople");
        Assert.True(person.Illness < illness);
    }

    [Fact]
    public void SeededColdSpellConnectsCropDamageFuelUseAndPersonalResponse()
    {
        var cold = Create();
        var warm = Create();
        var coldDay = Enumerable.Range(270, 90).First(day =>
            new DeterministicRandom(new WorldSeed(42)).NextUInt64(RandomDomain.DecisionVariation, 0, (ulong)(day / 7), 21000) % 100 is >= 12 and < 22);
        // Isolate the daily environmental transition with identical starting
        // supplies, fields, and people; only the calendar/weather differs.
        foreach (var (engine, day) in new[] { (cold, coldDay), (warm, 90) })
        {
            typeof(SimulationEngine).GetProperty(nameof(SimulationEngine.CurrentMinute))!.SetValue(engine, new WorldMinute(day * WorldCalendar.MinutesPerDay));
            var state = State(engine);
            state.Fields.Add(new LivingField { Id = state.NextId++, Location = engine.World.StartingSite, SownMinute = 0, Growth = 1000, Moisture = 8000 });
            state.Stock[state.Stock.FindIndex(x => x.Good == LivingGood.Fuel)] = new(LivingGood.Fuel, 10);
            Call(engine, "AdvanceLivingEnvironment");
            Call(engine, "AdvanceLivingPeople");
            Call(engine, "PlanLivingEconomy");
        }
        var coldState = State(cold);
        var warmState = State(warm);
        Assert.Equal(LivingWeatherKind.ColdSpell, coldState.Weather);
        Assert.True(coldState.Fields[0].Growth < warmState.Fields[0].Growth);
        Assert.True(coldState.Fields[0].Condition < warmState.Fields[0].Condition);
        Assert.True(coldState.Stock.Single(x => x.Good == LivingGood.Fuel).Quantity < warmState.Stock.Single(x => x.Good == LivingGood.Fuel).Quantity);
        Assert.True(coldState.People[0].Illness > warmState.People[0].Illness);
        Assert.True(coldState.Orders.Single(x => x.Kind == LivingWorkKind.CutFuel).Priority > warmState.Orders.Single(x => x.Kind == LivingWorkKind.CutFuel).Priority);
        Assert.Contains(coldState.Facts, x => x.Kind == LivingFactKind.WeatherChanged && x.Value == (int)LivingWeatherKind.ColdSpell);
        var observation = cold.CreateLivingObservation()!.Value;
        Assert.Equal("ColdSpell", observation.GetProperty("weather").GetString());
        Assert.Equal(coldState.Fields[0].Condition, observation.GetProperty("fields")[0].GetProperty("condition").GetInt32());
    }

    [Fact]
    public void FedWildlifeMovesLocallyReproducesAndKeepsStableIdentityOrder()
    {
        var engine = Create();
        var state = State(engine);
        var origin = engine.World.Tiles.First(x => x.Walkable && x.Fertility > 3000 &&
            Math.Abs(x.Coordinate.X - engine.World.StartingSite.X) + Math.Abs(x.Coordinate.Y - engine.World.StartingSite.Y) > 16).Coordinate;
        var minute = 30L * WorldCalendar.MinutesPerDay;
        typeof(SimulationEngine).GetProperty(nameof(SimulationEngine.CurrentMinute))!.SetValue(engine, new WorldMinute(minute));
        state.Temperature = 20;
        state.Animals.Clear();
        for (var i = 0; i < 2; i++) state.Animals.Add(new LivingAnimal { Id = state.NextId++, Location = origin, Energy = 10000 });
        var parents = state.Animals.Select(x => x.Id).ToHashSet();
        Call(engine, "AdvanceLivingWildlife", 30L);
        Assert.InRange(state.Animals.Count, 3, 40);
        Assert.Equal(state.Animals.Select(x => x.Id).Order(), state.Animals.Select(x => x.Id));
        Assert.All(state.Animals, animal =>
        {
            Assert.True(engine.World.GetTile(animal.Location).Walkable);
            Assert.InRange(Math.Abs(animal.Location.X - origin.X) + Math.Abs(animal.Location.Y - origin.Y), 0, 2);
            Assert.InRange(animal.Energy, 1, 10000);
            if (!parents.Contains(animal.Id)) Assert.Equal(minute, animal.BornMinute);
        });
        // Zero energy removes a starving animal without leaving a dead entity.
        var starving = state.Animals[0];
        starving.Predator = true;
        starving.Energy = 1;
        state.Animals.RemoveAll(x => x.Id != starving.Id);
        Call(engine, "AdvanceLivingWildlife", 31L);
        Assert.Empty(state.Animals);
    }
}
