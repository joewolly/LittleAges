using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class GrowthPersistenceTests
{
    [Theory]
    [InlineData(SimulationEngine.M8SimulationRulesVersion)]
    [InlineData(SimulationEngine.GrowthSimulationRulesVersion)]
    public async Task CheckpointReopenPreservesSelectedRulesAndContinuation(string rules)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-GrowthTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
            engine.AdvanceUntil(new WorldMinute(1440));
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            SimulationEngine restored;
            await using (var database = await WorldDatabase.OpenAsync(path))
                restored = SimulationEngine.FromPersistenceSnapshot(await database.CreateCheckpointStore().LoadAsync());
            Assert.Equal(rules, restored.SimulationRulesVersion);
            engine.AdvanceUntil(new WorldMinute(2880));
            restored.AdvanceUntil(new WorldMinute(2880));
            Assert.Equal(engine.ComputeSocialFingerprint(), restored.ComputeSocialFingerprint());
            Assert.Equal(engine.ComputeHistoryFingerprint(), restored.ComputeHistoryFingerprint());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RemarriagePreservesFormerPartnerMemoryAndBothHouseholdHistories()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-GrowthTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.GrowthSimulationRulesVersion);
            var citizens = Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
            var people = citizens.Values.OrderBy(c => c.Id.Value).Take(3).ToArray();
            Form(people[0], people[1]);
            Invoke("KillNatural", people[0]);
            Assert.Null(people[1].PartnerId);
            var bereavement = Assert.Single(engine.Memories, m => m.MemoryType == MemoryType.PartnerDied);
            Form(people[1], people[2]);
            Assert.Equal(people[2].Id, people[1].PartnerId);
            Assert.Equal(2, engine.HistoricalEvents.Count(e => e.EventType == HistoricalEventType.PartnershipFormed));
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var restored = SimulationEngine.FromPersistenceSnapshot(await reopened.CreateCheckpointStore().LoadAsync());
            Assert.Equal(engine.ComputeHistoryFingerprint(), restored.ComputeHistoryFingerprint());
            Assert.Contains(bereavement, restored.Memories);
            Assert.Equal(people[2].Id, restored.GetCitizen(people[1].Id)!.PartnerId);

            void Form(Citizen first, Citizen second)
            {
                Invoke("SetRelationship", first.Id, second.Id, 9000, 9000, 9000, 0);
                var pair = RelationshipState.Normalize(first.Id, second.Id);
                var relationship = engine.Relationships.Single(r => r.CitizenAId == pair.A && r.CitizenBId == pair.B);
                Invoke("TryFormPartnership", first, second, relationship);
                Invoke("RecordPartnershipHistory", first, second);
            }
            void Invoke(string method, params object[] args) => typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, args);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
