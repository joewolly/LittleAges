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

public sealed class M16FestivalObserverTests
{
    [Theory]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion, true)]
    [InlineData(SimulationEngine.RoadsSimulationRulesVersion, false)]
    public async Task LivingObservationAdvertisesFestivalsOnlyForM16AndRemainsReadOnly(string rules, bool enabled)
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m16-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
            engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 0) + 180));
            await using (var database = await WorldDatabase.OpenAsync(Path.Combine(root, "festival.db")))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            using var factory = new FestivalServerFactory(root);
            using var client = factory.CreateClient();
            await factory.Services.GetRequiredService<SimulationHost>().WaitForRunningForTestingAsync();
            using var response = await client.GetAsync("/api/v1/living");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(enabled, json.RootElement.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "festivals"));
            if (enabled)
            {
                var festival = Assert.Single(json.RootElement.GetProperty("festivals").EnumerateArray());
                Assert.Equal("1", festival.GetProperty("settlementId").GetString());
                Assert.True(festival.GetProperty("started").GetBoolean());
                Assert.False(festival.GetProperty("finished").GetBoolean());
                Assert.Contains(festival.GetProperty("attendance").EnumerateArray(), x => x.GetProperty("benefitsGranted").GetBoolean() && x.GetProperty("citizenId").ValueKind == JsonValueKind.String);
            }
            using var mutation = await client.PostAsync("/api/v1/living", new StringContent("{}"));
            Assert.Equal(HttpStatusCode.NotFound, mutation.StatusCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FestivalServerFactory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", root);
            builder.UseSetting("ActiveWorld", "festival");
            builder.UseSetting("AutoUpgradeWorldRules", "false");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", "0");
        }
    }
}
