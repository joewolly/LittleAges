using System.Globalization;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Simulation.Tests;

public sealed class M5LongHorizonAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public M5LongHorizonAcceptanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Seed42FirstBirthTraceFindsABirthWithinTenYears()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion, captureFamilyCheckDiagnostics: true);
        var limit = new WorldMinute(10L * WorldCalendar.MinutesPerYear);
        while (engine.NextScheduledEventMinute is { } due && due <= limit && engine.Citizens.All(citizen => citizen.FounderOrdinal is not null)) Assert.True(engine.ProcessNextEvent());

        var birth = engine.Citizens.Where(citizen => citizen.FounderOrdinal is null).OrderBy(citizen => citizen.BirthMinute).FirstOrDefault();
        Assert.NotNull(birth);
        Assert.InRange(birth!.BirthMinute, 0, limit.Value);
        _output.WriteLine($"seed=42 first-birth-minute={birth.BirthMinute} child={birth.Id.Value} family-checks={engine.FamilyCheckDiagnostics.Count} food={engine.Settlement.FoodStored} social={engine.SocialFingerprint}");
    }

    [Fact]
    public void Seed42DailyFamilyCheckCounterfactualAuditIsStateNeutralThroughTenYears()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion, captureFamilyCheckDiagnostics: true);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var limit = new WorldMinute(10L * WorldCalendar.MinutesPerYear);
        while (engine.NextScheduledEventMinute is { } due && due <= limit)
        {
            Assert.True(engine.ProcessNextEvent());
        }
        engine.AdvanceUntil(limit);
        stopwatch.Stop();

        var checks = engine.FamilyCheckDiagnostics;
        var opportunities = checks.SelectMany(check => check.Opportunities).ToArray();
        var canonicalRolls = opportunities.Where(opportunity => opportunity.Chance is not null && opportunity.Draw is not null).ToArray();
        var winningRolls = canonicalRolls.Where(opportunity => opportunity.Draw < opportunity.Chance).ToArray();
        var beforeFingerprint = engine.SocialFingerprint;
        var beforeCounters = engine.CounterSnapshot;
        var beforeDiagnostics = engine.FamilyCheckDiagnostics.Count;
        var directCapture = engine.CaptureFamilyCheckCounterfactualDiagnostic();
        Assert.Equal(beforeFingerprint, engine.SocialFingerprint);
        Assert.Equal(beforeCounters, engine.CounterSnapshot);
        Assert.Equal(beforeDiagnostics, engine.FamilyCheckDiagnostics.Count);
        Assert.Equal(checks[^1].Minute, engine.CurrentMinute);
        Assert.Equal(10 * WorldCalendar.DaysPerYear, checks.Count);
        Assert.NotEmpty(canonicalRolls);
        Assert.All(canonicalRolls, opportunity =>
        {
            Assert.InRange(opportunity.Chance!.Value, 1, 50);
            Assert.InRange(opportunity.Draw!.Value, 0, 9999);
        });

        _output.WriteLine($"seed=42 family-checks={checks.Count} active-household-records={opportunities.Length} canonical-rolls={canonicalRolls.Length} winning-rolls={winningRolls.Length} direct-capture-records={directCapture.Opportunities.Count} state-neutral=True fingerprint={beforeFingerprint} counters={beforeCounters}");
        _output.WriteLine("winning-rolls-by-mask=" + string.Join(",", winningRolls.GroupBy(opportunity => opportunity.Blockers).OrderBy(group => (int)group.Key).Select(group => $"{FormatBlockers(group.Key)}:{group.Count()}")) + $" food-only={winningRolls.Count(opportunity => opportunity.Blockers == FamilyCheckBlocker.Food)} dwelling-only={winningRolls.Count(opportunity => opportunity.Blockers == FamilyCheckBlocker.ActualDwelling)} age-only={winningRolls.Count(opportunity => opportunity.Blockers == FamilyCheckBlocker.Age)}");
        foreach (var opportunity in winningRolls)
        {
            _output.WriteLine($"winning-roll hh={opportunity.HouseholdId.Value} day={opportunity.Day} chance={opportunity.Chance} draw={opportunity.Draw} blockers={FormatBlockers(opportunity.Blockers)} food-required={opportunity.FoodRequired} food-actual={opportunity.FoodActual} dwelling={opportunity.DwellingStructureId?.Value.ToString(CultureInfo.InvariantCulture) ?? "none"} dwelling-occupancy={opportunity.DwellingOccupancy}/{CitizenSimulationRules.ShelterCapacityPerBuilding} ages={opportunity.ParentAAge?.ToString(CultureInfo.InvariantCulture) ?? "none"}/{opportunity.ParentBAge?.ToString(CultureInfo.InvariantCulture) ?? "none"} health={opportunity.ParentAHealth?.ToString(CultureInfo.InvariantCulture) ?? "none"}/{opportunity.ParentBHealth?.ToString(CultureInfo.InvariantCulture) ?? "none"} hunger={opportunity.ParentAProjectedHunger?.ToString(CultureInfo.InvariantCulture) ?? "none"}/{opportunity.ParentBProjectedHunger?.ToString(CultureInfo.InvariantCulture) ?? "none"} accessible-nonempty-food={opportunity.ParentAHasAccessibleNonemptyFoodNode}/{opportunity.ParentBHasAccessibleNonemptyFoodNode}");
        }
    }

    [Fact]
    public void Seed42NaturalM5RunProducesAnAutonomousThirdGeneration()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var thirtyYears = new WorldMinute(30L * WorldCalendar.MinutesPerYear);
        var peakPopulation = engine.LivingPopulation;
        AdvanceAndObserve(engine, thirtyYears, ref peakPopulation);
        var reachedYears = 30;
        if (!HasGrandchild(engine.Citizens))
        {
            AdvanceAndObserve(engine, new WorldMinute(40L * WorldCalendar.MinutesPerYear), ref peakPopulation);
            reachedYears = 40;
        }
        stopwatch.Stop();

        var citizens = engine.Citizens;
        var descendants = citizens.Where(x => x.FounderOrdinal is null).ToArray();
        var grandchildren = Grandchildren(citizens);
        var deaths = citizens.Where(x => !x.IsAlive).GroupBy(x => x.DeathCause ?? "unknown", StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var firstGrandchild = grandchildren.Select(x => (long?)x.BirthMinute).Min();
        var labels = engine.Relationships.GroupBy(relationship => RelationshipLabels.Derive(relationship, IsPartner(citizens, relationship), IsFamily(citizens, relationship.CitizenAId, relationship.CitizenBId))).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var firstGeneration = descendants.Count(x => x.ParentAId is { } a && x.ParentBId is { } b && citizens.Single(parent => parent.Id == a).FounderOrdinal is not null && citizens.Single(parent => parent.Id == b).FounderOrdinal is not null);
        var secondGeneration = grandchildren.Length;
        var maxDepth = citizens.Select(citizen => AncestryDepth(citizens, citizen)).Max();
        _output.WriteLine($"seed=42 years={reachedYears} minute={engine.CurrentMinute.Value} elapsed={stopwatch.Elapsed} processed-events={engine.ProcessedEventCount} start-living=20 peak-living={peakPopulation} living={engine.LivingPopulation} total={engine.TotalCitizenCount} births={descendants.Length} deaths={engine.DeadPopulation} natural-deaths={CountDeath(deaths, "natural")} starvation-deaths={CountDeath(deaths, "starvation")} exhaustion-deaths={CountDeath(deaths, "exhaustion")} exposure-deaths={CountDeath(deaths, "exposure")} deprivation-deaths={CountDeath(deaths, "deprivation")} relationships={engine.Relationships.Count} acquaintances={CountLabel(labels, RelationshipLabels.Acquaintance)} friends={CountLabel(labels, RelationshipLabels.Friend)} close-friends={CountLabel(labels, RelationshipLabels.CloseFriend)} rivals={CountLabel(labels, RelationshipLabels.Rival)} partnerships-formed={engine.Households.Count} active-households={engine.Households.Count(x => x.DissolvedMinute is null)} dissolved-households={engine.Households.Count(x => x.DissolvedMinute is not null)} founders-alive={citizens.Count(x => x.FounderOrdinal is not null && x.IsAlive)} first-generation={firstGeneration} second-generation={secondGeneration} max-ancestry-depth={maxDepth} first-grandchild-minute={firstGrandchild?.ToString(CultureInfo.InvariantCulture) ?? "none"} shelters={engine.Structures.Count(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete)} shelter-capacity={engine.ShelterCapacity} food={engine.Settlement.FoodStored} population-limit-headroom={CitizenSimulationRules.MaximumPopulation - peakPopulation} {DescribeSettlementReadiness(engine, reachedYears)} social={engine.SocialFingerprint}");

        Assert.Equal((long)reachedYears * WorldCalendar.MinutesPerYear, engine.CurrentMinute.Value);
        Assert.True(engine.LivingPopulation > 0, "Natural M5 seed 42 run must retain a living population.");
        Assert.NotEmpty(grandchildren);
    }

    private static bool HasGrandchild(IReadOnlyList<Citizen> citizens) => Grandchildren(citizens).Length != 0;
    private static string FormatBlockers(FamilyCheckBlocker blockers) => blockers == FamilyCheckBlocker.None ? "none" : blockers.ToString();

    private static string DescribeSettlementReadiness(SimulationEngine engine, int year)
    {
        var reproductionReadyIgnoringHousing = HasReproductionReadyHouseholdIgnoringHousing(engine);
        var selectedDemand = typeof(SimulationEngine).GetMethod("SelectSettlementDemand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);
        var activeProject = engine.ActiveConstructionProject;
        var shelters = engine.Structures.Where(x => x.Type == StructureType.Shelter).ToArray();
        return $"year={year} living={engine.LivingPopulation} food={engine.Settlement.FoodStored} active-households={engine.Households.Count(x => x.DissolvedMinute is null)} reproduction-ready-ignoring-housing={reproductionReadyIgnoringHousing} selected-demand={selectedDemand ?? "none"} active-project={activeProject?.Type.ToString() ?? "none"}:{activeProject?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "none"} shelters-complete={shelters.Count(x => x.Status == StructureStatus.Complete)} shelters-under-construction={shelters.Count(x => x.Status == StructureStatus.UnderConstruction)} shelter-capacity={engine.ShelterCapacity}";
    }

    private static bool HasReproductionReadyHouseholdIgnoringHousing(SimulationEngine engine) => Assert.IsType<bool>(typeof(SimulationEngine).GetMethod("HasReproductionReadyHouseholdIgnoringHousing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null));

    private static string DescribeFirstReadinessHousing(SimulationEngine engine, int year)
    {
        var activeHouseholds = engine.Households.Where(household => household.DissolvedMinute is null).OrderBy(household => household.Id.Value).ToArray();
        var housingAware = activeHouseholds.Where(household => IsReproductionReady(engine, household, requireDwellingCapacity: true, out _)).ToArray();
        var otherwiseReady = activeHouseholds.Where(household => IsReproductionReady(engine, household, requireDwellingCapacity: false, out _)).ToArray();
        var membersByHousehold = activeHouseholds.ToDictionary(household => household.Id, household => engine.Citizens.Where(citizen => citizen.IsAlive && citizen.HouseholdId == household.Id).OrderBy(citizen => citizen.Id.Value).ToArray());
        var shelters = engine.Structures.Where(structure => structure.Type == StructureType.Shelter && structure.Status == StructureStatus.Complete).OrderBy(structure => structure.Id.Value).ToArray();
        var shelterOccupancies = string.Join(",", shelters.Select(shelter =>
        {
            var occupants = engine.Citizens.Count(citizen => citizen.IsAlive && citizen.HomeStructureId == shelter.Id);
            var assignedHouseholds = activeHouseholds.Count(household => household.DwellingStructureId == shelter.Id);
            return $"{shelter.Id.Value}:{occupants}/{CitizenSimulationRules.ShelterCapacityPerBuilding}:households={assignedHouseholds}";
        }));
        var readyDetails = string.Join(";", otherwiseReady.Select(household =>
        {
            var members = membersByHousehold[household.Id];
            var occupancy = household.DwellingStructureId is { } dwelling ? engine.Citizens.Count(citizen => citizen.IsAlive && citizen.HomeStructureId == dwelling) : 0;
            return $"{household.Id.Value}:members={members.Length}:dwelling={household.DwellingStructureId?.Value.ToString(CultureInfo.InvariantCulture) ?? "none"}:dwelling-occupancy={occupancy}";
        }));
        var unassigned = string.Join(",", activeHouseholds.Where(household => household.DwellingStructureId is null).Select(household => household.Id.Value.ToString(CultureInfo.InvariantCulture)));
        var projected = ProjectHouseholdSheltersAfterOneNewShelter(engine, activeHouseholds, membersByHousehold, shelters);
        return $"first-readiness-year={year} housing-aware-ready-count={housingAware.Length} otherwise-ready=[{readyDetails}] shelter-occupancies=[{shelterOccupancies}] unassigned-households=[{unassigned}] one-new-shelter-projection=[{projected}]";
    }

    private static bool IsReproductionReady(SimulationEngine engine, Household household, bool requireDwellingCapacity, out Citizen[] parents)
    {
        var arguments = new object?[] { household, requireDwellingCapacity, null, null };
        var ready = Assert.IsType<bool>(typeof(SimulationEngine).GetMethod("IsReproductionReady", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, arguments));
        parents = Assert.IsType<Citizen[]>(arguments[2]);
        return ready;
    }

    private static string ProjectHouseholdSheltersAfterOneNewShelter(SimulationEngine engine, IReadOnlyList<Household> activeHouseholds, Dictionary<HouseholdId, Citizen[]> membersByHousehold, IReadOnlyList<Structure> existingShelters)
    {
        var projectedShelters = existingShelters.Select(shelter => (Id: shelter.Id, Used: 0)).Append((Id: new StructureId(engine.CounterSnapshot.NextEntityId), Used: 0)).OrderBy(shelter => shelter.Id.Value).ToList();
        var assignments = new Dictionary<HouseholdId, StructureId?>();
        foreach (var household in activeHouseholds.OrderByDescending(household => membersByHousehold[household.Id].Length).ThenBy(household => household.Id.Value))
        {
            var index = projectedShelters.Select((shelter, position) => (Shelter: shelter, Position: position)).Where(item => CitizenSimulationRules.ShelterCapacityPerBuilding - item.Shelter.Used >= membersByHousehold[household.Id].Length).OrderBy(item => item.Shelter.Used).ThenBy(item => item.Shelter.Id.Value).Select(item => (int?)item.Position).FirstOrDefault();
            if (index is not { } position) { assignments[household.Id] = null; continue; }
            var selected = projectedShelters[position];
            projectedShelters[position] = (selected.Id, checked(selected.Used + membersByHousehold[household.Id].Length));
            assignments[household.Id] = selected.Id;
        }
        var readyAssignments = activeHouseholds.Where(household => IsReproductionReady(engine, household, requireDwellingCapacity: false, out _)).Select(household =>
        {
            var dwelling = assignments[household.Id];
            var used = dwelling is { } assigned ? projectedShelters.Single(shelter => shelter.Id == assigned).Used : 0;
            return $"{household.Id.Value}:dwelling={dwelling?.Value.ToString(CultureInfo.InvariantCulture) ?? "none"}:occupancy={used}:actual-free-slot={dwelling is not null && used < CitizenSimulationRules.ShelterCapacityPerBuilding}";
        });
        return string.Join(";", readyAssignments);
    }

    private static Citizen[] Grandchildren(IReadOnlyList<Citizen> citizens) =>
        citizens.Where(x => x.ParentAId is { } a && x.ParentBId is { } b &&
            citizens.Any(parent => (parent.Id == a || parent.Id == b) && parent.FounderOrdinal is null)).ToArray();

    private static void AdvanceAndObserve(SimulationEngine engine, WorldMinute target, ref int peakPopulation)
    {
        while (engine.NextScheduledEventMinute is { } due && due <= target)
        {
            Assert.True(engine.ProcessNextEvent());
            peakPopulation = Math.Max(peakPopulation, engine.LivingPopulation);
        }
        engine.AdvanceUntil(target);
    }

    private static int CountDeath(Dictionary<string, int> deaths, string cause) => deaths.TryGetValue(cause, out var count) ? count : 0;
    private static int CountLabel(Dictionary<string, int> labels, string label) => labels.TryGetValue(label, out var count) ? count : 0;
    private static bool IsPartner(IReadOnlyList<Citizen> citizens, RelationshipState relationship) => citizens.Single(x => x.Id == relationship.CitizenAId).PartnerId == relationship.CitizenBId;
    private static bool IsFamily(IReadOnlyList<Citizen> citizens, CitizenId first, CitizenId second) => AncestorsInclusive(citizens, first).Overlaps(AncestorsInclusive(citizens, second));
    private static HashSet<CitizenId> AncestorsInclusive(IReadOnlyList<Citizen> citizens, CitizenId id)
    {
        var found = new HashSet<CitizenId>();
        var pending = new Stack<CitizenId>();
        pending.Push(id);
        while (pending.TryPop(out var current))
        {
            if (!found.Add(current)) continue;
            var citizen = citizens.Single(x => x.Id == current);
            if (citizen.ParentAId is { } parentA) pending.Push(parentA);
            if (citizen.ParentBId is { } parentB) pending.Push(parentB);
        }
        return found;
    }
    private static int AncestryDepth(IReadOnlyList<Citizen> citizens, Citizen citizen) => citizen.FounderOrdinal is not null ? 0 : 1 + Math.Max(AncestryDepth(citizens, citizens.Single(x => x.Id == citizen.ParentAId)), AncestryDepth(citizens, citizens.Single(x => x.Id == citizen.ParentBId)));
}
