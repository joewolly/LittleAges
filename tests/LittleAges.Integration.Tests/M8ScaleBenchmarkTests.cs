using System.Diagnostics;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Integration.Tests;

public sealed class M8ScaleBenchmarkTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(250)]
    [InlineData(500)]
    [Trait("Category", "Long")]
    public void SyntheticScaleWorldAdvancesAndProjectsDeterministically(int targetLivingCitizens)
    {
        var first = CreateScaleWorld(targetLivingCitizens);
        var second = CreateScaleWorld(targetLivingCitizens);

        Assert.Equal(targetLivingCitizens, first.Engine.LivingPopulation);
        Assert.Equal(targetLivingCitizens, second.Engine.LivingPopulation);
        Assert.Equal(targetLivingCitizens, first.Engine.Citizens.Select(static citizen => citizen.Id.Value).Distinct().Count());
        Assert.True(first.Engine.CounterSnapshot.NextEntityId > first.Engine.Citizens.Max(static citizen => citizen.Id.Value));
        Assert.Equal(first.ExpectedPendingEventCount, first.Engine.PendingEventCount);
        Assert.Empty(first.Engine.Relationships);
        Assert.Empty(first.Engine.Households);
        Assert.All(first.SyntheticCitizenIds, id =>
        {
            var citizen = first.Engine.GetCitizen(new CitizenId(id));
            Assert.NotNull(citizen);
            Assert.Null(citizen!.ParentAId);
            Assert.Null(citizen.ParentBId);
            Assert.Null(citizen.PartnerId);
            Assert.Null(citizen.HouseholdId);
            Assert.True(first.Engine.World.GetTile(citizen.Location).Walkable);
        });
        Assert.Equal(first.Engine.SurvivalFingerprint, second.Engine.SurvivalFingerprint);
        Assert.Equal(first.Engine.PendingEventCount, second.Engine.PendingEventCount);

        var advanceTimer = Stopwatch.StartNew();
        var processedEvents = first.Engine.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        advanceTimer.Stop();

        var snapshotTimer = Stopwatch.StartNew();
        var readSnapshot = first.Engine.CreateReadSnapshot();
        snapshotTimer.Stop();

        var options = new ServerOptions
        {
            DataRoot = Path.GetTempPath(),
            ActiveWorld = "m8-scale-benchmark",
            WorldSeed = new WorldSeed(42),
            ListenUrls = ServerOptions.DefaultListenUrls,
            SimulationMinutesPerSecond = 0
        };
        using var host = new SimulationHost(options, NullLogger<SimulationHost>.Instance);
        typeof(SimulationHost).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, first.Engine);
        var publish = typeof(SimulationHost).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var projectionTimer = Stopwatch.StartNew();
        publish.Invoke(host, [SimulationHostState.Running, null]);
        projectionTimer.Stop();
        var observation = host.Observation;

        var repeatTimer = Stopwatch.StartNew();
        var repeatProcessedEvents = second.Engine.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        repeatTimer.Stop();

        Assert.Equal(targetLivingCitizens, readSnapshot.Citizens.Count(static citizen => citizen.IsAlive));
        Assert.Equal(targetLivingCitizens, observation.Status.LivingPopulation);
        Assert.Equal(targetLivingCitizens, observation.Citizens.Count(static citizen => citizen.IsAlive));
        Assert.Equal(processedEvents, repeatProcessedEvents);
        Assert.Equal(first.Engine.SurvivalFingerprint, second.Engine.SurvivalFingerprint);
        Assert.Equal(first.Engine.HistoryFingerprint, second.Engine.HistoryFingerprint);
        Assert.Equal(first.Engine.CreateReadSnapshot().Citizens, second.Engine.CreateReadSnapshot().Citizens);

        output.WriteLine($"scale={targetLivingCitizens};events={processedEvents};advanceMs={advanceTimer.Elapsed.TotalMilliseconds:F3};snapshotMs={snapshotTimer.Elapsed.TotalMilliseconds:F3};projectionMs={projectionTimer.Elapsed.TotalMilliseconds:F3};repeatAdvanceMs={repeatTimer.Elapsed.TotalMilliseconds:F3};pending={first.Engine.PendingEventCount};living={first.Engine.LivingPopulation};total={first.Engine.TotalCitizenCount}");
    }

    private static ScaleWorld CreateScaleWorld(int targetLivingCitizens)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var citizens = (Dictionary<long, Citizen>)typeof(SimulationEngine)
            .GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(engine)!;
        var counters = (DeterministicCounters)typeof(SimulationEngine)
            .GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(engine)!;
        var scheduleCitizen = typeof(SimulationEngine).GetMethod("ScheduleCitizen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var scheduleSurvival = typeof(SimulationEngine).GetMethod("ScheduleSurvival", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var walkable = engine.World.EnumerateTilesRowMajor().Where(static tile => tile.Walkable).Select(static tile => tile.Coordinate).ToArray();
        var traits = new CitizenTraits(5000, 5000, 5000, 5000, 5000, 5000);
        var skills = new CitizenSkills(5000, 5000, 5000, 5000, 5000, 5000);
        var initialPendingEventCount = engine.PendingEventCount;
        var syntheticCount = targetLivingCitizens - engine.TotalCitizenCount;
        var syntheticCitizenIds = new List<long>(syntheticCount);
        for (var ordinal = 0; ordinal < syntheticCount; ordinal++)
        {
            var id = counters.AllocateCitizenId();
            syntheticCitizenIds.Add(id.Value);
            var citizen = new Citizen(
                id,
                founderOrdinal: null,
                $"Synthetic{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "Scale",
                -18L * WorldCalendar.MinutesPerYear,
                walkable[(ordinal + 20) % walkable.Length],
                traits,
                skills)
            {
                NeedsUpdatedMinute = 0,
                HealthUpdatedMinute = 0
            };
            citizens.Add(id.Value, citizen);
            scheduleCitizen.Invoke(engine, [citizen, CitizenEventNames.Decision, new WorldMinute(0), CitizenEventNames.DecisionPriority]);
            scheduleSurvival.Invoke(engine, [citizen]);
        }

        return new ScaleWorld(engine, syntheticCitizenIds, initialPendingEventCount + (syntheticCount * 2));
    }

    private sealed record ScaleWorld(SimulationEngine Engine, IReadOnlyList<long> SyntheticCitizenIds, int ExpectedPendingEventCount);
}
