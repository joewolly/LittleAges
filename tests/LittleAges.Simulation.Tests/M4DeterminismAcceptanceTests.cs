using System.Globalization;
using System.Text;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

/// <summary>Locked M4 determinism evidence. Existing M1-M3 goldens remain the compatibility authority.</summary>
public sealed class M4DeterminismAcceptanceTests
{
    [Fact]
    public void Seed42Day14SettlementFingerprintIsLockedWithAnActiveConstructionState()
    {
        // Fourteen days is long enough for the seed-42 settlement loop to establish a project,
        // while remaining short enough to be a focused, repeatable acceptance horizon.
        var engine = Advance(new WorldSeed(42), 14 * WorldCalendar.MinutesPerDay);

        Assert.Contains(engine.Structures, structure => structure.Status == StructureStatus.UnderConstruction);
        Assert.Equal("a5622ecf5cafc03d365329275736d828c262f6f5cbcab71ca878b02f28562e3e", engine.ComputeSettlementFingerprint());
    }

    [Fact]
    public void Seed0AndMaximumSeedShortHorizonSettlementFingerprintsAreLocked()
    {
        // A two-day horizon exercises the demand event and handles both valid UInt64 extremes.
        var zero = Advance(new WorldSeed(0), 2 * WorldCalendar.MinutesPerDay).ComputeSettlementFingerprint();
        var maximum = Advance(new WorldSeed(ulong.MaxValue), 2 * WorldCalendar.MinutesPerDay).ComputeSettlementFingerprint();
        Assert.Equal("128d3b790696118f2802cbbb6c0487b532487b408b056f4220aa8b6c2aa090ce", zero);
        Assert.Equal("086f441c7ad7064ec38d70ae0f746a6f9d52c9e00ad211de82056e9559eee9d2", maximum);
    }

    [Fact]
    public void Seed42FullCanonicalSettlementStateIsIndependentOfTimeChunking()
    {
        const long target = 14 * WorldCalendar.MinutesPerDay;
        var whole = Advance(new WorldSeed(42), target);
        var chunked = new SimulationEngine(new WorldSeed(42));
        foreach (var minute in new long[] { 1, 59, 360, 721, 1_440, 2_881, 5_760, 10_083, target })
            chunked.AdvanceUntil(new WorldMinute(minute));

        Assert.Equal(CanonicalSnapshot(whole.CreatePersistenceSnapshot()), CanonicalSnapshot(chunked.CreatePersistenceSnapshot()));
        Assert.Equal(whole.ComputeSettlementFingerprint(), chunked.ComputeSettlementFingerprint());
    }

