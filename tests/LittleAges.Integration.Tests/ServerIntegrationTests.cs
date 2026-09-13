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
using Microsoft.Extensions.Logging;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class ServerIntegrationTests
{
    [Fact]
    public async Task CitizensEndpointReturnsSortedRosterAndValidatesDecimalIds()
    {
        await WithFactoryAsync(async (factory, _) =>
        {
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            using var response = await client.GetAsync("/api/v1/citizens");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var citizens = document.RootElement.EnumerateArray().ToArray();
            Assert.Equal(20, citizens.Length);
            Assert.Equal(Enumerable.Range(1, 20).Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)), citizens.Select(x => x.GetProperty("citizenId").GetString()));
            Assert.All(citizens, citizen => Assert.InRange(citizen.GetProperty("health").GetInt32(), 0, 10000));

            using var detail = await client.GetAsync("/api/v1/citizens/1");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var malformed = await client.GetAsync("/api/v1/citizens/01");
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            using var missing = await client.GetAsync("/api/v1/citizens/999");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        });
    }

    [Fact]
    public async Task ConcurrentCitizenReadsRemainCoherentDuringAutomaticAdvancement()
    {
        var dataRoot = CreateDataRoot();
        var factory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 1_000, suppressLogs: true);
        try
        {
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            var host = factory.Services.GetRequiredService<SimulationHost>();
            var initialMinute = host.Status.WorldMinute;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var readers = Enumerable.Range(0, 8).Select(async _ =>
            {
                var reads = 0;
                do
                {
                    using var rosterResponse = await client.GetAsync("/api/v1/citizens", timeout.Token);
                    rosterResponse.EnsureSuccessStatusCode();
                    using var roster = JsonDocument.Parse(await rosterResponse.Content.ReadAsStringAsync(timeout.Token));
                    var ids = roster.RootElement.EnumerateArray().Select(item => item.GetProperty("citizenId").GetString()).ToArray();
                    Assert.Equal(20, ids.Length);
                    Assert.Equal(20, ids.Distinct(StringComparer.Ordinal).Count());
                    Assert.Equal(ids.OrderBy(id => long.Parse(id!, System.Globalization.CultureInfo.InvariantCulture)), ids);

                    using var detailResponse = await client.GetAsync("/api/v1/citizens/1", timeout.Token);
                    detailResponse.EnsureSuccessStatusCode();
                    using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync(timeout.Token));
                    Assert.Equal("1", detail.RootElement.GetProperty("citizenId").GetString());
                    reads++;
                }
                while (reads < 25 || host.Status.WorldMinute == initialMinute);
            });

            await Task.WhenAll(readers);
            Assert.True(host.Status.WorldMinute > initialMinute);
            Assert.Equal(20, host.Status.Population);
        }
        finally
        {
            factory.Dispose();
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task OperationalDriverAdvancesCitizensAndRestartContinuesCheckpointedState()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            long firstMinute;
            string initialLocation;
            var firstFactory = new ServerFactory(dataRoot);
            try
            {
                using var client = firstFactory.CreateClient();
                await WaitForRunningStatusAsync(client);
                using var initial = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                initialLocation = initial.RootElement.GetProperty("location").GetProperty("x").GetInt32() + "," + initial.RootElement.GetProperty("location").GetProperty("y").GetInt32();
                JsonDocument? advanced = null;
                for (var attempt = 0; attempt < 40 && advanced is null; attempt++)
                {
                    await Task.Delay(250);
                    var candidate = JsonDocument.Parse(await (await client.GetAsync("/api/v1/status")).Content.ReadAsStringAsync());
                    using var currentCitizen = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                    var location = currentCitizen.RootElement.GetProperty("location").GetProperty("x").GetInt32() + "," + currentCitizen.RootElement.GetProperty("location").GetProperty("y").GetInt32();
                    if (candidate.RootElement.GetProperty("worldMinute").GetInt64() >= 10 && location != initialLocation) advanced = candidate; else candidate.Dispose();
                }
                Assert.NotNull(advanced);
                firstMinute = advanced!.RootElement.GetProperty("worldMinute").GetInt64();
                advanced.Dispose();
                using var after = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                var afterLocation = after.RootElement.GetProperty("location").GetProperty("x").GetInt32() + "," + after.RootElement.GetProperty("location").GetProperty("y").GetInt32();
                Assert.NotEqual(initialLocation, afterLocation);
                Assert.True(after.RootElement.GetProperty("actionSequence").GetInt64() > 0);
            }
            finally { firstFactory.Dispose(); }

            var secondFactory = new ServerFactory(dataRoot);
            try
            {
                using var client = secondFactory.CreateClient();
                using var status = await WaitForRunningStatusAsync(client);
                Assert.True(status.RootElement.GetProperty("worldMinute").GetInt64() >= firstMinute);
                using var citizen = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                Assert.Equal("1", citizen.RootElement.GetProperty("citizenId").GetString());
                Assert.InRange(citizen.RootElement.GetProperty("actionSequence").GetInt64(), 1, long.MaxValue);
            }
            finally { secondFactory.Dispose(); }
        }
        finally { CleanupDataRoot(dataRoot); }
    }

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
            Assert.True(statusDocument.RootElement.GetProperty("worldMinute").GetInt64() >= 0);
            Assert.True(statusDocument.RootElement.GetProperty("pendingEventCount").GetInt32() >= 0);
            Assert.False(statusDocument.RootElement.TryGetProperty("citizens", out _));
            Assert.Equal("18446744073709551615", statusDocument.RootElement.GetProperty("worldSeed").GetString());
            using var worldResponse = await client.GetAsync("/api/v1/world");
            Assert.Equal(HttpStatusCode.OK, worldResponse.StatusCode);
            using var worldDocument = JsonDocument.Parse(await worldResponse.Content.ReadAsStringAsync());
            Assert.Equal("18446744073709551615", worldDocument.RootElement.GetProperty("worldSeed").GetString());
            Assert.Equal(160, worldDocument.RootElement.GetProperty("width").GetInt32());
            Assert.Equal(160, worldDocument.RootElement.GetProperty("height").GetInt32());
            Assert.Equal(25_600, worldDocument.RootElement.GetProperty("tileCount").GetInt32());
            Assert.True(worldDocument.RootElement.GetProperty("fingerprint").GetString()?.Length > 0);
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
            Assert.True(result.WorldMinute >= 0);
        }
        finally
        {
            factory.Dispose();
        }

        var path = Path.Combine(dataRoot, "integration-world.db");
        await using (var database = await WorldDatabase.OpenAsync(path))
        {
            var snapshot = await database.CreateCheckpointStore().LoadAsync();
            Assert.True(snapshot.WorldMinute.Value >= 0);
        }

        CleanupDataRoot(dataRoot);
    }

    [Fact]
    public async Task ExistingValidCheckpointLoadsOnHostRestart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            string? firstFingerprint = null;
            var firstFactory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0);
            try
            {
                using var firstClient = firstFactory.CreateClient();
                using var firstStatus = await WaitForRunningStatusAsync(firstClient);
                using var firstWorld = JsonDocument.Parse(await (await firstClient.GetAsync("/api/v1/world")).Content.ReadAsStringAsync());
                firstFingerprint = firstWorld.RootElement.GetProperty("fingerprint").GetString();
            }
            finally
            {
                firstFactory.Dispose();
            }

            var secondFactory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0);
            try
            {
                using var secondClient = secondFactory.CreateClient();
                using var secondStatus = await WaitForRunningStatusAsync(secondClient);
                Assert.Equal(0, secondStatus.RootElement.GetProperty("worldMinute").GetInt64());
                using var secondWorld = JsonDocument.Parse(await (await secondClient.GetAsync("/api/v1/world")).Content.ReadAsStringAsync());
                Assert.Equal(firstFingerprint, secondWorld.RootElement.GetProperty("fingerprint").GetString());
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
                var world = new WorldGenerator().Generate(new WorldSeed(7), WorldGenerationConfiguration.Default);
                var snapshot = new SimulationPersistenceSnapshot(
                    new WorldSeed(7),
                    WorldMinute.Zero,
                    SimulationEngine.CurrentWorldSchemaVersion,
                    "m0-rng1",
                    "integration-test",
                    world.Configuration.CanonicalJson,
                    new DeterministicCountersSnapshot(1, 1, 1),
                    [],
                    world);
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
            using var host = CreateSimulationHost(dataRoot, "host-world", simulationMinutesPerSecond: 0);
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
        for (var attempt = 0; attempt < 500; attempt++)
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
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (host.Status.State == expectedState)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new InvalidOperationException($"The simulation host did not reach {expectedState} state.");
    }

    private static IHost CreateSimulationHost(string dataRoot, string activeWorld = "host-world", double simulationMinutesPerSecond = 10)
    {
        var builder = Host.CreateApplicationBuilder();
        var options = new ServerOptions
        {
            DataRoot = dataRoot,
            ActiveWorld = activeWorld,
            WorldSeed = new WorldSeed(17),
            ListenUrls = ServerOptions.DefaultListenUrls,
            SimulationMinutesPerSecond = simulationMinutesPerSecond
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

    private sealed class ServerFactory(string dataRoot, double simulationMinutesPerSecond = 10, bool suppressLogs = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "integration-world");
            builder.UseSetting("WorldSeed", ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", simulationMinutesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (suppressLogs) builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
