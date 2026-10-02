using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class M17NewcomerObserverTests
{
    [Fact]
    public async Task PersistedApproachingGuestHasImmutableRouteAndBiographyWithoutResidentPopulation()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
        object? Invoke(string name, params object[] arguments) => typeof(SimulationEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length).Invoke(engine, arguments);
        var location = (TileCoordinate)Invoke("SelectConstructionSite", 1L, StructureType.Shelter)!;
        Invoke("CreateStructure", StructureType.Shelter, location);
        engine.AdvanceUntil(new WorldMinute(60L * WorldCalendar.MinutesPerDay));
        var population = engine.Population;
        Assert.True((bool)Invoke("TryCreateNewcomer", 6L)!);
        var guest = Assert.Single(engine.CreateNewcomerObservations());
        Assert.NotNull(guest.Person!.MovementPlan);
        var before = engine.LivingStateJson;
        _ = engine.CreateNewcomerObservations();
        Assert.Equal(before, engine.LivingStateJson);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-observer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(Path.Combine(root, "visitors.db")))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            await using var factory = new VisitorFactory(root);
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();
            Assert.Equal(population, host.Status.LivingPopulation);
            Assert.Equal(1, host.Status.GuestPopulation);
            Assert.Equal(1, host.Observation.Settlement!.GuestPopulation);
            Assert.Equal(population, host.Observation.Settlement.LivingPopulation);
            var observed = Assert.Single(host.Observation.Citizens, x => x.CitizenId == guest.CitizenId);
            Assert.False(observed.IsResident);
            Assert.True(observed.IsGuest);
            Assert.NotNull(observed.MovementPlan);
            using var biography = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/citizens/{guest.CitizenId}/biography"));
            Assert.Equal(JsonValueKind.Null, biography.RootElement.GetProperty("birthMinute").ValueKind);
            Assert.Contains(biography.RootElement.GetProperty("events").EnumerateArray(), item => item.GetProperty("eventType").GetString() == "NewcomerAppeared");
            var first = await client.GetStringAsync($"/api/v1/citizens/{guest.CitizenId}");
            Assert.Equal(first, await client.GetStringAsync($"/api/v1/citizens/{guest.CitizenId}"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(root);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), resolved, StringComparison.OrdinalIgnoreCase);
            Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class VisitorFactory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", root); builder.UseSetting("ActiveWorld", "visitors");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0"); builder.UseSetting("SimulationMinutesPerSecond", "0");
        }
    }

    [Fact]
    public void GuestArchiveKeepsAliveIdentityOutsideResidentCountsAndOmitsNativeMetadata()
    {
        var citizen = new CitizenReadSnapshot("21", null, "Iria", "Jory", "Iria Jory", 24, "Adult", new(0, 3), 9000,
            new(), new(5000, 5000, 5000, 5000, 5000, 5000), new(0, 0, 0, 0, 0, 0), CitizenAction.Idle, null, null, null, 3);
        var observation = new NewcomerReadSnapshot("21", NewcomerPhase.Departed, "1", "4", new(0, 3), 100, 120, 400, null, 300, null, 0, citizen, []);
        var guest = new ServerCitizenSnapshot(citizen, observation);
        Assert.True(guest.IsAlive);
        Assert.False(guest.IsResident);
        Assert.False(guest.IsGuest);
        Assert.Null(guest.BirthMinute);
        Assert.Equal(300, guest.Newcomer!.DepartedMinute);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(new ServerCitizenSnapshot(citizen), options));
        Assert.False(native.RootElement.TryGetProperty("newcomer", out _));
        using var external = JsonDocument.Parse(JsonSerializer.Serialize(guest, options));
        Assert.Equal("Departed", external.RootElement.GetProperty("newcomer").GetProperty("phase").GetString());
    }

    [Fact]
    public void ExternalTimelinesUsePayloadIdentityBeforeAdmissionAndSayLastKnownAlive()
    {
        var names = new Dictionary<long, string> { [21] = "Iria", [2] = "Bram" };
        var contact = new ServerHistoricalEventSnapshot(new(new(1), 120, HistoricalEventType.VisitorContact, HistoricalImportance.Routine,
            HistoricalEventOrigin.Live, null, HistoricalEventPayloads.VisitorContact(21, 1, 2, 100, 50, 80, 0, 1)), [], [], names);
        Assert.True(HistoricalEventSummary.InvolvesCitizen(contact, "21"));
        Assert.True(HistoricalEventSummary.InvolvesCitizen(contact, "2"));
        Assert.False(HistoricalEventSummary.InvolvesCitizen(contact, "3"));
        Assert.Contains("Iria spoke with Bram", contact.Summary);
        var departure = new ServerHistoricalEventSnapshot(new(new(2), 300, HistoricalEventType.VisitorDeparted, HistoricalImportance.Routine,
            HistoricalEventOrigin.Live, null, HistoricalEventPayloads.VisitorDeparted(21, 1, "deadline", 10)), [], [], names);
        Assert.Contains("departed alive", departure.Summary);
        Assert.Contains("later whereabouts are unknown", departure.Summary);
    }
}
