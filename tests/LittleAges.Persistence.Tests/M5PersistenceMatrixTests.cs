using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M5PersistenceMatrixTests
{
    [Fact]
    public async Task PopulatedSparseRelationshipAndHouseholdSqliteCheckpointRoundTripsExactly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M5-Matrix", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "world.db");
        Directory.CreateDirectory(directory);
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
            var citizens = Citizens(engine);
            var first = citizens[1];
            var second = citizens[2];
            var relationship = new RelationshipState(first.Id, second.Id, 7_000, 6_000, 5_000, 0, 0, 1);
            typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, relationship]);
            Relationships(engine).Add((first.Id.Value, second.Id.Value), relationship);
            var source = engine.CreatePersistenceSnapshot();

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var loaded = await store.LoadAsync();
            var reloaded = SimulationEngine.FromPersistenceSnapshot(loaded);

            Assert.Equal(source.Relationships, loaded.Relationships);
            Assert.Equal(source.Households.Select(HouseholdKey), loaded.Households.Select(HouseholdKey));
            Assert.Equal(engine.SocialFingerprint, reloaded.SocialFingerprint);
            Assert.Equal(source.Counters.NextHistoricalEventId, loaded.Counters.NextHistoricalEventId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string HouseholdKey(Household household) => $"{household.Id.Value}:{household.CreatedMinute}:{household.DissolvedMinute}:{household.DwellingStructureId?.Value}";
    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), RelationshipState> Relationships(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
}
