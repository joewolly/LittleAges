using System.Collections;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M15RoadBuildingTests
{
    private const string Roads = SimulationEngine.RoadsSimulationRulesVersion;
    private const long Season = 90L * WorldCalendar.MinutesPerDay;
    // Seed 42 has worn its first trails by the second season boundary.
    private const long Planning = 2 * Season;
    private const long StockMinute = Planning - 60;
    private const int StockedStone = 70;

    [Fact]
    public void SeasonalPlanningOrdersPavingOnTheMostWornTrailsOnly()
    {
        var unstocked = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: Roads);
        unstocked.AdvanceUntil(new WorldMinute(Planning));
        // The commons stay below the paving reserve, but stone can be bought from households, as construction does.
        Assert.True(unstocked.Settlement.StoneStored < SimulationEngine.RoadStoneReserve(unstocked.LivingPopulation));
        Assert.Contains(unstocked.RoadGrades, x => x.Grade == RoadGrade.Trail);
        Assert.NotEmpty(RoadOrders(unstocked));
        var roadsAtPlanning = unstocked.RoadGrades.Count(x => x.Grade == RoadGrade.Road);
        unstocked.AdvanceUntil(new WorldMinute(Planning + WorldCalendar.MinutesPerDay));
        MigrationValidation.Validate(unstocked.CreatePersistenceSnapshot());
        Assert.True(unstocked.RoadGrades.Count(x => x.Grade == RoadGrade.Road) > roadsAtPlanning);
        Assert.Contains(unstocked.CaptureEconomy()!.PublicSupplyTrades, x => x.Resource == ResourceType.Stone);

        var engine = Stocked(new WorldSeed(42), new WorldMinute(Planning));
        var snapshot = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(snapshot);
        var tiles = snapshot.MigrationState!.Roads!.Tiles.ToDictionary(x => x.Coordinate);
        var orders = RoadOrders(engine);

        Assert.Equal(4, orders.Length);
        Assert.Equal(orders.Length, orders.Select(x => x.Location).Distinct().Count());
        Assert.All(orders, order =>
        {
            Assert.Equal(RoadGrade.Trail, tiles[order.Location].Grade);
            Assert.Null(order.SubjectId);
            Assert.Equal(1800, order.Priority);
            Assert.Equal(120, order.RequiredWork);
            Assert.Equal([new LivingIngredient("Stone", 2)], order.Ingredients);
        });
        // With no daughter there is no intersite route, so the most worn trails come first.
        var ordered = orders.Select(x => x.Location).ToHashSet();
        var unordered = tiles.Values.Where(x => x.Grade == RoadGrade.Trail && !ordered.Contains(x.Coordinate)).ToArray();
        Assert.NotEmpty(unordered);
        Assert.True(orders.Min(x => tiles[x.Location].Wear) >= unordered.Max(x => x.Wear));
    }

    [Fact]
    public void FinishedRoadWorkTurnsTheTrailIntoARoad()
    {
        var engine = Stocked(new WorldSeed(42), new WorldMinute(Planning));
        var ordered = RoadOrders(engine).Select(x => x.Location).ToHashSet();
        var stone = engine.Settlement.StoneStored;
        Assert.Equal(StockedStone, stone);
        var before = engine.RoadGrades.Where(x => x.Grade == RoadGrade.Road).Select(x => x.Coordinate).ToHashSet();
        Assert.DoesNotContain(ordered, before.Contains);

        engine.AdvanceUntil(new WorldMinute(Planning + WorldCalendar.MinutesPerDay));
        var snapshot = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(snapshot);

        Assert.Empty(RoadOrders(engine));
        var paved = engine.RoadGrades.Where(x => x.Grade == RoadGrade.Road).Select(x => x.Coordinate).ToHashSet();
        paved.ExceptWith(before);
        Assert.Equal(ordered, paved);
        Assert.Equal(stone - ordered.Count * 2, engine.Settlement.StoneStored);
        Assert.Equal([new RoadSeasonWorkState(1, ordered.Count)], snapshot.MigrationState!.Roads!.SeasonWork!);
        Assert.DoesNotContain(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.RoadWorkSeason && x.WorldMinute > Planning);
    }

    [Fact]
    public void TheSeasonsPavingIsRecordedOnceWhenTheSeasonEnds()
    {
        var engine = Stocked(new WorldSeed(42), new WorldMinute(Planning + Season + 1));
        var snapshot = engine.CreatePersistenceSnapshot();
        var recorded = Assert.Single(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.RoadWorkSeason && x.WorldMinute > Planning);
        Assert.Equal(Planning + Season, recorded.WorldMinute);
        Assert.Equal(HistoricalImportance.Routine, recorded.Importance);
        Assert.Equal(engine.World.StartingSite, recorded.Location);
        Assert.Equal(HistoricalEventPayloads.RoadWorkSeason(1, 4, WorldSeason.Autumn, 0), recorded.PayloadJson);
        Assert.Null(snapshot.MigrationState!.Roads!.SeasonWork);
        // No daughter site exists, so no route between settlements can connect.
        Assert.DoesNotContain(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.RouteConnected);
    }

    [Theory]
    [InlineData(-30L)]
    [InlineData(30L)]
    public void ReopenedRunsMatchUninterruptedRunsAcrossRoadCompletion(long offsetFromPlanning)
    {
        var seed = new WorldSeed(42);
        var target = new WorldMinute(Planning + WorldCalendar.MinutesPerDay);
        var uninterrupted = Stocked(seed, target);

        var interrupted = Stocked(seed, new WorldMinute(Planning + offsetFromPlanning));
        var checkpoint = interrupted.CreatePersistenceSnapshot();
        MigrationValidation.Validate(checkpoint);
        if (offsetFromPlanning > 0)
            Assert.Contains(LivingWorldCodec.Deserialize(checkpoint.LivingStateJson!).Orders, x => x.Kind == LivingWorkKind.BuildRoad && x.CitizenId is not null);
        var reopened = new SimulationEngine(checkpoint);
        Assert.Equal(checkpoint.MigrationStateJson, reopened.CreatePersistenceSnapshot().MigrationStateJson);
        reopened.AdvanceUntil(target);

        Assert.Contains(reopened.RoadGrades, x => x.Grade == RoadGrade.Road);
        M15RoadNetworkTests.AssertEquivalent(uninterrupted, reopened);
    }

    [Fact]
    public void ChunkSizeDoesNotChangeRoadBuilding()
    {
        var seed = new WorldSeed(42);
        var target = new WorldMinute(Planning + WorldCalendar.MinutesPerDay);
        var single = Stocked(seed, target);
        var chunked = Stocked(seed, new WorldMinute(StockMinute));
        for (var minute = StockMinute + 97; minute < target.Value; minute += 97) chunked.AdvanceUntil(new WorldMinute(minute));
        chunked.AdvanceUntil(target);
        M15RoadNetworkTests.AssertEquivalent(single, chunked);
    }

    /// <summary>
    /// Runs a real M15 world and, just before the second season boundary, moves household stone into
    /// the commons so the settlement clears its paving reserve. Moving rather than creating stone keeps
    /// goods conservation and storage use unchanged.
    /// </summary>
    private static SimulationEngine Stocked(WorldSeed seed, WorldMinute until)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var engine = new SimulationEngine(seed, simulationRulesVersion: Roads);
        engine.AdvanceUntil(new WorldMinute(StockMinute));
        var type = typeof(SimulationEngine);
        var households = ((IDictionary)type.GetField("_householdStocks", flags)!.GetValue(engine)!).Keys.Cast<long>().ToArray();
        var availablePrivate = type.GetMethod("AvailablePrivate", flags)!;
        var addPrivate = type.GetMethod("AddPrivate", flags)!;
        var needed = (long)StockedStone - engine.Settlement.StoneStored;
        foreach (var household in households)
        {
            var moved = Math.Min(needed, (long)availablePrivate.Invoke(engine, [household, ResourceType.Stone])!);
            if (moved <= 0) continue;
            addPrivate.Invoke(engine, [household, ResourceType.Stone, -moved]);
            engine.Settlement.StoneStored += (int)moved;
            needed -= moved;
        }
        Assert.Equal(0, needed);
        engine.AdvanceUntil(until);
        return engine;
    }

    private static LivingWorkOrder[] RoadOrders(SimulationEngine engine) =>
        LivingWorldCodec.Deserialize(engine.LivingStateJson!).Orders.Where(x => x.Kind == LivingWorkKind.BuildRoad).ToArray();
}
