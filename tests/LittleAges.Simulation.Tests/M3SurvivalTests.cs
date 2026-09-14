using LittleAges.Domain;
using LittleAges.Simulation;
using System.Globalization;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M3SurvivalTests
{
    [Fact]
    public void M3ActionAndPhaseValuesAreCompatibilityData()
    {
        Assert.Equal(0, (int)CitizenAction.None); Assert.Equal(4, (int)CitizenAction.Explore);
        Assert.Equal(5, (int)CitizenAction.Eat); Assert.Equal(6, (int)CitizenAction.GatherFood); Assert.Equal(7, (int)CitizenAction.GatherWood); Assert.Equal(8, (int)CitizenAction.GatherStone); Assert.Equal(9, (int)CitizenAction.Dead);
        Assert.Equal(0, (int)CitizenActionPhase.None); Assert.Equal(1, (int)CitizenActionPhase.TravelToTarget); Assert.Equal(2, (int)CitizenActionPhase.Perform); Assert.Equal(3, (int)CitizenActionPhase.ReturnToStockpile);
    }

    [Fact]
    public void SeasonalHungerCrossingsUseIndependentIntegerSegments()
    {
        var season = NeedsProjection.MinutesPerSeason;
        var autumnToWinter = NeedsProjection.ProjectHunger(0, 3 * season - 7, 3 * season + 11);
        Assert.Equal(7 * 2 + 11 * 3, autumnToWinter);
        var winterToSpring = NeedsProjection.ProjectHunger(0, 4 * season - 9, 4 * season + 13);
        Assert.Equal(9 * 3 + 13 * 2, winterToSpring);
        var one = NeedsProjection.ProjectHunger(100, 3 * season - 120, 3 * season + 240);
        var chunks = NeedsProjection.ProjectHunger(NeedsProjection.ProjectHunger(100, 3 * season - 120, 3 * season), 3 * season, 3 * season + 80);
        chunks = NeedsProjection.ProjectHunger(chunks, 3 * season + 80, 3 * season + 120);
        chunks = NeedsProjection.ProjectHunger(chunks, 3 * season + 120, 3 * season + 240);
        Assert.Equal(one, chunks);
    }

    [Fact]
    public void FreshM3InitializesStarterStockpileResourcesAndCanonicalEvents()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        Assert.Equal("m3-rng1-survival1", engine.SimulationRulesVersion);
        Assert.Equal(400, engine.Settlement.FoodStored); Assert.Equal(0, engine.Settlement.WoodStored); Assert.Equal(0, engine.Settlement.StoneStored);
        Assert.Equal(engine.World.Resources.Count, engine.ResourceStates.Count);
        var events = engine.CreatePersistenceSnapshot().ScheduledEvents;
        Assert.Equal(1, events.Count(x => x.Name == CitizenEventNames.ResourceRegenerate));
        Assert.Equal(20, events.Count(x => x.Name == CitizenEventNames.SurvivalCheck));
        Assert.All(engine.Citizens, c => Assert.True(c.IsAlive));
    }

    [Fact]
    public void FreshM3AtNonzeroMinuteUsesCurrentUpdateBoundariesAndProjectsForward()
    {
        const long initialMinute = 1234;
        var engine = new SimulationEngine(new WorldSeed(42), new WorldMinute(initialMinute), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = engine.CreatePersistenceSnapshot();

        Assert.All(engine.Citizens, citizen =>
        {
            Assert.Equal(initialMinute, citizen.NeedsUpdatedMinute);
            Assert.Equal(initialMinute, citizen.HealthUpdatedMinute);
            Assert.Equal(citizen.Needs, citizen.GetProjectedNeeds(new WorldMinute(initialMinute)));
        });
        Assert.Equal(20, snapshot.ScheduledEvents.Count(eventSnapshot => eventSnapshot.Name == CitizenEventNames.Decision && eventSnapshot.Order.DueWorldMinute.Value == initialMinute));
        Assert.Equal(20, snapshot.ScheduledEvents.Count(eventSnapshot => eventSnapshot.Name == CitizenEventNames.SurvivalCheck && eventSnapshot.Order.DueWorldMinute.Value == initialMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var first = snapshot.Citizens[0];
        var projected = first.GetProjectedNeeds(new WorldMinute(initialMinute + 1));
        Assert.Equal(Math.Min(CitizenNeeds.Maximum, first.Needs.Hunger + NeedsProjection.HungerRatePerMinute), projected.Hunger);
        Assert.Equal(Math.Min(CitizenNeeds.Maximum, first.Needs.Rest + NeedsProjection.RestRatePerMinute), projected.Rest);
        Assert.Equal(Math.Min(CitizenNeeds.Maximum, first.Needs.Shelter + NeedsProjection.ShelterRatePerMinute), projected.Shelter);
        Assert.Equal(Math.Min(CitizenNeeds.Maximum, first.Needs.Social + NeedsProjection.SocialRatePerMinute), projected.Social);
        Assert.Equal(initialMinute, first.HealthUpdatedMinute);

        first.Needs = new CitizenNeeds(9000, 0, 0, 0);
        first.Health = 1000;
        foreach (var citizen in snapshot.Citizens)
        {
            citizen.CurrentAction = CitizenAction.Idle;
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionStartedMinute = new WorldMinute(initialMinute);
            citizen.ActionCompletesMinute = new WorldMinute(initialMinute + 1_000_000);
        }
        var isolatedEvents = snapshot.ScheduledEvents.Select(eventSnapshot =>
            eventSnapshot.Name == CitizenEventNames.Decision
                ? eventSnapshot with
                {
                    Name = CitizenEventNames.ActionComplete,
                    Order = new ScheduledEventOrder(new WorldMinute(initialMinute + 1_000_000), CitizenEventNames.CompletionPriority, eventSnapshot.Order.EntitySortKey, eventSnapshot.Order.Sequence),
                    PayloadJson = $"{{\"citizenId\":\"{eventSnapshot.Order.EntitySortKey}\",\"actionSequence\":0}}"
                }
                : eventSnapshot).ToArray();
        var isolated = SimulationEngine.FromPersistenceSnapshot(new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute,
            snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration,
            snapshot.Counters, isolatedEvents, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion,
            snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion));
        var beforeSurvival = isolated.GetCitizen(new CitizenId(1))!;
        var survivalMinute = new WorldMinute(initialMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes);
        var expectedNeeds = beforeSurvival.GetProjectedNeeds(survivalMinute);
        var expectedDamage = 0;
        if (expectedNeeds.Hunger >= CitizenSimulationRules.StarvationThreshold) expectedDamage += CitizenSimulationRules.StarvationDamagePerCheck;
        if (expectedNeeds.Rest >= CitizenSimulationRules.ExhaustionThreshold) expectedDamage += CitizenSimulationRules.ExhaustionDamagePerCheck;
        expectedDamage = checked((expectedDamage * (10000 - (beforeSurvival.Traits.Resilience / 4))) / 10000);
        var expectedHealth = expectedDamage > 0
            ? Math.Max(0, beforeSurvival.Health - expectedDamage)
            : expectedNeeds.Hunger < CitizenSimulationRules.RecoveryHungerThreshold && expectedNeeds.Rest < CitizenSimulationRules.RecoveryRestThreshold
                ? Math.Min(10000, beforeSurvival.Health + CitizenSimulationRules.HealthRecoveryPerCheck + beforeSurvival.Traits.Resilience / 1000)
                : beforeSurvival.Health;
        Assert.Equal(initialMinute, beforeSurvival.HealthUpdatedMinute);
        isolated.AdvanceUntil(survivalMinute);
        var afterSurvival = isolated.GetCitizen(new CitizenId(1))!;
        Assert.Equal(expectedNeeds, afterSurvival.Needs);
        Assert.Equal(expectedHealth, afterSurvival.Health);
        Assert.Equal(survivalMinute.Value, afterSurvival.NeedsUpdatedMinute);
        Assert.Equal(survivalMinute.Value, afterSurvival.HealthUpdatedMinute);
        _ = isolated.CreatePersistenceSnapshot();
    }

    [Fact]
    public void ZeroResourceM3SnapshotStillRequiresGameplayEventsAndAcceptsCanonicalState()
    {
        var source = CreateZeroResourceM3();
        var snapshot = source.CreatePersistenceSnapshot();
        Assert.Empty(snapshot.ResourceStates);
        Assert.Equal(20, snapshot.Citizens.Count);
        Assert.Equal(41, snapshot.ScheduledEvents.Count);

        var withoutGameplay = snapshot.ScheduledEvents.Where(eventSnapshot => eventSnapshot.Name is not (CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck or CitizenEventNames.ResourceRegenerate)).ToArray();
        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, withoutGameplay, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion));

        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(20, restored.LivingPopulation);
        Assert.Equal(41, restored.PendingEventCount);
    }

    [Fact]
    public void M3ValidationIgnoresSyntheticEventsThatCollideWithCitizenSortKeys()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = source.CreatePersistenceSnapshot();
        var sequence = snapshot.Counters.NextScheduledEventSequence;
        var synthetic = new ScheduledEventSnapshot(new ScheduledEventId(sequence), new ScheduledEventOrder(new WorldMinute(1000), 0, 1, sequence), "legacy.m0.event", "{}");
        var counters = snapshot.Counters with { NextScheduledEventSequence = sequence + 1 };
        var restoredSnapshot = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, counters, snapshot.ScheduledEvents.Append(synthetic).ToArray(), snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion);
        var restored = new SimulationEngine(restoredSnapshot);
        Assert.Contains(restored.CreatePersistenceSnapshot().ScheduledEvents, x => x.Name == synthetic.Name && x.Order.EntitySortKey == 1);
    }

    [Fact]
    public void M2ConstructionRetainsM2RulesAndDoesNotAddSurvivalEvents()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        Assert.Equal(SimulationEngine.PreviousSimulationRulesVersion, engine.SimulationRulesVersion);
        Assert.Equal(0, engine.Settlement.FoodStored);
        Assert.DoesNotContain(engine.CreatePersistenceSnapshot().ScheduledEvents, x => x.Name is CitizenEventNames.SurvivalCheck or CitizenEventNames.ResourceRegenerate);
    }

    [Fact]
    public void ResourceStateAndSettlementSnapshotsDoNotAliasEngineState()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = engine.CreatePersistenceSnapshot();
        snapshot.Settlement!.FoodStored = 1;
        snapshot.ResourceStates[0].CurrentQuantity = 0;
        Assert.Equal(400, engine.Settlement.FoodStored);
        Assert.NotEqual(1, engine.ResourceStates[0].CurrentQuantity);
    }

    [Fact]
    public void ReadSnapshotsDoNotExposeMutableSkillOrStockpileState()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = engine.CreateReadSnapshot();
        snapshot.Citizens[0].Skills.Foraging = int.MaxValue;
        snapshot.Settlement!.FoodStored = 0;
        Assert.NotEqual(int.MaxValue, engine.GetCitizen(new CitizenId(1))!.Skills.Foraging);
        Assert.Equal(400, engine.Settlement.FoodStored);
    }

    [Fact]
    public void RegenerationEventAdvancesDailyAndClampsQuantities()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var before = engine.ResourceStates.ToDictionary(x => x.ResourceNodeId.Value, x => x.CurrentQuantity);
        engine.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        Assert.Equal(WorldCalendar.MinutesPerDay, engine.CurrentMinute.Value);
        Assert.All(engine.ResourceStates, state => Assert.InRange(state.CurrentQuantity, 0, engine.World.Resources.Single(x => x.Id == state.ResourceNodeId).MaximumQuantity));
        Assert.Single(engine.CreatePersistenceSnapshot().ScheduledEvents, x => x.Name == CitizenEventNames.ResourceRegenerate);
        Assert.Equal(before.Count, engine.ResourceStates.Count);
    }

    [Fact]
    public void SurvivalFingerprintIsDeterministicAtDaySeven()
    {
        var first = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion); var second = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        first.AdvanceUntil(new WorldMinute(7 * WorldCalendar.MinutesPerDay)); second.AdvanceUntil(new WorldMinute(7 * WorldCalendar.MinutesPerDay));
        Assert.Equal(first.SurvivalFingerprint, second.SurvivalFingerprint);
        // Golden: seed=42, minute=10080 (post gather/target cleanup reconciliation).
        Assert.Equal("3671fac075c73d433f2b42344db4f2502e6558382152a36330a1b227854dd33a", first.SurvivalFingerprint);
        Assert.Equal(64, first.SurvivalFingerprint.Length);
    }

    [Fact]
    public void ShortSurvivalGoldensCoverZeroAndMaximumSeeds()
    {
        var zero = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion); zero.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        var maximum = new SimulationEngine(new WorldSeed(ulong.MaxValue), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion); maximum.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        // Golden: seed=0, minute=1440 (post gather/target cleanup reconciliation).
        Assert.Equal("028677a72fa65710a064dd5573c6bc5dafbf0377faed0e10597db652c3238950", zero.SurvivalFingerprint);
        // Golden: seed=UInt64.MaxValue, minute=1440 (post gather/target cleanup reconciliation).
        Assert.Equal("3ff6cfe4eea7240784a1b46365a637a74fb0785607edd5e2276ffb3b529a501f", maximum.SurvivalFingerprint);
    }

    [Fact]
    public void SurvivalFingerprintIsCultureInvariantWithNegativeFounderBirthMinutes()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var invariant = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
            invariant.AdvanceUntil(new WorldMinute(7 * WorldCalendar.MinutesPerDay));
            Assert.Contains(invariant.Citizens, citizen => citizen.BirthMinute < 0);
            var invariantHash = invariant.SurvivalFingerprint;

            var customizedCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customizedCulture.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = customizedCulture;
            CultureInfo.CurrentUICulture = customizedCulture;
            var customized = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
            customized.AdvanceUntil(new WorldMinute(7 * WorldCalendar.MinutesPerDay));
            Assert.Contains(customized.Citizens, citizen => citizen.BirthMinute < 0);
            Assert.Equal(invariantHash, customized.SurvivalFingerprint);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void DecisionEvaluationExposesSurvivalFactorsAndExplicitTies()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var evaluations = engine.EvaluateDecision(new CitizenId(1));
        Assert.Contains(evaluations, x => x.Action == CitizenAction.Eat);
        Assert.Contains(evaluations, x => x.Action == CitizenAction.GatherFood);
        Assert.All(evaluations, x => Assert.True(x.FinalScore == x.BaseUtility + x.NeedContribution + x.TraitContribution + x.Variation + x.SkillContribution + x.StockpileContribution - x.TravelPenalty));
    }

    [Fact]
    public void SurvivalRulesCentralizeGatherEatHealthAndRegenerationConstants()
    {
        Assert.Equal(180, CitizenSimulationRules.BaseGatherDurationMinutes); Assert.Equal(60, CitizenSimulationRules.MinimumGatherDurationMinutes);
        Assert.Equal(12, CitizenSimulationRules.FoodBaseYield); Assert.Equal(10, CitizenSimulationRules.WoodBaseYield); Assert.Equal(8, CitizenSimulationRules.StoneBaseYield);
        Assert.Equal(25, CitizenSimulationRules.GatherExperienceGain); Assert.Equal(30, CitizenSimulationRules.EatDurationMinutes); Assert.Equal(10, CitizenSimulationRules.MealFoodUnits);
        Assert.Equal(5000, CitizenSimulationRules.FullHungerReduction); Assert.Equal(4000, CitizenSimulationRules.RestNeedReduction); Assert.Equal(360, CitizenSimulationRules.SurvivalCheckIntervalMinutes);
        Assert.Equal(12500, CitizenSimulationRules.FoodSpringBasisPoints); Assert.Equal(15000, CitizenSimulationRules.FoodSummerBasisPoints); Assert.Equal(10000, CitizenSimulationRules.FoodAutumnBasisPoints); Assert.Equal(2500, CitizenSimulationRules.FoodWinterBasisPoints);
    }

    [Fact]
    public void ResourceRegenerationPayloadIsMinimalAndCanonical()
    {
        var valid = new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(WorldMinute.Zero, CitizenEventNames.RegenerationPriority, 0, 1), CitizenEventNames.ResourceRegenerate, "{\"version\":1}");
        Assert.Same(valid, valid.Validate());
        Assert.Throws<ArgumentException>(() => (valid with { PayloadJson = "{\"version\":1,\"extra\":0}" }).Validate());
    }

    [Fact]
    public void M3RunProducesGatheringAndPreservesResourceBounds()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var before = engine.Citizens.ToDictionary(x => x.Id.Value, x => x.Skills.Foraging);
        engine.AdvanceUntil(new WorldMinute(10080));
        Assert.Contains(engine.Citizens, x => x.Skills.Foraging > before[x.Id.Value]);
        Assert.All(engine.ResourceStates, state => Assert.InRange(state.CurrentQuantity, 0, engine.World.Resources.Single(x => x.Id == state.ResourceNodeId).MaximumQuantity));
    }

    [Fact]
    public void SurvivalCheckAppliesIndependentStarvationAndResilienceDamage()
    {
        var engine = ConfigureM3(citizen =>
        {
            citizen.Health = 1000;
            citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
        }, food: 0, zeroFoodNodes: true);
        var before = engine.GetCitizen(new CitizenId(1))!;
        var expectedDamage = (CitizenSimulationRules.StarvationDamagePerCheck * (10000 - before.Traits.Resilience / 4)) / 10000;
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var after = engine.GetCitizen(new CitizenId(1))!;
        Assert.Equal(1000 - expectedDamage, after.Health);
        Assert.Equal(CitizenSimulationRules.SurvivalCheckIntervalMinutes, after.HealthUpdatedMinute);
        Assert.True(after.IsAlive);
    }

    [Fact]
    public void StarvationDeathHasExactCleanupAndFrozenAge()
    {
        var engine = ConfigureM3(citizen =>
        {
            citizen.Health = 1;
            citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
        }, food: 0, zeroFoodNodes: true);
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var dead = engine.GetCitizen(new CitizenId(1))!;
        Assert.False(dead.IsAlive);
        Assert.Equal(CitizenAction.Dead, dead.CurrentAction);
        Assert.Equal("starvation", dead.DeathCause);
        Assert.Equal(CitizenSimulationRules.SurvivalCheckIntervalMinutes, dead.DeathMinute);
        Assert.Equal(dead.AgeYears(new WorldMinute(1)), dead.AgeYears(new WorldMinute(WorldCalendar.MinutesPerYear * 100)));
        Assert.DoesNotContain(engine.CreatePersistenceSnapshot().ScheduledEvents, e => e.Order.EntitySortKey == dead.Id.Value);
    }

    [Fact]
    public void AdditiveStarvationAndExhaustionDeathUsesDeprivationCause()
    {
        var engine = ConfigureM3(citizen =>
        {
            citizen.Health = 1;
            citizen.Needs = new CitizenNeeds(9000, 9500, 0, 0);
        }, food: 0, zeroFoodNodes: true);
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var dead = engine.GetCitizen(new CitizenId(1))!;
        Assert.False(dead.IsAlive);
        Assert.Equal("deprivation", dead.DeathCause);
    }

    [Fact]
    public void ExhaustionDamageIsIndependentOfStarvation()
    {
        var engine = ConfigureM3(citizen =>
        {
            citizen.Health = 1000;
            citizen.Needs = new CitizenNeeds(0, 9500, 0, 0);
        }, food: 0, zeroFoodNodes: true);
        var before = engine.GetCitizen(new CitizenId(1))!;
        var expectedDamage = (CitizenSimulationRules.ExhaustionDamagePerCheck * (10000 - before.Traits.Resilience / 4)) / 10000;
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        Assert.Equal(1000 - expectedDamage, engine.GetCitizen(new CitizenId(1))!.Health);
    }

    [Fact]
    public void RecoveryAndShelterSocialNeedsDoNotCauseDamage()
    {
        var engine = ConfigureM3(citizen =>
        {
            citizen.Health = 1000;
            citizen.Needs = new CitizenNeeds(0, 0, CitizenNeeds.Maximum, CitizenNeeds.Maximum);
        }, food: 0, zeroFoodNodes: true);
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var citizen = engine.GetCitizen(new CitizenId(1))!;
        Assert.Equal(1000 + CitizenSimulationRules.HealthRecoveryPerCheck + citizen.Traits.Resilience / 1000, citizen.Health);
        Assert.True(citizen.IsAlive);
    }

    [Fact]
    public void EatingCompetitionIsOrderedAndNeverMakesFoodNegative()
    {
        var engine = ConfigureM3Actions((first, second) =>
        {
            foreach (var citizen in new[] { first, second })
            {
                citizen.CurrentAction = CitizenAction.Eat;
                citizen.ActionPhase = CitizenActionPhase.Perform;
                citizen.ActionStartedMinute = WorldMinute.Zero;
                citizen.ActionCompletesMinute = WorldMinute.Zero;
            }
        }, food: 5);
        engine.AdvanceUntil(WorldMinute.Zero);
        Assert.Equal(0, engine.Settlement.FoodStored);
        Assert.True(engine.Settlement.FoodStored >= 0);
    }

    [Fact]
    public void GatherUsesActualDepletionYieldAndOnlyRelevantExperience()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = source.CreatePersistenceSnapshot();
        var node = source.World.Resources.First(resource => resource.Type == ResourceType.Food);
        snapshot.ResourceStates.Single(state => state.ResourceNodeId == node.Id).CurrentQuantity = 5;
        var citizen = snapshot.Citizens.First(c => c.Location != source.World.StartingSite);
        citizen.Location = node.Coordinate;
        var foraging = citizen.Skills.Foraging;
        var woodcutting = citizen.Skills.Woodcutting;
        var stoneworking = citizen.Skills.Stoneworking;
        citizen.CurrentAction = CitizenAction.GatherFood;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        citizen.ActionCompletesMinute = WorldMinute.Zero;
        citizen.TargetResourceNodeId = node.Id;
        var events = snapshot.ScheduledEvents.Select(eventSnapshot => eventSnapshot.Order.EntitySortKey == citizen.Id.Value && eventSnapshot.Name == CitizenEventNames.Decision ? eventSnapshot with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(WorldMinute.Zero, CitizenEventNames.CompletionPriority, citizen.Id.Value, eventSnapshot.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" } : eventSnapshot).ToArray();
        var adjusted = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion);
        var engine = SimulationEngine.FromPersistenceSnapshot(adjusted);
        engine.AdvanceUntil(WorldMinute.Zero);
        var gathered = engine.GetCitizen(citizen.Id)!;
        Assert.Equal(0, engine.GetResourceState(node.Id)!.CurrentQuantity);
        Assert.Equal(5, gathered.CarriedResourceQuantity);
        Assert.Equal(ResourceType.Food, gathered.CarriedResourceType);
        Assert.Equal(foraging + CitizenSimulationRules.GatherExperienceGain, gathered.Skills.Foraging);
        Assert.Equal(woodcutting, gathered.Skills.Woodcutting);
        Assert.Equal(stoneworking, gathered.Skills.Stoneworking);
        Assert.Equal(CitizenActionPhase.ReturnToStockpile, gathered.ActionPhase);
    }

    [Fact]
    public void M3SimulationIsEquivalentAcrossTimeChunks()
    {
        var whole = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var chunked = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        whole.AdvanceUntil(new WorldMinute(720));
        chunked.AdvanceUntil(new WorldMinute(360));
        chunked.AdvanceUntil(new WorldMinute(720));
        Assert.Equal(whole.SurvivalFingerprint, chunked.SurvivalFingerprint);
    }

    [Fact]
    public void M3SnapshotRoundTripPreservesMidActionExecution()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        source.ProcessNextEvent();
        var restored = SimulationEngine.FromPersistenceSnapshot(source.CreatePersistenceSnapshot());
        source.AdvanceUntil(new WorldMinute(720));
        restored.AdvanceUntil(new WorldMinute(720));
        Assert.Equal(source.SurvivalFingerprint, restored.SurvivalFingerprint);
    }

    [Fact]
    public void M3SnapshotAtMinute35RetainsHealthBoundarySurvivalDueMinute360()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(35));
        var snapshot = source.CreatePersistenceSnapshot();
        var citizen = snapshot.Citizens.Single(x => x.Id.Value == 1);
        var survival = snapshot.ScheduledEvents.Single(x => x.Name == CitizenEventNames.SurvivalCheck && x.Order.EntitySortKey == 1);

        Assert.Equal(35, snapshot.WorldMinute.Value);
        Assert.Equal(0, citizen.HealthUpdatedMinute);
        Assert.Equal(360, survival.Order.DueWorldMinute.Value);
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(source.SurvivalFingerprint, restored.SurvivalFingerprint);
    }

    [Fact]
    public void M3SnapshotRejectsOffCadenceSurvivalDueMinute()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(35));
        var snapshot = source.CreatePersistenceSnapshot();
        var events = snapshot.ScheduledEvents.Select(x => x.Name == CitizenEventNames.SurvivalCheck && x.Order.EntitySortKey == 1
            ? x with { Order = new ScheduledEventOrder(new WorldMinute(361), x.Order.Priority, x.Order.EntitySortKey, x.Order.Sequence) }
            : x).ToArray();

        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion));
    }

    [Fact]
    public void M3SnapshotRejectsPastSurvivalDueMinute()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(35));
        var snapshot = source.CreatePersistenceSnapshot();
        var events = snapshot.ScheduledEvents.Select(x => x.Name == CitizenEventNames.SurvivalCheck && x.Order.EntitySortKey == 1
            ? x with { Order = new ScheduledEventOrder(new WorldMinute(34), x.Order.Priority, x.Order.EntitySortKey, x.Order.Sequence) }
            : x).ToArray();

        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion));
    }

    private static SimulationEngine ConfigureM3(Action<Citizen> configure, int food, bool zeroFoodNodes)
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = source.CreatePersistenceSnapshot();
        var citizen = snapshot.Citizens[0];
        configure(citizen);
        citizen.CurrentAction = CitizenAction.Idle;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        citizen.ActionCompletesMinute = new WorldMinute(1_000_000);
        snapshot.Settlement!.FoodStored = food;
        if (zeroFoodNodes) foreach (var state in snapshot.ResourceStates.Where(state => source.World.Resources.Single(node => node.Id == state.ResourceNodeId).Type == ResourceType.Food)) state.CurrentQuantity = 0;
        var events = snapshot.ScheduledEvents.Select(eventSnapshot => eventSnapshot.Order.EntitySortKey == citizen.Id.Value && eventSnapshot.Name == CitizenEventNames.Decision ? eventSnapshot with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority, eventSnapshot.Order.EntitySortKey, eventSnapshot.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" } : eventSnapshot).ToArray();
        var adjusted = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion);
        return SimulationEngine.FromPersistenceSnapshot(adjusted);
    }

    private static SimulationEngine ConfigureM3Actions(Action<Citizen, Citizen> configure, int food)
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = source.CreatePersistenceSnapshot();
        snapshot.Citizens[0].Location = source.World.StartingSite;
        snapshot.Citizens[1].Location = source.World.StartingSite;
        configure(snapshot.Citizens[0], snapshot.Citizens[1]);
        snapshot.Settlement!.FoodStored = food;
        var events = snapshot.ScheduledEvents.Select(eventSnapshot =>
        {
            if (eventSnapshot.Name != CitizenEventNames.Decision || eventSnapshot.Order.EntitySortKey is not (1 or 2)) return eventSnapshot;
            var citizen = snapshot.Citizens[(int)eventSnapshot.Order.EntitySortKey - 1];
            return eventSnapshot with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(eventSnapshot.Order.DueWorldMinute, CitizenEventNames.CompletionPriority, eventSnapshot.Order.EntitySortKey, eventSnapshot.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
        }).ToArray();
        var adjusted = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion);
        return SimulationEngine.FromPersistenceSnapshot(adjusted);
    }

    private static SimulationEngine CreateZeroResourceM3()
    {
        var configuration = WorldGenerationConfiguration.Default with
        {
            Width = 8,
            Height = 8,
            TerrainWaterThreshold = 0,
            TerrainRockThreshold = 10000,
            TerrainForestFertilityThreshold = 9998,
            TerrainDenseFertilityThreshold = 9999,
            FoodPlacementThreshold = 10000,
            WoodPlacementThreshold = 10000,
            StonePlacementThreshold = 10000,
            MinimumNearbyFood = 0,
            MinimumNearbyWood = 0,
            MinimumNearbyStone = 0,
            MinimumNearbyFreshwater = 0,
            MinimumWalkableCount = 24
        };
        for (ulong seed = 0; seed < 256; seed++)
        {
            var candidate = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion, worldConfiguration: configuration.CanonicalJson);
            if (candidate.World.Resources.Count == 0) return candidate;
        }
        throw new InvalidOperationException("The bounded zero-resource M3 test configuration did not produce a zero-resource world.");
    }
}
