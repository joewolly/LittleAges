using System.Net;
using System.Reflection;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class M15ObserverApiTests
{
    [Fact]
    public async Task RoadsRouteListsGradedTilesWithoutWearAndSettlementDetailAddsTrade()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var (checkpoint, graded) = CreateTradeInProgress(new WorldSeed(42));
            await WriteCheckpointAsync(Path.Combine(dataRoot, "integration-world.db"), checkpoint);
            using var factory = new ServerFactory(dataRoot);
            using var client = factory.CreateClient();
            await factory.Services.GetRequiredService<SimulationHost>().WaitForRunningForTestingAsync();

            using var roadsResponse = await client.GetAsync("/api/v1/roads");
            roadsResponse.EnsureSuccessStatusCode();
            using var roads = JsonDocument.Parse(await roadsResponse.Content.ReadAsStringAsync());
            var tiles = roads.RootElement.GetProperty("tiles").EnumerateArray().ToArray();
            Assert.All(tiles, tile => Assert.False(tile.TryGetProperty("wear", out _)));
            Assert.Equal(tiles.OrderBy(t => t.GetProperty("y").GetInt32()).ThenBy(t => t.GetProperty("x").GetInt32())
                .Select(t => t.GetRawText()), tiles.Select(t => t.GetRawText()));
            foreach (var (coordinate, grade) in graded)
                Assert.Contains(tiles, tile => tile.GetProperty("x").GetInt32() == coordinate.X &&
                    tile.GetProperty("y").GetInt32() == coordinate.Y && tile.GetProperty("grade").GetString() == grade.ToString());

            var party = checkpoint.MigrationState!.InTransitParties.Single(x => x.JourneyKind == MigrationJourneyKind.Trade);
            var completed = checkpoint.HistoricalEvents.Single(x => x.EventType == HistoricalEventType.TradeCompleted);
            foreach (var id in new[] { "1", "2" })
            {
                using var detailResponse = await client.GetAsync($"/api/v1/settlements/{id}");
                detailResponse.EnsureSuccessStatusCode();
                using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
                var observed = Assert.Single(detail.RootElement.GetProperty("tradeParties").EnumerateArray());
                Assert.Equal(party.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), observed.GetProperty("partyId").GetString());
                Assert.Equal("1", observed.GetProperty("originSettlementId").GetString());
                Assert.Equal("2", observed.GetProperty("destinationSettlementId").GetString());
                Assert.Equal(party.VisitPhase.ToString(), observed.GetProperty("phase").GetString());
                Assert.Equal(party.TradeLoad, observed.GetProperty("load").GetInt32());
                Assert.Equal(party.Cargo.Sum(x => x.Quantity), observed.GetProperty("cargo").EnumerateArray().Sum(x => x.GetProperty("quantity").GetInt64()));
                var trade = Assert.Single(detail.RootElement.GetProperty("recentTrades").EnumerateArray());
                Assert.Equal(completed.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), trade.GetProperty("eventId").GetString());
                Assert.Equal("Fuel", trade.GetProperty("outboundGood").GetString());
                Assert.Equal("Medicine", trade.GetProperty("returnGood").GetString());
                Assert.True(trade.GetProperty("returnQuantity").GetInt32() > 0);
            }

            using var listResponse = await client.GetAsync("/api/v1/settlements");
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            Assert.All(list.RootElement.EnumerateArray(), site => Assert.False(site.TryGetProperty("tradeParties", out _)));
            using var singularResponse = await client.GetAsync("/api/v1/settlement");
            using var singular = JsonDocument.Parse(await singularResponse.Content.ReadAsStringAsync());
            Assert.False(singular.RootElement.TryGetProperty("tradeParties", out _));
            Assert.False(singular.RootElement.TryGetProperty("recentTrades", out _));
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task M14WorldsHaveNoRoadOverlayOrTradeFields()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            await WriteCheckpointAsync(Path.Combine(dataRoot, "integration-world.db"), engine.CreatePersistenceSnapshot());
            using var factory = new ServerFactory(dataRoot);
            using var client = factory.CreateClient();
            await factory.Services.GetRequiredService<SimulationHost>().WaitForRunningForTestingAsync();

            using var roads = await client.GetAsync("/api/v1/roads");
            Assert.Equal(HttpStatusCode.NotFound, roads.StatusCode);
            using var detailResponse = await client.GetAsync("/api/v1/settlements/1");
            detailResponse.EnsureSuccessStatusCode();
            using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            Assert.False(detail.RootElement.TryGetProperty("tradeParties", out _));
            Assert.False(detail.RootElement.TryGetProperty("recentTrades", out _));
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    /// <summary>
    /// The phase 4 trade fixture (eight households at a daughter site, site 1 spare fuel, the daughter spare
    /// medicine) with a Track, a Trail, and a Road seeded, advanced until the exchange has been recorded.
    /// </summary>
    private static (SimulationPersistenceSnapshot Snapshot, (TileCoordinate Coordinate, RoadGrade Grade)[] Graded) CreateTradeInProgress(WorldSeed seed)
    {
        var baseline = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion).CreatePersistenceSnapshot();
        var daughterHouseholds = baseline.Households.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .Take(8).Select(x => x.Id.Value).ToHashSet();
        var world = baseline.World!;
        var daughterSite = world.Tiles.Where(x => x.Walkable &&
                Math.Abs(x.Coordinate.X - world.StartingSite.X) + Math.Abs(x.Coordinate.Y - world.StartingSite.Y) == 6)
            .OrderBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .First(x => DeterministicPathfinder.Find(world, world.StartingSite, x.Coordinate) is { Count: >= 2 }).Coordinate;
        long Site(long? householdId) => householdId is { } id && daughterHouseholds.Contains(id) ? 2 : 1;
        var citizens = baseline.Citizens.Select(citizen =>
        {
            if (Site(citizen.HouseholdId?.Value) == 2)
            {
                citizen.Location = daughterSite;
                citizen.HomeStructureId = null;
            }
            return citizen;
        }).ToArray();
        var graded = world.Tiles.Where(x => x.Walkable).Take(3).Select(x => x.Coordinate)
            .Zip([RoadGrade.Track, RoadGrade.Trail, RoadGrade.Road]).ToArray();
        var current = baseline.MigrationState!;
        var roads = new RoadNetworkState(1, graded.Select(x => new RoadTileState(x.First.X, x.First.Y,
            x.Second switch { RoadGrade.Track => 30, RoadGrade.Trail => 80, _ => 0 }, x.Second)).ToArray(), current.Roads!.ActiveRoutes);
        var migration = new MigrationWorldState(current.Version,
            citizens.Select(x => new MigrationEntityResidence(x.Id.Value, Site(x.HouseholdId?.Value))).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value, Site(x.Id.Value))).ToArray(),
            current.StructureOwners, current.FacilityOwners, current.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                    baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, x == LivingGood.Medicine ? 40 : 0)).ToArray()),
            roads: roads);
        var engine = SimulationEngine.FromPersistenceSnapshot(new SimulationPersistenceSnapshot(baseline.Seed,
            baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion,
            baseline.WorldConfiguration, baseline.Counters, baseline.ScheduledEvents, baseline.World, citizens,
            baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion,
            baseline.SettlementVersion, baseline.Structures, baseline.StructureContributions, baseline.SocialVersion,
            baseline.Relationships, baseline.Households, baseline.HistoryVersion, baseline.HistoryState,
            baseline.HistoricalEvents, baseline.HistoricalEventCitizens, baseline.HistoricalEventStructures,
            baseline.StatisticsSamples, baseline.Memories, baseline.Agriculture, baseline.Economy,
            baseline.LivingStateJson, migration.ToCanonicalJson()));
        Invoke(engine, "ChangeGoodAt", 1L, LivingGood.Fuel, 150);
        Invoke(engine, "EvaluateMigrationTrade");
        for (var step = 0; step < 48; step++)
        {
            engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 60));
            var snapshot = engine.CreatePersistenceSnapshot();
            if (snapshot.HistoricalEvents.Any(x => x.EventType == HistoricalEventType.TradeCompleted))
            {
                Assert.Contains(snapshot.MigrationState!.InTransitParties, x => x.JourneyKind == MigrationJourneyKind.Trade);
                return (snapshot, graded.Select(x => (x.First, x.Second)).ToArray());
            }
        }
        throw new InvalidOperationException("The trade fixture did not complete an exchange within two days.");
    }

    private static object? Invoke(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(x => x.Name == name && x.GetParameters().Length == arguments.Length).Invoke(instance, arguments);

    private static async Task WriteCheckpointAsync(string path, SimulationPersistenceSnapshot checkpoint)
    {
        await using var database = await WorldDatabase.OpenAsync(path);
        await database.CreateCheckpointStore().CheckpointAsync(checkpoint, DateTime.UtcNow);
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "little-ages-m15-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDataRoot(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class ServerFactory(string dataRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "integration-world");
            builder.UseSetting("WorldSeed", "42");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", "0");
        }
    }
}
