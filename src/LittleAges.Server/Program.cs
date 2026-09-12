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
app.MapGet("/api/v1/status", (SimulationHost simulationHost) => Results.Ok(simulationHost.Status));
app.MapGet("/api/v1/world", (SimulationHost simulationHost) =>
{
    var world = simulationHost.Status.World;
    return world is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(world);
});

app.Run();

public partial class Program
{
}
