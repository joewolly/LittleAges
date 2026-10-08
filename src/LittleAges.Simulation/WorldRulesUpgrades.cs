using System.Security.Cryptography;
using System.Text.Json;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed record WorldRulesUpgradePlan(
    string SourceRules, string TargetRules, string SourceFingerprint, string TargetFingerprint,
    IReadOnlyList<string> Converters, SimulationPersistenceSnapshot Snapshot);

/// <summary>Explicit, deterministic conversions of a checkpoint, without running the simulation.</summary>
public static class WorldRulesUpgrades
{
    private static readonly long[] SettlementIds = [1, 2];
    public static IReadOnlyList<string> SupportedRules { get; } = Array.AsReadOnly(new[]
    {
        SimulationEngine.MigrationSimulationRulesVersion, SimulationEngine.RoadsSimulationRulesVersion,
        SimulationEngine.PlannedSimulationRulesVersion, SimulationEngine.FestivalsSimulationRulesVersion,
        SimulationEngine.NewcomersRulesVersion
    });

    public static bool CanUpgrade(string sourceRules) =>
        SupportedRules.Contains(sourceRules, StringComparer.Ordinal) && sourceRules != SimulationEngine.CurrentSimulationRulesVersion;

    // Operational fingerprint of the entire checkpoint, including the scheduler and retained history.
    // It does not replace any existing simulation fingerprint or become canonical state.
    public static string Fingerprint(SimulationPersistenceSnapshot snapshot) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));

    public static WorldRulesUpgradePlan Plan(SimulationPersistenceSnapshot source,
        string targetRules = SimulationEngine.CurrentSimulationRulesVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = SimulationEngine.FromPersistenceSnapshot(source);
        var first = SupportedRules.ToList().IndexOf(source.SimulationRulesVersion);
        var last = SupportedRules.ToList().IndexOf(targetRules);
        if (first < 0 || last < first)
            throw new NotSupportedException($"No forward rules upgrade from '{source.SimulationRulesVersion}' to '{targetRules}'.");
        var converted = Copy(source, source.SimulationRulesVersion);
        var converters = new List<string>();
        for (var index = first; index < last; index++)
        {
            converted = ConvertStep(converted, SupportedRules[index + 1]);
            _ = SimulationEngine.FromPersistenceSnapshot(converted);
            converters.Add($"{SupportedRules[index]}->{SupportedRules[index + 1]}:v1");
        }
        return new(source.SimulationRulesVersion, targetRules, Fingerprint(source), Fingerprint(converted),
            converters.AsReadOnly(), converted);
    }

    private static SimulationPersistenceSnapshot ConvertStep(SimulationPersistenceSnapshot source, string target)
    {
        // Adding a default must also add an explicit converter; never silently copy unknown rules.
        if (target is not (SimulationEngine.RoadsSimulationRulesVersion or SimulationEngine.PlannedSimulationRulesVersion or
            SimulationEngine.FestivalsSimulationRulesVersion or SimulationEngine.NewcomersRulesVersion))
            throw new NotSupportedException($"No snapshot converter is registered for '{target}'.");
        var migration = source.MigrationState!;
        var living = LivingWorldCodec.Deserialize(source.LivingStateJson!);
        var citizens = source.Citizens.ToArray();
        var events = source.ScheduledEvents.ToArray();
        if (target == SimulationEngine.RoadsSimulationRulesVersion)
        {
            // M14 rebuilds a remaining path from the current tile on reload. Freeze that same
            // old-pathfinder route before enabling the road-aware heuristic.
            var routes = citizens.Where(c => c.IsAlive && c.ActionTarget is not null &&
                events.Any(e => e.Name == CitizenEventNames.MoveStep && IsFor(e, c)))
                .Select(c => new RoadActiveRouteState(c.Id.Value, c.ActionSequence,
                    DeterministicPathfinder.Find(source.World!, c.Location, c.ActionTarget!.Value)
                        ?? throw new InvalidDataException("An existing journey cannot be reconstructed."))).ToArray();
            migration = WithRoads(migration, new RoadNetworkState(1, [], routes));
        }
        if (target == SimulationEngine.FestivalsSimulationRulesVersion)
        {
            var year = source.WorldMinute.ToCalendar().Year;
            living.Festivals = SettlementIds.Where(site => site == 1 || migration.DaughterSettlement is not null)
                .Select(site =>
                {
                    var nextYear = FestivalRules.Start(site, year) > source.WorldMinute.Value ? year : checked(year + 1);
                    var start = FestivalRules.Start(site, nextYear);
                    return new FestivalState { SettlementId = site, Year = nextYear,
                        Location = site == 1 ? source.World!.StartingSite : migration.DaughterSettlement!.Site,
                        StartMinute = start, EndMinute = checked(start + FestivalRules.DurationMinutes) };
                }).ToList();
        }
        if (target == SimulationEngine.NewcomersRulesVersion)
        {
            var year = source.WorldMinute.ToCalendar().Year;
            var summer = new WorldCalendarDate(year, 4, 1, 0, 0).ToWorldMinute().Value;
            living.Newcomers = new NewcomersWorldState { LastAttemptYear = source.WorldMinute.Value >= summer ? year : year - 1 };
            NormalizeFarmReturns(source, citizens, events, ref migration);
        }
        return Copy(source, target, citizens, events, LivingWorldCodec.Serialize(living), migration.ToCanonicalJson());
    }

    private static void NormalizeFarmReturns(SimulationPersistenceSnapshot source, Citizen[] citizens,
        ScheduledEventSnapshot[] events, ref MigrationWorldState migration)
    {
        var grades = new RoadGradeMap(source.World!.Width, source.World.Height);
        foreach (var tile in migration.Roads!.Tiles) grades.SetGrade(tile.Coordinate, tile.Grade);
        var routes = migration.Roads.ActiveRoutes.ToDictionary(x => x.CitizenId);
        foreach (var citizen in citizens.Where(c => c.CurrentAction is CitizenAction.WorkFarm or CitizenAction.HaulHarvest &&
                     c.ActionPhase == CitizenActionPhase.ReturnToStockpile))
        {
            var site = migration.CitizenResidences.Single(x => x.EntityId == citizen.Id.Value).SettlementId;
            var target = site == 1 ? source.World.StartingSite : migration.DaughterSettlement!.Site;
            if (citizen.ActionTarget == target) continue;
            var route = DeterministicPathfinder.Find(source.World, citizen.Location, target, grades)
                ?? throw new InvalidDataException("A farm return cannot reach its resident stockpile.");
            var eventIndex = Array.FindIndex(events, e => e.Name == CitizenEventNames.MoveStep && IsFor(e, citizen));
            if (eventIndex < 0) throw new InvalidDataException("A farm return has no movement event.");
            citizen.ActionTarget = target;
            citizen.ActionStartedMinute = source.WorldMinute;
            citizen.ActionCompletesMinute = source.WorldMinute.Add(TravelCost.Path(route, source.World, grades));
            var due = route.Count == 1 ? source.WorldMinute : source.WorldMinute.Add(TravelCost.Step(source.World, route[0], route[1], grades));
            var old = events[eventIndex];
            events[eventIndex] = old with { Order = new ScheduledEventOrder(due, old.Order.Priority, old.Order.EntitySortKey, old.Order.Sequence) };
            routes[citizen.Id.Value] = new(citizen.Id.Value, citizen.ActionSequence, route);
        }
        var roads = migration.Roads;
        migration = WithRoads(migration, new RoadNetworkState(roads.Version, roads.Tiles,
            routes.Values.OrderBy(x => x.CitizenId).ToArray(), roads.TrailConnectedMinute, roads.RoadConnectedMinute, roads.SeasonWork));
        Array.Sort(events, (a, b) => a.Order.CompareTo(b.Order));
    }

    private static bool IsFor(ScheduledEventSnapshot item, Citizen citizen) =>
        SimulationPersistenceSnapshot.TryReadEventCitizenId(item, out var id) && id == citizen.Id.Value;

    private static MigrationWorldState WithRoads(MigrationWorldState state, RoadNetworkState roads) => new(
        state.Version, state.CitizenResidences, state.HouseholdResidences, state.StructureOwners,
        state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement, state.InTransitParties,
        state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear, roads);

    private static SimulationPersistenceSnapshot Copy(SimulationPersistenceSnapshot s, string rules,
        IReadOnlyList<Citizen>? citizens = null, IReadOnlyList<ScheduledEventSnapshot>? events = null,
        string? living = null, string? migration = null) => new(s.Seed, s.WorldMinute, s.WorldSchemaVersion,
        rules, s.ApplicationVersion, s.WorldConfiguration, s.Counters, events ?? s.ScheduledEvents, s.World,
        citizens ?? s.Citizens, s.CitizenGenerationVersion, s.ResourceStates, s.Settlement, s.SurvivalVersion,
        s.SettlementVersion, s.Structures, s.StructureContributions, s.SocialVersion, s.Relationships, s.Households,
        s.HistoryVersion, s.HistoryState, s.HistoricalEvents, s.HistoricalEventCitizens, s.HistoricalEventStructures,
        s.StatisticsSamples, s.Memories, s.Agriculture, s.Economy, living ?? s.LivingStateJson, migration ?? s.MigrationStateJson);
}
