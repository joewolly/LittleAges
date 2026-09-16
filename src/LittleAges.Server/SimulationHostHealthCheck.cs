using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LittleAges.Server;

/// <summary>Reports simulation readiness without inspecting mutable engine or database state.</summary>
public sealed class SimulationHostHealthCheck(SimulationHost simulationHost) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = cancellationToken;
        var status = simulationHost.Observation.Status;
        if (status.PersistenceState == PersistenceState.Faulted)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(status.Error ?? "Simulation persistence is faulted."));
        }

        if (status.PersistenceState == PersistenceState.Degraded)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Simulation persistence is degraded."));
        }

        return Task.FromResult(status.State switch
        {
            SimulationHostState.Running => HealthCheckResult.Healthy("The simulation host is running."),
            SimulationHostState.Starting => HealthCheckResult.Degraded("The simulation host is starting."),
            SimulationHostState.Stopping => HealthCheckResult.Degraded("The simulation host is stopping."),
            SimulationHostState.Faulted => HealthCheckResult.Unhealthy(status.Error ?? "The simulation host faulted."),
            _ => HealthCheckResult.Unhealthy("The simulation host has an unknown state.")
        });
    }
}
