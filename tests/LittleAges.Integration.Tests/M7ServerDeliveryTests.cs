using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using LittleAges.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class M7ServerDeliveryTests
{
    [Fact]
    public void WorldChangeBroadcasterIsLatestOnlyAndNoChangeFlushIsEmpty()
    {
        var broadcaster = new WorldChangeBroadcaster();
        broadcaster.Publish(new WorldChangedPayload(1, 10, SimulationHostState.Running, PersistenceState.Healthy));
        broadcaster.Publish(new WorldChangedPayload(3, 30, SimulationHostState.Running, PersistenceState.Degraded));
        broadcaster.Publish(new WorldChangedPayload(2, 20, SimulationHostState.Running, PersistenceState.Healthy));

        Assert.True(broadcaster.TryFlush(out var latest));
        Assert.NotNull(latest);
        Assert.Equal(3, latest!.Revision);
        Assert.Equal(30, latest.WorldMinute);
        Assert.Equal(PersistenceState.Degraded, latest.PersistenceState);
        Assert.False(broadcaster.TryFlush(out var noChange));
        Assert.Null(noChange);

        broadcaster.Publish(new WorldChangedPayload(4, 40, SimulationHostState.Stopping, PersistenceState.Healthy));
        Assert.True(broadcaster.TryFlush(out var next));
        Assert.Equal(4, next!.Revision);
    }

    [Fact]
    public async Task ObservationRevisionIncrementsForPublishedHostObservations()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "revision");
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var before = simulationHost.Observation.Revision;
            var beforeStatus = simulationHost.Status;
            await simulationHost.AdvanceForTestingAsync(0);
            var after = simulationHost.Observation.Revision;
            Assert.Equal(before + 1, after);
            Assert.Equal(beforeStatus.WorldMinute, simulationHost.Status.WorldMinute);
            Assert.Same(simulationHost.Observation, simulationHost.Observation);
            await host.StopAsync();
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => host.StopAsync());
            host.Dispose();
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task WorldHubStreamsFramesContinuouslyWithoutSimulationMutation()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "continuous-stream");
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var options = host.Services.GetRequiredService<ServerOptions>();
            var hub = new WorldHub(simulationHost, options);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var frames = hub.StreamWorld(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

            Assert.True(await frames.MoveNextAsync());
            var first = frames.Current;
            Assert.True(await frames.MoveNextAsync());
            var second = frames.Current;

            Assert.Equal(1, first.Sequence);
            Assert.Equal(2, second.Sequence);
            Assert.Equal(first.Revision, second.Revision);
            Assert.Equal(first.Status.WorldMinute, second.Status.WorldMinute);
            Assert.True(second.SentAtUnixMilliseconds >= first.SentAtUnixMilliseconds);
            Assert.Same(simulationHost.Observation.Citizens, second.Citizens);
            Assert.Same(simulationHost.Observation.Structures, second.Structures);
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => host.StopAsync());
            host.Dispose();
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task StaticSpaFallbackApiAndWorldHubRoutesAreSeparated()
    {
        var root = CreateDataRoot();
        var webRoot = CreateWebRoot();
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><html><body>M7 test shell</body></html>");
        var dioramaRoot = Path.Combine(webRoot, "assets", "diorama");
        Directory.CreateDirectory(dioramaRoot);
        File.WriteAllBytes(Path.Combine(dioramaRoot, "villager.glb"), [0x67, 0x6c, 0x54, 0x46]);
        using var factory = new ServerFactory(root, webRoot);
        try
        {
            using var client = factory.CreateClient();
            var simulationHost = factory.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();

            using var rootResponse = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
            Assert.StartsWith("text/html", rootResponse.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            var rootBody = await rootResponse.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrWhiteSpace(rootBody));

            using var deepResponse = await client.GetAsync("/world/unknown-route");
            Assert.Equal(HttpStatusCode.OK, deepResponse.StatusCode);
            Assert.Equal(rootBody, await deepResponse.Content.ReadAsStringAsync());

            using var glbResponse = await client.GetAsync("/assets/diorama/villager.glb");
            Assert.Equal(HttpStatusCode.OK, glbResponse.StatusCode);
            Assert.Equal("model/gltf-binary", glbResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal([0x67, 0x6c, 0x54, 0x46], await glbResponse.Content.ReadAsByteArrayAsync());

            using var unknownApi = await client.GetAsync("/api/v1/unknown");
            Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);
            Assert.NotEqual("text/html", unknownApi.Content.Headers.ContentType?.MediaType);

            using var unknownHub = await client.GetAsync("/hubs/unknown");
            Assert.Equal(HttpStatusCode.NotFound, unknownHub.StatusCode);

            using var negotiate = await client.PostAsync("/hubs/world/negotiate?negotiateVersion=1", content: null);
            Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
            using var negotiateDocument = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
            Assert.True(negotiateDocument.RootElement.TryGetProperty("connectionId", out _));
            Assert.True(negotiateDocument.RootElement.TryGetProperty("availableTransports", out _));

            using var status = await client.GetAsync("/api/v1/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var statusDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            Assert.Equal("Running", statusDocument.RootElement.GetProperty("state").GetString());
            using var world = await client.GetAsync("/api/v1/world");
            Assert.Equal(HttpStatusCode.OK, world.StatusCode);
        }
        finally
        {
            factory.Dispose();
            CleanupDataRoot(root);
            CleanupDataRoot(webRoot);
        }
    }

    private static IHost BuildHost(string root, string world)
    {
        var builder = Host.CreateApplicationBuilder();
        var options = new ServerOptions
        {
            DataRoot = root,
            ActiveWorld = world,
            WorldSeed = new LittleAges.Domain.WorldSeed(17),
            ListenUrls = ServerOptions.DefaultListenUrls,
            SimulationMinutesPerSecond = 0,
            CheckpointSimulationMinutes = 0,
            CheckpointMinimumRealSeconds = 0,
            CheckpointRetryCount = 0,
            CheckpointRetryDelaySeconds = 0,
            BrowserUpdateIntervalMilliseconds = 1,
            ObserverStreamIntervalMilliseconds = 1
        };
        builder.Services.Configure<HostOptions>(hostOptions => hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SimulationHost>();
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SimulationHost>());
        return builder.Build();
    }

    private sealed class ServerFactory(string root, string webRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseWebRoot(webRoot);
            builder.UseSetting("DataRoot", root);
            builder.UseSetting("ActiveWorld", "delivery-world");
            builder.UseSetting("WorldSeed", "17");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", "0");
            builder.UseSetting("CheckpointSimulationMinutes", "0");
            builder.UseSetting("CheckpointMinimumRealSeconds", "0");
            builder.UseSetting("CheckpointRetryCount", "0");
            builder.UseSetting("CheckpointRetryDelaySeconds", "0");
            builder.UseSetting("BrowserUpdateIntervalMilliseconds", "1");
            builder.UseSetting("Environment", "Production");
        }
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "LittleAges-M7-Delivery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateWebRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "LittleAges-M7-WebRoot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDataRoot(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
