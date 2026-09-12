using System.Net;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class ServerIntegrationTests
{
    [Fact]
    public async Task HealthAndStatusAreAvailableWithoutBrowserClients()
    {
        await WithFactoryAsync(async (factory, dataRoot) =>
        {
            using var client = factory.CreateClient();
            var health = await client.GetAsync("/api/v1/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            using var statusDocument = await WaitForRunningStatusAsync(client);
            Assert.Equal("Running", statusDocument.RootElement.GetProperty("state").GetString());
            Assert.Equal(0, statusDocument.RootElement.GetProperty("worldMinute").GetInt64());
            Assert.Equal(0, statusDocument.RootElement.GetProperty("pendingEventCount").GetInt32());
            Assert.True(File.Exists(Path.Combine(dataRoot, "integration-world.db")));
        });
    }

    [Fact]
    public async Task CheckpointCommandUsesTheSingleWriterAndShutdownLeavesLoadableWorld()
    {
        var dataRoot = CreateDataRoot();
        var factory = new ServerFactory(dataRoot);
        try
        {
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            var result = await host.RequestCheckpointAsync();
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.WorldMinute);
        }
        finally
        {
            factory.Dispose();
        }

        var path = Path.Combine(dataRoot, "integration-world.db");
        await using (var database = await WorldDatabase.OpenAsync(path))
        {
            var snapshot = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(0, snapshot.WorldMinute.Value);
        }

        CleanupDataRoot(dataRoot);
    }

    [Fact]
    public async Task ExistingValidCheckpointLoadsOnHostRestart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var firstFactory = new ServerFactory(dataRoot);
            try
            {
                using var firstClient = firstFactory.CreateClient();
                using var firstStatus = await WaitForRunningStatusAsync(firstClient);
            }
            finally
            {
                firstFactory.Dispose();
            }

            var secondFactory = new ServerFactory(dataRoot);
            try
            {
                using var secondClient = secondFactory.CreateClient();
                using var secondStatus = await WaitForRunningStatusAsync(secondClient);
                Assert.Equal(0, secondStatus.RootElement.GetProperty("worldMinute").GetInt64());
            }
            finally
            {
                secondFactory.Dispose();
            }
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task CorruptExistingCheckpointBecomesFaultedAndHealthTurnsUnhealthy()
    {
        var dataRoot = CreateDataRoot();
        var path = Path.Combine(dataRoot, "integration-world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var snapshot = new SimulationPersistenceSnapshot(
                    new WorldSeed(7),
                    WorldMinute.Zero,
                    SimulationEngine.CurrentWorldSchemaVersion,
                    SimulationEngine.CurrentSimulationRulesVersion,
                    "integration-test",
                    "{}",
                    new DeterministicCountersSnapshot(1, 1, 1),
                    []);
                await database.CreateCheckpointStore().CheckpointAsync(snapshot, new DateTime(2026, 9, 12, 4, 0, 0, DateTimeKind.Utc));
            }

            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE world_meta SET simulation_rules_version = 'unsupported-rules';";
                await command.ExecuteNonQueryAsync();
            }

            var factory = new ServerFactory(dataRoot);
            try
            {
                var host = factory.Services.GetRequiredService<SimulationHost>();
                using var client = factory.CreateClient();
                await WaitForStateAsync(host, SimulationHostState.Faulted);
                Assert.Contains("not supported", host.Status.Error, StringComparison.OrdinalIgnoreCase);
                var health = await new SimulationHostHealthCheck(host).CheckHealthAsync(new HealthCheckContext());
                Assert.Equal(HealthStatus.Unhealthy, health.Status);
            }
            finally
            {
                Assert.IsType<NotSupportedException>(Record.Exception(factory.Dispose));
            }
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task CommandLoopFailureRemainsFaultedAndStopsHost()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var factory = new ServerFactory(dataRoot);
            try
            {
                using var client = factory.CreateClient();
                var host = factory.Services.GetRequiredService<SimulationHost>();
                await WaitForStateAsync(host, SimulationHostState.Running);

                await Assert.ThrowsAsync<InvalidOperationException>(() => host.TriggerCommandLoopFailureForTestingAsync());
                await WaitForStateAsync(host, SimulationHostState.Faulted);
                Assert.Contains("Controlled simulation command failure", host.Status.Error, StringComparison.Ordinal);
            }
            finally
            {
                Assert.IsType<InvalidOperationException>(Record.Exception(factory.Dispose));
            }
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task CommandFailureCancelsApplicationStoppingAndRegisteredHealthIsUnhealthy()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            using var host = CreateSimulationHost(dataRoot);
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            var healthChecks = host.Services.GetRequiredService<HealthCheckService>();
            await WaitForStateAsync(simulationHost, SimulationHostState.Running);

            var stopping = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stoppingRegistration = host.Services
                .GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping
                .Register(() => stopping.TrySetResult(true));

            await Assert.ThrowsAsync<InvalidOperationException>(() => simulationHost.TriggerCommandLoopFailureForTestingAsync());
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForStateAsync(simulationHost, SimulationHostState.Faulted);
            Assert.Contains("Controlled simulation command failure", simulationHost.Status.Error, StringComparison.Ordinal);

            var health = await healthChecks.CheckHealthAsync();
            Assert.Equal(HealthStatus.Unhealthy, health.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.StopAsync());
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task FinalCheckpointFailurePropagatesFromStopAfterDatabaseCleanup()
    {
        var dataRoot = CreateDataRoot();
        var databasePath = Path.Combine(dataRoot, "host-world.db");
        try
        {
            using var host = CreateSimulationHost(dataRoot, "host-world");
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await WaitForStateAsync(simulationHost, SimulationHostState.Running);
            simulationHost.FailNextFinalCheckpointForTesting();

            var stopping = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stoppingRegistration = host.Services
                .GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping
                .Register(() => stopping.TrySetResult(true));

            var stopTask = host.StopAsync();
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => stopTask);
            Assert.Equal(SimulationHostState.Faulted, simulationHost.Status.State);
            Assert.Contains("Controlled final checkpoint failure", simulationHost.Status.Error, StringComparison.Ordinal);

            await using (var database = await WorldDatabase.OpenAsync(databasePath))
            {
                var snapshot = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(WorldMinute.Zero, snapshot.WorldMinute);
            }
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public void WindowsServiceIntegrationIsCompileRegisteredWithoutInstallingAService()
    {
        var services = new ServiceCollection();
        var configuredServices = services.AddWindowsService(options => options.ServiceName = "Little Ages Test");
        Assert.Same(services, configuredServices);
    }

    private static async Task WithFactoryAsync(Func<ServerFactory, string, Task> test)
    {
        var dataRoot = CreateDataRoot();
        var factory = new ServerFactory(dataRoot);
        try
        {
            await test(factory, dataRoot);
        }
        finally
        {
            factory.Dispose();
            CleanupDataRoot(dataRoot);
        }
    }

    private static async Task<JsonDocument> WaitForRunningStatusAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await client.GetAsync("/api/v1/status");
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (string.Equals(document.RootElement.GetProperty("state").GetString(), "Running", StringComparison.Ordinal))
            {
                return document;
            }

            document.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new InvalidOperationException("The simulation host did not reach Running state.");
    }

    private static async Task WaitForStateAsync(SimulationHost host, SimulationHostState expectedState)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (host.Status.State == expectedState)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new InvalidOperationException($"The simulation host did not reach {expectedState} state.");
    }

    private static IHost CreateSimulationHost(string dataRoot, string activeWorld = "host-world")
    {
        var builder = Host.CreateApplicationBuilder();
        var options = new ServerOptions
        {
            DataRoot = dataRoot,
            ActiveWorld = activeWorld,
            WorldSeed = new WorldSeed(17),
            ListenUrls = ServerOptions.DefaultListenUrls
        };
        builder.Services.Configure<HostOptions>(hostOptions => hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SimulationHost>();
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SimulationHost>());
        builder.Services.AddHealthChecks().AddCheck<SimulationHostHealthCheck>("simulation-host");
        return builder.Build();
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "LittleAges-Integration-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDataRoot(string dataRoot)
    {
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private sealed class ServerFactory(string dataRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "integration-world");
            builder.UseSetting("WorldSeed", ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
        }
    }
}
