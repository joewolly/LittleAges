using System.Text.Json;
using LittleAges.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LittleAges.Integration.Tests;

/// <summary>
/// End-to-end M4 acceptance evidence. These tests deliberately use only the
/// immutable HTTP read surface; advancing is the existing deterministic test hook.
/// </summary>
public sealed class M4ServerAcceptanceTests
{
    [Fact]
    public async Task AggressiveM4ReadPollingDoesNotChangeCanonicalSeed42Observation()
    {
        var noPollingRoot = CreateDataRoot();
        var pollingRoot = CreateDataRoot();
        try
        {
            var noPollingFingerprint = await AdvanceWithoutPollingAsync(noPollingRoot);
            var pollingFingerprint = await AdvanceWithAggressivePollingAsync(pollingRoot);

            Assert.Equal(noPollingFingerprint, pollingFingerprint);
        }
        finally
        {
            CleanupDataRoot(noPollingRoot);
            CleanupDataRoot(pollingRoot);
        }
    }

    [Fact]
    public async Task FreshM4CheckpointRestartPreservesHttpStateAndContinuesConstruction()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            string checkpointFingerprint;
            string structureId;
            ConstructionProgress checkpointProgress;

            var firstFactory = new M4ServerFactory(dataRoot);
            try
            {
                using var client = firstFactory.CreateClient();
                var host = firstFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();

                await host.AdvanceForTestingAsync(360);
                (structureId, checkpointProgress) = await GetSingleConstructionProgressAsync(client);
                checkpointFingerprint = await BuildCanonicalHttpObservationFingerprintAsync(client);

                var checkpoint = await host.RequestCheckpointAsync();
                Assert.True(checkpoint.Succeeded);
                Assert.Equal(360, checkpoint.WorldMinute);
            }
            finally
            {
                firstFactory.Dispose();
            }

