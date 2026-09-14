using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using LittleAges.Server;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
var serverOptions = ServerOptions.FromConfiguration(builder.Configuration);

builder.WebHost.UseUrls(serverOptions.ListenUrls);
builder.Logging.AddJsonConsole();
builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);
builder.Services.Configure<HostOptions>(options => options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
builder.Services.AddWindowsService(options => options.ServiceName = "Little Ages");
var healthChecks = builder.Services.AddHealthChecks();
healthChecks.AddCheck<SimulationHostHealthCheck>("simulation-host");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton<SimulationHost>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SimulationHost>());

var app = builder.Build();
app.MapHealthChecks("/api/v1/health", new HealthCheckOptions());
app.MapGet("/api/v1/status", (SimulationHost simulationHost) => Results.Ok(simulationHost.Observation.Status));
app.MapGet("/api/v1/world", (SimulationHost simulationHost) =>
{
    var world = simulationHost.Observation.Status.World;
    return world is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(world);
});
app.MapGet("/api/v1/map", (SimulationHost simulationHost) =>
{
    var map = simulationHost.Observation.Map;
    return map is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(map);
});
app.MapGet("/api/v1/citizens", (SimulationHost simulationHost) => Results.Ok(simulationHost.Observation.Citizens));
app.MapGet("/api/v1/citizens/{id}", (string id, SimulationHost simulationHost) =>
{
    if (!long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0 || value.ToString(System.Globalization.CultureInfo.InvariantCulture) != id) return Results.BadRequest();
    var citizen = simulationHost.Observation.Citizens.FirstOrDefault(x => x.CitizenId == id);
    return citizen is null ? Results.NotFound() : Results.Ok(citizen);
});
app.MapGet("/api/v1/settlement", (SimulationHost simulationHost) =>
{
    var settlement = simulationHost.Observation.Settlement;
    return settlement is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(settlement);
});
app.MapGet("/api/v1/structures", (SimulationHost simulationHost) =>
{
    var observation = simulationHost.Observation;
    return observation.Map is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(observation.Structures);
});
app.MapGet("/api/v1/structures/{id}", (string id, SimulationHost simulationHost) =>
{
    if (!long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0 || value.ToString(System.Globalization.CultureInfo.InvariantCulture) != id) return Results.BadRequest();
    var observation = simulationHost.Observation;
    if (observation.Map is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var structure = observation.Structures.FirstOrDefault(x => x.StructureId == id);
    return structure is null ? Results.NotFound() : Results.Ok(structure);
});

app.Run();

public partial class Program
{
}