    [Fact]
    public void SettlementFingerprintIsInvariantUnderCustomNegativeSignCulture()
    {
        var expected = Advance(new WorldSeed(42), 14 * WorldCalendar.MinutesPerDay).ComputeSettlementFingerprint();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        customCulture.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = customCulture;
            CultureInfo.CurrentUICulture = customCulture;
            Assert.Equal(expected, Advance(new WorldSeed(42), 14 * WorldCalendar.MinutesPerDay).ComputeSettlementFingerprint());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void CheckpointReloadPreservesEqualCostGatherHaulBuildAndShelterRestRoutes()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var baseline = source.CreatePersistenceSnapshot();
        var gatherNode = Assert.Single(source.World.Resources, node => node.Coordinate == new TileCoordinate(15, 0));
        Assert.Equal(ResourceType.Wood, gatherNode.Type);
        var gatherRoute = LockedEqualCostRoute(source.World, new TileCoordinate(0, 1), gatherNode.Coordinate);
        AssertCheckpointPreservesRoute("gather", CreateActiveSnapshot(baseline, gatherRoute, CitizenAction.GatherWood, CitizenActionPhase.TravelToTarget, gatherNode.Coordinate, gatherNode.Id));

        var projectSite = ValidSite(source.World, source.World.StartingSite);
        var haulProject = NewShelter(baseline, projectSite);
        Assert.Equal(new TileCoordinate(131, 130), source.World.StartingSite);
        var haulRoute = LockedEqualCostRoute(source.World, new TileCoordinate(0, 38), source.World.StartingSite);
        AssertCheckpointPreservesRoute("haul", CreateActiveSnapshot(baseline, haulRoute, CitizenAction.HaulConstruction, CitizenActionPhase.TravelToStockpile, source.World.StartingSite, targetStructureId: haulProject.Id, structures: new[] { haulProject }));

        var buildProject = NewShelter(baseline, projectSite);
        buildProject.DeliveredWood = buildProject.RequiredWood;
        buildProject.DeliveredStone = buildProject.RequiredStone;
        var buildContribution = new StructureContribution(buildProject.Id, baseline.Citizens[0].Id, 0, buildProject.RequiredWood, buildProject.RequiredStone);
        Assert.Equal(new TileCoordinate(0, 0), buildProject.Location);
        var buildRoute = LockedEqualCostRoute(source.World, new TileCoordinate(0, 23), buildProject.Location);
        AssertCheckpointPreservesRoute("build", CreateActiveSnapshot(baseline, buildRoute, CitizenAction.Build, CitizenActionPhase.TravelToTarget, buildProject.Location, targetStructureId: buildProject.Id, structures: new[] { buildProject }, contributions: new[] { buildContribution }));

        var home = NewShelter(baseline, projectSite);
        home.Status = StructureStatus.Complete;
        home.CompletedMinute = 0;
        home.DeliveredWood = home.RequiredWood;
        home.DeliveredStone = home.RequiredStone;
        home.CompletedWork = home.RequiredWork;
        var homeContribution = new StructureContribution(home.Id, baseline.Citizens[0].Id, home.RequiredWork, home.RequiredWood, home.RequiredStone);
        var restRoute = LockedEqualCostRoute(source.World, new TileCoordinate(0, 23), home.Location);
        AssertCheckpointPreservesRoute("rest-to-shelter", CreateActiveSnapshot(baseline, restRoute, CitizenAction.Rest, CitizenActionPhase.TravelToTarget, home.Location, targetStructureId: home.Id, homeStructureId: home.Id, structures: new[] { home }, contributions: new[] { homeContribution }));
    }

    private static SimulationEngine Advance(WorldSeed seed, long target)
    {
        var engine = new SimulationEngine(seed);
        engine.AdvanceUntil(new WorldMinute(target));
        return engine;
    }

    private static void AssertCheckpointPreservesRoute(string scenario, ActiveSnapshot active)
    {
        var uninterrupted = SimulationEngine.FromPersistenceSnapshot(active.Snapshot);
        uninterrupted.AdvanceUntil(active.FirstDue);
        var checkpoint = uninterrupted.CreatePersistenceSnapshot();
        var reloaded = SimulationEngine.FromPersistenceSnapshot(checkpoint);

        var afterFirst = Assert.IsType<Citizen>(uninterrupted.GetCitizen(active.CitizenId));
        var afterReload = Assert.IsType<Citizen>(reloaded.GetCitizen(active.CitizenId));
        Assert.Equal(active.Route[1], afterFirst.Location);
        Assert.Equal(afterFirst.Location, afterReload.Location);
        Assert.Equal(afterFirst.ActionCompletesMinute, afterReload.ActionCompletesMinute);
        Assert.Equal(uninterrupted.ComputeSettlementFingerprint(), reloaded.ComputeSettlementFingerprint());

        var nextDue = checkpoint.ScheduledEvents.Single(item => item.Name == CitizenEventNames.MoveStep && item.Order.EntitySortKey == active.CitizenId.Value).Order.DueWorldMinute;
        uninterrupted.AdvanceUntil(nextDue);
        reloaded.AdvanceUntil(nextDue);
        var afterNext = Assert.IsType<Citizen>(uninterrupted.GetCitizen(active.CitizenId));
        var afterNextReloaded = Assert.IsType<Citizen>(reloaded.GetCitizen(active.CitizenId));
        Assert.Equal(active.Route[2], afterNext.Location);
        Assert.Equal(afterNext.Location, afterNextReloaded.Location);
        Assert.Equal(uninterrupted.ComputeSettlementFingerprint(), reloaded.ComputeSettlementFingerprint());

        uninterrupted.AdvanceUntil(active.Arrival);
        reloaded.AdvanceUntil(active.Arrival);
        var arrived = Assert.IsType<Citizen>(uninterrupted.GetCitizen(active.CitizenId));
        var arrivedReloaded = Assert.IsType<Citizen>(reloaded.GetCitizen(active.CitizenId));
        Assert.Equal(active.Target, arrived.Location);
        Assert.Equal(arrived.Location, arrivedReloaded.Location);
        Assert.Equal(CanonicalSnapshot(uninterrupted.CreatePersistenceSnapshot()), CanonicalSnapshot(reloaded.CreatePersistenceSnapshot()));
        Assert.Equal(uninterrupted.ComputeSettlementFingerprint(), reloaded.ComputeSettlementFingerprint());
        Assert.True(active.HasEqualCostAlternative, $"{scenario} route must retain an equal-cost first-step alternative.");
    }