            var secondFactory = new M4ServerFactory(dataRoot);
            try
            {
                using var client = secondFactory.CreateClient();
                var host = secondFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();

                Assert.Equal(checkpointFingerprint, await BuildCanonicalHttpObservationFingerprintAsync(client));
                Assert.Equal(360, host.Status.WorldMinute);

                var progressed = false;
                for (var interval = 0; interval < 24 && !progressed; interval++)
                {
                    await host.AdvanceForTestingAsync(360);
                    var (_, current) = await GetConstructionProgressAsync(client, structureId);
                    progressed = current.HasAdvancedFrom(checkpointProgress);
                }

                Assert.True(progressed, "Construction did not make any material or work progress within the bounded 8,640-minute restart horizon.");
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
    public async Task FreshSeed42ServerCompletesShelterCreatesNextProjectAndRestartsThroughHttp()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            string checkpointFingerprint;
            string activeStructureId;
            ConstructionProgress checkpointProgress;

            var firstFactory = new M4ServerFactory(dataRoot);
            try
            {
                using var client = firstFactory.CreateClient();
                var host = firstFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();

                var foundMilestone = false;
                activeStructureId = string.Empty;
                checkpointProgress = default;
                for (var interval = 0; interval < 50 && !foundMilestone; interval++)
                {
                    await host.AdvanceForTestingAsync(360);
                    var settlement = await GetSettlementAsync(client);
                    var structures = await GetStructuresAsync(client);
                    if (settlement.GetProperty("completedShelters").GetInt32() < 1 ||
                        settlement.GetProperty("activeConstructionProject").ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var active = settlement.GetProperty("activeConstructionProject");
                    activeStructureId = active.GetProperty("structureId").GetString()!;
                    checkpointProgress = ConstructionProgress.From(active);
                    Assert.Contains(structures, structure => structure.GetProperty("structureId").GetString() == activeStructureId);
                    foundMilestone = true;
                }

                Assert.True(foundMilestone, "Fresh seed 42 did not complete a shelter and create the next construction project within 18,000 simulated minutes.");

                await AssertSmokeEndpointShapesAsync(client, activeStructureId);
                checkpointFingerprint = await BuildCanonicalHttpObservationFingerprintAsync(client);
                Assert.True((await host.RequestCheckpointAsync()).Succeeded);
            }
            finally
            {
                firstFactory.Dispose();
            }

            var secondFactory = new M4ServerFactory(dataRoot);
            try
            {
                using var client = secondFactory.CreateClient();
                var host = secondFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();

                Assert.Equal(checkpointFingerprint, await BuildCanonicalHttpObservationFingerprintAsync(client));
                await AssertSmokeEndpointShapesAsync(client, activeStructureId);

                var progressed = false;
                for (var interval = 0; interval < 24 && !progressed; interval++)
                {
                    await host.AdvanceForTestingAsync(360);
                    var (_, current) = await GetConstructionProgressAsync(client, activeStructureId);
                    progressed = current.HasAdvancedFrom(checkpointProgress);
                }

                Assert.True(progressed, "The post-restart active construction project did not progress within 8,640 simulated minutes.");
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

    private static async Task<string> AdvanceWithoutPollingAsync(string dataRoot)
    {
        var factory = new M4ServerFactory(dataRoot);
        try
        {
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            for (var interval = 0; interval < 15; interval++)
            {
                await host.AdvanceForTestingAsync(120);
            }

            Assert.Equal(1_800, host.Status.WorldMinute);
            return await BuildCanonicalHttpObservationFingerprintAsync(client);
        }
        finally
        {
            factory.Dispose();
        }
    }

    private static async Task<string> AdvanceWithAggressivePollingAsync(string dataRoot)
    {
        var factory = new M4ServerFactory(dataRoot);
        try
        {
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            for (var interval = 0; interval < 15; interval++)
            {
                await PollReadEndpointsAsync(client, repetitions: 4);
                await host.AdvanceForTestingAsync(120);
                await PollReadEndpointsAsync(client, repetitions: 4);
            }

            Assert.Equal(1_800, host.Status.WorldMinute);
            return await BuildCanonicalHttpObservationFingerprintAsync(client);
        }
        finally
        {
            factory.Dispose();
        }
    }

    private static async Task PollReadEndpointsAsync(HttpClient client, int repetitions)
    {
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            foreach (var endpoint in new[] { "/api/v1/status", "/api/v1/citizens", "/api/v1/settlement", "/api/v1/structures" })
            {
                using var response = await client.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
            }
        }
    }

    private static async Task<string> BuildCanonicalHttpObservationFingerprintAsync(HttpClient client)
    {
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var endpoint in new[] { "/api/v1/status", "/api/v1/citizens", "/api/v1/settlement", "/api/v1/structures", "/api/v1/map" })
            {
                using var response = await client.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                documents.Add(JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
            }

            var status = documents[0].RootElement;
            Assert.Equal("Running", status.GetProperty("state").GetString());
            Assert.False(string.IsNullOrEmpty(status.GetProperty("world").GetProperty("fingerprint").GetString()));
            return string.Join("|", documents.Select(static document => document.RootElement.GetRawText()));
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    private static async Task AssertSmokeEndpointShapesAsync(HttpClient client, string activeStructureId)
    {
        using var statusResponse = await client.GetAsync("/api/v1/status");
        statusResponse.EnsureSuccessStatusCode();
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal("Running", status.RootElement.GetProperty("state").GetString());
        Assert.True(status.RootElement.GetProperty("worldMinute").GetInt64() > 0);

        using var citizensResponse = await client.GetAsync("/api/v1/citizens");
        citizensResponse.EnsureSuccessStatusCode();
        using var citizens = JsonDocument.Parse(await citizensResponse.Content.ReadAsStringAsync());
        var firstCitizen = citizens.RootElement.EnumerateArray().First();
        var citizenId = firstCitizen.GetProperty("citizenId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(firstCitizen.GetProperty("occupation").GetString()));
        Assert.Equal(JsonValueKind.Object, firstCitizen.GetProperty("lifetimeWorkActivity").ValueKind);

        using var citizenDetailResponse = await client.GetAsync("/api/v1/citizens/" + citizenId);
        citizenDetailResponse.EnsureSuccessStatusCode();
        using var citizenDetail = JsonDocument.Parse(await citizenDetailResponse.Content.ReadAsStringAsync());
        Assert.Equal(citizenId, citizenDetail.RootElement.GetProperty("citizenId").GetString());

        using var settlementResponse = await client.GetAsync("/api/v1/settlement");
        settlementResponse.EnsureSuccessStatusCode();
        using var settlement = JsonDocument.Parse(await settlementResponse.Content.ReadAsStringAsync());
        Assert.True(settlement.RootElement.GetProperty("completedShelters").GetInt32() >= 1);
        Assert.Equal(activeStructureId, settlement.RootElement.GetProperty("activeConstructionProject").GetProperty("structureId").GetString());

        using var structuresResponse = await client.GetAsync("/api/v1/structures");
        structuresResponse.EnsureSuccessStatusCode();
        using var structures = JsonDocument.Parse(await structuresResponse.Content.ReadAsStringAsync());
        Assert.Contains(structures.RootElement.EnumerateArray(), structure => structure.GetProperty("structureId").GetString() == activeStructureId);

        using var structureDetailResponse = await client.GetAsync("/api/v1/structures/" + activeStructureId);
        structureDetailResponse.EnsureSuccessStatusCode();
        using var structureDetail = JsonDocument.Parse(await structureDetailResponse.Content.ReadAsStringAsync());
        Assert.Equal(activeStructureId, structureDetail.RootElement.GetProperty("structureId").GetString());
        Assert.Equal(JsonValueKind.Array, structureDetail.RootElement.GetProperty("contributions").ValueKind);

        using var mapResponse = await client.GetAsync("/api/v1/map");
        mapResponse.EnsureSuccessStatusCode();
        using var map = JsonDocument.Parse(await mapResponse.Content.ReadAsStringAsync());
        Assert.Equal(map.RootElement.GetProperty("width").GetInt32() * map.RootElement.GetProperty("height").GetInt32(), map.RootElement.GetProperty("terrain").GetArrayLength());
    }

    private static async Task<JsonElement> GetSettlementAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/settlement");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement[]> GetStructuresAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/structures");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(static structure => structure.Clone()).ToArray();
    }

    private static async Task<(string StructureId, ConstructionProgress Progress)> GetSingleConstructionProgressAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/structures");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var structure = Assert.Single(document.RootElement.EnumerateArray());
        return (structure.GetProperty("structureId").GetString()!, ConstructionProgress.From(structure));
    }

    private static async Task<(string StructureId, ConstructionProgress Progress)> GetConstructionProgressAsync(HttpClient client, string expectedStructureId)
    {
        using var response = await client.GetAsync("/api/v1/structures/" + expectedStructureId);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedStructureId, document.RootElement.GetProperty("structureId").GetString());
        return (expectedStructureId, ConstructionProgress.From(document.RootElement));
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "LittleAges-M4-Server-Acceptance", Guid.NewGuid().ToString("N"));
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

    private readonly record struct ConstructionProgress(int DeliveredWood, int DeliveredStone, int CompletedWork)
    {
        public static ConstructionProgress From(JsonElement structure) => new(
            structure.GetProperty("deliveredWood").GetInt32(),
            structure.GetProperty("deliveredStone").GetInt32(),
            structure.GetProperty("completedWork").GetInt32());

        public bool HasAdvancedFrom(ConstructionProgress baseline) =>
            DeliveredWood > baseline.DeliveredWood ||
            DeliveredStone > baseline.DeliveredStone ||
            CompletedWork > baseline.CompletedWork;
    }

    private sealed class M4ServerFactory(string dataRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "m4-acceptance-world");
            builder.UseSetting("WorldSeed", "42");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", "0");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
