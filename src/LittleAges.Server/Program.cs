using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using LittleAges.Server;
using LittleAges.Domain;
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
app.MapGet("/api/v1/citizens/{id}/relationships", (string id, SimulationHost simulationHost) =>
{
    if (!long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0 || value.ToString(System.Globalization.CultureInfo.InvariantCulture) != id) return Results.BadRequest();
    var observation = simulationHost.Observation;
    var citizen = observation.Citizens.FirstOrDefault(x => x.CitizenId == id);
    if (citizen is null) return Results.NotFound();
    var related = observation.Relationships.Where(x => x.CitizenAId.Value == value || x.CitizenBId.Value == value)
        .Select(relationship =>
        {
            var otherId = relationship.CitizenAId.Value == value ? relationship.CitizenBId.Value : relationship.CitizenAId.Value;
            var other = observation.Citizens.Single(x => x.CitizenId == otherId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var family = AreFamily(citizen, other, observation.Citizens);
            var label = RelationshipLabels.Derive(relationship, citizen.PartnerId == other.CitizenId, family);
            return new ServerRelationshipSnapshot(other.CitizenId, other.Name, relationship.Familiarity, relationship.Affinity, relationship.Trust, relationship.Conflict, relationship.LastInteractionMinute, relationship.InteractionCount, label);
        }).OrderBy(x => long.Parse(x.OtherCitizenId, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    return Results.Ok(related);
});
app.MapGet("/api/v1/history", (HttpRequest request, SimulationHost simulationHost) =>
{
    var history = simulationHost.Observation.History;
    if (history is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (!TryParseBound(request.Query["limit"].ToString(), 50, 1, 100, out var limit) ||
        !TryParseBound(request.Query["minimumImportance"].ToString(), 2, 0, 5, out var minimumImportance)) return Results.BadRequest();
    HistoricalEventType? type = null;
    HistoricalEventType parsedEventType = default;
    var typeText = request.Query["eventType"].ToString();
    if (!string.IsNullOrWhiteSpace(typeText) && (!Enum.TryParse<HistoricalEventType>(typeText, true, out parsedEventType) || !Enum.IsDefined(parsedEventType))) return Results.BadRequest();
    if (!string.IsNullOrWhiteSpace(typeText)) type = parsedEventType;
    var citizenId = ParsePositive(request.Query["citizenId"]);
    var familyCitizenId = ParsePositive(request.Query["familyCitizenId"]);
    var structureId = ParsePositive(request.Query["structureId"]);
    var from = ParseNonNegative(request.Query["fromMinute"]);
    var to = ParseNonNegative(request.Query["toMinute"]);
    var beforeEventId = ParsePositive(request.Query["beforeEventId"]);
    if ((request.Query.ContainsKey("citizenId") && citizenId is null) || (request.Query.ContainsKey("familyCitizenId") && familyCitizenId is null) || (request.Query.ContainsKey("structureId") && structureId is null) || (request.Query.ContainsKey("fromMinute") && from is null) || (request.Query.ContainsKey("toMinute") && to is null) || (request.Query.ContainsKey("beforeEventId") && beforeEventId is null)) return Results.BadRequest();
    var familyIds = familyCitizenId is { } familyRoot ? FamilyClosureRules.Closure(familyRoot.ToString(System.Globalization.CultureInfo.InvariantCulture), simulationHost.Observation.Citizens) : null;
    var events = history.Events.Where(x => (int)x.Importance >= minimumImportance && (type is null || x.EventType == type) && (from is null || x.WorldMinute >= from) && (to is null || x.WorldMinute <= to) && (beforeEventId is null || long.Parse(x.EventId, System.Globalization.CultureInfo.InvariantCulture) < beforeEventId) && (citizenId is null || x.CitizenLinks.Any(link => link.CitizenId == citizenId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))) && (familyIds is null || x.CitizenLinks.Any(link => familyIds.Contains(link.CitizenId))) && (structureId is null || x.StructureLinks.Any(link => link.StructureId == structureId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))).OrderByDescending(x => x.WorldMinute).ThenByDescending(x => long.Parse(x.EventId, System.Globalization.CultureInfo.InvariantCulture)).Take(limit).ToArray();
    return Results.Ok(events);
});
app.MapGet("/api/v1/history/{eventId}", (string eventId, SimulationHost simulationHost) =>
{
    if (ParsePositive(eventId) is null) return Results.BadRequest();
    var history = simulationHost.Observation.History;
    if (history is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var eventItem = history.Events.FirstOrDefault(x => x.EventId == eventId);
    return eventItem is null ? Results.NotFound() : Results.Ok(eventItem);
});
app.MapGet("/api/v1/citizens/{id}/biography", (string id, SimulationHost simulationHost) =>
{
    if (ParsePositive(id) is not { } citizenId) return Results.BadRequest();
    var observation = simulationHost.Observation;
    var citizen = observation.Citizens.FirstOrDefault(x => x.CitizenId == id);
    var history = observation.History;
    if (citizen is null) return Results.NotFound();
    if (history is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var events = history.Events.Where(x => (int)x.Importance >= 2 && x.CitizenLinks.Any(link => link.CitizenId == id)).OrderBy(x => x.WorldMinute).ThenBy(x => long.Parse(x.EventId, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    var memories = history.Memories.Where(x => x.CitizenId == id).OrderBy(x => x.CreatedMinute).ThenBy(x => long.Parse(x.EventId, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    return Results.Ok(new ServerCitizenBiographySnapshot(citizen, events, memories, new[] { citizen.ParentAId, citizen.ParentBId }.Where(x => x is not null).Select(x => x!).ToArray(), citizen.PartnerId, citizen.ChildrenIds, citizen.BirthMinute, citizen.DeathMinute, citizen.DeathCause));
});
app.MapGet("/api/v1/citizens/{id}/memories", (string id, SimulationHost simulationHost) =>
{
    if (ParsePositive(id) is null) return Results.BadRequest();
    if (simulationHost.Observation.Citizens.All(x => x.CitizenId != id)) return Results.NotFound();
    var history = simulationHost.Observation.History;
    if (history is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(history.Memories.Where(x => x.CitizenId == id).OrderByDescending(x => x.CreatedMinute).Take(100).ToArray());
});
app.MapGet("/api/v1/statistics", (HttpRequest request, SimulationHost simulationHost) =>
{
    var history = simulationHost.Observation.History;
    if (history is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (!TryParseBound(request.Query["limit"].ToString(), 50, 1, 100, out var limit)) return Results.BadRequest();
    var from = ParseNonNegative(request.Query["fromMinute"]);
    var to = ParseNonNegative(request.Query["toMinute"]);
    if ((request.Query.ContainsKey("fromMinute") && from is null) || (request.Query.ContainsKey("toMinute") && to is null)) return Results.BadRequest();
    return Results.Ok(history.Statistics.Where(x => (from is null || x.WorldMinute >= from) && (to is null || x.WorldMinute <= to)).OrderBy(x => x.WorldMinute).Take(limit).ToArray());
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
app.MapGet("/api/v1/households", (SimulationHost simulationHost) =>
{
    var observation = simulationHost.Observation;
    return Results.Ok(observation.Households.Select(household => ToHouseholdSnapshot(household, observation.Citizens)).OrderBy(x => long.Parse(x.HouseholdId, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
});
app.MapGet("/api/v1/households/{id}", (string id, SimulationHost simulationHost) =>
{
    if (!long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0 || value.ToString(System.Globalization.CultureInfo.InvariantCulture) != id) return Results.BadRequest();
    var observation = simulationHost.Observation;
    var household = observation.Households.FirstOrDefault(x => x.Id.Value == value);
    return household is null ? Results.NotFound() : Results.Ok(ToHouseholdSnapshot(household, observation.Citizens));
});

static ServerHouseholdSnapshot ToHouseholdSnapshot(Household household, IReadOnlyList<ServerCitizenSnapshot> citizens)
{
    var members = citizens.Where(x => x.HouseholdId == household.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).OrderBy(x => long.Parse(x.CitizenId, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    var partners = members.Where(x => x.PartnerId is not null && members.Any(y => y.CitizenId == x.PartnerId)).Select(x => x.CitizenId).OrderBy(x => long.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    var children = members.Where(x => x.ParentAId is not null || x.ParentBId is not null).Select(x => x.CitizenId).OrderBy(x => long.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    return new ServerHouseholdSnapshot(household.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), household.CreatedMinute, household.DissolvedMinute, household.DwellingStructureId?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), Array.AsReadOnly(members.Select(x => x.CitizenId).ToArray()), Array.AsReadOnly(members.Where(x => x.IsAlive).Select(x => x.CitizenId).ToArray()), partners.Length == 2 ? Array.AsReadOnly(partners) : null, Array.AsReadOnly(children));
}

static bool AreFamily(ServerCitizenSnapshot first, ServerCitizenSnapshot second, IReadOnlyList<ServerCitizenSnapshot> citizens)
{
    static HashSet<string> Ancestors(string start, IReadOnlyList<ServerCitizenSnapshot> all)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.TryPop(out var id) && found.Add(id))
        {
            var citizen = all.FirstOrDefault(x => x.CitizenId == id);
            if (citizen?.ParentAId is { } parentA) pending.Push(parentA);
            if (citizen?.ParentBId is { } parentB) pending.Push(parentB);
        }
        return found;
    }

    return Ancestors(first.CitizenId, citizens).Overlaps(Ancestors(second.CitizenId, citizens));
}

app.Run();

static bool TryParseBound(string? value, int fallback, int minimum, int maximum, out int result)
{
    if (string.IsNullOrWhiteSpace(value)) { result = fallback; return true; }
    if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) { result = fallback; return false; }
    result = Math.Clamp(parsed, minimum, maximum);
    return true;
}
static long? ParsePositive(string? value) => long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0 && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value ? parsed : null;
static long? ParseNonNegative(string? value) => long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value ? parsed : null;

public partial class Program
{
}

internal static class FamilyClosureRules
{
    public static HashSet<string> Closure(string rootId, IReadOnlyList<ServerCitizenSnapshot> citizens)
    {
        var byId = citizens.ToDictionary(x => x.CitizenId, StringComparer.Ordinal);
        if (!byId.TryGetValue(rootId, out var root)) return new HashSet<string>(StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal) { rootId };

        void AddIfKnown(string? id)
        {
            if (id is not null && byId.ContainsKey(id)) result.Add(id);
        }

        void AddAncestors(ServerCitizenSnapshot citizen)
        {
            foreach (var parentId in new[] { citizen.ParentAId, citizen.ParentBId }.Where(x => x is not null).Select(x => x!))
            {
                if (!byId.TryGetValue(parentId, out var parent) || !result.Add(parentId)) continue;
                AddAncestors(parent);
            }
        }

        void AddDescendants(string parentId)
        {
            foreach (var child in citizens.Where(x => x.ParentAId == parentId || x.ParentBId == parentId))
            {
                if (!result.Add(child.CitizenId)) continue;
                AddDescendants(child.CitizenId);
            }
        }

        // Traverse only the root's direct sibling group. Ancestor siblings and
        // sibling partners/children are intentionally outside this closure.
        var rootParentIds = new[] { root.ParentAId, root.ParentBId }.Where(x => x is not null).Select(x => x!).ToArray();
        foreach (var sibling in citizens.Where(x => rootParentIds.Any(parentId => x.ParentAId == parentId || x.ParentBId == parentId))) result.Add(sibling.CitizenId);
        AddAncestors(root);
        AddDescendants(rootId);
        AddIfKnown(root.PartnerId);
        return result;
    }
}