    private static ActiveSnapshot CreateActiveSnapshot(
        SimulationPersistenceSnapshot baseline,
        EqualCostRoute route,
        CitizenAction action,
        CitizenActionPhase phase,
        TileCoordinate target,
        ResourceNodeId? targetNodeId = null,
        StructureId? targetStructureId = null,
        StructureId? homeStructureId = null,
        IReadOnlyList<Structure>? structures = null,
        IReadOnlyList<StructureContribution>? contributions = null)
    {
        var citizen = baseline.Citizens[0];
        citizen.Location = route.Start;
        citizen.CurrentAction = action;
        citizen.ActionPhase = phase;
        citizen.ActionTarget = target;
        citizen.TargetResourceNodeId = targetNodeId;
        citizen.TargetStructureId = targetStructureId;
        citizen.HomeStructureId = homeStructureId;
        citizen.CarriedResourceType = null;
        citizen.CarriedResourceQuantity = 0;
        citizen.ActionStartedMinute = baseline.WorldMinute;
        citizen.ActionCompletesMinute = baseline.WorldMinute.Add(SimulationEngine.RemainingPathCost(route.Path, baseline.World!));
        var firstDue = baseline.WorldMinute.Add(SimulationEngine.StepCost(route.Path[0], route.Path[1], baseline.World!));
        var events = baseline.ScheduledEvents.Select(item => item.Name == CitizenEventNames.Decision && item.Order.EntitySortKey == citizen.Id.Value
            ? item with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(firstDue, CitizenEventNames.MovementPriority, citizen.Id.Value, item.Order.Sequence) }
            : item).ToArray();
        var effectiveStructures = structures ?? baseline.Structures;
        var counters = effectiveStructures.Count == 0 ? baseline.Counters : baseline.Counters with { NextEntityId = Math.Max(baseline.Counters.NextEntityId, checked(effectiveStructures.Max(x => x.Id.Value) + 1)) };
        var snapshot = new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, counters, events, baseline.World, baseline.Citizens, baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion, baseline.SettlementVersion, effectiveStructures, contributions ?? baseline.StructureContributions);
        return new ActiveSnapshot(snapshot, citizen.Id, route.Path, target, firstDue, citizen.ActionCompletesMinute.Value, route.HasEqualCostAlternative);
    }

    private static EqualCostRoute LockedEqualCostRoute(WorldMap world, TileCoordinate start, TileCoordinate target)
    {
        var canonical = Assert.IsAssignableFrom<IReadOnlyList<TileCoordinate>>(DeterministicPathfinder.Find(world, start, target));
        Assert.True(canonical.Count >= 3);
        Assert.Equal(start, canonical[0]);
        Assert.Equal(target, canonical[^1]);
        var canonicalCost = SimulationEngine.RemainingPathCost(canonical, world);
        var alternatives = new[] { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) }
            .Select(offset => (X: start.X + offset.Item1, Y: start.Y + offset.Item2))
            .Where(value => value.X >= 0 && value.X < world.Width && value.Y >= 0 && value.Y < world.Height)
            .Select(value => new TileCoordinate(value.X, value.Y))
            .Where(value => world.GetTile(value).Walkable)
            .Select(value => (Next: value, Tail: DeterministicPathfinder.Find(world, value, target)))
            .Where(value => value.Tail is not null)
            .Select(value => (value.Next, Cost: checked(SimulationEngine.StepCost(start, value.Next, world) + SimulationEngine.RemainingPathCost(value.Tail!, world)))
            ).ToArray();
        Assert.Contains(alternatives, value => value.Next != canonical[1] && value.Cost == canonicalCost);
        return new EqualCostRoute(start, canonical, true);
    }

    private static Structure NewShelter(SimulationPersistenceSnapshot baseline, TileCoordinate site) => new(new StructureId(baseline.Counters.NextEntityId), StructureType.Shelter, site, baseline.WorldMinute.Value, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
    private static TileCoordinate ValidSite(WorldMap world, TileCoordinate excluded) => world.Tiles.First(tile => tile.Coordinate != excluded && tile.Buildable && world.GetResources(tile.Coordinate).Count == 0).Coordinate;

    private static string CanonicalSnapshot(SimulationPersistenceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.Seed.Value.ToString(CultureInfo.InvariantCulture)).Append('|').Append(snapshot.WorldMinute.Value.ToString(CultureInfo.InvariantCulture)).Append('|').Append(snapshot.World!.Fingerprint).Append('|').Append(snapshot.Counters);
        builder.Append('|').Append(snapshot.Settlement!.FoodStored.ToString(CultureInfo.InvariantCulture)).Append(',').Append(snapshot.Settlement.WoodStored.ToString(CultureInfo.InvariantCulture)).Append(',').Append(snapshot.Settlement.StoneStored.ToString(CultureInfo.InvariantCulture)).Append(',').Append(snapshot.Settlement.BaseStorageCapacity.ToString(CultureInfo.InvariantCulture)).Append(',').Append(snapshot.Settlement.DemandUpdatedMinute.ToString(CultureInfo.InvariantCulture)).Append(',').Append(snapshot.Settlement.ExposureConsequencesStartMinute.ToString(CultureInfo.InvariantCulture));
        foreach (var state in snapshot.ResourceStates.OrderBy(value => value.ResourceNodeId.Value)) builder.Append(CultureInfo.InvariantCulture, $"|r:{state.ResourceNodeId.Value}:{state.CurrentQuantity}");
        foreach (var citizen in snapshot.Citizens.OrderBy(value => value.Id.Value)) builder.Append(CultureInfo.InvariantCulture, $"|c:{citizen.Id.Value}:{citizen.Location.X},{citizen.Location.Y}:{citizen.Health}:{citizen.Needs.Hunger},{citizen.Needs.Rest},{citizen.Needs.Shelter},{citizen.Needs.Social}:{(int)citizen.CurrentAction}:{(int)citizen.ActionPhase}:{citizen.ActionSequence}:{citizen.ActionStartedMinute?.Value}:{citizen.ActionCompletesMinute?.Value}:{citizen.ActionTarget?.X},{citizen.ActionTarget?.Y}:{citizen.TargetResourceNodeId?.Value}:{citizen.TargetStructureId?.Value}:{citizen.HomeStructureId?.Value}:{citizen.CarriedResourceType}:{citizen.CarriedResourceQuantity}:{citizen.LifetimeForagingMinutes},{citizen.LifetimeWoodcuttingMinutes},{citizen.LifetimeStoneworkingMinutes},{citizen.LifetimeConstructionMinutes},{citizen.LifetimeHaulingMinutes}");
        foreach (var structure in snapshot.Structures.OrderBy(value => value.Id.Value)) builder.Append(CultureInfo.InvariantCulture, $"|s:{structure.Id.Value}:{(int)structure.Type}:{(int)structure.Status}:{structure.Location.X},{structure.Location.Y}:{structure.ConstructionStartedMinute}:{structure.CompletedMinute}:{structure.DeliveredWood},{structure.DeliveredStone},{structure.CompletedWork}");
        foreach (var contribution in snapshot.StructureContributions.OrderBy(value => value.StructureId.Value).ThenBy(value => value.CitizenId.Value)) builder.Append(CultureInfo.InvariantCulture, $"|x:{contribution.StructureId.Value}:{contribution.CitizenId.Value}:{contribution.ConstructionWork},{contribution.WoodDelivered},{contribution.StoneDelivered}");
        foreach (var item in snapshot.ScheduledEvents.OrderBy(value => value.Order)) builder.Append(CultureInfo.InvariantCulture, $"|e:{item.Id.Value}:{item.Order.DueWorldMinute.Value}:{item.Order.Priority}:{item.Order.EntitySortKey}:{item.Order.Sequence}:{item.Name}:{item.PayloadJson}");
        return builder.ToString();
    }

    private sealed record EqualCostRoute(TileCoordinate Start, IReadOnlyList<TileCoordinate> Path, bool HasEqualCostAlternative);
    private sealed record ActiveSnapshot(SimulationPersistenceSnapshot Snapshot, CitizenId CitizenId, IReadOnlyList<TileCoordinate> Route, TileCoordinate Target, WorldMinute FirstDue, WorldMinute Arrival, bool HasEqualCostAlternative);
}
