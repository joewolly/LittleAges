using System.Text;
using System.Globalization;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Simulation.Tests;

/// <summary>Independent M3 acceptance evidence.  These assertions calculate expected values
/// from the published rules instead of comparing two executions of the same implementation.</summary>
public sealed class M3AcceptanceEvidenceTests
{
    private readonly ITestOutputHelper _output;
    public M3AcceptanceEvidenceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0, 12500)]
    [InlineData(1, 15000)]
    [InlineData(2, 10000)]
    [InlineData(3, 2500)]
    public void RegenerationUsesExactSeasonRulesBelowTheCap(int seasonIndex, int foodBasis)
    {
        // Regeneration is scheduled at the day boundary.  Use the first day of
        // each 90-day season so the event's minute is unambiguously in that season.
        var target = checked((long)(seasonIndex * WorldCalendar.DaysPerSeason + 1) * WorldCalendar.MinutesPerDay);
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = SnapshotAtMinute(source.CreatePersistenceSnapshot(), target - 1, target);
        var controlledNodes = source.World.Resources.Select(node =>
        {
            var amount = ExpectedRegeneration(node, foodBasis);
            var maximum = Math.Max(node.MaximumQuantity, checked(amount + 2));
            return new ResourceNode(node.Id, node.Coordinate, node.Type, node.InitialQuantity, maximum, node.RegenerationPotential);
        }).ToArray();
        var controlledWorld = new WorldMap(source.World.Seed, source.World.GenerationVersion, source.World.GenerationAttempt,
            source.World.Configuration, source.World.Tiles, controlledNodes, source.World.StartingSite);
        var expectedBefore = new Dictionary<long, int>();
        foreach (var state in snapshot.ResourceStates)
        {
            var node = controlledWorld.Resources.Single(x => x.Id == state.ResourceNodeId);
            var amount = ExpectedRegeneration(node, foodBasis);
            var before = node.MaximumQuantity - amount - 1;
            Assert.True(before >= 0);
            Assert.True(before + amount < node.MaximumQuantity);
            state.CurrentQuantity = before;
            expectedBefore[node.Id.Value] = before;
        }
        snapshot = WithWorld(snapshot, controlledWorld);

        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
        engine.AdvanceUntil(new WorldMinute(target));
        foreach (var state in engine.ResourceStates)
        {
            var node = engine.World.Resources.Single(x => x.Id == state.ResourceNodeId);
            var amount = ExpectedRegeneration(node, foodBasis);
            var before = expectedBefore[node.Id.Value];
            Assert.Equal(before + amount, state.CurrentQuantity);
            Assert.InRange(state.CurrentQuantity, 0, node.MaximumQuantity);
        }
    }

    [Fact]
    public void RegenerationClampsAtMaximumAsASeparateRule()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var target = (long)WorldCalendar.MinutesPerDay;
        var snapshot = SnapshotAtMinute(source.CreatePersistenceSnapshot(), target - 1, target);
        var node = source.World.Resources.First(x => x.RegenerationPotential > 0);
        snapshot.ResourceStates.Single(x => x.ResourceNodeId == node.Id).CurrentQuantity = node.MaximumQuantity;
        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
        engine.AdvanceUntil(new WorldMinute(target));
        Assert.Equal(node.MaximumQuantity, engine.GetResourceState(node.Id)!.CurrentQuantity);
    }

    [Fact]
    public void ZeroRegenerationPotentialAddsExactlyZero()
    {
        const long target = WorldCalendar.MinutesPerDay;
        var source = new SimulationEngine(new WorldSeed(42));
        var atBoundary = SnapshotAtMinute(source.CreatePersistenceSnapshot(), target - 1, target);
        source = SimulationEngine.FromPersistenceSnapshot(atBoundary);
        var original = source.World.Resources[0];
        var replacement = new ResourceNode(original.Id, original.Coordinate, original.Type, original.InitialQuantity, original.MaximumQuantity, 0);
        var world = new WorldMap(source.World.Seed, source.World.GenerationVersion, source.World.GenerationAttempt,
            source.World.Configuration, source.World.Tiles, source.World.Resources.Select(x => x.Id == original.Id ? replacement : x), source.World.StartingSite);
        var snapshot = new SimulationPersistenceSnapshot(source.Seed, source.CurrentMinute, source.WorldSchemaVersion,
            source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.CounterSnapshot,
            source.CreatePersistenceSnapshot().ScheduledEvents, world, source.CreatePersistenceSnapshot().Citizens,
            SimulationEngine.CitizenGenerationVersion, source.ResourceStates, source.Settlement, SimulationEngine.SurvivalVersion);
        var before = snapshot.ResourceStates.Single(x => x.ResourceNodeId == original.Id).CurrentQuantity;
        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
        engine.AdvanceUntil(new WorldMinute(target));
        Assert.Equal(0, engine.World.Resources.Single(x => x.Id == original.Id).RegenerationPotential);
        Assert.Equal(before, engine.GetResourceState(original.Id)!.CurrentQuantity);
    }

    [Fact]
    public void ResourceEnumerationOrderCannotChangeCanonicalResult()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var baseline = source.CreatePersistenceSnapshot();
        var reversedWorld = new WorldMap(source.World.Seed, source.World.GenerationVersion, source.World.GenerationAttempt,
            source.World.Configuration, source.World.Tiles, source.World.Resources.Reverse(), source.World.StartingSite);
        var reversed = new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion,
            baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, baseline.Counters,
            baseline.ScheduledEvents.Reverse().ToArray(), reversedWorld, baseline.Citizens.Reverse().ToArray(),
            baseline.CitizenGenerationVersion, baseline.ResourceStates.Reverse().ToArray(), baseline.Settlement, baseline.SurvivalVersion);
        var first = SimulationEngine.FromPersistenceSnapshot(baseline);
        var second = SimulationEngine.FromPersistenceSnapshot(reversed);
        first.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        second.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        Assert.Equal(Canonical(first.CreatePersistenceSnapshot()), Canonical(second.CreatePersistenceSnapshot()));
    }

    [Theory]
    [InlineData(ResourceType.Food, CitizenAction.GatherFood, 12)]
    [InlineData(ResourceType.Wood, CitizenAction.GatherWood, 10)]
    [InlineData(ResourceType.Stone, CitizenAction.GatherStone, 8)]
    public void GatheringCompletesOutboundPerformReturnAndDeposit(ResourceType type, CitizenAction action, int baseYield)
    {
        var prepared = PrepareGather(action, type, 200, out var node, out var citizenId, out var travel, out var skill);
        var citizen = prepared.GetCitizen(citizenId)!;
        prepared.AdvanceUntil(new WorldMinute(travel));
        citizen = prepared.GetCitizen(citizenId)!;
        Assert.Equal(node.Coordinate, citizen.Location);
        Assert.Equal(CitizenActionPhase.Perform, citizen.ActionPhase);
        var duration = Math.Max(CitizenSimulationRules.MinimumGatherDurationMinutes,
            CitizenSimulationRules.BaseGatherDurationMinutes - skill / 100);
        Assert.Equal(travel + duration, citizen.ActionCompletesMinute!.Value.Value);

        var expectedYield = baseYield + skill / 1000;
        var beforeNode = prepared.GetResourceState(node.Id)!.CurrentQuantity;
        var beforeStock = Stock(prepared.Settlement, type);
        prepared.AdvanceUntil(citizen.ActionCompletesMinute.Value);
        citizen = prepared.GetCitizen(citizenId)!;
        Assert.Equal(CitizenActionPhase.ReturnToStockpile, citizen.ActionPhase);
        Assert.Equal(Math.Min(expectedYield, beforeNode), citizen.CarriedResourceQuantity);
        Assert.Equal(beforeNode - citizen.CarriedResourceQuantity, prepared.GetResourceState(node.Id)!.CurrentQuantity);
        Assert.Equal(skill + CitizenSimulationRules.GatherExperienceGain, Skill(citizen, action));

        var returnMinute = citizen.ActionCompletesMinute!.Value;
        prepared.AdvanceUntil(returnMinute);
        citizen = prepared.GetCitizen(citizenId)!;
        Assert.Equal(0, citizen.CarriedResourceQuantity);
        Assert.Equal(beforeStock + Math.Min(expectedYield, beforeNode), Stock(prepared.Settlement, type));
    }

    [Fact]
    public void DepletedGatherProducesNoYieldExperienceOrCarriedGoods()
    {
        var prepared = PrepareGather(CitizenAction.GatherFood, ResourceType.Food, 0, out var node, out var citizenId, out var travel, out var skill);
        prepared.AdvanceUntil(new WorldMinute(travel));
        var citizen = prepared.GetCitizen(citizenId)!;
        while (prepared.ProcessNextEvent())
        {
            citizen = prepared.GetCitizen(citizenId)!;
            if (citizen.CurrentAction == CitizenAction.None) break;
        }
        Assert.Equal(0, citizen.CarriedResourceQuantity);
        Assert.Equal(skill, citizen.Skills.Foraging);
        Assert.Equal(0, prepared.GetResourceState(node.Id)!.CurrentQuantity);
        Assert.Equal(400, prepared.Settlement.FoodStored);
    }

    [Fact]
    public void ScarcityDepletionMovesGatherTargetToTheNextReachableNode()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var candidates = source.World.Resources.Where(node => node.Type == ResourceType.Food)
            .Select(node => (Node: node, Path: DeterministicPathfinder.Find(source.World, source.World.StartingSite, node.Coordinate)))
            .Where(item => item.Path is { Count: >= 2 })
            .Select(item => (item.Node, Path: item.Path!, Cost: SimulationEngine.RemainingPathCost(item.Path!, source.World)))
            .OrderBy(item => item.Cost).ThenBy(item => item.Node.Id.Value).ToArray();
        var near = candidates[0];
        var far = candidates.First(item => item.Cost > near.Cost);
        var snapshot = source.CreatePersistenceSnapshot();
        snapshot.Settlement!.FoodStored = 0;
        var citizen = snapshot.Citizens.Single(item => item.Id == new CitizenId(1));
        citizen.Location = source.World.StartingSite;
        citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
        citizen.CurrentAction = CitizenAction.GatherFood;
        citizen.ActionPhase = CitizenActionPhase.TravelToTarget;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        citizen.ActionCompletesMinute = new WorldMinute(near.Cost);
        citizen.ActionTarget = near.Node.Coordinate;
        citizen.TargetResourceNodeId = near.Node.Id;
        // Leave one unit in the near node: it is enough to prove a real gather
        // and deposit, but not enough to satisfy the following meal.  Once that
        // meal consumes the one deposited unit, the next decision must retarget
        // the still-stocked farther node.
        var nearQuantity = 1;
        foreach (var state in snapshot.ResourceStates.Where(state => source.World.Resources.Single(node => node.Id == state.ResourceNodeId).Type == ResourceType.Food))
            state.CurrentQuantity = state.ResourceNodeId == near.Node.Id ? nearQuantity : 0;
        snapshot.ResourceStates.Single(state => state.ResourceNodeId == far.Node.Id).CurrentQuantity = far.Node.MaximumQuantity;
        foreach (var other in snapshot.Citizens.Where(item => item.Id != citizen.Id))
        {
            other.CurrentAction = CitizenAction.Idle;
            other.ActionPhase = CitizenActionPhase.Perform;
            other.ActionStartedMinute = WorldMinute.Zero;
            other.ActionCompletesMinute = new WorldMinute(1_000_000);
        }
        var events = snapshot.ScheduledEvents.Select(item =>
        {
            if (item.Name != CitizenEventNames.Decision) return item;
            var current = snapshot.Citizens.Single(item2 => item2.Id.Value == item.Order.EntitySortKey);
            if (current.Id == citizen.Id)
            {
                var firstStep = near.Path[1];
                var firstCost = SimulationEngine.StepCost(near.Path[0], firstStep, source.World);
                return item with { Name = CitizenEventNames.MoveStep,
                    Order = new ScheduledEventOrder(new WorldMinute(firstCost), CitizenEventNames.MovementPriority, citizen.Id.Value, item.Order.Sequence),
                    PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
            }
            return item with { Name = CitizenEventNames.ActionComplete,
                Order = new ScheduledEventOrder(new WorldMinute(1_000_000), CitizenEventNames.CompletionPriority, current.Id.Value, item.Order.Sequence),
                PayloadJson = $"{{\"citizenId\":\"{current.Id.Value}\",\"actionSequence\":{current.ActionSequence}}}" };
        }).ToArray();
        var engine = FromSnapshot(snapshot, events);

        engine.AdvanceUntil(new WorldMinute(near.Cost));
        var performing = engine.GetCitizen(citizen.Id)!;
        Assert.Equal(near.Node.Id, performing.TargetResourceNodeId);
        Assert.Equal(CitizenActionPhase.Perform, performing.ActionPhase);
        var performDue = performing.ActionCompletesMinute!.Value;
        engine.AdvanceUntil(performDue);
        var returning = engine.GetCitizen(citizen.Id)!;
        Assert.Equal(CitizenActionPhase.ReturnToStockpile, returning.ActionPhase);
        var returnDue = returning.ActionCompletesMinute!.Value;
        engine.AdvanceUntil(returnDue);

        var shifted = engine.GetCitizen(citizen.Id)!;
        Assert.Equal(0, engine.GetResourceState(near.Node.Id)!.CurrentQuantity);
        Assert.Equal(nearQuantity, engine.Settlement.FoodStored);
        Assert.Equal(CitizenAction.Eat, shifted.CurrentAction);
        Assert.Equal(CitizenActionPhase.Perform, shifted.ActionPhase);
        engine.AdvanceUntil(shifted.ActionCompletesMinute!.Value);
        Assert.Equal(0, engine.Settlement.FoodStored);
        engine.ProcessNextEvent();
        shifted = engine.GetCitizen(citizen.Id)!;
        Assert.Equal(CitizenAction.GatherFood, shifted.CurrentAction);
        Assert.Equal(CitizenActionPhase.TravelToTarget, shifted.ActionPhase);
        Assert.Equal(far.Node.Id, shifted.TargetResourceNodeId);
    }

    [Fact]
    public void EatingFullPartialAndEmptyMealsUseExactFoodAndHungerMath()
    {
        foreach (var (food, expectedConsumed) in new[] { (20, 10), (5, 5), (0, 0) })
        {
            var source = new SimulationEngine(new WorldSeed(42));
            var snapshot = source.CreatePersistenceSnapshot();
            var citizen = snapshot.Citizens[0];
            citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
            citizen.CurrentAction = CitizenAction.Eat; citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = WorldMinute.Zero;
            snapshot.Settlement!.FoodStored = food;
            var events = ReplaceActionEvent(snapshot, citizen, CitizenEventNames.ActionComplete, WorldMinute.Zero);
            var engine = FromSnapshot(snapshot, events);
            engine.ProcessNextEvent();
            var actual = engine.GetCitizen(citizen.Id)!;
            Assert.Equal(food - expectedConsumed, engine.Settlement.FoodStored);
            Assert.Equal(9000 - (CitizenSimulationRules.FullHungerReduction * expectedConsumed / CitizenSimulationRules.MealFoodUnits), actual.Needs.Hunger);
            Assert.Equal(CitizenAction.None, actual.CurrentAction);
        }
    }

    [Fact]
    public void SimultaneousInsufficientMealsAreOrderedAndConserved()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        snapshot.Settlement!.FoodStored = 15;
        foreach (var citizen in snapshot.Citizens.Take(2))
        {
            citizen.Location = source.World.StartingSite;
            citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
            citizen.CurrentAction = CitizenAction.Eat; citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = WorldMinute.Zero;
        }
        var events = snapshot.ScheduledEvents.Select(e =>
        {
            if (e.Name != CitizenEventNames.Decision || e.Order.EntitySortKey is not (1 or 2)) return e;
            var citizen = snapshot.Citizens[(int)e.Order.EntitySortKey - 1];
            return e with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(WorldMinute.Zero, CitizenEventNames.CompletionPriority, citizen.Id.Value, e.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
        }).ToArray();
        var uninterrupted = FromSnapshot(snapshot, events);
        uninterrupted.ProcessNextEvent();
        uninterrupted.ProcessNextEvent();

        // The checkpoint is deliberately taken after both completion events have
        // been scheduled, but before either completion is processed.
        var checkpoint = FromSnapshot(snapshot, events).CreatePersistenceSnapshot();
        var reloaded = SimulationEngine.FromPersistenceSnapshot(checkpoint);
        reloaded.ProcessNextEvent();
        reloaded.ProcessNextEvent();

        foreach (var engine in new[] { uninterrupted, reloaded })
        {
            Assert.Equal(0, engine.Settlement.FoodStored);
            Assert.Equal(4000, engine.GetCitizen(new CitizenId(1))!.Needs.Hunger);
            Assert.Equal(6500, engine.GetCitizen(new CitizenId(2))!.Needs.Hunger);
            Assert.All(engine.Citizens, c => Assert.True(c.Health >= 0));
        }
        Assert.Equal(Canonical(uninterrupted.CreatePersistenceSnapshot()), Canonical(reloaded.CreatePersistenceSnapshot()));
    }

    [Fact]
    public void SimultaneousGatherersUseCitizenIdOrderAndConserveNearlyDepletedNode()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var node = source.World.Resources.Where(x => x.Type == ResourceType.Food)
            .First(x => DeterministicPathfinder.Find(source.World, source.World.StartingSite, x.Coordinate) is { Count: >= 2 });
        var snapshot = source.CreatePersistenceSnapshot();
        var first = snapshot.Citizens[0];
        var second = snapshot.Citizens[1];
        first.Location = node.Coordinate; second.Location = node.Coordinate;
        foreach (var citizen in new[] { first, second })
        {
            citizen.CurrentAction = CitizenAction.GatherFood; citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = WorldMinute.Zero;
            citizen.ActionTarget = null; citizen.TargetResourceNodeId = node.Id;
        }
        var firstYield = CitizenSimulationRules.FoodBaseYield + first.Skills.Foraging / 1000;
        var quantity = Math.Min(node.MaximumQuantity, firstYield + 1);
        snapshot.ResourceStates.Single(x => x.ResourceNodeId == node.Id).CurrentQuantity = quantity;
        var events = snapshot.ScheduledEvents.Select(e =>
        {
            if (e.Name != CitizenEventNames.Decision || e.Order.EntitySortKey is not (1 or 2)) return e;
            var citizen = e.Order.EntitySortKey == 1 ? first : second;
            return e with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(WorldMinute.Zero, CitizenEventNames.CompletionPriority, citizen.Id.Value, e.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
        }).ToArray();
        var engine = FromSnapshot(snapshot, events);
        engine.ProcessNextEvent();
        engine.ProcessNextEvent();
        var actualFirst = engine.GetCitizen(first.Id)!;
        var actualSecond = engine.GetCitizen(second.Id)!;
        var secondYield = CitizenSimulationRules.FoodBaseYield + second.Skills.Foraging / 1000;
        var expectedFirst = Math.Min(firstYield, quantity);
        var expectedSecond = Math.Min(secondYield, quantity - expectedFirst);
        Assert.Equal(expectedFirst, actualFirst.CarriedResourceQuantity);
        Assert.Equal(expectedSecond, actualSecond.CarriedResourceQuantity);
        Assert.Equal(0, engine.GetResourceState(node.Id)!.CurrentQuantity);
        Assert.Equal(quantity, actualFirst.CarriedResourceQuantity + actualSecond.CarriedResourceQuantity);
        var reloaded = SimulationEngine.FromPersistenceSnapshot(engine.CreatePersistenceSnapshot());
        Assert.Equal(Canonical(engine.CreatePersistenceSnapshot()), Canonical(reloaded.CreatePersistenceSnapshot()));
    }

    [Fact]
    public void FullCanonicalStateIsIndependentOfAdvanceChunking()
    {
        const long target = 3 * WorldCalendar.MinutesPerDay;
        var whole = new SimulationEngine(new WorldSeed(42));
        whole.AdvanceUntil(new WorldMinute(target));
        var chunks = new SimulationEngine(new WorldSeed(42));
        for (var minute = 60L; minute <= target; minute += 60) chunks.AdvanceUntil(new WorldMinute(minute));
        var events = new SimulationEngine(new WorldSeed(42));
        while (events.CurrentMinute.Value < target)
        {
            var next = events.CreatePersistenceSnapshot().ScheduledEvents.Min(e => e.Order.DueWorldMinute.Value);
            events.AdvanceUntil(new WorldMinute(Math.Min(target, next)));
        }
        Assert.Equal(Canonical(whole.CreatePersistenceSnapshot()), Canonical(chunks.CreatePersistenceSnapshot()));
        Assert.Equal(Canonical(whole.CreatePersistenceSnapshot()), Canonical(events.CreatePersistenceSnapshot()));
        Assert.Equal(whole.SurvivalFingerprint, chunks.SurvivalFingerprint);
        Assert.Equal(whole.SurvivalFingerprint, events.SurvivalFingerprint);
    }

    [Fact]
    public void DefaultSeed42UnattendedThirtyDaysRemainsViableAndReportsObservedMetrics()
    {
        const long target = 30L * WorldCalendar.MinutesPerDay;
        var engine = new SimulationEngine(new WorldSeed(42));
        var starting = engine.CreatePersistenceSnapshot();
        var startingSkills = starting.Citizens.ToDictionary(x => x.Id.Value, x => x.Skills.Foraging + x.Skills.Woodcutting + x.Skills.Stoneworking);
        var observedActions = new HashSet<string>(StringComparer.Ordinal);
        var resourceDepletionObservations = 0;
        var regenerationObservations = 0;
        var gathered = new Dictionary<ResourceType, long>();
        var foodConsumed = 0L;
        var foodDeposited = 0L;
        var minimumHealth = int.MaxValue;
        var maximumHealth = int.MinValue;
        for (var minute = 1L; minute <= target; minute++)
        {
            var beforeResources = engine.ResourceStates.ToDictionary(x => x.ResourceNodeId.Value, x => x.CurrentQuantity);
            var beforeFood = engine.Settlement.FoodStored;
            engine.AdvanceUntil(new WorldMinute(minute));
            foreach (var citizen in engine.Citizens)
            {
                observedActions.Add($"{citizen.Id.Value}:{citizen.CurrentAction}");
                minimumHealth = Math.Min(minimumHealth, citizen.Health);
                maximumHealth = Math.Max(maximumHealth, citizen.Health);
            }
            foreach (var state in engine.ResourceStates)
            {
                var decrease = Math.Max(0, beforeResources[state.ResourceNodeId.Value] - state.CurrentQuantity);
                if (decrease > 0)
                {
                    var type = engine.World.Resources.Single(node => node.Id == state.ResourceNodeId).Type;
                    gathered[type] = gathered.GetValueOrDefault(type) + decrease;
                }
                regenerationObservations += state.CurrentQuantity > beforeResources[state.ResourceNodeId.Value] ? 1 : 0;
                resourceDepletionObservations += state.CurrentQuantity == 0 ? 1 : 0;
            }
            var foodDelta = engine.Settlement.FoodStored - beforeFood;
            if (foodDelta > 0) foodDeposited += foodDelta;
            if (foodDelta < 0) foodConsumed -= foodDelta;
        }

        var ending = engine.CreatePersistenceSnapshot();
        var progressed = ending.Citizens.Count(c => c.Skills.Foraging + c.Skills.Woodcutting + c.Skills.Stoneworking > startingSkills[c.Id.Value]);
        Assert.Equal(target, ending.WorldMinute.Value);
        Assert.InRange(engine.LivingPopulation, 1, engine.TotalCitizenCount);
        Assert.All(ending.Citizens, citizen => Assert.InRange(citizen.Health, 0, 10000));
        Assert.All(ending.ResourceStates, state => Assert.InRange(state.CurrentQuantity, 0, engine.World.Resources.Single(node => node.Id == state.ResourceNodeId).MaximumQuantity));
        Assert.True(ending.Settlement!.FoodStored >= 0 && ending.Settlement.WoodStored >= 0 && ending.Settlement.StoneStored >= 0);
        Assert.True(progressed > 0);
        Assert.True(gathered.GetValueOrDefault(ResourceType.Food) > 0);
        Assert.True(foodConsumed > 0);
        _output.WriteLine($"seed=42 days=30 startingLiving={starting.Citizens.Count(x => x.IsAlive)} endingLiving={ending.Citizens.Count(x => x.IsAlive)} deaths={ending.Citizens.Count(x => !x.IsAlive)} deathCauses={string.Join(',', ending.Citizens.Where(x => !x.IsAlive).GroupBy(x => x.DeathCause).Select(g => $"{g.Key}:{g.Count()}"))}");
        _output.WriteLine($"foodConsumed={foodConsumed} foodDeposited={foodDeposited} foodGathered={gathered.GetValueOrDefault(ResourceType.Food)} woodGathered={gathered.GetValueOrDefault(ResourceType.Wood)} stoneGathered={gathered.GetValueOrDefault(ResourceType.Stone)} finalFood={ending.Settlement!.FoodStored} finalWood={ending.Settlement.WoodStored} finalStone={ending.Settlement.StoneStored} resourceDepletionObservations={resourceDepletionObservations} regenerationObservations={regenerationObservations}");
        _output.WriteLine($"minHealth={minimumHealth} maxHealth={maximumHealth} progressedCitizens={progressed} actionExamples={string.Join(',', observedActions.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    private static SimulationEngine PrepareGather(CitizenAction action, ResourceType type, int quantity,
        out ResourceNode node, out CitizenId citizenId, out long travel, out int skill)
    {
        var source = new SimulationEngine(new WorldSeed(42));
        node = source.World.Resources.Where(x => x.Type == type)
            .Select(x => (Node: x, Path: DeterministicPathfinder.Find(source.World, source.World.StartingSite, x.Coordinate)))
            .Where(x => x.Path is { Count: >= 2 })
            .OrderBy(x => SimulationEngine.RemainingPathCost(x.Path!, source.World))
            .First().Node;
        var path = DeterministicPathfinder.Find(source.World, source.World.StartingSite, node.Coordinate)!;
        travel = SimulationEngine.RemainingPathCost(path, source.World);
        var snapshot = source.CreatePersistenceSnapshot();
        citizenId = new CitizenId(1);
        var selectedCitizenId = citizenId;
        var selectedNode = node;
        var citizen = snapshot.Citizens.Single(x => x.Id == selectedCitizenId);
        citizen.Location = source.World.StartingSite; citizen.CurrentAction = action; citizen.ActionPhase = CitizenActionPhase.TravelToTarget;
        citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = new WorldMinute(travel);
        citizen.ActionTarget = node.Coordinate; citizen.TargetResourceNodeId = node.Id;
        skill = Skill(citizen, action);
        snapshot.ResourceStates.Single(x => x.ResourceNodeId == selectedNode.Id).CurrentQuantity = Math.Min(quantity, selectedNode.MaximumQuantity);
        if (quantity == 0)
        {
            foreach (var otherNode in source.World.Resources.Where(x => x.Type == type))
                snapshot.ResourceStates.Single(x => x.ResourceNodeId == otherNode.Id).CurrentQuantity = 0;
        }
        foreach (var other in snapshot.Citizens.Where(x => x.Id != selectedCitizenId))
        {
            other.CurrentAction = CitizenAction.Idle; other.ActionPhase = CitizenActionPhase.Perform;
            other.ActionStartedMinute = WorldMinute.Zero; other.ActionCompletesMinute = new WorldMinute(1_000_000);
        }
        var events = snapshot.ScheduledEvents.Select(e =>
        {
            if (e.Name != CitizenEventNames.Decision) return e;
            var id = e.Order.EntitySortKey;
            var current = snapshot.Citizens.Single(x => x.Id.Value == id);
            if (id == selectedCitizenId.Value)
            {
                var first = path[1];
                var firstCost = SimulationEngine.StepCost(path[0], first, source.World);
                return e with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(new WorldMinute(firstCost), CitizenEventNames.MovementPriority, id, e.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{id}\",\"actionSequence\":{current.ActionSequence}}}" };
            }
            return e with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(new WorldMinute(1_000_000), CitizenEventNames.CompletionPriority, id, e.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{id}\",\"actionSequence\":{current.ActionSequence}}}" };
        }).ToArray();
        return FromSnapshot(snapshot, events);
    }

    private static SimulationPersistenceSnapshot SnapshotAtMinute(SimulationPersistenceSnapshot baseline, long minute, long regenerationMinute)
    {
        var citizens = baseline.Citizens.ToArray();
        foreach (var citizen in citizens)
        {
            citizen.CurrentAction = CitizenAction.None;
            citizen.ActionPhase = CitizenActionPhase.None;
            citizen.ActionStartedMinute = null;
            citizen.ActionCompletesMinute = null;
            citizen.ActionTarget = null;
            citizen.TargetResourceNodeId = null;
            citizen.CarriedResourceType = null;
            citizen.CarriedResourceQuantity = 0;
            citizen.NeedsUpdatedMinute = minute;
            citizen.HealthUpdatedMinute = minute;
        }
        var events = baseline.ScheduledEvents.Select(e =>
        {
            if (e.Name == CitizenEventNames.Decision)
                return e with { Order = new ScheduledEventOrder(new WorldMinute(minute), CitizenEventNames.DecisionPriority, e.Order.EntitySortKey, e.Order.Sequence) };
            if (e.Name == CitizenEventNames.SurvivalCheck)
                return e with { Order = new ScheduledEventOrder(new WorldMinute(checked(minute + CitizenSimulationRules.SurvivalCheckIntervalMinutes)), CitizenEventNames.SurvivalPriority, e.Order.EntitySortKey, e.Order.Sequence) };
            if (e.Name == CitizenEventNames.ResourceRegenerate)
                return e with { Order = new ScheduledEventOrder(new WorldMinute(regenerationMinute), CitizenEventNames.RegenerationPriority, 0, e.Order.Sequence) };
            return e;
        }).ToArray();
        return new SimulationPersistenceSnapshot(baseline.Seed, new WorldMinute(minute), baseline.WorldSchemaVersion,
            baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, baseline.Counters,
            events, baseline.World, citizens, baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion);
    }

    private static SimulationPersistenceSnapshot WithWorld(SimulationPersistenceSnapshot snapshot, WorldMap world)
        => new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion,
            snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents,
            world, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates,
            snapshot.Settlement, snapshot.SurvivalVersion);

    private static int ExpectedRegeneration(ResourceNode node, int foodBasis) => node.Type switch
    {
        ResourceType.Food => checked((node.RegenerationPotential * foodBasis) / 10000),
        ResourceType.Wood => node.RegenerationPotential / CitizenSimulationRules.WoodRegenerationDivisor,
        _ => 0
    };

    private static ScheduledEventSnapshot[] ReplaceActionEvent(SimulationPersistenceSnapshot snapshot, Citizen citizen, string name, WorldMinute due) => snapshot.ScheduledEvents.Select(e => e.Name == CitizenEventNames.Decision && e.Order.EntitySortKey == citizen.Id.Value ? e with { Name = name, Order = new ScheduledEventOrder(due, CitizenEventNames.CompletionPriority, citizen.Id.Value, e.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" } : e).ToArray();

    private static SimulationEngine FromSnapshot(SimulationPersistenceSnapshot snapshot, IReadOnlyList<ScheduledEventSnapshot> events)
        => SimulationEngine.FromPersistenceSnapshot(new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion));

    private static int Skill(Citizen citizen, CitizenAction action) => action == CitizenAction.GatherFood ? citizen.Skills.Foraging : action == CitizenAction.GatherWood ? citizen.Skills.Woodcutting : citizen.Skills.Stoneworking;
    private static int Stock(SettlementState settlement, ResourceType type) => type == ResourceType.Food ? settlement.FoodStored : type == ResourceType.Wood ? settlement.WoodStored : settlement.StoneStored;

    private static string Canonical(SimulationPersistenceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.Seed.Value).Append('|').Append(snapshot.WorldMinute.Value).Append('|').Append(snapshot.World?.Fingerprint).Append('|').Append(snapshot.Counters);
        builder.Append('|').Append(snapshot.Settlement is null ? "" : string.Create(CultureInfo.InvariantCulture, $"{snapshot.Settlement.FoodStored},{snapshot.Settlement.WoodStored},{snapshot.Settlement.StoneStored}"));
        foreach (var state in snapshot.ResourceStates.OrderBy(x => x.ResourceNodeId.Value)) builder.Append(string.Create(CultureInfo.InvariantCulture, $"|r:{state.ResourceNodeId.Value}:{state.CurrentQuantity}"));
        foreach (var citizen in snapshot.Citizens.OrderBy(x => x.Id.Value)) builder.Append(string.Create(CultureInfo.InvariantCulture, $"|c:{citizen.Id.Value}:{citizen.Location.X},{citizen.Location.Y}:{citizen.Health}:{citizen.Needs.Hunger},{citizen.Needs.Rest},{citizen.Needs.Shelter},{citizen.Needs.Social}:{citizen.Skills.Foraging},{citizen.Skills.Woodcutting},{citizen.Skills.Stoneworking}:{citizen.CurrentAction}:{citizen.ActionPhase}:{citizen.ActionSequence}:{citizen.ActionStartedMinute?.Value}:{citizen.ActionCompletesMinute?.Value}:{citizen.TargetResourceNodeId?.Value}:{citizen.CarriedResourceType}:{citizen.CarriedResourceQuantity}:{citizen.NeedsUpdatedMinute}:{citizen.HealthUpdatedMinute}:{citizen.DeathMinute}:{citizen.DeathCause}"));
        foreach (var e in snapshot.ScheduledEvents.OrderBy(x => x.Order)) builder.Append(string.Create(CultureInfo.InvariantCulture, $"|e:{e.Id.Value}:{e.Order.DueWorldMinute.Value}:{e.Order.Priority}:{e.Order.EntitySortKey}:{e.Order.Sequence}:{e.Name}:{e.PayloadJson}"));
        return builder.ToString();
    }
}
