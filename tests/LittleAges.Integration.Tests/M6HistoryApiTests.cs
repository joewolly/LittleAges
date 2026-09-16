using System.Net;
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

public sealed class M6HistoryApiTests
{
    private static readonly string[] HistoricEventTypes = ["SettlementFounded", "WorldCreated"];

    [Fact]
    public async Task HistoryBiographyAndStatisticsRoutesExposeStableFilteredFactualSnapshots()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "LittleAges-M6-Api", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        try
        {
            var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
            engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
            await using (var database = await WorldDatabase.OpenAsync(Path.Combine(dataRoot, "integration-world.db")))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot(), DateTime.UtcNow);

            using var factory = new ServerFactory(dataRoot);
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();
            var before = HistoryKey(host.Observation.History!);

            using var defaultHistory = await GetJsonAsync(client, "/api/v1/history");
            Assert.Equal(JsonValueKind.Array, defaultHistory.RootElement.ValueKind);
            Assert.All(defaultHistory.RootElement.EnumerateArray(), item => Assert.True((int)Enum.Parse<HistoricalImportance>(item.GetProperty("importance").GetString()!, ignoreCase: false) >= 2));
            Assert.DoesNotContain(defaultHistory.RootElement.EnumerateArray(), item => item.GetProperty("eventType").GetString() == nameof(HistoricalEventType.SeasonStarted));

            using var historic = await GetJsonAsync(client, "/api/v1/history?minimumImportance=5");
            Assert.Equal(HistoricEventTypes, historic.RootElement.EnumerateArray().Select(item => item.GetProperty("eventType").GetString()));
            Assert.All(historic.RootElement.EnumerateArray(), item =>
            {
                Assert.Equal("Live", item.GetProperty("origin").GetString());
                Assert.Equal(1, item.GetProperty("schemaVersion").GetInt32());
                Assert.StartsWith("{", item.GetProperty("payloadJson").GetString());
            });

            using var filtered = await GetJsonAsync(client, "/api/v1/history?eventType=SettlementFounded&minimumImportance=0&citizenId=1&fromMinute=0&toMinute=0&limit=1");
            var settlement = Assert.Single(filtered.RootElement.EnumerateArray());
            Assert.Equal("SettlementFounded", settlement.GetProperty("eventType").GetString());
            Assert.Equal(20, settlement.GetProperty("citizenLinks").GetArrayLength());
            Assert.Contains(settlement.GetProperty("citizenLinks").EnumerateArray(), link => link.GetProperty("citizenId").GetString() == "1" && link.GetProperty("role").GetString() == "founder");
            Assert.Contains("founders", settlement.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);

            using var family = await GetJsonAsync(client, "/api/v1/history?familyCitizenId=1&minimumImportance=0&limit=100");
            Assert.NotEmpty(family.RootElement.EnumerateArray());
            var familyIds = FamilyClosureRules.Closure("1", host.Observation.Citizens);
            Assert.Contains(family.RootElement.EnumerateArray(), item => item.GetProperty("eventType").GetString() == "SettlementFounded");
            Assert.All(family.RootElement.EnumerateArray(), item =>
                Assert.Contains(item.GetProperty("citizenLinks").EnumerateArray(), link => familyIds.Contains(link.GetProperty("citizenId").GetString()!)));
            using var cursor = await GetJsonAsync(client, "/api/v1/history?minimumImportance=0&fromMinute=0&toMinute=0&beforeEventId=2&limit=100");
            Assert.Equal("WorldCreated", Assert.Single(cursor.RootElement.EnumerateArray()).GetProperty("eventType").GetString());

            using var eventDetail = await GetJsonAsync(client, "/api/v1/history/1");
            Assert.Equal("WorldCreated", eventDetail.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/history/01")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/history?eventType=NoSuchEvent")).StatusCode);

            using var biography = await GetJsonAsync(client, "/api/v1/citizens/1/biography");
            Assert.Equal("1", biography.RootElement.GetProperty("citizen").GetProperty("citizenId").GetString());
            Assert.Equal(JsonValueKind.Array, biography.RootElement.GetProperty("events").ValueKind);
            Assert.Contains(biography.RootElement.GetProperty("events").EnumerateArray(), item => item.GetProperty("eventType").GetString() == "SettlementFounded");
            Assert.Equal(JsonValueKind.Array, biography.RootElement.GetProperty("memories").ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/citizens/999/biography")).StatusCode);

            using var statistics = await GetJsonAsync(client, "/api/v1/statistics?fromMinute=0&toMinute=999999&limit=1");
            var sample = Assert.Single(statistics.RootElement.EnumerateArray());
            Assert.Equal(30L * WorldCalendar.MinutesPerDay, sample.GetProperty("worldMinute").GetInt64());
            Assert.Equal(0, sample.GetProperty("periodStartMinute").GetInt64());
            Assert.Equal(host.Observation.Status.LivingPopulation, sample.GetProperty("population").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/statistics?limit=nope")).StatusCode);

            using var memories = await GetJsonAsync(client, "/api/v1/citizens/1/memories");
            Assert.Equal(JsonValueKind.Array, memories.RootElement.ValueKind);
            Assert.Equal(before, HistoryKey(host.Observation.History!));
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string HistoryKey(ServerHistorySnapshot history) =>
        string.Join("|", history.Events.Select(item => $"{item.EventId}:{item.WorldMinute}:{item.EventType}:{item.Importance}:{item.Origin}:{item.PayloadJson}")) +
        $";state={history.State.HistoryStartMinute}:{history.State.HistoryStartEventId}:{history.State.PeriodStartMinute}:{history.State.BirthsSinceSample}:{history.State.DeathsSinceSample}:{history.State.FoodProducedSinceSample}:{history.State.FoodConsumedSinceSample}:{history.State.ActiveFoodShortage}:{history.State.PopulationMilestoneWatermark}" +
        $";stats={string.Join("|", history.Statistics.Select(item => $"{item.WorldMinute}:{item.PeriodStartMinute}:{item.Population}:{item.BirthsPeriod}:{item.DeathsPeriod}:{item.FoodStored}:{item.FoodProducedPeriod}:{item.FoodConsumedPeriod}"))}";

    private sealed class ServerFactory(string dataRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "integration-world");
            builder.UseSetting("WorldSeed", "0");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", "0");
        }
    }
}
