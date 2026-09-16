using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

/// <summary>
/// High-risk M5 contracts which intentionally exercise the canonical writer directly.
/// The helpers keep the setup local: production APIs remain observer-only.
/// </summary>
public sealed class M5AcceptanceMatrixTests
{
    [Fact]
    public void SocializeCompletionHasExactPositiveAndNegativeDeltasAndNeverDamagesHealth()
    {
        var positive = PrepareSocialEngine(FindSocialSeed(negative: false));
        var positiveInitiator = Citizens(positive)[1];
        var positiveTarget = Citizens(positive)[2];
        positiveInitiator.Needs = new CitizenNeeds(0, 0, 0, 8_000);
        positiveTarget.Needs = new CitizenNeeds(0, 0, 0, 7_000);
        positiveTarget.NeedsUpdatedMinute = 0;
        CompleteSocialize(positive, positiveInitiator, positiveTarget);
        var good = Assert.Single(positive.Relationships);
        Assert.Equal((500, 400, 300, 0, 1L), (good.Familiarity, good.Affinity, good.Trust, good.Conflict, good.InteractionCount));
        Assert.Equal(3_000, positiveInitiator.Needs.Social);
        Assert.Equal(4_500, positiveTarget.Needs.Social);
        Assert.Equal(10_000, positiveInitiator.Health);
        Assert.Equal(10_000, positiveTarget.Health);

        var negative = PrepareSocialEngine(FindSocialSeed(negative: true));
        var negativeInitiator = Citizens(negative)[1];
        var negativeTarget = Citizens(negative)[2];
        CompleteSocialize(negative, negativeInitiator, negativeTarget);
        var bad = Assert.Single(negative.Relationships);
        Assert.Equal((350, -500, 0, 600, 1L), (bad.Familiarity, bad.Affinity, bad.Trust, bad.Conflict, bad.InteractionCount));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FailedSocializeForDeadOrOutOfRadiusTargetChangesNothing(bool dead, bool distant)
    {
        var engine = PrepareSocialEngine(42);
        var initiator = Citizens(engine)[1];
        var target = Citizens(engine)[2];
        initiator.Needs = new CitizenNeeds(0, 0, 0, 9_000);
        target.Needs = new CitizenNeeds(0, 0, 0, 9_000);
        if (dead) { target.Health = 0; target.DeathMinute = 0; target.DeathCause = "natural"; target.CurrentAction = CitizenAction.Dead; }
        if (distant) target.Location = new TileCoordinate(initiator.Location.X + 3, initiator.Location.Y);

        CompleteSocialize(engine, initiator, target);

        Assert.Empty(engine.Relationships);
        Assert.Equal(9_000, initiator.Needs.Social);
        Assert.Equal(9_000, target.Needs.Social);
    }

    [Fact]
    public void PartnershipUsesExactThresholdAndRejectsKnownCloseKin()
    {
        var engine = PrepareSocialEngine(42);
        var people = Citizens(engine);
        var first = new Citizen(new CitizenId(101), null, "A", "One", -20L * WorldCalendar.MinutesPerYear, new TileCoordinate(20, 20), new CitizenTraits(5000, 5000, 5000, 5000, 5000, 5000), new CitizenSkills(0, 0, 0, 0, 0, 0));
        var second = new Citizen(new CitizenId(102), null, "B", "Two", -20L * WorldCalendar.MinutesPerYear, new TileCoordinate(21, 20), new CitizenTraits(5000, 5000, 5000, 5000, 5000, 5000), new CitizenSkills(0, 0, 0, 0, 0, 0));
        people.Add(first.Id.Value, first);
        people.Add(second.Id.Value, second);
        var belowScore = new RelationshipState(first.Id, second.Id, 5_000, 4_500, 3_500, 1_500, 0, 1);
        FormPartnership(engine, first, second, belowScore);
        Assert.Null(first.PartnerId);
        var qualifying = new RelationshipState(first.Id, second.Id, 7_000, 6_000, 5_000, 0, 0, 1);
        FormPartnership(engine, first, second, qualifying);
        Assert.Equal(second.Id, first.PartnerId);
        Assert.Equal(first.Id, second.PartnerId);
        Assert.NotNull(first.HouseholdId);
        Assert.Equal(first.HouseholdId, second.HouseholdId);
        Assert.Single(engine.Households);

        var child = new Citizen(new CitizenId(103), null, "Child", first.FamilyName, -18L * WorldCalendar.MinutesPerYear, first.Location, first.Traits, new CitizenSkills(0, 0, 0, 0, 0, 0))
        { ParentAId = first.Id, ParentBId = second.Id };
        people.Add(child.Id.Value, child);
        var before = engine.Households.Count;
        FormPartnership(engine, first, child, new RelationshipState(first.Id, child.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        Assert.Equal(before, engine.Households.Count);
        Assert.Null(child.PartnerId);
    }

    [Fact]
    public void BirthCreatesCanonicalChildAndPreservesHistoricalEventCounter()
    {
        var engine = PrepareSocialEngine(FindBirthSeed());
        var parents = PrepareBirthHousehold(engine);
        var historicalBefore = engine.CounterSnapshot.NextHistoricalEventId;
        CreateChild(engine, parents.First, parents.Second, parents.Household);
        var child = Assert.Single(engine.Citizens, c => c.FounderOrdinal is null);

        Assert.Equal(parents.First.Id, child.ParentAId);
        Assert.Equal(parents.Second.Id, child.ParentBId);
        Assert.Equal(parents.Household.Id, child.HouseholdId);
        Assert.Equal(parents.Household.DwellingStructureId, child.HomeStructureId);
        Assert.Equal(parents.First.FamilyName, child.FamilyName);
        Assert.Equal(new CitizenSkills(0, 0, 0, 0, 0, 0), child.Skills);
        Assert.Equal(historicalBefore, engine.CounterSnapshot.NextHistoricalEventId);
        Assert.Equal(2, engine.Relationships.Count(r => r.CitizenAId == child.Id || r.CitizenBId == child.Id));
        Assert.Equal(2, ScheduledEvents(engine).Count(e => e.Order.EntitySortKey == child.Id.Value));
    }

    [Fact]
    public void FamilyCheckHonorsCooldownHealthHungerFoodAndDwellingCapacityGates()
    {
        foreach (var blocker in new Action<SimulationEngine, (Citizen First, Citizen Second, Household Household)>[]
        {
            static (engine, setup) => setup.First.Health = 6_999,
            static (engine, setup) => setup.Second.Needs = new CitizenNeeds(8_000, 0, 0, 0),
            static (engine, setup) => engine.Settlement.FoodStored = 0,
            static (engine, setup) => setup.Household.DwellingStructureId = null
        })
        {
            var engine = PrepareSocialEngine(FindBirthSeed());
            var setup = PrepareBirthHousehold(engine);
            blocker(engine, setup);
            TryBirth(engine, setup.Household);
            Assert.Equal(CitizenGenerator.FounderCount, engine.TotalCitizenCount);
        }

        var successful = PrepareSocialEngine(FindBirthSeed());
        var valid = PrepareBirthHousehold(successful);
        TryBirth(successful, valid.Household);
        Assert.Equal(CitizenGenerator.FounderCount + 1, successful.TotalCitizenCount);

        var cooldown = PrepareSocialEngine(FindBirthSeed());
        var cooldownSetup = PrepareBirthHousehold(cooldown);
        CreateChild(cooldown, cooldownSetup.First, cooldownSetup.Second, cooldownSetup.Household);
        TryBirth(cooldown, cooldownSetup.Household);
        Assert.Equal(CitizenGenerator.FounderCount + 1, cooldown.TotalCitizenCount);
    }

    [Theory]
    [InlineData(1, 9, 10)]
    [InlineData(10, 0, 1000)]
    [InlineData(40, 0, 4000)]
    [InlineData(41, 9, 4000)]
    [InlineData(20, 200, 0)]
    public void FoodSecurityGatherContributionHasExactDeficitBoundaries(int population, int foodStored, int expected) =>
        Assert.Equal(expected, SimulationEngine.CalculateFoodSecurityGatherContribution(population, foodStored));

    [Fact]
    public void FoodSecurityGatherContributionRequiresAFoodOnlyBlockedHouseholdWithActualDwellingCapacity()
    {
        var engine = PrepareSocialEngine(42);
        var setup = PrepareBirthHousehold(engine);
        engine.Settlement.FoodStored = engine.LivingPopulation * 10 - 100;

        Assert.Equal(1000, FoodSecurityGatherContribution(engine));

        setup.First.Health = 6_999;
        Assert.Equal(0, FoodSecurityGatherContribution(engine));
        setup.First.Health = 10_000;
        setup.Household.DwellingStructureId = null;
        Assert.Equal(0, FoodSecurityGatherContribution(engine));
    }

    [Fact]
    public void FoodSecurityGatherContributionIsUniformForEveryEligibleFoodCandidate()
    {
        var engine = PrepareSocialEngine(42);
        PrepareBirthHousehold(engine);
        engine.Settlement.FoodStored = engine.LivingPopulation * 10;
        var baseline = engine.Citizens.Where(citizen => citizen.AgeYears(engine.CurrentMinute) >= 6)
            .Select(citizen => (citizen.Id, Evaluation: engine.EvaluateDecision(citizen.Id).Single(evaluation => evaluation.Action == CitizenAction.GatherFood)))
            .ToDictionary(item => item.Id, item => item.Evaluation);
        engine.Settlement.FoodStored -= 100;
        var boosted = engine.Citizens.Where(citizen => citizen.AgeYears(engine.CurrentMinute) >= 6)
            .Select(citizen => (citizen.Id, Evaluation: engine.EvaluateDecision(citizen.Id).Single(evaluation => evaluation.Action == CitizenAction.GatherFood)))
            .ToDictionary(item => item.Id, item => item.Evaluation);

        Assert.NotEmpty(boosted);
        Assert.All(boosted, item =>
        {
            Assert.Equal(baseline[item.Key].FinalScore + 1000, item.Value.FinalScore);
            Assert.Equal(baseline[item.Key].StockpileContribution + 1000, item.Value.StockpileContribution);
        });
    }

    [Fact]
    public void FoodSecurityDoesNotCreateAnUnavailableFoodCandidateAndCriticalEatingStillWins()
    {
        var unavailable = PrepareSocialEngine(42);
        PrepareBirthHousehold(unavailable);
        unavailable.Settlement.FoodStored = unavailable.LivingPopulation * 10 - 100;
        foreach (var state in ResourceStates(unavailable).Values.Where(state => unavailable.World.Resources.Single(node => node.Id == state.ResourceNodeId).Type == ResourceType.Food)) state.CurrentQuantity = 0;
        Assert.Equal(1000, FoodSecurityGatherContribution(unavailable));
        Assert.All(unavailable.Citizens.Where(citizen => citizen.AgeYears(unavailable.CurrentMinute) >= 6), citizen => Assert.DoesNotContain(unavailable.EvaluateDecision(citizen.Id), evaluation => evaluation.Action == CitizenAction.GatherFood));

        var critical = PrepareSocialEngine(42);
        PrepareBirthHousehold(critical);
        critical.Settlement.FoodStored = critical.LivingPopulation * 10 - 100;
        var citizen = Citizens(critical)[3];
        citizen.Needs = new CitizenNeeds(9_999, 0, 0, 0);
        var decisions = critical.EvaluateDecision(citizen.Id);
        Assert.Contains(decisions, evaluation => evaluation.Action == CitizenAction.GatherFood);
        Assert.Equal(CitizenAction.Eat, SimulationEngine.SelectDecision(decisions));
    }

    [Fact]
    public void FoodSecurityContributionIsExactlyDisabledForM3AndM4()
    {
        var m3 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var m4 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M4SimulationRulesVersion);

        Assert.Equal(0, FoodSecurityGatherContribution(m3));
        Assert.Equal(0, FoodSecurityGatherContribution(m4));
        Assert.Equal(500, m3.EvaluateDecision(m3.Citizens[0].Id).Single(evaluation => evaluation.Action == CitizenAction.GatherFood).StockpileContribution);
        Assert.Equal(500, m4.EvaluateDecision(m4.Citizens[0].Id).Single(evaluation => evaluation.Action == CitizenAction.GatherFood).StockpileContribution);
    }

    [Fact]
    public void M5ValidationRejectsRelationshipHouseholdAndFamilyGraphCorruption()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var people = source.Citizens.ToArray();
        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.Counters, source.ScheduledEvents, source.World, people, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, source.Structures, source.StructureContributions, source.SocialVersion, [new RelationshipState(people[1].Id, people[0].Id, 1, 0, 0, 0, 0, 1)], source.Households));

