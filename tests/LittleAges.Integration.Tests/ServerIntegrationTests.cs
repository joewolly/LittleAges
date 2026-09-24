using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void FreshWorldConfigurationDefaultsToCurrentRulesWithoutChangingLegacyRules()
    {
        var options = ServerOptions.FromConfiguration(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, SimulationEngine.CurrentSimulationRulesVersion);
        Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, options.NewWorldRules);
        Assert.Equal("m12-rng1-spaced1", SimulationEngine.SpacedSimulationRulesVersion);
        Assert.Equal("m11-rng1-barter1", SimulationEngine.BarterSimulationRulesVersion);
        Assert.Equal("m8-rng1-balance1", SimulationEngine.M8SimulationRulesVersion);
        Assert.Equal("m9-rng1-growth1", SimulationEngine.GrowthSimulationRulesVersion);
        Assert.Equal("m10-rng1-agriculture1", SimulationEngine.AgricultureSimulationRulesVersion);
    }

    [Fact]
    public async Task SettlementStorageIncludesPrivateStocksAndCountsNewBuildings()
    {
        var root = CreateDataRoot();
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
            engine.AdvanceUntil(new WorldMinute(10L * WorldCalendar.MinutesPerDay));
            var snapshot = engine.CreatePersistenceSnapshot();
            Assert.True(snapshot.Economy!.Households.Sum(h => h.Holdings.Total) > 0);
            await using (var db = await WorldDatabase.OpenAsync(Path.Combine(root, "integration-world.db"))) await db.CreateCheckpointStore().CheckpointAsync(snapshot);
            using var factory = new ServerFactory(root, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/settlement"));
            Assert.Equal(snapshot.Economy.StoredGoods(snapshot.Settlement!).Total, json.RootElement.GetProperty("storageUsed").GetInt64());
            Assert.Equal(snapshot.Structures.Count(s => s.Type == StructureType.Marketplace && s.Status == StructureStatus.Complete), json.RootElement.GetProperty("completedMarketplaces").GetInt32());
            Assert.Equal(snapshot.Structures.Count(s => s.Type == StructureType.Farm && s.Status == StructureStatus.Complete), json.RootElement.GetProperty("completedFarms").GetInt32());
            Assert.Equal(snapshot.Structures.Count(s => s.Type == StructureType.Granary && s.Status == StructureStatus.Complete), json.RootElement.GetProperty("completedGranaries").GetInt32());
            Assert.Equal(snapshot.Settlement!.FoodStored, json.RootElement.GetProperty("foodStored").GetInt32());
        }
        finally { CleanupDataRoot(root); }
    }

    [Fact]
    public async Task EconomyReadsAreBoundedLosslessAndDoNotAdvanceTheWorld()
    {
        var root = CreateDataRoot();
        try
        {
            using var factory = new ServerFactory(root, simulationMinutesPerSecond: 0, worldSeed: 42,
                suppressLogs: true, newWorldRules: SimulationEngine.BarterSimulationRulesVersion);
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            var first = await client.GetStringAsync("/api/v1/economy?limit=1");
            Assert.Equal(first, await client.GetStringAsync("/api/v1/economy?limit=1"));
            using var json = JsonDocument.Parse(first);
            Assert.True(json.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Equal(20, json.RootElement.GetProperty("communalPercent").GetInt32());
            Assert.Equal(20, json.RootElement.GetProperty("totalHouseholds").GetInt32());
            var household = Assert.Single(json.RootElement.GetProperty("households").EnumerateArray());
            Assert.Equal(JsonValueKind.String, household.GetProperty("householdId").ValueKind);
            using var detail = JsonDocument.Parse(await client.GetStringAsync("/api/v1/households/" + household.GetProperty("householdId").GetString()));
            Assert.Equal(0, detail.RootElement.GetProperty("economy").GetProperty("inventory").GetProperty("food").GetInt32());
            using var invalid = await client.GetAsync("/api/v1/economy?limit=201");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var status = JsonDocument.Parse(await client.GetStringAsync("/api/v1/status"));
            Assert.Equal(0, status.RootElement.GetProperty("worldMinute").GetInt64());
        }
        finally { CleanupDataRoot(root); }
    }

    [Fact]
    public async Task AgricultureEndpointUsesBoundedReadOnlyPagesAndSelectedRules()
    {
        var root = CreateDataRoot();
        try
        {
            using var factory = new ServerFactory(root, simulationMinutesPerSecond: 0, worldSeed: 42,
                suppressLogs: true, newWorldRules: SimulationEngine.AgricultureSimulationRulesVersion);
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            var first = await client.GetStringAsync("/api/v1/agriculture?limit=1");
            Assert.Equal(first, await client.GetStringAsync("/api/v1/agriculture?limit=1"));
            using var document = JsonDocument.Parse(first);
            Assert.True(document.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Equal(16200, document.RootElement.GetProperty("winterReserveTarget").GetInt32());
            using var invalid = await client.GetAsync("/api/v1/agriculture?limit=201");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var negative = await client.GetAsync("/api/v1/agriculture?offset=-1");
            Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        }
        finally { CleanupDataRoot(root); }
    }

    [Fact]
    public async Task GrowthEndpointPublishesNewWorldHouseholdsWithoutChangingTheWorld()
    {
        var root = CreateDataRoot();
        try
        {
            using var factory = new ServerFactory(root, simulationMinutesPerSecond: 0, worldSeed: 42,
                suppressLogs: true, newWorldRules: SimulationEngine.GrowthSimulationRulesVersion);
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            using var first = await client.GetAsync("/api/v1/growth");
            using var second = await client.GetAsync("/api/v1/growth");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var json = await first.Content.ReadAsStringAsync();
            Assert.Equal(json, await second.Content.ReadAsStringAsync());
            using var result = JsonDocument.Parse(json);
            Assert.Equal(20, result.RootElement.GetProperty("living").GetInt32());
            var households = result.RootElement.GetProperty("households").EnumerateArray().ToArray();
            Assert.Equal(20, households.Length);
            Assert.All(households, h => Assert.Equal(JsonValueKind.String, h.GetProperty("householdId").ValueKind));
            Assert.All(households, h => Assert.Contains("No partnered pair", h.GetProperty("blockers").EnumerateArray().Select(b => b.GetString())));
        }
        finally { CleanupDataRoot(root); }
    }
    [Fact]
    public void ObservationProjectionCopiesMutableDomainReadModels()
    {
        var sourceSkills = new CitizenSkills(1, 2, 3, 4, 5, 6);
        var sourceNeeds = new CitizenNeeds(100, 200, 300, 400);
        var source = new CitizenReadSnapshot(
            "9007199254740993", 0, "Dead", "Founder", "Dead Founder", 42, "Adult", new TileCoordinate(1, 2), 0,
            sourceNeeds, new CitizenTraits(1, 2, 3, 4, 5, 6), sourceSkills, CitizenAction.Dead, null, null, null, 0, false,
            "starvation", null, 0, null, CitizenActionPhase.None, new WorldMinute(360));
        var status = new ServerStatusSnapshot(SimulationHostState.Running, 360, 0, "42", null, Population: 0);
        var sourceStructure = new Structure(new StructureId(17), StructureType.Shelter, new TileCoordinate(3, 4), 360, 40, 10, 600)
        {
            DeliveredWood = 5
        };
        var observation = new ServerObservationSnapshot(
            status,
            new[] { new ServerCitizenSnapshot(source) },
            null,
            new[] { new ServerStructureSnapshot(sourceStructure, Enumerable.Repeat("9007199254740993", 1)) });

        sourceSkills.Foraging = 999;
        sourceNeeds = new CitizenNeeds(999, 999, 999, 999);
        sourceStructure.DeliveredWood = 40;
        Assert.Equal(1, observation.Citizens.Single().Skills.Foraging);
        Assert.Equal(100, observation.Citizens.Single().Hunger);
        Assert.False(observation.Citizens.Single().IsAlive);
        Assert.Equal("9007199254740993", observation.Citizens.Single().CitizenId);
        Assert.Equal("starvation", observation.Citizens.Single().DeathCause);
        Assert.Equal(360, observation.Citizens.Single().DeathMinute);
        Assert.Equal(5, observation.Structures.Single().DeliveredWood);
        Assert.Equal("9007199254740993", observation.Structures.Single().CurrentOccupantIds.Single());
    }

    [Fact]
    public void HouseholdObserverSnapshotDoesNotRetainCallerOwnedLists()
    {
        var members = new List<string> { "1", "2" };
        var living = new List<string> { "1", "2" };
        var partners = new List<string> { "1", "2" };
        var children = new List<string> { "3" };
        var snapshot = new ServerHouseholdSnapshot("7", 12, null, "9", members, living, partners, children);

        members[0] = "mutated";
        living.Clear();
        partners.Add("mutated");
        children.Clear();

        Assert.Equal("1", snapshot.MemberIds[0]);
        Assert.Equal("2", snapshot.MemberIds[1]);
        Assert.Equal("1", snapshot.LivingMemberIds[0]);
        Assert.Equal("2", snapshot.LivingMemberIds[1]);
        Assert.NotNull(snapshot.PartnerPair);
        Assert.Equal("1", snapshot.PartnerPair![0]);
        Assert.Equal("2", snapshot.PartnerPair[1]);
        Assert.Equal("3", Assert.Single(snapshot.ChildrenIds));
    }

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
            Assert.All(citizens, citizen =>
            {
                Assert.True(citizen.GetProperty("isAlive").GetBoolean());
                Assert.True(citizen.TryGetProperty("hunger", out var hunger));
                Assert.True(citizen.TryGetProperty("rest", out var rest));
                Assert.True(citizen.TryGetProperty("currentAction", out var currentAction));
                Assert.True(citizen.TryGetProperty("actionPhase", out var actionPhase));
                Assert.True(hunger.ValueKind != JsonValueKind.Undefined);
                Assert.True(rest.ValueKind != JsonValueKind.Undefined);
                Assert.True(currentAction.ValueKind != JsonValueKind.Undefined);
                Assert.True(actionPhase.ValueKind != JsonValueKind.Undefined);
            });

            using var settlementResponse = await client.GetAsync("/api/v1/settlement");
            settlementResponse.EnsureSuccessStatusCode();
            using var settlement = JsonDocument.Parse(await settlementResponse.Content.ReadAsStringAsync());
            Assert.Equal(400, settlement.RootElement.GetProperty("foodStored").GetInt32());
            Assert.Equal(20, settlement.RootElement.GetProperty("livingPopulation").GetInt32());
            Assert.Equal(0, settlement.RootElement.GetProperty("deadPopulation").GetInt32());

            using var detail = await client.GetAsync("/api/v1/citizens/1");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var malformed = await client.GetAsync("/api/v1/citizens/01");
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            using var missing = await client.GetAsync("/api/v1/citizens/999");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        });
    }

    [Fact]
    public async Task BrowserControlRequestsRejectForeignOriginsWithoutChangingStatus()
    {
        var dataRoot = CreateDataRoot();
        using var factory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
        try
        {
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            foreach (var origin in new[] { "https://untrusted.example", "null", "http://localhost:81", "https://localhost", "http://localhost.untrusted.example" })
            foreach (var action in new[] { "pause", "resume", "speed" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/control/{action}");
                request.Headers.Add("Origin", origin);
                request.Content = new StringContent("{\"speed\":10}", Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            using var status = await client.GetAsync("/api/v1/status");
            using var unchanged = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            Assert.True(unchanged.RootElement.GetProperty("paused").GetBoolean());
            Assert.Equal(0, unchanged.RootElement.GetProperty("operationalSpeed").GetDouble());
            Assert.Equal(0, unchanged.RootElement.GetProperty("worldMinute").GetInt64());

            foreach (var origin in new[] { "http://localhost", "http://localhost:80" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/control/speed");
                request.Headers.Add("Origin", origin);
                request.Content = new StringContent("{\"speed\":5}", Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            factory.Dispose();
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task OperationalControlRoutesValidateAndReportImmutableStatus()
    {
        var dataRoot = CreateDataRoot();
        using var factory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
        try
        {
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);
            Assert.True(running.RootElement.GetProperty("paused").GetBoolean());
            using var pause = await client.PostAsync("/api/v1/control/pause", content: null);
            Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
            using var paused = JsonDocument.Parse(await pause.Content.ReadAsStringAsync());
            Assert.True(paused.RootElement.GetProperty("paused").GetBoolean());

            using var invalidSpeed = await client.PostAsync("/api/v1/control/speed", new StringContent("{\"speed\":1001}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, invalidSpeed.StatusCode);
            using var speed = await client.PostAsync("/api/v1/control/speed", new StringContent("{\"speed\":5}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, speed.StatusCode);
            using var changed = JsonDocument.Parse(await speed.Content.ReadAsStringAsync());
            Assert.Equal(5, changed.RootElement.GetProperty("operationalSpeed").GetDouble());
            Assert.True(changed.RootElement.GetProperty("paused").GetBoolean());

            using var resume = await client.PostAsync("/api/v1/control/resume", content: null);
            Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
            using var resumed = JsonDocument.Parse(await resume.Content.ReadAsStringAsync());
            Assert.False(resumed.RootElement.GetProperty("paused").GetBoolean());
            Assert.Equal(5, resumed.RootElement.GetProperty("operationalSpeed").GetDouble());
        }
        finally
        {
            factory.Dispose();
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task M5SocialReadRoutesValidateIdsAndExposeImmutableEmptyFoundingState()
    {
        await WithFactoryAsync(async (factory, _) =>
        {
            using var client = factory.CreateClient();
            using var running = await WaitForRunningStatusAsync(client);

            using var relationships = await client.GetAsync("/api/v1/citizens/1/relationships");
            relationships.EnsureSuccessStatusCode();
            using var relationshipDocument = JsonDocument.Parse(await relationships.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, relationshipDocument.RootElement.ValueKind);

            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/citizens/01/relationships")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/citizens/999/relationships")).StatusCode);

            using var households = await client.GetAsync("/api/v1/households");
            households.EnsureSuccessStatusCode();
            using var householdDocument = JsonDocument.Parse(await households.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, householdDocument.RootElement.ValueKind);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/households/01")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/households/999")).StatusCode);
        });
    }

    [Fact]
    public async Task M4ReadEndpointsPublishCompactImmutableSettlementObservation()
    {
        var dataRoot = CreateDataRoot();
        var factory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
        try
        {
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            using var initialMapResponse = await client.GetAsync("/api/v1/map");
            initialMapResponse.EnsureSuccessStatusCode();
            using var initialMap = JsonDocument.Parse(await initialMapResponse.Content.ReadAsStringAsync());
            var width = initialMap.RootElement.GetProperty("width").GetInt32();
            var height = initialMap.RootElement.GetProperty("height").GetInt32();
            var terrain = initialMap.RootElement.GetProperty("terrain").EnumerateArray().ToArray();
            var elevation = initialMap.RootElement.GetProperty("elevation").EnumerateArray().Select(static item => item.GetInt32()).ToArray();
            var resources = initialMap.RootElement.GetProperty("resources").EnumerateArray().ToArray();
            Assert.Equal(width * height, terrain.Length);
            Assert.Equal(width * height, elevation.Length);
            Assert.All(elevation, static value => Assert.InRange(value, 0, 10000));
            Assert.NotEmpty(resources);
            var resourceIds = resources.Select(static item => long.Parse(item.GetProperty("resourceNodeId").GetString()!, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(resourceIds.OrderBy(static value => value), resourceIds);
            Assert.All(resources, resource =>
            {
                Assert.True(resource.GetProperty("resourceType").GetString() is "Food" or "Wood" or "Stone");
                Assert.InRange(resource.GetProperty("location").GetProperty("x").GetInt32(), 0, width - 1);
                Assert.InRange(resource.GetProperty("location").GetProperty("y").GetInt32(), 0, height - 1);
                Assert.True(resource.GetProperty("maximumQuantity").GetInt32() > 0);
                Assert.InRange(resource.GetProperty("regenerationPotential").GetInt32(), 0, 10000);
            });
            Assert.All(terrain, value => Assert.Equal(JsonValueKind.Number, value.ValueKind));
            Assert.Equal(160, width);
            Assert.Equal(160, height);
            Assert.Equal(JsonValueKind.Object, initialMap.RootElement.GetProperty("startingSite").ValueKind);

            using var citizens = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
            var citizen = citizens.RootElement[0];
            Assert.Equal(JsonValueKind.String, citizen.GetProperty("occupation").ValueKind);
            Assert.Equal(JsonValueKind.Object, citizen.GetProperty("lifetimeWorkActivity").ValueKind);
            Assert.True(citizen.TryGetProperty("homeStructureId", out _));
            Assert.True(citizen.TryGetProperty("targetStructureId", out _));

            await host.AdvanceForTestingAsync(CitizenSimulationRules.SettlementDemandIntervalMinutes);
            using var structuresResponse = await client.GetAsync("/api/v1/structures");
            structuresResponse.EnsureSuccessStatusCode();
            using var structures = JsonDocument.Parse(await structuresResponse.Content.ReadAsStringAsync());
            var listed = structures.RootElement.EnumerateArray().ToArray();
            var structure = Assert.Single(listed);
            Assert.Equal(nameof(StructureType.Shelter), structure.GetProperty("type").GetString());
            Assert.Equal(nameof(StructureStatus.UnderConstruction), structure.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Array, structure.GetProperty("currentOccupantIds").ValueKind);
            Assert.Equal(JsonValueKind.Array, structure.GetProperty("contributions").ValueKind);
            Assert.Equal(4, structure.GetProperty("capacity").GetInt32());
            Assert.Equal(JsonValueKind.Null, structure.GetProperty("storageBonus").ValueKind);
            Assert.Equal(JsonValueKind.Null, structure.GetProperty("constructionMultiplierBasisPoints").ValueKind);
            var id = structure.GetProperty("structureId").GetString()!;

            using var detail = await client.GetAsync("/api/v1/structures/" + id);
            detail.EnsureSuccessStatusCode();
            using var malformed = await client.GetAsync("/api/v1/structures/01");
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            using var missing = await client.GetAsync("/api/v1/structures/999");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            using var settlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
            Assert.Equal(800, settlement.RootElement.GetProperty("storageCapacity").GetInt32());
            Assert.InRange(settlement.RootElement.GetProperty("storageUsed").GetInt32(), 0, 800);
            Assert.Equal(0, settlement.RootElement.GetProperty("shelterCapacity").GetInt32());
            Assert.Equal(20, settlement.RootElement.GetProperty("unhousedPopulation").GetInt32());
            Assert.Equal(id, settlement.RootElement.GetProperty("activeConstructionProject").GetProperty("structureId").GetString());

            using var laterMap = JsonDocument.Parse(await (await client.GetAsync("/api/v1/map")).Content.ReadAsStringAsync());
            Assert.Equal(initialMap.RootElement.GetRawText(), laterMap.RootElement.GetRawText());
        }
        finally
        {
            factory.Dispose();
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task DeterministicHostAdvancePublishesCoherentM3Observation()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            long checkpointMinute;
            string beforeRestart;
            Dictionary<string, int> initialSkills;
            var firstFactory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
            try
            {
                using var client = firstFactory.CreateClient();
                var host = firstFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();
                using var initialCitizens = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
                initialSkills = initialCitizens.RootElement.EnumerateArray().ToDictionary(
                    citizen => citizen.GetProperty("citizenId").GetString()!,
                    citizen => citizen.GetProperty("skills").EnumerateObject().Sum(skill => skill.Value.GetInt32()),
                    StringComparer.Ordinal);
                await host.AdvanceForTestingAsync(360);
                using var status = JsonDocument.Parse(await (await client.GetAsync("/api/v1/status")).Content.ReadAsStringAsync());
                using var citizens = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
                using var detail = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                using var settlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
                var statusLiving = status.RootElement.GetProperty("livingPopulation").GetInt32();
                var statusDead = status.RootElement.GetProperty("deadPopulation").GetInt32();
                Assert.Equal(statusLiving, citizens.RootElement.EnumerateArray().Count(item => item.GetProperty("isAlive").GetBoolean()));
                Assert.Equal(statusDead, citizens.RootElement.EnumerateArray().Count(item => !item.GetProperty("isAlive").GetBoolean()));
                Assert.Equal(statusLiving, settlement.RootElement.GetProperty("livingPopulation").GetInt32());
                Assert.Equal(statusDead, settlement.RootElement.GetProperty("deadPopulation").GetInt32());
                Assert.Equal(statusLiving, status.RootElement.GetProperty("population").GetInt32());
                Assert.Equal("1", detail.RootElement.GetProperty("citizenId").GetString());
                checkpointMinute = status.RootElement.GetProperty("worldMinute").GetInt64();
                beforeRestart = await BuildObservationFingerprintAsync(client);
                await host.RequestCheckpointAsync();
            }
            finally
            {
                firstFactory.Dispose();
            }

            var secondFactory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
            try
            {
                using var client = secondFactory.CreateClient();
                var host = secondFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();
                Assert.Equal(beforeRestart, await BuildObservationFingerprintAsync(client));
                Assert.Equal(checkpointMinute, host.Status.WorldMinute);
                await host.AdvanceForTestingAsync(60);
                Assert.True(host.Status.WorldMinute > checkpointMinute);
                await host.AdvanceForTestingAsync(10080);
                using var continuedStatus = JsonDocument.Parse(await (await client.GetAsync("/api/v1/status")).Content.ReadAsStringAsync());
                using var continuedCitizens = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
                using var continuedSettlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
                Assert.True(continuedStatus.RootElement.GetProperty("worldMinute").GetInt64() > checkpointMinute);
                Assert.All(continuedCitizens.RootElement.EnumerateArray(), citizen =>
                {
                    Assert.InRange(citizen.GetProperty("health").GetInt32(), 0, 10000);
                    Assert.InRange(citizen.GetProperty("hunger").GetInt32(), 0, 10000);
                    Assert.InRange(citizen.GetProperty("rest").GetInt32(), 0, 10000);
                });
                Assert.All(continuedSettlement.RootElement.GetProperty("resources").EnumerateArray(), resource =>
                    Assert.True(resource.GetProperty("currentQuantity").GetInt32() >= 0));
                Assert.Contains(continuedCitizens.RootElement.EnumerateArray(), citizen =>
                    citizen.GetProperty("skills").EnumerateObject().Sum(skill => skill.Value.GetInt32()) > initialSkills[citizen.GetProperty("citizenId").GetString()!]);
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
    public async Task ActualM3ServerSmokeObservesGatherDepletionDepositEatNeedsCheckpointAndRestart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            string beforeRestart;
            long checkpointMinute;
            var seenGatherTravel = false;
            var seenGatherPerform = false;
            var seenGatherReturn = false;
            var seenEat = false;
            var observedDepletion = false;
            var observedDeposit = false;
            var previousFood = -1;
            var previousCarried = new Dictionary<string, int>(StringComparer.Ordinal);
            var firstFactory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
            try
            {
                using var client = firstFactory.CreateClient();
                var host = firstFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();
                using var initialRoster = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
                Assert.Equal(20, initialRoster.RootElement.GetArrayLength());
                using var initialDetail = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                Assert.Equal("1", initialDetail.RootElement.GetProperty("citizenId").GetString());
                using var initialSettlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
                previousFood = initialSettlement.RootElement.GetProperty("foodStored").GetInt32();
                var initialNodes = initialSettlement.RootElement.GetProperty("resources").EnumerateArray().ToDictionary(
                    resource => resource.GetProperty("resourceNodeId").GetString()!,
                    resource => resource.GetProperty("currentQuantity").GetInt32(), StringComparer.Ordinal);

                for (var step = 0; step < 2_500 && !(seenGatherTravel && seenGatherPerform && seenGatherReturn && seenEat && observedDepletion && observedDeposit); step++)
                {
                    await host.AdvanceForTestingAsync(5);
                    using var roster = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
                    foreach (var citizen in roster.RootElement.EnumerateArray())
                    {
                        var id = citizen.GetProperty("citizenId").GetString()!;
                        var action = citizen.GetProperty("currentAction").GetString();
                        var phase = citizen.GetProperty("actionPhase").GetString();
                        seenEat |= action == nameof(CitizenAction.Eat);
                        if (action is nameof(CitizenAction.GatherFood) or nameof(CitizenAction.GatherWood) or nameof(CitizenAction.GatherStone))
                        {
                            seenGatherTravel |= phase == nameof(CitizenActionPhase.TravelToTarget);
                            seenGatherPerform |= phase == nameof(CitizenActionPhase.Perform);
                            seenGatherReturn |= phase == nameof(CitizenActionPhase.ReturnToStockpile) && citizen.GetProperty("carriedQuantity").GetInt32() > 0;
                        }
                        var carried = citizen.TryGetProperty("carriedQuantity", out var carriedValue) && carriedValue.ValueKind == JsonValueKind.Number ? carriedValue.GetInt32() : 0;
                        if (previousCarried.TryGetValue(id, out var prior) && prior > 0 && carried == 0) observedDeposit = true;
                        previousCarried[id] = carried;
                        Assert.InRange(citizen.GetProperty("health").GetInt32(), 0, 10000);
                        Assert.InRange(citizen.GetProperty("hunger").GetInt32(), 0, 10000);
                        Assert.InRange(citizen.GetProperty("rest").GetInt32(), 0, 10000);
                    }
                    using var settlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
                    var food = settlement.RootElement.GetProperty("foodStored").GetInt32();
                    observedDeposit |= food > previousFood;
                    previousFood = food;
                    observedDepletion |= settlement.RootElement.GetProperty("resources").EnumerateArray().Any(resource =>
                        initialNodes[resource.GetProperty("resourceNodeId").GetString()!] > resource.GetProperty("currentQuantity").GetInt32());
                }

                Assert.True(seenGatherTravel && seenGatherPerform && seenGatherReturn, "The server did not expose all gather phases through immutable observations.");
                Assert.True(observedDepletion, "No resource-node depletion was observed.");
                Assert.True(observedDeposit, "No physical gather deposit was observed.");
                Assert.True(seenEat, "No Eat action was observed.");
                using var finalDetail = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                Assert.True(finalDetail.RootElement.TryGetProperty("hunger", out _));
                Assert.True(finalDetail.RootElement.TryGetProperty("health", out _));
                using var finalSettlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
                Assert.True(finalSettlement.RootElement.TryGetProperty("resources", out _));
                beforeRestart = await BuildObservationFingerprintAsync(client);
                checkpointMinute = host.Status.WorldMinute;
                Assert.True((await host.RequestCheckpointAsync()).Succeeded);
            }
            finally { firstFactory.Dispose(); }

            var secondFactory = new ServerFactory(dataRoot, simulationMinutesPerSecond: 0, worldSeed: 42, suppressLogs: true);
            try
            {
                using var client = secondFactory.CreateClient();
                var host = secondFactory.Services.GetRequiredService<SimulationHost>();
                await host.WaitForRunningForTestingAsync();
                Assert.Equal(beforeRestart, await BuildObservationFingerprintAsync(client));
                Assert.Equal(checkpointMinute, host.Status.WorldMinute);
                await host.AdvanceForTestingAsync(5);
                Assert.True(host.Status.WorldMinute > checkpointMinute);
                using var roster = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
                using var detail = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
                using var settlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
                Assert.Equal("1", detail.RootElement.GetProperty("citizenId").GetString());
                Assert.Equal(20, roster.RootElement.GetArrayLength());
                Assert.True(settlement.RootElement.GetProperty("foodStored").GetInt32() >= 0);
            }
            finally { secondFactory.Dispose(); }
        }
        finally { CleanupDataRoot(dataRoot); }
    }

    private static async Task<string> BuildObservationFingerprintAsync(HttpClient client)
    {
        using var status = JsonDocument.Parse(await (await client.GetAsync("/api/v1/status")).Content.ReadAsStringAsync());
        using var citizens = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens")).Content.ReadAsStringAsync());
        using var settlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
        var canonicalStatus = JsonNode.Parse(status.RootElement.GetRawText())!.AsObject();
        canonicalStatus.Remove("persistenceState");
        canonicalStatus.Remove("lastSuccessfulCheckpointWorldMinute");
        canonicalStatus.Remove("lastSuccessfulCheckpointUtc");
        canonicalStatus.Remove("consecutiveCheckpointFailures");
        return string.Concat(canonicalStatus.ToJsonString(), "|", citizens.RootElement.GetRawText(), "|", settlement.RootElement.GetRawText());
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

            using var host = CreateSimulationHost(dataRoot, "integration-world");
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await host.StartAsync();
            await WaitForStateAsync(simulationHost, SimulationHostState.Faulted);
            Assert.Contains("not supported", simulationHost.Status.Error, StringComparison.OrdinalIgnoreCase);
            var health = await new SimulationHostHealthCheck(simulationHost).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, health.Status);
            await Assert.ThrowsAsync<NotSupportedException>(() => host.StopAsync());
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

    private static IHost CreateSimulationHost(string dataRoot, string activeWorld = "host-world", double simulationMinutesPerSecond = 10, int checkpointRetryCount = 0)
    {
        var builder = Host.CreateApplicationBuilder();
        var options = new ServerOptions
        {
            DataRoot = dataRoot,
            ActiveWorld = activeWorld,
            WorldSeed = new WorldSeed(17),
            ListenUrls = ServerOptions.DefaultListenUrls,
            SimulationMinutesPerSecond = simulationMinutesPerSecond,
            CheckpointRetryCount = checkpointRetryCount
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

    private sealed class ServerFactory(string dataRoot, double simulationMinutesPerSecond = 10, ulong worldSeed = ulong.MaxValue, bool suppressLogs = false, string newWorldRules = SimulationEngine.M8SimulationRulesVersion) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "integration-world");
            builder.UseSetting("NewWorldRules", newWorldRules);
            builder.UseSetting("WorldSeed", worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", simulationMinutesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (suppressLogs) builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
