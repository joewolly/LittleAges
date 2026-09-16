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

public sealed class M5PopulatedApiMatrixTests
{
    private static readonly string[] PartnerMemberIds = ["1", "2"];

    [Fact]
    public async Task PopulatedM5ReadRoutesHaveStableSocialFamilyHouseholdAndSettlementProjections()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "LittleAges-M5-Api", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        try
        {
            await WritePopulatedCheckpointAsync(Path.Combine(dataRoot, "integration-world.db"));
            using var factory = new ServerFactory(dataRoot);
            using var client = factory.CreateClient();
            await factory.Services.GetRequiredService<SimulationHost>().WaitForRunningForTestingAsync();

            using var citizen = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1")).Content.ReadAsStringAsync());
            Assert.Equal(0, citizen.RootElement.GetProperty("founderOrdinal").GetInt32());
            Assert.Equal("2", citizen.RootElement.GetProperty("partnerId").GetString());
            Assert.Equal("21", citizen.RootElement.GetProperty("householdId").GetString());
            Assert.Equal(JsonValueKind.Array, citizen.RootElement.GetProperty("childrenIds").ValueKind);
            Assert.True(citizen.RootElement.TryGetProperty("targetCitizenId", out _));

            using var relationships = JsonDocument.Parse(await (await client.GetAsync("/api/v1/citizens/1/relationships")).Content.ReadAsStringAsync());
            var relationship = Assert.Single(relationships.RootElement.EnumerateArray());
            Assert.Equal("2", relationship.GetProperty("otherCitizenId").GetString());
            Assert.Equal("Partner", relationship.GetProperty("label").GetString());
            Assert.Equal(7_000, relationship.GetProperty("familiarity").GetInt32());
            Assert.Equal(6_000, relationship.GetProperty("affinity").GetInt32());
            Assert.Equal(5_000, relationship.GetProperty("trust").GetInt32());

            using var households = JsonDocument.Parse(await (await client.GetAsync("/api/v1/households")).Content.ReadAsStringAsync());
            var household = Assert.Single(households.RootElement.EnumerateArray());
            Assert.Equal("21", household.GetProperty("householdId").GetString());
            Assert.Equal(PartnerMemberIds, household.GetProperty("memberIds").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(PartnerMemberIds, household.GetProperty("livingMemberIds").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(PartnerMemberIds, household.GetProperty("partnerPair").EnumerateArray().Select(x => x.GetString()));
            Assert.Empty(household.GetProperty("childrenIds").EnumerateArray());
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/households/021")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/households/999")).StatusCode);

            using var settlement = JsonDocument.Parse(await (await client.GetAsync("/api/v1/settlement")).Content.ReadAsStringAsync());
            Assert.Equal(1, settlement.RootElement.GetProperty("householdCount").GetInt32());
            Assert.Equal(1, settlement.RootElement.GetProperty("activeHouseholdCount").GetInt32());
            Assert.Equal(1, settlement.RootElement.GetProperty("partnershipCount").GetInt32());
            Assert.Equal(1, settlement.RootElement.GetProperty("relationshipCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static async Task WritePopulatedCheckpointAsync(string path)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var citizens = Citizens(engine);
        var first = citizens[1];
        var second = citizens[2];
        var relationship = new RelationshipState(first.Id, second.Id, 7_000, 6_000, 5_000, 0, 0, 1);
        typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, relationship]);
        Relationships(engine).Add((first.Id.Value, second.Id.Value), relationship);
        await using var database = await WorldDatabase.OpenAsync(path);
        await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot(), DateTime.UtcNow);
    }

    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), RelationshipState> Relationships(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));

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
