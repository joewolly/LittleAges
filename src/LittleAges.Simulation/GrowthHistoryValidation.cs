using System.Text.Json;
using LittleAges.Domain;

namespace LittleAges.Simulation;

internal static class GrowthHistoryValidation
{
    public static void ValidatePartnerships(IReadOnlyList<HistoricalEvent> events,
        IReadOnlyList<HistoricalEventCitizenLink> links, IReadOnlyList<Citizen> citizens)
    {
        var citizensById = citizens.ToDictionary(c => c.Id.Value);
        var linksByEvent = links.ToLookup(l => l.HistoricalEventId.Value);
        var active = new Dictionary<long, long>();
        var atDeath = new Dictionary<long, long?>();
        var formations = new Dictionary<long, (long Minute, long First, long Second)>();
        foreach (var item in events)
        {
            if (item.EventType == HistoricalEventType.PartnershipFormed)
            {
                var pair = linksByEvent[item.Id.Value].Where(l => l.Role == "partner").Select(l => l.CitizenId.Value).Order().ToArray();
                if (pair.Length != 2 || active.ContainsKey(pair[0]) || active.ContainsKey(pair[1]) || atDeath.ContainsKey(pair[0]) || atDeath.ContainsKey(pair[1]) ||
                    pair.Any(id => (item.WorldMinute - citizensById[id].BirthMinute) / WorldCalendar.MinutesPerYear < 18) ||
                    GrowthKinship.AreCloseKin(new CitizenId(pair[0]), new CitizenId(pair[1]), citizensById))
                    throw new ArgumentException("Growth partnership history is overlapping, underage, or close kin.", nameof(events));
                active.Add(pair[0], pair[1]);
                active.Add(pair[1], pair[0]);
                using var payload = JsonDocument.Parse(item.PayloadJson);
                var household = long.Parse(payload.RootElement.GetProperty("householdId").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                if (!formations.TryAdd(household, (item.WorldMinute, pair[0], pair[1]))) throw new ArgumentException("A household has duplicate partnership formation.", nameof(events));
            }
            else if (item.EventType == HistoricalEventType.HouseholdCreated)
            {
                using var payload = JsonDocument.Parse(item.PayloadJson);
                var household = long.Parse(payload.RootElement.GetProperty("householdId").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var pair = linksByEvent[item.Id.Value].Where(l => l.Role == "member").Select(l => l.CitizenId.Value).Order().ToArray();
                if (!formations.TryGetValue(household, out var formation) || formation.Minute != item.WorldMinute || !pair.SequenceEqual(new[] { formation.First, formation.Second }))
                    throw new ArgumentException("Household history requires its preceding partnership.", nameof(events));
            }
            else if (item.EventType == HistoricalEventType.CitizenDied)
            {
                var id = linksByEvent[item.Id.Value].Single(l => l.Role == "subject").CitizenId.Value;
                var partner = active.Remove(id, out var previous) ? (long?)previous : null;
                atDeath.Add(id, partner);
                if (partner is { } survivor) active.Remove(survivor);
            }
        }
        foreach (var citizen in citizens)
        {
            var expected = citizen.IsAlive ? (active.TryGetValue(citizen.Id.Value, out var partner) ? (long?)partner : null) : atDeath.GetValueOrDefault(citizen.Id.Value);
            if (citizen.PartnerId?.Value != expected) throw new ArgumentException("Current partnership does not match its recorded transitions.", nameof(citizens));
        }
    }
}
