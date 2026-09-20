using System.Text.Json;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class LivingObserverTests
{
    [Theory]
    [InlineData(SimulationEngine.LivingSimulationRulesVersion)]
    [InlineData(SimulationEngine.CurrentSimulationRulesVersion)]
    public async Task FreshRulesAndReloadPreserveImmutableObserverCapability(string rules)
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-living-observer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first;
            await using (var factory = new Factory(root, rules))
            {
                using var client = factory.CreateClient();
                var host = factory.Services.GetRequiredService<SimulationHost>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (host.Status.State == SimulationHostState.Starting) await Task.Delay(20, timeout.Token);
                Assert.Equal(SimulationHostState.Running, host.Status.State);
                first = await client.GetStringAsync("/api/v1/living", timeout.Token);
                var read = host.Observation.Living;
                if (rules == SimulationEngine.LivingSimulationRulesVersion)
                {
                    using var payload = JsonDocument.Parse(first);
                    Assert.Equal(rules, payload.RootElement.GetProperty("rulesVersion").GetString());
                    Assert.Equal(20, payload.RootElement.GetProperty("people").GetArrayLength());
                    Assert.Equal(1, payload.RootElement.GetProperty("version").GetInt32());
                }
                else Assert.Null(read);
                for (var i = 0; i < 5; i++) Assert.Equal(first, await client.GetStringAsync("/api/v1/living", timeout.Token));
                Assert.Equal(read, host.Observation.Living);
            }
            // A different fresh-world option must never switch an existing world's rules.
            await using (var factory = new Factory(root, rules == SimulationEngine.LivingSimulationRulesVersion ? SimulationEngine.CurrentSimulationRulesVersion : SimulationEngine.LivingSimulationRulesVersion))
            {
                using var client = factory.CreateClient();
                var host = factory.Services.GetRequiredService<SimulationHost>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (host.Status.State == SimulationHostState.Starting) await Task.Delay(20, timeout.Token);
                Assert.Equal(SimulationHostState.Running, host.Status.State);
                Assert.Equal(first, await client.GetStringAsync("/api/v1/living", timeout.Token));
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
    }

    private sealed class Factory(string root, string rules) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", root);
            builder.UseSetting("ActiveWorld", "observer");
            builder.UseSetting("WorldSeed", "42");
            builder.UseSetting("NewWorldRules", rules);
            builder.UseSetting("SimulationMinutesPerSecond", "0");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
