using System.Globalization;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class M7PersistentHostTests
{
    private static readonly string[] ExpectedConfiguredListenHosts = ["0.0.0.0", "localhost"];

    [Fact]
    public void ServerOptionsDefaultsAndConfiguredValuesAreCanonical()
    {
        var root = CreateDataRoot();
        try
        {
            var defaults = ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
            Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data")), defaults.DataRoot);
            Assert.Equal(ServerOptions.DefaultActiveWorld, defaults.ActiveWorld);
            Assert.Equal(new WorldSeed(0), defaults.WorldSeed);
            Assert.Equal(ServerOptions.DefaultListenUrls, defaults.ListenUrls);
            Assert.Equal("127.0.0.1", Assert.Single(defaults.GetListenUris()).Host);
            Assert.Equal(10d, defaults.SimulationMinutesPerSecond);
            Assert.Equal(ServerOptions.DefaultCheckpointSimulationMinutes, defaults.CheckpointSimulationMinutes);
            Assert.Equal(ServerOptions.DefaultCheckpointMinimumRealSeconds, defaults.CheckpointMinimumRealSeconds);
            Assert.Equal(ServerOptions.DefaultCheckpointRetryCount, defaults.CheckpointRetryCount);
            Assert.Equal(ServerOptions.DefaultCheckpointRetryDelaySeconds, defaults.CheckpointRetryDelaySeconds);
            Assert.Equal(ServerOptions.DefaultBrowserUpdateIntervalMilliseconds, defaults.BrowserUpdateIntervalMilliseconds);
            Assert.Equal(ServerOptions.DefaultObserverStreamIntervalMilliseconds, defaults.ObserverStreamIntervalMilliseconds);

            var configured = ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRoot"] = root,
                ["ActiveWorld"] = "configured-world",
                ["WorldSeed"] = "18446744073709551615",
                ["ListenUrls"] = "http://0.0.0.0:0;https://localhost:5275",
                ["SimulationMinutesPerSecond"] = "0",
                ["CheckpointSimulationMinutes"] = "0",
                ["CheckpointMinimumRealSeconds"] = "0",
                ["CheckpointRetryCount"] = "0",
                ["CheckpointRetryDelaySeconds"] = "0",
                ["BrowserUpdateIntervalMilliseconds"] = "1",
                ["ObserverStreamIntervalMilliseconds"] = "2"
            }).Build());
            Assert.Equal(Path.GetFullPath(root), configured.DataRoot);
            Assert.Equal("configured-world", configured.ActiveWorld);
            Assert.Equal(new WorldSeed(ulong.MaxValue), configured.WorldSeed);
            Assert.Equal(0d, configured.SimulationMinutesPerSecond);
            Assert.Equal(0, configured.CheckpointSimulationMinutes);
            Assert.Equal(0, configured.CheckpointMinimumRealSeconds);
            Assert.Equal(0, configured.CheckpointRetryCount);
            Assert.Equal(0, configured.CheckpointRetryDelaySeconds);
            Assert.Equal(1, configured.BrowserUpdateIntervalMilliseconds);
            Assert.Equal(2, configured.ObserverStreamIntervalMilliseconds);
            Assert.Equal(ExpectedConfiguredListenHosts, configured.GetListenUris().Select(uri => uri.Host));
        }
        finally
        {
            CleanupDataRoot(root);
        }
    }

    [Theory]
    [InlineData("SimulationMinutesPerSecond", "-1")]
    [InlineData("SimulationMinutesPerSecond", "1001")]
    [InlineData("SimulationMinutesPerSecond", "NaN")]
    [InlineData("SimulationMinutesPerSecond", "Infinity")]
    [InlineData("CheckpointSimulationMinutes", "-1")]
    [InlineData("CheckpointMinimumRealSeconds", "-1")]
    [InlineData("CheckpointRetryCount", "-1")]
    [InlineData("CheckpointRetryCount", "2147483647")]
    [InlineData("CheckpointRetryDelaySeconds", "-1")]
    [InlineData("BrowserUpdateIntervalMilliseconds", "0")]
    [InlineData("ObserverStreamIntervalMilliseconds", "0")]
    [InlineData("ListenUrls", "")]
    [InlineData("ListenUrls", "file:///tmp/not-http")]
    public void ServerOptionsRejectInvalidConfiguredValues(string key, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["DataRoot"] = CreateDataRoot(),
            ["ActiveWorld"] = "invalid-test-world",
            ["WorldSeed"] = "17",
            [key] = value
        };
        try
        {
            Assert.ThrowsAny<ArgumentException>(() => ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        }
        finally
        {
            CleanupDataRoot(values["DataRoot"]!);
        }
    }

    [Fact]
    public async Task OperationalControlsUseHostCommandsWithoutChangingCanonicalState()
    {
        var root = CreateDataRoot();
        IHost? host = null;
        try
        {
            host = BuildHost(root, "operational-controls", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0);
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            Assert.True(simulationHost.Status.Paused);
            Assert.Equal(0d, simulationHost.Status.OperationalSpeed);

            await simulationHost.AdvanceOperationalForTestingAsync(1);
            Assert.Equal(0, simulationHost.Status.WorldMinute);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => simulationHost.RequestOperationalSpeedAsync(ServerOptions.MaximumSimulationMinutesPerSecond + 1));
            var changed = await simulationHost.RequestOperationalSpeedAsync(5);
            Assert.True(changed.Paused);
            Assert.Equal(5d, changed.OperationalSpeed);
            var resumed = await simulationHost.RequestResumeAsync();
            Assert.False(resumed.Paused);
            Assert.Equal(5d, resumed.OperationalSpeed);
            await simulationHost.AdvanceOperationalForTestingAsync(1);
            var runningMinute = simulationHost.Status.WorldMinute;
            Assert.True(runningMinute >= 1);

            var paused = await simulationHost.RequestPauseAsync();
            Assert.True(paused.Paused);
            var pausedMinute = simulationHost.Status.WorldMinute;
            await simulationHost.AdvanceOperationalForTestingAsync(1);
            Assert.Equal(pausedMinute, simulationHost.Status.WorldMinute);
            await simulationHost.RequestCheckpointAsync();
            var beforeControls = await LoadSnapshotAsync(Path.Combine(root, "operational-controls.db"));
            await simulationHost.RequestOperationalSpeedAsync(6);
            await simulationHost.RequestPauseAsync();
            await simulationHost.RequestCheckpointAsync();
            var afterSnapshot = await LoadSnapshotAsync(Path.Combine(root, "operational-controls.db"));
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(beforeControls).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(afterSnapshot).HistoryFingerprint);
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(beforeControls).SurvivalFingerprint, SimulationEngine.FromPersistenceSnapshot(afterSnapshot).SurvivalFingerprint);
        }
        finally
        {
            if (host is not null) _ = await Record.ExceptionAsync(() => host.StopAsync());
            host?.Dispose();
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public void CheckpointRetryCountMaxMinusOneAllowsAnInt32AttemptCount()
    {
        var root = CreateDataRoot();
        try
        {
            var options = ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRoot"] = root,
                ["ActiveWorld"] = "max-retry-world",
                ["WorldSeed"] = "17",
                ["CheckpointRetryCount"] = (int.MaxValue - 1).ToString(CultureInfo.InvariantCulture)
            }).Build());

            Assert.Equal(int.MaxValue - 1, options.CheckpointRetryCount);
        }
        finally
        {
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task PeriodicCheckpointUsesSimulationBoundaryAndRealMinimumBatching()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "periodic-boundary", checkpointSimulationMinutes: 360, checkpointMinimumRealSeconds: 0);
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var path = Path.Combine(root, "periodic-boundary.db");

            await simulationHost.AdvanceForTestingAsync(359);
            var beforeBoundary = await LoadSnapshotAsync(path);
            Assert.Equal(0, beforeBoundary.WorldMinute.Value);

            await simulationHost.AdvanceForTestingAsync(1);
            var atBoundary = await LoadSnapshotAsync(path);
            Assert.Equal(360, atBoundary.WorldMinute.Value);

            await host.StopAsync();
        }
        finally
        {
            await StopAndDisposeAsync(host);
            CleanupDataRoot(root);
        }

        var batchedRoot = CreateDataRoot();
        using var batchedHost = BuildHost(batchedRoot, "real-minimum", checkpointSimulationMinutes: 1, checkpointMinimumRealSeconds: int.MaxValue);
        try
        {
            await batchedHost.StartAsync();
            var simulationHost = batchedHost.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var path = Path.Combine(batchedRoot, "real-minimum.db");
            await simulationHost.AdvanceForTestingAsync(5);
            var persisted = await LoadSnapshotAsync(path);
            Assert.Equal(0, persisted.WorldMinute.Value);
            Assert.Equal(5, simulationHost.Status.WorldMinute);
        }
        finally
        {
            await StopAndDisposeAsync(batchedHost);
            CleanupDataRoot(batchedRoot);
        }
    }

    [Fact]
    public async Task CheckpointRetryRecoversAfterOneInjectedAttemptAndPreservesCanonicalState()
    {
        var root = CreateDataRoot();
        var path = Path.Combine(root, "retry-recovery.db");
        using var host = BuildHost(root, "retry-recovery", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0, checkpointRetryCount: 1);
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var before = await LoadSnapshotAsync(path);
            var degradedObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            simulationHost.CheckpointAttemptFailureHookForTesting = (kind, attempt) =>
            {
                if (kind == "explicit" && attempt == 2 && simulationHost.Status.PersistenceState == PersistenceState.Degraded && simulationHost.Status.ConsecutiveCheckpointFailures == 1)
                {
                    degradedObserved.TrySetResult(true);
                }
                return null;
            };
            simulationHost.FailNextCheckpointAttemptsForTesting(1);

            var result = await simulationHost.RequestCheckpointAsync();
            Assert.True(result.Succeeded);
            await degradedObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(PersistenceState.Healthy, simulationHost.Status.PersistenceState);
            Assert.Equal(0, simulationHost.Status.ConsecutiveCheckpointFailures);
            Assert.Equal(0, simulationHost.Status.LastSuccessfulCheckpointWorldMinute);
            var after = await LoadSnapshotAsync(path);
            AssertSnapshotsEqual(before, after);
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => host.StopAsync());
            host.Dispose();
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task CheckpointRetryExhaustionFaultsHostAndRetainsLastCommittedCheckpoint()
    {
        var root = CreateDataRoot();
        var path = Path.Combine(root, "retry-exhaustion.db");
        using var host = BuildHost(root, "retry-exhaustion", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0, checkpointRetryCount: 1);
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var before = await LoadSnapshotAsync(path);
            var stopping = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stoppingRegistration = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult(true));
            simulationHost.FailNextCheckpointAttemptsForTesting(2);

            await Assert.ThrowsAsync<InvalidOperationException>(() => simulationHost.RequestCheckpointAsync());
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForStateAsync(simulationHost, SimulationHostState.Faulted);
            Assert.Equal(PersistenceState.Faulted, simulationHost.Status.PersistenceState);
            Assert.Equal(2, simulationHost.Status.ConsecutiveCheckpointFailures);
            Assert.Contains("Controlled explicit checkpoint attempt failure", simulationHost.Status.Error, StringComparison.Ordinal);

            var after = await LoadSnapshotAsync(path);
            AssertSnapshotsEqual(before, after);
            Assert.Equal(before.WorldMinute, after.WorldMinute);
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => host.StopAsync());
            host.Dispose();
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task InitialCheckpointRetryKeepsHostStartingUntilItIsRunning()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "initial-retry", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0, checkpointRetryCount: 1);
        try
        {
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            var degradedObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            simulationHost.CheckpointAttemptFailureHookForTesting = (kind, attempt) =>
            {
                if (kind == "initial" && attempt == 2)
                {
                    Assert.Equal(SimulationHostState.Starting, simulationHost.Status.State);
                    Assert.Equal(PersistenceState.Degraded, simulationHost.Status.PersistenceState);
                    degradedObserved.TrySetResult(true);
                }

                return null;
            };
            simulationHost.FailNextCheckpointAttemptsForTesting(1);

            await host.StartAsync();
            await simulationHost.WaitForRunningForTestingAsync();
            await degradedObserved.Task;

            Assert.Equal(SimulationHostState.Running, simulationHost.Status.State);
            Assert.Equal(PersistenceState.Healthy, simulationHost.Status.PersistenceState);
            Assert.Equal(0, simulationHost.Status.LastSuccessfulCheckpointWorldMinute);
            Assert.Equal(0, simulationHost.Status.ConsecutiveCheckpointFailures);
        }
        finally
        {
            await StopAndDisposeAsync(host);
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task GracefulStopDrainsAcceptedCommandsAndRejectsCommandsAfterStopBegins()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "shutdown-drain", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0);
        var releaseCheckpoint = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            var checkpointStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            simulationHost.CheckpointAttemptFailureHookForTesting = (kind, attempt) =>
            {
                if (kind == "explicit" && attempt == 1)
                {
                    checkpointStarted.TrySetResult(true);
                    releaseCheckpoint.Task.GetAwaiter().GetResult();
                }

                return null;
            };

            var checkpoint = simulationHost.RequestCheckpointAsync();
            await checkpointStarted.Task;
            var acceptedAdvance = simulationHost.AdvanceForTestingAsync(7);
            Assert.False(acceptedAdvance.IsCompleted);

            var stop = host.StopAsync();
            releaseCheckpoint.TrySetResult(true);
            await checkpoint;
            await acceptedAdvance;
            await stop;

            Assert.Equal(SimulationHostState.Stopping, simulationHost.Status.State);
            Assert.Equal(PersistenceState.Healthy, simulationHost.Status.PersistenceState);
            Assert.Equal(7, simulationHost.Status.LastSuccessfulCheckpointWorldMinute);
            Assert.Equal(7, (await LoadSnapshotAsync(Path.Combine(root, "shutdown-drain.db"))).WorldMinute.Value);
            await Assert.ThrowsAsync<InvalidOperationException>(() => simulationHost.RequestCheckpointAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => simulationHost.AdvanceForTestingAsync(1));
        }
        finally
        {
            releaseCheckpoint.TrySetResult(true);
            await StopAndDisposeAsync(host);
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task FinalCheckpointRetryRecoversWithStoppingHealthyMetadata()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "final-recovery", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0, checkpointRetryCount: 1);
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            await simulationHost.AdvanceForTestingAsync(7);
            var priorCheckpointUtc = simulationHost.Status.LastSuccessfulCheckpointUtc;
            var degradedObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            simulationHost.CheckpointAttemptFailureHookForTesting = (kind, attempt) =>
            {
                if (kind == "final" && attempt == 2)
                {
                    Assert.Equal(SimulationHostState.Stopping, simulationHost.Status.State);
                    Assert.Equal(PersistenceState.Degraded, simulationHost.Status.PersistenceState);
                    Assert.Equal(1, simulationHost.Status.ConsecutiveCheckpointFailures);
                    degradedObserved.TrySetResult(true);
                }

                return null;
            };
            simulationHost.FailNextFinalCheckpointForTesting();

            await host.StopAsync();
            await degradedObserved.Task;

            var status = simulationHost.Status;
            Assert.Equal(SimulationHostState.Stopping, status.State);
            Assert.Equal(PersistenceState.Healthy, status.PersistenceState);
            Assert.Equal(7, status.LastSuccessfulCheckpointWorldMinute);
            Assert.True(status.LastSuccessfulCheckpointUtc > priorCheckpointUtc);
            Assert.Equal(0, status.ConsecutiveCheckpointFailures);
        }
        finally
        {
            await StopAndDisposeAsync(host);
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task FinalCheckpointExhaustionPublishesStoppingDegradedBeforeFaulting()
    {
        var root = CreateDataRoot();
        using var host = BuildHost(root, "final-exhaustion", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0, checkpointRetryCount: 1);
        try
        {
            await host.StartAsync();
            var simulationHost = host.Services.GetRequiredService<SimulationHost>();
            await simulationHost.WaitForRunningForTestingAsync();
            await simulationHost.AdvanceForTestingAsync(3);
            var degradedObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            simulationHost.CheckpointAttemptFailureHookForTesting = (kind, attempt) =>
            {
                if (kind == "final")
                {
                    if (attempt == 2)
                    {
                        Assert.Equal(SimulationHostState.Stopping, simulationHost.Status.State);
                        Assert.Equal(PersistenceState.Degraded, simulationHost.Status.PersistenceState);
                        Assert.Equal(1, simulationHost.Status.ConsecutiveCheckpointFailures);
                        degradedObserved.TrySetResult(true);
                    }

                    return new InvalidOperationException("Controlled final checkpoint failure.");
                }

                return null;
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => host.StopAsync());
            await degradedObserved.Task;

            Assert.Equal(SimulationHostState.Faulted, simulationHost.Status.State);
            Assert.Equal(PersistenceState.Faulted, simulationHost.Status.PersistenceState);
            Assert.Equal(2, simulationHost.Status.ConsecutiveCheckpointFailures);
            Assert.Equal(0, (await LoadSnapshotAsync(Path.Combine(root, "final-exhaustion.db"))).WorldMinute.Value);
        }
        finally
        {
            await StopAndDisposeAsync(host);
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task GracefulStopRestoresCompleteSnapshotAndContinuesLikeControl()
    {
        var root = CreateDataRoot();
        var path = Path.Combine(root, "graceful.db");
        SimulationPersistenceSnapshot checkpoint;
        try
        {
            using (var firstHost = BuildHost(root, "graceful", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0))
            {
                await firstHost.StartAsync();
                var simulationHost = firstHost.Services.GetRequiredService<SimulationHost>();
                await simulationHost.WaitForRunningForTestingAsync();
                await simulationHost.AdvanceForTestingAsync(25);
                Assert.True((await simulationHost.RequestCheckpointAsync()).Succeeded);
                checkpoint = await LoadSnapshotAsync(path);
                await firstHost.StopAsync();
            }

            var afterStop = await LoadSnapshotAsync(path);
            AssertSnapshotsEqual(checkpoint, afterStop);

            using (var restoredHost = BuildHost(root, "graceful", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0))
            {
                await restoredHost.StartAsync();
                var simulationHost = restoredHost.Services.GetRequiredService<SimulationHost>();
                await simulationHost.WaitForRunningForTestingAsync();
                Assert.Equal(checkpoint.WorldMinute.Value, simulationHost.Status.WorldMinute);
                await simulationHost.AdvanceForTestingAsync(5);
                await simulationHost.RequestCheckpointAsync();
                await restoredHost.StopAsync();
            }

            var restored = await LoadSnapshotAsync(path);
            var control = SimulationEngine.FromPersistenceSnapshot(checkpoint);
            control.AdvanceUntil(new WorldMinute(30));
            AssertSnapshotsEqual(control.CreatePersistenceSnapshot(), restored);
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(control.CreatePersistenceSnapshot()).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(restored).HistoryFingerprint);
        }
        finally
        {
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task AbruptCommandFailureLeavesLastCheckpointAndResumesWithoutDuplicateHistory()
    {
        var root = CreateDataRoot();
        var path = Path.Combine(root, "abrupt.db");
        SimulationPersistenceSnapshot checkpoint;
        try
        {
            using (var failedHost = BuildHost(root, "abrupt", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0))
            {
                await failedHost.StartAsync();
                var simulationHost = failedHost.Services.GetRequiredService<SimulationHost>();
                await simulationHost.WaitForRunningForTestingAsync();
                await simulationHost.AdvanceForTestingAsync(10);
                await simulationHost.RequestCheckpointAsync();
                checkpoint = await LoadSnapshotAsync(path);
                var stopping = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = failedHost.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult(true));

                await Assert.ThrowsAsync<InvalidOperationException>(() => simulationHost.TriggerCommandLoopFailureForTestingAsync());
                await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitForStateAsync(simulationHost, SimulationHostState.Faulted);
                Assert.Equal(SimulationHostState.Faulted, simulationHost.Status.State);
                Assert.Equal(checkpoint.WorldMinute.Value, (await LoadSnapshotAsync(path)).WorldMinute.Value);
                _ = await Record.ExceptionAsync(() => failedHost.StopAsync());
            }

            var afterFailure = await LoadSnapshotAsync(path);
            AssertSnapshotsEqual(checkpoint, afterFailure);

            using (var resumedHost = BuildHost(root, "abrupt", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0))
            {
                await resumedHost.StartAsync();
                var simulationHost = resumedHost.Services.GetRequiredService<SimulationHost>();
                await simulationHost.WaitForRunningForTestingAsync();
                Assert.Equal(checkpoint.WorldMinute.Value, simulationHost.Status.WorldMinute);
                Assert.Equal(checkpoint.HistoricalEvents.Select(item => item.Id.Value), simulationHost.Observation.History!.Events.Select(item => long.Parse(item.EventId, CultureInfo.InvariantCulture)));
                await resumedHost.StopAsync();
            }
        }
        finally
        {
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task ClosedDatabaseBackupOpensAndPreservesCanonicalAndHistoryFingerprints()
    {
        var root = CreateDataRoot();
        var path = Path.Combine(root, "backup.db");
        var backupPath = Path.Combine(root, "backup-copy.db");
        try
        {
            SimulationPersistenceSnapshot source;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
                engine.AdvanceUntil(new WorldMinute(20));
                source = engine.CreatePersistenceSnapshot();
                await database.CreateCheckpointStore().CheckpointAsync(source, new DateTime(2026, 9, 16, 2, 0, 0, DateTimeKind.Utc));
            }

            File.Copy(path, backupPath, overwrite: false);
            await using var backup = await WorldDatabase.OpenAsync(backupPath);
            var loaded = await backup.CreateCheckpointStore().LoadAsync();
            AssertSnapshotsEqual(source, loaded);
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(source).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(loaded).HistoryFingerprint);
        }
        finally
        {
            CleanupDataRoot(root);
        }
    }

    [Fact]
    public async Task IdenticalShortRunsHaveSameCanonicalFingerprintWithoutBrowserClients()
    {
        var roots = new[] { CreateDataRoot(), CreateDataRoot() };
        var snapshots = new List<SimulationPersistenceSnapshot>();
        try
        {
            foreach (var root in roots)
            {
                var host = BuildHost(root, "same-run", checkpointSimulationMinutes: 0, checkpointMinimumRealSeconds: 0, seed: 42);
                try
                {
                    await host.StartAsync();
                    var simulationHost = host.Services.GetRequiredService<SimulationHost>();
                    await simulationHost.WaitForRunningForTestingAsync();
                    await simulationHost.AdvanceForTestingAsync(20);
                    await simulationHost.RequestCheckpointAsync();
                    snapshots.Add(await LoadSnapshotAsync(Path.Combine(root, "same-run.db")));
                }
                finally
                {
                    _ = await Record.ExceptionAsync(() => host.StopAsync());
                    host.Dispose();
                }
            }

            AssertSnapshotsEqual(snapshots[0], snapshots[1]);
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(snapshots[0]).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(snapshots[1]).HistoryFingerprint);
        }
        finally
        {
            foreach (var root in roots) CleanupDataRoot(root);
        }
    }

    private static IHost BuildHost(string root, string world, int checkpointSimulationMinutes, int checkpointMinimumRealSeconds, int checkpointRetryCount = 0, ulong seed = 17)
    {
        var builder = Host.CreateApplicationBuilder();
        var options = new ServerOptions
        {
            DataRoot = root,
            ActiveWorld = world,
            WorldSeed = new WorldSeed(seed),
            ListenUrls = ServerOptions.DefaultListenUrls,
            SimulationMinutesPerSecond = 0,
            CheckpointSimulationMinutes = checkpointSimulationMinutes,
            CheckpointMinimumRealSeconds = checkpointMinimumRealSeconds,
            CheckpointRetryCount = checkpointRetryCount,
            CheckpointRetryDelaySeconds = 0,
            BrowserUpdateIntervalMilliseconds = 1
        };
        builder.Services.Configure<HostOptions>(hostOptions => hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SimulationHost>();
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SimulationHost>());
        builder.Services.AddLogging();
        return builder.Build();
    }

    private static async Task<SimulationPersistenceSnapshot> LoadSnapshotAsync(string path)
    {
        await using var database = await WorldDatabase.OpenAsync(path);
        return await database.CreateCheckpointStore().LoadAsync();
    }

    private static async Task WaitForStateAsync(SimulationHost host, SimulationHostState expected)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            if (host.Status.State == expected) return;
            await Task.Yield();
        }
        throw new InvalidOperationException($"The simulation host did not reach {expected} state.");
    }

    private static void AssertSnapshotsEqual(SimulationPersistenceSnapshot expected, SimulationPersistenceSnapshot actual)
    {
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.WorldMinute, actual.WorldMinute);
        Assert.Equal(expected.WorldSchemaVersion, actual.WorldSchemaVersion);
        Assert.Equal(expected.SimulationRulesVersion, actual.SimulationRulesVersion);
        Assert.Equal(expected.ApplicationVersion, actual.ApplicationVersion);
        Assert.Equal(expected.WorldConfiguration, actual.WorldConfiguration);
        Assert.Equal(expected.Counters, actual.Counters);
        Assert.Equal(expected.ScheduledEvents, actual.ScheduledEvents);
        Assert.Equal(expected.World!.Fingerprint, actual.World!.Fingerprint);
        Assert.Equal(expected.CitizenGenerationVersion, actual.CitizenGenerationVersion);
        Assert.Equal(expected.Citizens, actual.Citizens);
        Assert.Equal(expected.ResourceStates, actual.ResourceStates);
        Assert.Equal(expected.Settlement, actual.Settlement);
        Assert.Equal(expected.SurvivalVersion, actual.SurvivalVersion);
        Assert.Equal(expected.SettlementVersion, actual.SettlementVersion);
        Assert.Equal(expected.Structures.Select(StructureKey), actual.Structures.Select(StructureKey));
        Assert.Equal(expected.StructureContributions.Select(ContributionKey), actual.StructureContributions.Select(ContributionKey));
        Assert.Equal(expected.SocialVersion, actual.SocialVersion);
        Assert.Equal(expected.Relationships, actual.Relationships);
        Assert.Equal(expected.Households.Select(HouseholdKey), actual.Households.Select(HouseholdKey));
        Assert.Equal(expected.HistoryVersion, actual.HistoryVersion);
        Assert.Equal(expected.HistoryState, actual.HistoryState);
        Assert.Equal(expected.HistoricalEvents, actual.HistoricalEvents);
        Assert.Equal(expected.HistoricalEventCitizens, actual.HistoricalEventCitizens);
        Assert.Equal(expected.HistoricalEventStructures, actual.HistoricalEventStructures);
        Assert.Equal(expected.StatisticsSamples, actual.StatisticsSamples);
        Assert.Equal(expected.Memories, actual.Memories);
    }

    private static string StructureKey(Structure value) => string.Join(":", value.Id.Value, (int)value.Type, (int)value.Status, value.Location.X, value.Location.Y, value.ConstructionStartedMinute, value.CompletedMinute, value.RequiredWood, value.DeliveredWood, value.RequiredStone, value.DeliveredStone, value.RequiredWork, value.CompletedWork);
    private static string ContributionKey(StructureContribution value) => string.Join(":", value.StructureId.Value, value.CitizenId.Value, value.ConstructionWork, value.WoodDelivered, value.StoneDelivered);
    private static string HouseholdKey(Household value) => string.Join(":", value.Id.Value, value.CreatedMinute, value.DissolvedMinute, value.DwellingStructureId?.Value);

    private static async Task StopAndDisposeAsync(IHost host)
    {
        _ = await Record.ExceptionAsync(() => host.StopAsync());
        host.Dispose();
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "LittleAges-M7-Integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDataRoot(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
