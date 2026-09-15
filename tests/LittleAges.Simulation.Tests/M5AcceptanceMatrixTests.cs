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
        (42UL, 360L) => "e74bc960449ddca847e027d0219bf3032569557b7df8f1b5c87f464a84473ffe",
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

    private static void CompleteSocialize(SimulationEngine engine, Citizen initiator, Citizen target)
    {
        initiator.CurrentAction = CitizenAction.Socialize; initiator.ActionPhase = CitizenActionPhase.Perform; initiator.TargetCitizenId = target.Id;
        typeof(SimulationEngine).GetMethod("CompleteSocialize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [initiator]);
    }
    private static void FormPartnership(SimulationEngine engine, Citizen first, Citizen second, RelationshipState relationship) => typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, relationship]);
    private static void TryBirth(SimulationEngine engine, Household household) => typeof(SimulationEngine).GetMethod("TryBirth", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [household, null]);
    private static void CreateChild(SimulationEngine engine, Citizen first, Citizen second, Household household) => typeof(SimulationEngine).GetMethod("CreateChild", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, household]);
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
