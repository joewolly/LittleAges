using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M17FarmReturnPersistenceTests
{
    private static readonly Lazy<SimulationPersistenceSnapshot> HarvestReturn = new(() => CreateHarvestReturn().CreatePersistenceSnapshot());

    [Fact]
    public async Task DaughterHarvestReturnReopensAndContinuesAcrossDifferentChunks()
    {
        var engine = new SimulationEngine(HarvestReturn.Value);
        var checkpoint = engine.CreatePersistenceSnapshot();
        var carrier = Assert.Single(checkpoint.Citizens, x => x.CurrentAction == CitizenAction.HaulHarvest &&
            checkpoint.MigrationState!.CitizenResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 2);
        Assert.Equal(CitizenActionPhase.ReturnToStockpile, carrier.ActionPhase);
        Assert.Equal(checkpoint.MigrationState!.DaughterSettlement!.Site, carrier.ActionTarget);
        Assert.Equal(108, carrier.CarriedResourceQuantity);
        var grain = LivingWorldCodec.Deserialize(checkpoint.LivingStateJson!).Orders.Single(x => x.CitizenId == carrier.Id.Value);
        Assert.Equal(new LivingStock(LivingGood.Grain, 12), Assert.Single(grain.Cargo));
        var end = engine.CurrentMinute.Add(1440);
        var expected = new SimulationEngine(checkpoint);
        expected.AdvanceUntil(end);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-farm-return-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using (var db = await WorldDatabase.OpenAsync(path))
                await db.CreateCheckpointStore().CheckpointAsync(checkpoint);
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await db.CreateCheckpointStore().LoadAsync();
                var corrupt = new SimulationEngine(checkpoint).CreatePersistenceSnapshot();
                corrupt.Citizens.Single(x => x.Id == carrier.Id).ActionTarget = corrupt.World!.StartingSite;
                await Assert.ThrowsAsync<ArgumentException>(() => db.CreateCheckpointStore().CheckpointAsync(corrupt));
                Assert.Equal(checkpoint.WorldMinute, (await db.CreateCheckpointStore().LoadAsync()).WorldMinute);
                var actual = new SimulationEngine(loaded);
                for (var minute = loaded.WorldMinute.Value + 37; minute < end.Value; minute += 37)
                    actual.AdvanceUntil(new(minute));
                actual.AdvanceUntil(end);
                var a = expected.CreatePersistenceSnapshot();
                var b = actual.CreatePersistenceSnapshot();
                Assert.Equal(a.Citizens, b.Citizens);
                Assert.Equal(a.ScheduledEvents, b.ScheduledEvents);
                Assert.Equal(a.Agriculture!.ToCanonicalJson(), b.Agriculture!.ToCanonicalJson());
                Assert.Equal(a.MigrationStateJson, b.MigrationStateJson);
                Assert.Equal(a.LivingStateJson, b.LivingStateJson);
                Assert.Equal(a.Economy!.ToUnifiedCanonicalJson(), b.Economy!.ToUnifiedCanonicalJson());
                Assert.Equal(expected.HistoryFingerprint, actual.HistoryFingerprint);
                Assert.Equal(expected.SurvivalFingerprint, actual.SurvivalFingerprint);
                await db.CreateCheckpointStore().CheckpointAsync(b);
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void ExactOwnershipRejectsWrongSiteAndLegacyDefaultStillRejectsDaughterReturn()
    {
        var snapshot = new SimulationEngine(HarvestReturn.Value).CreatePersistenceSnapshot();
        var context = AgricultureWorkSiteContext.FromMigration(snapshot.MigrationState!, snapshot.World!);
        void Validate(AgricultureWorkSiteContext? sites) => snapshot.Agriculture!.Validate(snapshot.World!,
            snapshot.Structures, snapshot.Citizens, snapshot.Settlement!.DemandUpdatedMinute,
            snapshot.WorldMinute.Value, sites);
        Validate(context);
        Assert.Throws<ArgumentException>(() => Validate(null));
        var carrier = snapshot.Citizens.Single(x => x.CurrentAction == CitizenAction.HaulHarvest &&
            snapshot.MigrationState!.CitizenResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 2);
        carrier.ActionTarget = snapshot.World!.StartingSite;
        Assert.Throws<ArgumentException>(() => Validate(context));
        carrier.ActionTarget = snapshot.MigrationState!.DaughterSettlement!.Site;
        var state = snapshot.MigrationState;
        var wrongOwner = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners.Select(x => x.EntityId == carrier.TargetStructureId!.Value.Value ? x with { SettlementId = 1 } : x).ToArray(),
            state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement, state.InTransitParties,
            state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear, state.Roads);
        Assert.Throws<ArgumentException>(() => Validate(AgricultureWorkSiteContext.FromMigration(wrongOwner, snapshot.World!)));
    }

    [Fact]
    public void M17CargoLimitsUseExactSiteAndRejectOversizedOrImpossibleCarrierSplit()
    {
        var snapshot = new SimulationEngine(HarvestReturn.Value).CreatePersistenceSnapshot();
        var carrier = snapshot.Citizens.Single(x => x.CurrentAction == CitizenAction.HaulHarvest &&
            snapshot.MigrationState!.CitizenResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 2);
        var order = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!).Orders.Single(x => x.CitizenId == carrier.Id.Value);
        Assert.True(LivingWorkDefinitions.ValidCargo(order, SimulationEngine.NewcomersRulesVersion, 2));
        Assert.False(LivingWorkDefinitions.ValidCargo(order, SimulationEngine.NewcomersRulesVersion, 1));
        Assert.False(LivingWorkDefinitions.ValidCargo(order, SimulationEngine.NewcomersRulesVersion));
        Assert.False(LivingWorkDefinitions.ValidCargo(order, SimulationEngine.FestivalsSimulationRulesVersion, 2));
        order.CitizenId = null;
        order.CargoInTransit = false;
        Assert.True(LivingWorkDefinitions.ValidCargo(order, SimulationEngine.NewcomersRulesVersion, 2));
        order.Cargo = [new(LivingGood.Grain, 13)];
        Assert.False(LivingWorkDefinitions.ValidCargo(order, SimulationEngine.NewcomersRulesVersion, 2));
        carrier.CarriedResourceQuantity = 109;
        Assert.Throws<ArgumentException>(() => LivingValidation.Validate(snapshot));
        carrier.CarriedResourceQuantity = 80;
        Assert.Throws<ArgumentException>(() => LivingValidation.Validate(snapshot));
        carrier.CarriedResourceQuantity = 108;
        carrier.TargetStructureId = snapshot.Structures.First(x => x.Type == StructureType.Farm && x.Id != carrier.TargetStructureId).Id;
        Assert.Throws<ArgumentException>(() => LivingValidation.Validate(snapshot));
    }
    internal static SimulationEngine CreateHarvestReturn(string rules = SimulationEngine.NewcomersRulesVersion)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
        engine.AdvanceUntil(new(2L * WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay + 60));
        var snapshot = engine.CreatePersistenceSnapshot();
        var living = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!);
        var farm = snapshot.Structures.First(x => x.Type == StructureType.Farm && x.Status == StructureStatus.Complete &&
            !snapshot.Citizens.Any(c => c.TargetStructureId == x.Id) &&
            !living.Orders.Any(o => o.Kind == LivingWorkKind.Harvest && o.SubjectId == x.Id.Value));
        var citizens = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var worker = citizens.Values.First(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 13 &&
            x.HouseholdId is not null && x.CarriedResourceQuantity == 0 && x.CurrentAction != CitizenAction.LivingWork &&
            !citizens.Values.Any(c => c.HouseholdId == x.HouseholdId && living.Orders.Any(o => o.CitizenId == c.Id.Value)) &&
            !citizens.Values.Any(c => c.HouseholdId == x.HouseholdId && c.CurrentAction is CitizenAction.WorkFarm or CitizenAction.HaulHarvest));
        var householdId = worker.HouseholdId!.Value;
        var members = citizens.Values.Where(x => x.HouseholdId == householdId).Select(x => x.Id.Value).ToHashSet();
        var daughterSite = engine.World.Tiles.Where(x => x.Walkable && x.Coordinate != farm.Location && x.Coordinate != engine.World.StartingSite)
            .OrderBy(x => Math.Abs(x.Coordinate.X - farm.Location.X) + Math.Abs(x.Coordinate.Y - farm.Location.Y))
            .First(x => Invoke(engine, "FindPathCached", farm.Location, x.Coordinate) is IReadOnlyList<TileCoordinate> { Count: >= 2 }).Coordinate;
        var state = snapshot.MigrationState!;
        var migration = new MigrationWorldState(state.Version,
            state.CitizenResidences.Select(x => members.Contains(x.EntityId) ? x with { SettlementId = 2 } : x).ToArray(),
            state.HouseholdResidences.Select(x => x.EntityId == householdId.Value ? x with { SettlementId = 2 } : x).ToArray(),
            state.StructureOwners.Select(x => x.EntityId == farm.Id.Value ? x with { SettlementId = 2 } : x).ToArray(),
            state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, 4000, engine.CurrentMinute.Value, engine.CurrentMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()),
            state.InTransitParties, state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear, state.Roads);
        typeof(SimulationEngine).GetField("_migrationState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, migration);
        Invoke(engine, "InitializeMigrationRuntime");
        foreach (var member in citizens.Values.Where(x => members.Contains(x.Id.Value))) member.HomeStructureId = null;
        Field<Dictionary<long, Household>>(engine, "_households")[householdId.Value].DwellingStructureId = null;
        var crops = Field<SortedDictionary<long, FarmCrop>>(engine, "_farms");
        // A tiny legacy crop carries food only: the older farm and grain validators
        // disagree about daughter return destinations when a grain order is present.
        var planting = rules == SimulationEngine.NewcomersRulesVersion ? AgricultureRules.PlantingWork : 1;
        var yield = AgricultureRules.LaborYield(engine.World.GetTile(farm.Location), planting, AgricultureRules.TendingWork);
        crops[farm.Id.Value] = new(farm.Id.Value, engine.CurrentMinute.ToCalendar().Year, CropStage.Harvest,
            planting, AgricultureRules.TendingWork, yield, yield, 0);
        var pending = Field<object>(engine, "_scheduledEvents");
        foreach (var item in ((System.Collections.IEnumerable)pending).Cast<object>().ToArray())
        {
            var type = item.GetType();
            var name = (string)type.GetProperty("Name")!.GetValue(item)!;
            var order = (ScheduledEventOrder)type.GetProperty("Order")!.GetValue(item)!;
            if (order.EntitySortKey == worker.Id.Value && name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete)
                pending.GetType().GetMethod("Remove")!.Invoke(pending, [item]);
        }
        worker.Location = farm.Location;
        worker.ActionSequence++;
        worker.CurrentAction = CitizenAction.HaulHarvest;
        worker.ActionPhase = CitizenActionPhase.Perform;
        worker.TargetStructureId = farm.Id;
        worker.TargetCitizenId = null;
        Invoke(engine, "CompleteFarmWork", worker);
        if (rules != SimulationEngine.NewcomersRulesVersion)
        {
            // Valid legacy checkpoints target the original stockpile.
            foreach (var item in ((System.Collections.IEnumerable)pending).Cast<object>().ToArray())
            {
                var type = item.GetType();
                var name = (string)type.GetProperty("Name")!.GetValue(item)!;
                var order = (ScheduledEventOrder)type.GetProperty("Order")!.GetValue(item)!;
                if (order.EntitySortKey == worker.Id.Value && name == CitizenEventNames.MoveStep)
                    pending.GetType().GetMethod("Remove")!.Invoke(pending, [item]);
            }
            Invoke(engine, "BeginTravel", worker, CitizenAction.HaulHarvest, engine.World.StartingSite, null,
                CitizenActionPhase.ReturnToStockpile, farm.Id);
        }
        return engine;
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Invoke(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(x => x.Name == name && x.GetParameters().Length == args.Length).Invoke(instance, args);
}