        people[0].PartnerId = people[1].Id;
        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.Counters, source.ScheduledEvents, source.World, people, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, source.Structures, source.StructureContributions, source.SocialVersion, source.Relationships, source.Households));
    }

    [Fact]
    public void LifeStagesProductivityAndNaturalMortalityUseExactBoundaries()
    {
        var traits = new CitizenTraits(0, 0, 0, 0, 0, 0);
        var skills = new CitizenSkills(0, 0, 0, 0, 0, 0);
        foreach (var (age, stage, productivity, risk) in new[] { (5, "Young Child", 0, 0), (6, "Child", 5000, 0), (13, "Adolescent", 7500, 0), (18, "Adult", 10000, 1), (40, "Adult", 10000, 3), (50, "Adult", 10000, 10), (60, "Elder", 7500, 50), (70, "Elder", 7500, 150), (80, "Elder", 7500, 500), (90, "Elder", 7500, 1500), (100, "Elder", 7500, 5000) })
        {
            var citizen = new Citizen(new CitizenId(age + 1), 0, "A", "B", -1L * age * WorldCalendar.MinutesPerYear, new TileCoordinate(0, 0), traits, skills);
            Assert.Equal(stage, citizen.LifeStage(WorldMinute.Zero));
            Assert.Equal(productivity, LifeStages.ProductivityBasisPoints(age));
            Assert.Equal(risk, SimulationEngine.NaturalMortalityRisk(citizen, WorldMinute.Zero));
        }
    }

    [Fact]
    public void AgeEligibilityFiltersYoungChildrenAndAllowsOnlyChildFoodGathering()
    {
        Assert.False(IsAgeEligible(CitizenAction.GatherFood, 5));
        Assert.False(IsAgeEligible(CitizenAction.HaulConstruction, 5));
        Assert.False(IsAgeEligible(CitizenAction.Build, 5));
        Assert.True(IsAgeEligible(CitizenAction.Socialize, 5));
        Assert.True(IsAgeEligible(CitizenAction.GatherFood, 6));
        Assert.False(IsAgeEligible(CitizenAction.GatherWood, 6));
        Assert.False(IsAgeEligible(CitizenAction.HaulConstruction, 6));
        Assert.False(IsAgeEligible(CitizenAction.Build, 6));
        Assert.True(IsAgeEligible(CitizenAction.Build, 13));
    }

    [Fact]
    public void TwoSocialCompletionsOnOnePairRetainBothCanonicalInteractions()
    {
        var engine = PrepareSocialEngine(FindSocialSeed(negative: false));
        var first = Citizens(engine)[1];
        var second = Citizens(engine)[2];
        CompleteSocialize(engine, first, second);
        CompleteSocialize(engine, second, first);
        var relationship = Assert.Single(engine.Relationships);
        Assert.Equal(2, relationship.InteractionCount);
        Assert.Equal(0, relationship.LastInteractionMinute);
    }

    [Fact]
    public void NaturalDeathDuringAnActualSocializeCancelsTargetAndLeavesOneDecisionFlow()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        Citizen? initiator = null;
        while (engine.NextScheduledEventMinute is { } due && due < new WorldMinute(14L * WorldCalendar.MinutesPerDay))
        {
            engine.ProcessNextEvent();
            initiator = engine.Citizens.FirstOrDefault(c => c.CurrentAction == CitizenAction.Socialize);
            if (initiator is not null) break;
        }
        var active = Assert.IsType<Citizen>(initiator);
        var target = Assert.IsType<Citizen>(engine.GetCitizen(Assert.IsType<CitizenId>(active.TargetCitizenId)));
        Relationships(engine).TryAdd((Math.Min(active.Id.Value, target.Id.Value), Math.Max(active.Id.Value, target.Id.Value)), new RelationshipState(new CitizenId(Math.Min(active.Id.Value, target.Id.Value)), new CitizenId(Math.Max(active.Id.Value, target.Id.Value)), 500, 0, 0, 0, engine.CurrentMinute.Value, 1));
        typeof(SimulationEngine).GetMethod("KillNatural", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [Citizens(engine)[target.Id.Value]]);

        var cancelled = Assert.IsType<Citizen>(engine.GetCitizen(active.Id));
        Assert.Equal(CitizenAction.None, cancelled.CurrentAction);
        Assert.Null(cancelled.TargetCitizenId);
        Assert.Equal("natural", engine.GetCitizen(target.Id)!.DeathCause);
        Assert.Contains(engine.Relationships, relationship => relationship.CitizenAId == new CitizenId(Math.Min(active.Id.Value, target.Id.Value)) && relationship.CitizenBId == new CitizenId(Math.Max(active.Id.Value, target.Id.Value)));
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Single(snapshot.ScheduledEvents, e => e.Name == CitizenEventNames.Decision && e.Order.EntitySortKey == active.Id.Value);
        Assert.DoesNotContain(snapshot.ScheduledEvents, e => e.Order.EntitySortKey == target.Id.Value && e.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck);
    }

    [Fact]
    public void SocializePartnershipHousingChangeResetsOnlyRestTravelAndPreservesSurvival()
    {
        var engine = PrepareSocialEngine(42);
        var people = Citizens(engine);
        var first = people[1];
        var second = people[2];
        var donor = people[3];
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var sites = engine.World.Tiles.Where(tile => tile.Buildable && tile.Coordinate != engine.World.StartingSite && engine.World.GetResources(tile.Coordinate).Count == 0 && DeterministicPathfinder.Find(engine.World, second.Location, tile.Coordinate) is { Count: >= 2 }).Select(tile => tile.Coordinate).Take(2).ToArray();
        Assert.Equal(2, sites.Length);
        var source = CompletedShelter(counters.AllocateStructureId(), sites[0]);
        var destination = CompletedShelter(counters.AllocateStructureId(), sites[1]);
        Structures(engine).Add(source.Id.Value, source);
        Structures(engine).Add(destination.Id.Value, destination);
        var contributions = Assert.IsType<Dictionary<(long, long), StructureContribution>>(typeof(SimulationEngine).GetField("_structureContributions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        foreach (var shelter in new[] { source, destination }) contributions.Add((shelter.Id.Value, first.Id.Value), new StructureContribution(shelter.Id, first.Id, shelter.CompletedWork, shelter.DeliveredWood, shelter.DeliveredStone));
        donor.HomeStructureId = source.Id;
        second.HomeStructureId = source.Id;
        second.CurrentAction = CitizenAction.Rest;
        second.ActionPhase = CitizenActionPhase.TravelToTarget;
        second.TargetStructureId = source.Id;
        second.ActionTarget = source.Location;
        second.ActionStartedMinute = engine.CurrentMinute;
        var path = Assert.IsAssignableFrom<IReadOnlyList<TileCoordinate>>(DeterministicPathfinder.Find(engine.World, second.Location, source.Location));
        second.ActionCompletesMinute = engine.CurrentMinute.Add(SimulationEngine.RemainingPathCost(path, engine.World));
        RemoveActionEvent(engine, second.Id);
        ScheduleCitizen(engine, second, CitizenEventNames.MoveStep, engine.CurrentMinute.Add(SimulationEngine.StepCost(path[0], path[1], engine.World)), CitizenEventNames.MovementPriority);

        RemoveActionEvent(engine, first.Id);
        first.CurrentAction = CitizenAction.Socialize;
        first.ActionPhase = CitizenActionPhase.Perform;
        first.TargetCitizenId = second.Id;
        first.ActionStartedMinute = engine.CurrentMinute;
        first.ActionCompletesMinute = engine.CurrentMinute.Add(CitizenSimulationRules.SocializeDurationMinutes);
        Relationships(engine).Add((first.Id.Value, second.Id.Value), new RelationshipState(first.Id, second.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        var completeAction = typeof(SimulationEngine).GetMethod("CompleteAction", BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types: new[] { typeof(Citizen), typeof(bool) }, modifiers: null)!;
        completeAction.Invoke(engine, new object?[] { first, false });

        var snapshot = engine.CreatePersistenceSnapshot();
        var reloaded = SimulationEngine.FromPersistenceSnapshot(snapshot).CreatePersistenceSnapshot();
        Assert.Equal(first.Id, second.PartnerId);
        Assert.Equal(second.Id, first.PartnerId);
        Assert.Equal(CitizenAction.None, second.CurrentAction);
        Assert.Equal(CitizenActionPhase.None, second.ActionPhase);
        Assert.Single(snapshot.ScheduledEvents, item => item.Name == CitizenEventNames.Decision && item.Order.EntitySortKey == second.Id.Value);
        Assert.Single(snapshot.ScheduledEvents, item => item.Name == CitizenEventNames.SurvivalCheck && item.Order.EntitySortKey == second.Id.Value);
        Assert.Equal(snapshot.Citizens, reloaded.Citizens);
        Assert.Equal(snapshot.ScheduledEvents, reloaded.ScheduledEvents);
    }

    [Fact]
    public void SchedulerDrivenSocializeContentionUsesStableEntityOrderAndPersistsOnePartnership()
    {
        var engine = PrepareSocialEngine(42);
        var people = Citizens(engine);
        var first = people[1];
        var second = people[2];
        var target = people[3];
        first.Location = target.Location;
        second.Location = target.Location;
        var nextEntityBefore = engine.CounterSnapshot.NextEntityId;

        foreach (var citizen in people.Values)
        {
            RemoveActionEvent(engine, citizen.Id);
            if (citizen.Id is { Value: 1 or 2 }) continue;

            citizen.CurrentAction = CitizenAction.Idle;
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionStartedMinute = engine.CurrentMinute;
            citizen.ActionCompletesMinute = new WorldMinute(1_000);
            ScheduleCitizen(engine, citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
        }
        foreach (var initiator in new[] { first, second })
        {
            RemoveActionEvent(engine, initiator.Id);
            initiator.CurrentAction = CitizenAction.Socialize;
            initiator.ActionPhase = CitizenActionPhase.Perform;
            initiator.TargetCitizenId = target.Id;
            initiator.ActionStartedMinute = engine.CurrentMinute;
            initiator.ActionCompletesMinute = engine.CurrentMinute.Add(CitizenSimulationRules.SocializeDurationMinutes);
            ScheduleCitizen(engine, initiator, CitizenEventNames.ActionComplete, initiator.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
        }
        Relationships(engine).Add((first.Id.Value, target.Id.Value), new RelationshipState(first.Id, target.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        Relationships(engine).Add((second.Id.Value, target.Id.Value), new RelationshipState(second.Id, target.Id, 10_000, 10_000, 10_000, 0, 0, 1));

        Assert.True(engine.ProcessNextEvent());
        Assert.Equal(CitizenAction.Socialize, second.CurrentAction);
        Assert.Equal(first.Id, target.PartnerId);
        Assert.True(engine.ProcessNextEvent());

        Assert.Equal(first.Id, target.PartnerId);
        Assert.Equal(target.Id, first.PartnerId);
        Assert.Null(second.PartnerId);
        Assert.Single(engine.Households);
        Assert.Equal(nextEntityBefore + 1, engine.CounterSnapshot.NextEntityId);
        var snapshot = engine.CreatePersistenceSnapshot();
        var reloaded = SimulationEngine.FromPersistenceSnapshot(snapshot).CreatePersistenceSnapshot();
        Assert.Equal(snapshot.Citizens, reloaded.Citizens);
        Assert.Equal(snapshot.Relationships, reloaded.Relationships);
        Assert.Equal(snapshot.Households.Select(HouseholdKey), reloaded.Households.Select(HouseholdKey));
        Assert.Equal(snapshot.Counters, reloaded.Counters);
    }

    [Fact]
    public void OneScheduledFamilyCheckBirthsTwoHouseholdsInAscendingIdAndSharedEntityOrder()
    {
        var seed = FindTwoBirthSeed();
        var firstRun = PrepareTwoBirthHouseholdEngine(seed);
        ProcessOneFamilyCheck(firstRun.Engine);
        var firstSnapshot = firstRun.Engine.CreatePersistenceSnapshot();
        var firstChildren = firstSnapshot.Citizens.Where(citizen => citizen.ParentAId is not null).OrderBy(citizen => citizen.Id.Value).ToArray();

        Assert.Equal(2, firstChildren.Length);
        Assert.Equal(firstRun.FirstHousehold.Id, firstChildren[0].HouseholdId);
        Assert.Equal(firstRun.SecondHousehold.Id, firstChildren[1].HouseholdId);
        Assert.Equal(new CitizenId(firstRun.InitialNextEntityId), firstChildren[0].Id);
        Assert.Equal(new CitizenId(firstRun.InitialNextEntityId + 1), firstChildren[1].Id);
        Assert.Equal(firstRun.InitialNextEntityId + 2, firstSnapshot.Counters.NextEntityId);
        Assert.Equal(firstRun.FirstParentIds, (firstChildren[0].ParentAId, firstChildren[0].ParentBId));
        Assert.Equal(firstRun.SecondParentIds, (firstChildren[1].ParentAId, firstChildren[1].ParentBId));
        AssertEntityCollectionsDisjoint(firstSnapshot);

        var reloaded = SimulationEngine.FromPersistenceSnapshot(firstSnapshot).CreatePersistenceSnapshot();
        Assert.Equal(firstSnapshot.Citizens, reloaded.Citizens);
        Assert.Equal(firstSnapshot.Relationships, reloaded.Relationships);
        Assert.Equal(firstSnapshot.Households.Select(HouseholdKey), reloaded.Households.Select(HouseholdKey));
        Assert.Equal(firstSnapshot.Counters, reloaded.Counters);
        Assert.Equal(SimulationEngine.FromPersistenceSnapshot(firstSnapshot).SocialFingerprint, SimulationEngine.FromPersistenceSnapshot(reloaded).SocialFingerprint);

        var secondRun = PrepareTwoBirthHouseholdEngine(seed);
        ProcessOneFamilyCheck(secondRun.Engine);
        var secondSnapshot = secondRun.Engine.CreatePersistenceSnapshot();
        Assert.Equal(firstSnapshot.Citizens, secondSnapshot.Citizens);
        Assert.Equal(firstSnapshot.Relationships, secondSnapshot.Relationships);
        Assert.Equal(firstSnapshot.Households.Select(HouseholdKey), secondSnapshot.Households.Select(HouseholdKey));
        Assert.Equal(firstSnapshot.Counters, secondSnapshot.Counters);
        Assert.Equal(SimulationEngine.FromPersistenceSnapshot(firstSnapshot).SocialFingerprint, SimulationEngine.FromPersistenceSnapshot(secondSnapshot).SocialFingerprint);
    }

    [Fact]
    public void ChunkingReloadAndAggressiveObserverReadsAreSocialFingerprintIndependent()
    {
        var target = new WorldMinute(28L * WorldCalendar.MinutesPerDay);
        var oneShot = Advance(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion), target, [target.Value]);
        var fixedChunks = Advance(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion), target, Enumerable.Range(1, 28).Select(day => (long)day * WorldCalendar.MinutesPerDay));
        var irregular = Advance(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion), target, [37L, 211L, 1_037L, 8_001L, target.Value]);
        var reloaded = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        for (var day = 1; day <= 28; day++)
        {
            reloaded.AdvanceUntil(new WorldMinute(day * WorldCalendar.MinutesPerDay));
            reloaded = SimulationEngine.FromPersistenceSnapshot(reloaded.CreatePersistenceSnapshot());
        }
        var observed = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        while (observed.NextScheduledEventMinute is { } due && due <= target) { _ = observed.CreateReadSnapshot(); _ = observed.Citizens; _ = observed.Relationships; _ = observed.Households; observed.ProcessNextEvent(); }
        observed.AdvanceUntil(target);

        Assert.Equal(oneShot.SocialFingerprint, fixedChunks.SocialFingerprint);
        Assert.Equal(oneShot.SocialFingerprint, irregular.SocialFingerprint);
        Assert.Equal(oneShot.SocialFingerprint, reloaded.SocialFingerprint);
        Assert.Equal(oneShot.SocialFingerprint, observed.SocialFingerprint);
    }

    [Theory]
    [InlineData(42UL, 360L)]
    [InlineData(0UL, 7L)]
    [InlineData(ulong.MaxValue, 7L)]
    public void M5SocialFingerprintGoldenVectorsRemainLocked(ulong seed, long days)
    {
        var engine = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(days * WorldCalendar.MinutesPerDay));
        Assert.NotEmpty(engine.Relationships);
        Assert.Contains(engine.Relationships, relationship => RelationshipLabels.Derive(relationship, false, false) != RelationshipLabels.Stranger);
        Assert.Equal(Golden(seed, days), engine.SocialFingerprint);
    }

    private static string Golden(ulong seed, long days) => (seed, days) switch
    {
        (42UL, 360L) => "ad8c239554f69661dbd8368166e3677f9d75b9ad51f4cfcfb384ef4fb06ea781",
        (0UL, 7L) => "c89ececb36dea0cff34ce1e8ad7da885a8c5ac3dc457b4bdeb62d1ef95556de1",
        (ulong.MaxValue, 7L) => "ed1f56877aad14350a351ea6325357f43026cdd1fb9c8e547e16fbdc048b198d",
        _ => throw new ArgumentOutOfRangeException(nameof(seed))
    };

    private static SimulationEngine Advance(SimulationEngine engine, WorldMinute target, IEnumerable<long> chunks)
    {
        foreach (var chunk in chunks.OrderBy(x => x)) engine.AdvanceUntil(new WorldMinute(Math.Min(chunk, target.Value)));
        return engine;
    }

    private static SimulationEngine PrepareSocialEngine(ulong seed)
    {
        var engine = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var people = Citizens(engine);
        people[1].Location = new TileCoordinate(20, 20);
        people[2].Location = new TileCoordinate(21, 20);
        return engine;
    }

    private static ulong FindTwoBirthSeed()
    {
        for (ulong seed = 0; seed < 2_000_000; seed++)
        {
            var random = new DeterministicRandom(new WorldSeed(seed));
            var firstDraw = random.NextUInt64(RandomDomain.Reproduction, 23, 1, 0x4249525448UL) % 10_000;
            var secondDraw = random.NextUInt64(RandomDomain.Reproduction, 24, 1, 0x4249525448UL) % 10_000;
            if (firstDraw < 35 && secondDraw < 35) return seed;
        }

        throw new InvalidOperationException("No deterministic two-household birth seed found in the bounded search.");
    }

    private sealed record TwoBirthSetup(SimulationEngine Engine, Household FirstHousehold, Household SecondHousehold, long InitialNextEntityId, (CitizenId A, CitizenId B) FirstParentIds, (CitizenId A, CitizenId B) SecondParentIds);

    private static TwoBirthSetup PrepareTwoBirthHouseholdEngine(ulong seed)
    {
        var engine = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion, captureFamilyCheckDiagnostics: true);
        var people = Citizens(engine);
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var sites = engine.World.Tiles.Where(tile => tile.Buildable && tile.Coordinate != engine.World.StartingSite && engine.World.GetResources(tile.Coordinate).Count == 0).Select(tile => tile.Coordinate).Take(2).ToArray();
        Assert.Equal(2, sites.Length);
        var firstShelter = CompletedShelter(counters.AllocateStructureId(), sites[0]);
        var secondShelter = CompletedShelter(counters.AllocateStructureId(), sites[1]);
        Structures(engine).Add(firstShelter.Id.Value, firstShelter);
        Structures(engine).Add(secondShelter.Id.Value, secondShelter);
        Contributions(engine).Add((firstShelter.Id.Value, people[1].Id.Value), new StructureContribution(firstShelter.Id, people[1].Id, firstShelter.CompletedWork, firstShelter.DeliveredWood, firstShelter.DeliveredStone));
        Contributions(engine).Add((secondShelter.Id.Value, people[3].Id.Value), new StructureContribution(secondShelter.Id, people[3].Id, secondShelter.CompletedWork, secondShelter.DeliveredWood, secondShelter.DeliveredStone));
        var firstHousehold = new Household(counters.AllocateHouseholdId(), engine.CurrentMinute.Value) { DwellingStructureId = firstShelter.Id };
        var secondHousehold = new Household(counters.AllocateHouseholdId(), engine.CurrentMinute.Value) { DwellingStructureId = secondShelter.Id };
        Households(engine).Add(firstHousehold.Id.Value, firstHousehold);
        Households(engine).Add(secondHousehold.Id.Value, secondHousehold);
        var first = people[1];
        var second = people[2];
        var third = people[3];
        var fourth = people[4];
        first.PartnerId = second.Id;
        second.PartnerId = first.Id;
        first.HouseholdId = firstHousehold.Id;
        second.HouseholdId = firstHousehold.Id;
        first.HomeStructureId = firstShelter.Id;
        second.HomeStructureId = firstShelter.Id;
        third.PartnerId = fourth.Id;
        fourth.PartnerId = third.Id;
        third.HouseholdId = secondHousehold.Id;
        fourth.HouseholdId = secondHousehold.Id;
        third.HomeStructureId = secondShelter.Id;
        fourth.HomeStructureId = secondShelter.Id;
        Relationships(engine).Add((first.Id.Value, second.Id.Value), new RelationshipState(first.Id, second.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        Relationships(engine).Add((third.Id.Value, fourth.Id.Value), new RelationshipState(third.Id, fourth.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        foreach (var citizen in people.Values.Where(citizen => citizen.Id.Value >= 5)) KillFixtureCitizen(engine, citizen);
        var familyMinute = new WorldMinute(WorldCalendar.MinutesPerDay);
        foreach (var citizen in people.Values)
        {
            if (!citizen.IsAlive) continue;
            RemoveActionEvent(engine, citizen.Id);
            ScheduleCitizen(engine, citizen, CitizenEventNames.Decision, familyMinute, CitizenEventNames.DecisionPriority);
        }
        var demand = ScheduledEvents(engine).Single(item => item.Name == CitizenEventNames.SettlementEvaluateDemand);
        RemoveScheduledEvent(engine, demand.Id);
        engine.Settlement.DemandUpdatedMinute = familyMinute.Value;
        typeof(SimulationEngine).GetMethod("ScheduleSettlementDemand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);
        Assert.Equal(2, people.Values.Count(citizen => citizen.IsAlive && citizen.HomeStructureId == firstShelter.Id));
        Assert.Equal(2, people.Values.Count(citizen => citizen.IsAlive && citizen.HomeStructureId == secondShelter.Id));
        return new TwoBirthSetup(engine, firstHousehold, secondHousehold, counters.Snapshot.NextEntityId, (first.Id, second.Id), (third.Id, fourth.Id));
    }

    private static void ProcessOneFamilyCheck(SimulationEngine engine)
    {
        while (true)
        {
            var next = ScheduledEvents(engine).OrderBy(item => item.Order.DueWorldMinute.Value).ThenBy(item => item.Order.Priority).ThenBy(item => item.Order.EntitySortKey).ThenBy(item => item.Order.Sequence).First();
            if (next.Name == CitizenEventNames.FamilyCheck) break;
            Assert.True(engine.ProcessNextEvent());
        }
        Assert.True(engine.ProcessNextEvent());
        Assert.Equal(CitizenEventNames.LifecycleCheck, ScheduledEvents(engine).OrderBy(item => item.Order.DueWorldMinute.Value).ThenBy(item => item.Order.Priority).First().Name);
        Assert.True(engine.ProcessNextEvent());
    }

    private static void AssertEntityCollectionsDisjoint(SimulationPersistenceSnapshot snapshot)
    {
        var citizenIds = snapshot.Citizens.Select(citizen => citizen.Id.Value).ToHashSet();
        var structureIds = snapshot.Structures.Select(structure => structure.Id.Value).ToHashSet();
        var householdIds = snapshot.Households.Select(household => household.Id.Value).ToHashSet();
        Assert.Equal(snapshot.Citizens.Count, citizenIds.Count);
        Assert.Equal(snapshot.Structures.Count, structureIds.Count);
        Assert.Equal(snapshot.Households.Count, householdIds.Count);
        Assert.Empty(citizenIds.Intersect(structureIds));
        Assert.Empty(citizenIds.Intersect(householdIds));
        Assert.Empty(structureIds.Intersect(householdIds));
    }

    private static ulong FindSocialSeed(bool negative)
    {
        for (ulong seed = 0; ; seed++)
        {
            var engine = PrepareSocialEngine(seed);
            var a = Citizens(engine)[1]; var b = Citizens(engine)[2];
            var draw = new DeterministicRandom(new WorldSeed(seed)).NextUInt64(RandomDomain.Relationships, 1, 2, 1 ^ 0x544f4e45UL) % 10000;
            if ((draw < 500) == negative) return seed;
        }
    }

    private static ulong FindBirthSeed()
    {
        for (ulong seed = 0; ; seed++)
            if (new DeterministicRandom(new WorldSeed(seed)).NextUInt64(RandomDomain.Reproduction, 22, 0, 0x4249525448UL) % 10000 < 35) return seed;
    }

    private static (Citizen First, Citizen Second, Household Household) PrepareBirthHousehold(SimulationEngine engine)
    {
        var people = Citizens(engine); var first = people[1]; var second = people[2];
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var shelter = new Structure(counters.AllocateStructureId(), StructureType.Shelter, new TileCoordinate(20, 20), 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
        { Status = StructureStatus.Complete, CompletedMinute = 0, DeliveredWood = CitizenSimulationRules.ShelterRequiredWood, DeliveredStone = CitizenSimulationRules.ShelterRequiredStone, CompletedWork = CitizenSimulationRules.ShelterRequiredWork };
        Structures(engine).Add(shelter.Id.Value, shelter);
        Contributions(engine).Add((shelter.Id.Value, first.Id.Value), new StructureContribution(shelter.Id, first.Id, shelter.CompletedWork, shelter.DeliveredWood, shelter.DeliveredStone));
        var household = new Household(counters.AllocateHouseholdId(), 0) { DwellingStructureId = shelter.Id };
        Households(engine).Add(household.Id.Value, household);
        first.PartnerId = second.Id; second.PartnerId = first.Id; first.HouseholdId = household.Id; second.HouseholdId = household.Id; first.HomeStructureId = shelter.Id; second.HomeStructureId = shelter.Id;
        Relationships(engine).Add((first.Id.Value, second.Id.Value), new RelationshipState(first.Id, second.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        return (first, second, household);
    }

    private static Structure CompletedShelter(StructureId id, TileCoordinate site) => new(id, StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
    {
        Status = StructureStatus.Complete,
        CompletedMinute = 0,
        DeliveredWood = CitizenSimulationRules.ShelterRequiredWood,
        DeliveredStone = CitizenSimulationRules.ShelterRequiredStone,
        CompletedWork = CitizenSimulationRules.ShelterRequiredWork
    };

    private static void CompleteSocialize(SimulationEngine engine, Citizen initiator, Citizen target)
    {
        initiator.CurrentAction = CitizenAction.Socialize; initiator.ActionPhase = CitizenActionPhase.Perform; initiator.TargetCitizenId = target.Id;
        typeof(SimulationEngine).GetMethod("CompleteSocialize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [initiator]);
    }
    private static void FormPartnership(SimulationEngine engine, Citizen first, Citizen second, RelationshipState relationship) => typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, relationship]);
    private static void TryBirth(SimulationEngine engine, Household household) => typeof(SimulationEngine).GetMethod("TryBirth", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [household, null]);
    private static void CreateChild(SimulationEngine engine, Citizen first, Citizen second, Household household) => typeof(SimulationEngine).GetMethod("CreateChild", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, household]);
    private static void ScheduleCitizen(SimulationEngine engine, Citizen citizen, string name, WorldMinute due, int priority) => typeof(SimulationEngine).GetMethod("ScheduleCitizen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [citizen, name, due, priority]);
    private static void RemoveActionEvent(SimulationEngine engine, CitizenId citizenId)
    {
        var events = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var set = events.GetType();
        var remove = set.GetMethod("Remove")!;
        foreach (var item in events.Cast<object>().Where(item =>
        {
            var name = item.GetType().GetProperty("Name")!.GetValue(item) as string;
            var order = (ScheduledEventOrder)item.GetType().GetProperty("Order")!.GetValue(item)!;
            return name is (CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) && order.EntitySortKey == citizenId.Value;
        }).ToArray()) remove.Invoke(events, [item]);
    }
    private static void RemoveScheduledEvent(SimulationEngine engine, ScheduledEventId id)
    {
        var events = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var remove = events.GetType().GetMethod("Remove")!;
        foreach (var item in events.Cast<object>().Where(item => ((ScheduledEventId)item.GetType().GetProperty("Id")!.GetValue(item)!) == id).ToArray()) remove.Invoke(events, [item]);
    }
    private static void KillFixtureCitizen(SimulationEngine engine, Citizen citizen)
    {
        RemoveActionEvent(engine, citizen.Id);
        foreach (var item in ScheduledEvents(engine).Where(item => item.Name == CitizenEventNames.SurvivalCheck && item.Order.EntitySortKey == citizen.Id.Value).ToArray()) RemoveScheduledEvent(engine, item.Id);
        citizen.Health = 0;
        citizen.DeathMinute = engine.CurrentMinute.Value;
        citizen.DeathCause = "natural";
        citizen.CurrentAction = CitizenAction.Dead;
        citizen.ActionPhase = CitizenActionPhase.None;
        citizen.ActionStartedMinute = null;
        citizen.ActionCompletesMinute = null;
        citizen.TargetCitizenId = null;
        citizen.TargetStructureId = null;
        citizen.ActionTarget = null;
    }
    private static (long Id, long Created, long? Dissolved, long? Dwelling) HouseholdKey(Household household) => (household.Id.Value, household.CreatedMinute, household.DissolvedMinute, household.DwellingStructureId?.Value);
    private static int FoodSecurityGatherContribution(SimulationEngine engine) => Assert.IsType<int>(typeof(SimulationEngine).GetMethod("FoodSecurityGatherContribution", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null));
    private static bool IsAgeEligible(CitizenAction action, int age) => (bool)typeof(SimulationEngine).GetMethod("IsAgeEligible", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action, age])!;
    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, Structure> Structures(SimulationEngine engine) => Assert.IsType<Dictionary<long, Structure>>(typeof(SimulationEngine).GetField("_structures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, Household> Households(SimulationEngine engine) => Assert.IsType<Dictionary<long, Household>>(typeof(SimulationEngine).GetField("_households", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), RelationshipState> Relationships(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), StructureContribution> Contributions(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), StructureContribution>>(typeof(SimulationEngine).GetField("_structureContributions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, ResourceState> ResourceStates(SimulationEngine engine) => Assert.IsType<Dictionary<long, ResourceState>>(typeof(SimulationEngine).GetField("_resourceStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static IEnumerable<ScheduledEventSnapshot> ScheduledEvents(SimulationEngine engine) =>
        Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine))
            .Cast<object>()
            .Select(item => new ScheduledEventSnapshot(
                (ScheduledEventId)item.GetType().GetProperty("Id")!.GetValue(item)!,
                (ScheduledEventOrder)item.GetType().GetProperty("Order")!.GetValue(item)!,
                (string)item.GetType().GetProperty("Name")!.GetValue(item)!,
                (string)item.GetType().GetProperty("PayloadJson")!.GetValue(item)!));
}
