using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;

namespace LittleAges.Headless;

public enum HeadlessCommand
{
    Run,
    Benchmark,
    Acceptance,
    Diagnose
}

public sealed record HeadlessOptions(
    HeadlessCommand Command,
    ulong Seed,
    int Years,
    string Rules,
    string? Output,
    long ChunkMinutes,
    int? CheckpointYear = null,
    string? DatabasePath = null)
{
    public const long DefaultChunkMinutes = WorldCalendar.MinutesPerYear;
    public static HeadlessOptions Default(HeadlessCommand command) =>
        new(command, 42, 1, SimulationEngine.CurrentSimulationRulesVersion, null, DefaultChunkMinutes);
    public long? CheckpointMinute => CheckpointYear is int year ? checked((long)year * WorldCalendar.MinutesPerYear) : null;
}

public sealed record HeadlessParseResult(bool Succeeded, HeadlessOptions? Options, string? Error)
{
    public static HeadlessParseResult Success(HeadlessOptions options) => new(true, options, null);
    public static HeadlessParseResult Failure(string error) => new(false, null, error);
}

public static class HeadlessCommandLine
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "run", "benchmark", "acceptance", "diagnose"
    };

    public static HeadlessParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0) return HeadlessParseResult.Failure("A command is required: run, benchmark, or acceptance.");

        var index = 0;
        if (string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase))
            return HeadlessParseResult.Failure(string.Empty);

        if (!Commands.Contains(args[index])) return HeadlessParseResult.Failure($"Unknown command '{args[index]}'.");
        var command = Enum.Parse<HeadlessCommand>(args[index++], ignoreCase: true);
        var options = HeadlessOptions.Default(command);

        ulong seed = options.Seed;
        var years = options.Years;
        var rules = options.Rules;
        string? output = options.Output;
        var chunkMinutes = options.ChunkMinutes;
        int? checkpointYear = options.CheckpointYear;
        string? databasePath = options.DatabasePath;

        while (index < args.Count)
        {
            var argument = args[index++];
            if (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase))
                return HeadlessParseResult.Failure(string.Empty);

            var (name, inlineValue) = SplitOption(argument);
            if (name is not ("--seed" or "--years" or "--rules" or "--output" or "--chunk-minutes" or "--chunk-size" or "--checkpoint-year" or "--database"))
                return HeadlessParseResult.Failure($"Unknown option '{name}'.");

            var valueResult = ReadValue(name, inlineValue, args, ref index);
            if (!valueResult.Succeeded) return HeadlessParseResult.Failure(valueResult.Error!);
            var value = valueResult.Value!;
            switch (name)
            {
                case "--seed":
                    if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                        return HeadlessParseResult.Failure("--seed must be an unsigned decimal integer.");
                    break;
                case "--years":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out years) || years is not (1 or 10 or 100 or 500))
                        return HeadlessParseResult.Failure("--years must be one of: 1, 10, 100, 500.");
                    break;
                case "--rules":
                    if (!SimulationEngine.IsHistoryRulesVersion(value))
                        return HeadlessParseResult.Failure($"Unsupported rules '{value}'. This runner supports explicitly versioned history rules only.");
                    rules = value;
                    break;
                case "--output":
                    if (string.IsNullOrWhiteSpace(value)) return HeadlessParseResult.Failure("--output requires a non-empty path.");
                    output = value;
                    break;
                case "--checkpoint-year":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCheckpointYear) || parsedCheckpointYear <= 0)
                        return HeadlessParseResult.Failure("--checkpoint-year must be a positive decimal integer.");
                    checkpointYear = parsedCheckpointYear;
                    break;
                case "--database":
                    if (string.IsNullOrWhiteSpace(value)) return HeadlessParseResult.Failure("--database requires a non-empty path.");
                    databasePath = value;
                    break;
                default:
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out chunkMinutes) || chunkMinutes <= 0)
                        return HeadlessParseResult.Failure("--chunk-minutes must be a positive decimal integer.");
                    break;
            }
        }

        if (checkpointYear is not null && command != HeadlessCommand.Acceptance)
            return HeadlessParseResult.Failure("--checkpoint-year is only valid for acceptance.");
        if (databasePath is not null && command != HeadlessCommand.Acceptance)
            return HeadlessParseResult.Failure("--database is only valid for acceptance.");
        if (command == HeadlessCommand.Acceptance && checkpointYear is null)
            return HeadlessParseResult.Failure("acceptance requires --checkpoint-year.");
        if (checkpointYear is not null && checkpointYear >= years)
            return HeadlessParseResult.Failure("--checkpoint-year must be less than --years.");
        return HeadlessParseResult.Success(new HeadlessOptions(command, seed, years, rules, output, chunkMinutes, checkpointYear, databasePath));
    }

    private static (string Name, string? InlineValue) SplitOption(string argument)
    {
        var separator = argument.IndexOf('=');
        return separator < 0 ? (argument, null) : (argument[..separator], argument[(separator + 1)..]);
    }

    private static (bool Succeeded, string? Value, string? Error) ReadValue(string name, string? inlineValue, IReadOnlyList<string> args, ref int index)
    {
        if (inlineValue is not null) return (true, inlineValue, null);
        if (index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
            return (false, null, $"{name} requires a value.");
        return (true, args[index++], null);
    }
}

public sealed record HeadlessInvariantResult(string Name, bool Passed, string Details);
public sealed record HeadlessEvidence(string Name, string Details);
public sealed record HeadlessPopulationPoint(int Year, long Minute, int LivingCitizens);
internal sealed record HeadlessShortageMetrics(IReadOnlyDictionary<int, int> StartsByYear, IReadOnlyDictionary<int, int> EndsByYear, double TransitionPercentageOfHistory);

public sealed record HeadlessRunSummary(
    long FinalMinute,
    double RealElapsedMilliseconds,
    double EventsPerSecond,
    double SimulatedYearsPerRealMinute,
    int ProcessedScheduledEvents,
    string SurvivalFingerprint,
    string SettlementFingerprint,
    string SocialFingerprint,
    string HistoryFingerprint,
    string DeterministicReportFingerprint,
    bool MandatoryInvariantsPassed);

public sealed record HeadlessAcceptanceResult(
    int CheckpointYear,
    long CheckpointMinute,
    string DatabasePath,
    string DatabasePathKind,
    HeadlessRunSummary RunA,
    HeadlessRunSummary RunB,
    string RunASnapshotFingerprint,
    string RunBSnapshotFingerprint,
    bool Equivalent,
    IReadOnlyList<string> Mismatches);

public sealed record HeadlessReport
{
    public string Command { get; init; } = string.Empty;
    public ulong Seed { get; init; }
    public string Rules { get; init; } = string.Empty;
    public int RequestedYears { get; init; }
    public long FinalMinute { get; init; }
    public double RealElapsedMilliseconds { get; init; }
    public double EventsPerSecond { get; init; }
    public double SimulatedYearsPerRealMinute { get; init; }
    public int ProcessedScheduledEvents { get; init; }
    public int LivingCitizens { get; init; }
    public int PeakLivingCitizens { get; init; }
    public int TotalCitizens { get; init; }
    public int Births { get; init; }
    public int Deaths { get; init; }
    public int MaximumAncestryDepth { get; init; }
    public int Households { get; init; }
    public int Relationships { get; init; }
    public int Structures { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AgricultureState? Agriculture { get; init; }
    public int FoodStored { get; init; }
    public int WoodStored { get; init; }
    public int StoneStored { get; init; }
    public int StorageCapacity { get; init; }
    public int ShelterCapacity { get; init; }
    public int HistoricalEventTotal { get; init; }
    public IReadOnlyDictionary<string, int> HistoricalEventsByType { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public int StatisticsCount { get; init; }
    public int MemoryCount { get; init; }
    public int ShortageStarts { get; init; }
    public int ShortageEnds { get; init; }
    public IReadOnlyDictionary<int, int> ShortageStartsByYear { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, int> ShortageEndsByYear { get; init; } = new Dictionary<int, int>();
    public double ShortageTransitionPercentageOfHistory { get; init; }
    public string PopulationTrajectoryCadence { get; init; } = string.Empty;
    public IReadOnlyList<HeadlessPopulationPoint> PopulationTrajectory { get; init; } = Array.Empty<HeadlessPopulationPoint>();
    public long? FirstDescendantCitizenId { get; init; }
    public long? FirstDescendantMinute { get; init; }
    public long? FirstGrandchildCitizenId { get; init; }
    public long? FirstGrandchildMinute { get; init; }
    public string PeakSamplingCadence { get; init; } = string.Empty;
    public string SurvivalFingerprint { get; init; } = string.Empty;
    public string SettlementFingerprint { get; init; } = string.Empty;
    public string SocialFingerprint { get; init; } = string.Empty;
    public string HistoryFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<HeadlessEvidence> Evidence { get; init; } = Array.Empty<HeadlessEvidence>();
    public HeadlessAcceptanceResult? Acceptance { get; init; }
    public IReadOnlyList<HeadlessInvariantResult> Invariants { get; init; } = Array.Empty<HeadlessInvariantResult>();
    public bool MandatoryInvariantsPassed => Invariants.All(x => x.Passed);

    public string ToDeterministicReportJson() => HeadlessReportSerialization.SerializeDeterministicReport(this);
    public string DeterministicReportFingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToDeterministicReportJson()))).ToLowerInvariant();
}

public static class HeadlessRunner
{
    public static HeadlessReport Run(HeadlessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var targetMinute = checked((long)options.Years * WorldCalendar.MinutesPerYear);
        var run = RunEngine(options.Seed, options.Rules, targetMinute, options.ChunkMinutes);
        return BuildReport(options, run, targetMinute);
    }

    public static async Task<HeadlessReport> RunAcceptanceAsync(HeadlessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        if (options.Command != HeadlessCommand.Acceptance || options.CheckpointMinute is not long checkpointMinute)
            throw new ArgumentException("Acceptance requires --checkpoint-year.", nameof(options));
        var targetMinute = checked((long)options.Years * WorldCalendar.MinutesPerYear);
        if (checkpointMinute <= 0 || checkpointMinute >= targetMinute) throw new ArgumentException("The checkpoint must be strictly between minute zero and the target horizon.", nameof(options));
        var databasePath = ResolveDatabasePath(options);
        var runA = RunEngine(options.Seed, options.Rules, targetMinute, options.ChunkMinutes);
        var runBStopwatch = Stopwatch.StartNew();
        var runBEngine = new SimulationEngine(new WorldSeed(options.Seed), simulationRulesVersion: options.Rules);
        AdvanceToTarget(runBEngine, checkpointMinute, options.ChunkMinutes);
        var reloadedSnapshot = await PersistAndReloadAsync(databasePath, runBEngine.CreatePersistenceSnapshot(), cancellationToken);
        runBEngine = SimulationEngine.FromPersistenceSnapshot(reloadedSnapshot);
        AdvanceToTarget(runBEngine, targetMinute, options.ChunkMinutes);
        runBStopwatch.Stop();
        var runB = new HeadlessEngineRun(runBEngine, runBStopwatch.Elapsed.TotalMilliseconds, ComputeExactPeak(runBEngine));
        var reportA = BuildReport(options, runA, targetMinute);
        var reportB = BuildReport(options, runB, targetMinute);
        var comparison = HeadlessSnapshotComparer.Compare(runA.Engine.CreatePersistenceSnapshot(), runB.Engine.CreatePersistenceSnapshot());
        var acceptance = new HeadlessAcceptanceResult(
            options.CheckpointYear!.Value,
            checkpointMinute,
            databasePath,
            options.DatabasePath is null ? "temporary" : "caller-selected",
            ToSummary(reportA),
            ToSummary(reportB),
            comparison.LeftFingerprint,
            comparison.RightFingerprint,
            comparison.Equivalent,
            comparison.Mismatches);
        var invariants = reportA.Invariants
            .Concat(reportB.Invariants.Select(item => item with { Name = $"run-b:{item.Name}" }))
            .Append(new HeadlessInvariantResult("acceptance-equivalence", comparison.Equivalent, comparison.Equivalent ? "Run A and Run B persistence snapshots are canonically identical." : string.Join("; ", comparison.Mismatches)))
            .ToArray();
        return reportA with { Acceptance = acceptance, Invariants = invariants };
    }

    internal static SimulationEngine RunEngineForTesting(ulong seed, long targetMinute, long chunkMinutes = 240)
    {
        return RunEngine(seed, SimulationEngine.CurrentSimulationRulesVersion, targetMinute, chunkMinutes).Engine;
    }

    internal static void AdvanceEngineForTesting(SimulationEngine engine, long targetMinute, long chunkMinutes = 240)
    {
        ArgumentNullException.ThrowIfNull(engine);
        AdvanceToTarget(engine, targetMinute, chunkMinutes);
    }

    internal static Task<SimulationPersistenceSnapshot> PersistAndReloadForTestingAsync(string databasePath, SimulationPersistenceSnapshot snapshot, CancellationToken cancellationToken = default) => PersistAndReloadAsync(databasePath, snapshot, cancellationToken);

    internal static int ComputeExactPeakForTesting(SimulationEngine engine) => ComputeExactPeak(engine);

    internal static HeadlessPopulationPoint[] BuildPopulationTrajectoryForTesting(SimulationPersistenceSnapshot snapshot, int years) => BuildPopulationTrajectory(snapshot, years);

    internal static HeadlessShortageMetrics BuildShortageMetricsForTesting(SimulationPersistenceSnapshot snapshot) => BuildShortageMetrics(snapshot);

    private static void ValidateOptions(HeadlessOptions options)
    {
        if (!SimulationEngine.IsHistoryRulesVersion(options.Rules))
            throw new ArgumentException($"Unsupported rules '{options.Rules}'. This runner supports explicitly versioned history rules only.", nameof(options));
        if (options.Years is not (1 or 10 or 100 or 500)) throw new ArgumentOutOfRangeException(nameof(options), "Years must be one of: 1, 10, 100, 500.");
        if (options.ChunkMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Chunk minutes must be positive.");
    }

    private static HeadlessEngineRun RunEngine(ulong seed, string rules, long targetMinute, long chunkMinutes)
    {
        var engine = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: rules);
        var stopwatch = Stopwatch.StartNew();
        AdvanceToTarget(engine, targetMinute, chunkMinutes);
        stopwatch.Stop();
        return new HeadlessEngineRun(engine, stopwatch.Elapsed.TotalMilliseconds, ComputeExactPeak(engine));
    }

    private static void AdvanceToTarget(SimulationEngine engine, long targetMinute, long chunkMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetMinute, engine.CurrentMinute.Value);
        while (engine.CurrentMinute.Value < targetMinute)
        {
            var nextMinute = checked(engine.CurrentMinute.Value + Math.Min(targetMinute - engine.CurrentMinute.Value, chunkMinutes));
            engine.AdvanceUntil(new WorldMinute(nextMinute));
        }
    }

    private static HeadlessReport BuildReport(HeadlessOptions options, HeadlessEngineRun run, long targetMinute)
    {
        var engine = run.Engine;
        var snapshot = engine.CreatePersistenceSnapshot();
        var citizens = snapshot.Citizens;
        var historyEvents = snapshot.HistoricalEvents;
        var citizensById = citizens.ToDictionary(citizen => citizen.Id.Value);
        var descendants = citizens.Where(x => x.ParentAId is not null && x.ParentBId is not null).OrderBy(x => x.BirthMinute).ThenBy(x => x.Id.Value).ToArray();
        var firstDescendant = descendants.FirstOrDefault();
        var firstGrandchild = descendants.FirstOrDefault(x => HeadlessFactEvidence.AncestryDepth(x, citizensById) >= 2);
        var historyByType = Enum.GetValues<HistoricalEventType>().ToDictionary(value => value.ToString(), value => historyEvents.Count(item => item.EventType == value), StringComparer.Ordinal);
        var invariants = HeadlessInvariantValidator.Validate(engine, snapshot, targetMinute);
        var shortageMetrics = BuildShortageMetrics(snapshot);
        var trajectoryCadence = options.Years <= 10 ? "annual (year 0 through requested horizon)" : "decade (year 0 through requested horizon)";
        var elapsedMilliseconds = run.ElapsedMilliseconds;
        var elapsedSeconds = elapsedMilliseconds / 1000d;
        var processedEvents = engine.ProcessedEventCount;
        return new HeadlessReport
        {
            Command = options.Command.ToString().ToLowerInvariant(),
            Seed = options.Seed,
            Rules = options.Rules,
            RequestedYears = options.Years,
            FinalMinute = engine.CurrentMinute.Value,
            RealElapsedMilliseconds = elapsedMilliseconds,
            EventsPerSecond = elapsedSeconds <= 0 ? 0 : processedEvents / elapsedSeconds,
            SimulatedYearsPerRealMinute = elapsedMilliseconds <= 0 ? 0 : options.Years / (elapsedMilliseconds / 60000d),
            ProcessedScheduledEvents = processedEvents,
            LivingCitizens = engine.LivingPopulation,
            PeakLivingCitizens = run.PeakLiving,
            TotalCitizens = engine.TotalCitizenCount,
            Births = citizens.Count(x => x.ParentAId is not null && x.ParentBId is not null),
            Deaths = engine.DeadPopulation,
            MaximumAncestryDepth = citizens.Select(x => HeadlessFactEvidence.AncestryDepth(x, citizensById)).DefaultIfEmpty(0).Max(),
            Households = engine.Households.Count,
            Relationships = engine.Relationships.Count,
            Structures = engine.Structures.Count,
            Agriculture = engine.CaptureAgriculture(),
            FoodStored = engine.Settlement.FoodStored,
            WoodStored = engine.Settlement.WoodStored,
            StoneStored = engine.Settlement.StoneStored,
            StorageCapacity = engine.StorageCapacity,
            ShelterCapacity = engine.ShelterCapacity,
            HistoricalEventTotal = historyEvents.Count,
            HistoricalEventsByType = historyByType,
            StatisticsCount = engine.StatisticsSamples.Count,
            MemoryCount = engine.Memories.Count,
            ShortageStarts = shortageMetrics.StartsByYear.Values.Sum(),
            ShortageEnds = shortageMetrics.EndsByYear.Values.Sum(),
            ShortageStartsByYear = shortageMetrics.StartsByYear,
            ShortageEndsByYear = shortageMetrics.EndsByYear,
            ShortageTransitionPercentageOfHistory = shortageMetrics.TransitionPercentageOfHistory,
            PopulationTrajectoryCadence = trajectoryCadence,
            PopulationTrajectory = BuildPopulationTrajectory(snapshot, options.Years),
            FirstDescendantCitizenId = firstDescendant?.Id.Value,
            FirstDescendantMinute = firstDescendant?.BirthMinute,
            FirstGrandchildCitizenId = firstGrandchild?.Id.Value,
            FirstGrandchildMinute = firstGrandchild?.BirthMinute,
            PeakSamplingCadence = "Exact peak from ordered retained citizen birth/death facts; no checkpoint sampling.",
            SurvivalFingerprint = engine.SurvivalFingerprint,
            SettlementFingerprint = engine.SettlementFingerprint,
            SocialFingerprint = engine.SocialFingerprint,
            HistoryFingerprint = engine.HistoryFingerprint,
            Evidence = HeadlessFactEvidence.Build(snapshot),
            Invariants = invariants
        };
    }

    private static int ComputeExactPeak(SimulationEngine engine)
    {
        var snapshot = engine.CreatePersistenceSnapshot();
        var eventOrder = snapshot.HistoricalEvents
            .Where(item => item.EventType is HistoricalEventType.CitizenBorn or HistoricalEventType.CitizenDied)
            .SelectMany(item => snapshot.HistoricalEventCitizens
                .Where(link => link.HistoricalEventId == item.Id && link.Role == "subject")
                .Select(link => (Key: (item.EventType, CitizenId: link.CitizenId.Value), Order: item.Id.Value)))
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Order));
        var living = snapshot.Citizens.Count(IsAliveAtMinuteZero);
        var peak = living;
        var facts = snapshot.Citizens.SelectMany(citizen => PopulationFacts(citizen, snapshot.WorldMinute.Value, eventOrder))
            .OrderBy(fact => fact.Minute)
            .ThenBy(fact => fact.EventOrder)
            .ThenBy(fact => fact.KindOrder)
            .ThenBy(fact => fact.Id)
            .ToArray();
        foreach (var fact in facts)
        {
            living += fact.Delta;
            peak = Math.Max(peak, living);
        }
        return Math.Max(peak, engine.LivingPopulation);
    }

    private static bool IsAliveAtMinuteZero(Citizen citizen) => citizen.BirthMinute <= 0 && (citizen.DeathMinute is null || citizen.DeathMinute.Value > 0);

    private static IEnumerable<(long Minute, long EventOrder, int KindOrder, long Id, int Delta)> PopulationFacts(Citizen citizen, long currentMinute, Dictionary<(HistoricalEventType EventType, long CitizenId), long> eventOrder)
    {
        if (citizen.BirthMinute > 0 && citizen.BirthMinute <= currentMinute)
            yield return (citizen.BirthMinute, eventOrder.TryGetValue((HistoricalEventType.CitizenBorn, citizen.Id.Value), out var birthOrder) ? birthOrder : long.MaxValue, 1, citizen.Id.Value, 1);
        if (citizen.DeathMinute is long death && death > 0 && death <= currentMinute)
            yield return (death, eventOrder.TryGetValue((HistoricalEventType.CitizenDied, citizen.Id.Value), out var deathOrder) ? deathOrder : long.MaxValue, 0, citizen.Id.Value, -1);
    }

    private static HeadlessPopulationPoint[] BuildPopulationTrajectory(SimulationPersistenceSnapshot snapshot, int years)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(years);
        var cadence = years <= 10 ? 1 : 10;
        var checkpoints = Enumerable.Range(0, years / cadence + 1).Select(index => index * cadence).ToList();
        if (checkpoints[^1] != years) checkpoints.Add(years);
        return checkpoints.Select(year => new HeadlessPopulationPoint(year, checked((long)year * WorldCalendar.MinutesPerYear), LivingAt(snapshot.Citizens, checked((long)year * WorldCalendar.MinutesPerYear)))).ToArray();
    }

    private static int LivingAt(IEnumerable<Citizen> citizens, long minute) => citizens.Count(citizen => citizen.BirthMinute <= minute && (citizen.DeathMinute is null || citizen.DeathMinute.Value > minute));

    private static HeadlessShortageMetrics BuildShortageMetrics(SimulationPersistenceSnapshot snapshot)
    {
        static IReadOnlyDictionary<int, int> ByYear(SimulationPersistenceSnapshot value, HistoricalEventType type) => value.HistoricalEvents
            .Where(item => item.EventType == type)
            .GroupBy(item => checked((int)(item.WorldMinute / WorldCalendar.MinutesPerYear)))
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());
        var starts = ByYear(snapshot, HistoricalEventType.ResourceShortageStarted);
        var ends = ByYear(snapshot, HistoricalEventType.ResourceShortageEnded);
        var transitionCount = starts.Values.Sum() + ends.Values.Sum();
        var percentage = snapshot.HistoricalEvents.Count == 0 ? 0d : transitionCount * 100d / snapshot.HistoricalEvents.Count;
        return new HeadlessShortageMetrics(starts, ends, percentage);
    }

    private static string ResolveDatabasePath(HeadlessOptions options)
    {
        var path = options.DatabasePath is null
            ? Path.Combine(Path.GetTempPath(), "LittleAges-Headless", $"acceptance-{Guid.NewGuid():N}.db")
            : Path.GetFullPath(options.DatabasePath);
        if (File.Exists(path)) throw new IOException($"Acceptance database path already exists: {path}");
        return path;
    }

    private static async Task<SimulationPersistenceSnapshot> PersistAndReloadAsync(string databasePath, SimulationPersistenceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (directory is not null) Directory.CreateDirectory(directory);
        await using (var database = await WorldDatabase.OpenAsync(databasePath, cancellationToken))
        {
            await database.CreateCheckpointStore().CheckpointAsync(snapshot, DateTime.UtcNow, cancellationToken);
        }
        await using var reopened = await WorldDatabase.OpenAsync(databasePath, cancellationToken);
        return await reopened.CreateCheckpointStore().LoadAsync(cancellationToken);
    }

    private static HeadlessRunSummary ToSummary(HeadlessReport report) => new(report.FinalMinute, report.RealElapsedMilliseconds, report.EventsPerSecond, report.SimulatedYearsPerRealMinute, report.ProcessedScheduledEvents, report.SurvivalFingerprint, report.SettlementFingerprint, report.SocialFingerprint, report.HistoryFingerprint, report.DeterministicReportFingerprint, report.MandatoryInvariantsPassed);
}

public static class HeadlessReportSerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null
    };

    public static string Serialize(HeadlessReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string SerializeDeterministicReport(HeadlessReport report)
    {
        var projection = new
        {
            report.Command,
            report.Seed,
            report.Rules,
            report.RequestedYears,
            report.FinalMinute,
            report.ProcessedScheduledEvents,
            report.LivingCitizens,
            report.PeakLivingCitizens,
            report.TotalCitizens,
            report.Births,
            report.Deaths,
            report.MaximumAncestryDepth,
            report.Households,
            report.Relationships,
            report.Structures,
            report.FoodStored,
            report.WoodStored,
            report.StoneStored,
            report.StorageCapacity,
            report.ShelterCapacity,
            report.HistoricalEventTotal,
            report.HistoricalEventsByType,
            report.StatisticsCount,
            report.MemoryCount,
            report.ShortageStarts,
            report.ShortageEnds,
            report.ShortageStartsByYear,
            report.ShortageEndsByYear,
            report.ShortageTransitionPercentageOfHistory,
            report.PopulationTrajectoryCadence,
            report.PopulationTrajectory,
            report.FirstDescendantCitizenId,
            report.FirstDescendantMinute,
            report.FirstGrandchildCitizenId,
            report.FirstGrandchildMinute,
            report.PeakSamplingCadence,
            report.SurvivalFingerprint,
            report.SettlementFingerprint,
            report.SocialFingerprint,
            report.HistoryFingerprint,
            report.Invariants,
            report.Evidence,
            Acceptance = report.Acceptance is null ? null : new
            {
                report.Acceptance.CheckpointYear,
                report.Acceptance.CheckpointMinute,
                report.Acceptance.RunASnapshotFingerprint,
                report.Acceptance.RunBSnapshotFingerprint,
                report.Acceptance.Equivalent,
                report.Acceptance.Mismatches
            }
        };
        return report.Agriculture is null ? JsonSerializer.Serialize(projection, JsonOptions)
            : JsonSerializer.Serialize(new { Summary = projection, report.Agriculture }, JsonOptions);
    }

    public static void WriteArtifacts(HeadlessReport report, HeadlessOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Output)) return;
        var (jsonPath, markdownPath) = ArtifactPaths(options.Output!, report);
        var jsonDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonPath));
        var markdownDirectory = Path.GetDirectoryName(Path.GetFullPath(markdownPath));
        if (jsonDirectory is not null) Directory.CreateDirectory(jsonDirectory);
        if (markdownDirectory is not null) Directory.CreateDirectory(markdownDirectory);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(jsonPath, Serialize(report) + Environment.NewLine, utf8);
        File.WriteAllText(markdownPath, ToMarkdown(report) + Environment.NewLine, utf8);
    }

    private static (string JsonPath, string MarkdownPath) ArtifactPaths(string output, HeadlessReport report)
    {
        var extension = Path.GetExtension(output);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)) return (output, Path.ChangeExtension(output, ".md"));
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)) return (Path.ChangeExtension(output, ".json"), output);
        var fileName = $"headless-{report.Command}-seed-{report.Seed.ToString(CultureInfo.InvariantCulture)}-years-{report.RequestedYears.ToString(CultureInfo.InvariantCulture)}";
        return (Path.Combine(output, fileName + ".json"), Path.Combine(output, fileName + ".md"));
    }

    private static string ToMarkdown(HeadlessReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Little Ages headless report");
        builder.AppendLine();
        builder.AppendLine("Timing fields are operational measurements and are excluded from canonical comparison.");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        AddRow(builder, "Command", report.Command);
        AddRow(builder, "Seed", report.Seed.ToString(CultureInfo.InvariantCulture));
        AddRow(builder, "Rules", report.Rules);
        AddRow(builder, "Requested years", report.RequestedYears.ToString(CultureInfo.InvariantCulture));
        AddRow(builder, "Final minute", report.FinalMinute.ToString(CultureInfo.InvariantCulture));
        AddRow(builder, "Real elapsed milliseconds (operational)", report.RealElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        AddRow(builder, "Processed scheduled events", report.ProcessedScheduledEvents.ToString(CultureInfo.InvariantCulture));
        AddRow(builder, "Events per second (operational)", report.EventsPerSecond.ToString("F3", CultureInfo.InvariantCulture));
        AddRow(builder, "Simulated years per real minute (operational)", report.SimulatedYearsPerRealMinute.ToString("F3", CultureInfo.InvariantCulture));
        AddRow(builder, "Living / peak / total citizens", $"{report.LivingCitizens.ToString(CultureInfo.InvariantCulture)} / {report.PeakLivingCitizens.ToString(CultureInfo.InvariantCulture)} / {report.TotalCitizens.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Births / deaths", $"{report.Births.ToString(CultureInfo.InvariantCulture)} / {report.Deaths.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Ancestry depth", report.MaximumAncestryDepth.ToString(CultureInfo.InvariantCulture));
        AddRow(builder, "Households / relationships / structures", $"{report.Households.ToString(CultureInfo.InvariantCulture)} / {report.Relationships.ToString(CultureInfo.InvariantCulture)} / {report.Structures.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Food / wood / stone", $"{report.FoodStored.ToString(CultureInfo.InvariantCulture)} / {report.WoodStored.ToString(CultureInfo.InvariantCulture)} / {report.StoneStored.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Storage / shelter capacity", $"{report.StorageCapacity.ToString(CultureInfo.InvariantCulture)} / {report.ShelterCapacity.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Historical events / statistics / memories", $"{report.HistoricalEventTotal.ToString(CultureInfo.InvariantCulture)} / {report.StatisticsCount.ToString(CultureInfo.InvariantCulture)} / {report.MemoryCount.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Shortage starts / ends", $"{report.ShortageStarts.ToString(CultureInfo.InvariantCulture)} / {report.ShortageEnds.ToString(CultureInfo.InvariantCulture)}");
        AddRow(builder, "Shortage transition percentage of history", report.ShortageTransitionPercentageOfHistory.ToString("F3", CultureInfo.InvariantCulture));
        AddRow(builder, "First descendant", FormatCitizen(report.FirstDescendantCitizenId, report.FirstDescendantMinute));
        AddRow(builder, "First grandchild", FormatCitizen(report.FirstGrandchildCitizenId, report.FirstGrandchildMinute));
        AddRow(builder, "Peak sampling cadence", report.PeakSamplingCadence);
        builder.AppendLine();
        builder.AppendLine("## Historical events by type");
        builder.AppendLine();
        foreach (var item in report.HistoricalEventsByType) AddRow(builder, item.Key, item.Value.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine();
        builder.AppendLine("## Population trajectory");
        builder.AppendLine();
        AddRow(builder, "Cadence", report.PopulationTrajectoryCadence);
        builder.AppendLine("| Year | Minute | Living citizens |");
        builder.AppendLine("| ---: | ---: | ---: |");
        foreach (var point in report.PopulationTrajectory)
            builder.Append('|').Append(' ').Append(point.Year.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(point.Minute.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(point.LivingCitizens.ToString(CultureInfo.InvariantCulture)).AppendLine(" |");
        builder.AppendLine();
        builder.AppendLine("## Shortage transitions by year");
        builder.AppendLine();
        builder.AppendLine("| Year | Starts | Ends |");
        builder.AppendLine("| ---: | ---: | ---: |");
        foreach (var year in report.ShortageStartsByYear.Keys.Concat(report.ShortageEndsByYear.Keys).Distinct().OrderBy(value => value))
            builder.Append('|').Append(' ').Append(year.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(report.ShortageStartsByYear.GetValueOrDefault(year).ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(report.ShortageEndsByYear.GetValueOrDefault(year).ToString(CultureInfo.InvariantCulture)).AppendLine(" |");
        builder.AppendLine();
        builder.AppendLine("## Factual evidence");
        builder.AppendLine();
        foreach (var item in report.Evidence) AddRow(builder, item.Name, item.Details);
        if (report.Acceptance is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Acceptance equivalence");
            builder.AppendLine();
            AddRow(builder, "Checkpoint year / minute", $"{report.Acceptance.CheckpointYear.ToString(CultureInfo.InvariantCulture)} / {report.Acceptance.CheckpointMinute.ToString(CultureInfo.InvariantCulture)}");
            AddRow(builder, "Database path semantics", $"{report.Acceptance.DatabasePathKind} · {report.Acceptance.DatabasePath}");
            AddRow(builder, "Run A real milliseconds (operational)", report.Acceptance.RunA.RealElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            AddRow(builder, "Run B real milliseconds (operational)", report.Acceptance.RunB.RealElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            AddRow(builder, "Run A / Run B events per second (operational)", $"{report.Acceptance.RunA.EventsPerSecond.ToString("F3", CultureInfo.InvariantCulture)} / {report.Acceptance.RunB.EventsPerSecond.ToString("F3", CultureInfo.InvariantCulture)}");
            AddRow(builder, "Run A snapshot fingerprint", report.Acceptance.RunASnapshotFingerprint);
            AddRow(builder, "Run B snapshot fingerprint", report.Acceptance.RunBSnapshotFingerprint);
            AddRow(builder, "Canonical equivalence", report.Acceptance.Equivalent ? "PASS" : "FAIL");
            foreach (var mismatch in report.Acceptance.Mismatches) AddRow(builder, "Mismatch", mismatch);
        }
        builder.AppendLine();
        builder.AppendLine("## Canonical engine fingerprints and deterministic report fingerprint");
        builder.AppendLine();
        AddRow(builder, "SurvivalFingerprint", report.SurvivalFingerprint);
        AddRow(builder, "SettlementFingerprint", report.SettlementFingerprint);
        AddRow(builder, "SocialFingerprint", report.SocialFingerprint);
        AddRow(builder, "HistoryFingerprint", report.HistoryFingerprint);
        AddRow(builder, "DeterministicReportFingerprint", report.DeterministicReportFingerprint);
        builder.AppendLine();
        builder.AppendLine("## Mandatory invariants");
        builder.AppendLine();
        foreach (var invariant in report.Invariants) AddRow(builder, invariant.Name, invariant.Passed ? "PASS" : $"FAIL: {invariant.Details}");
        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static string FormatCitizen(long? id, long? minute) => id is null ? "none" : $"citizen {id.Value.ToString(CultureInfo.InvariantCulture)} at minute {minute!.Value.ToString(CultureInfo.InvariantCulture)}";
    private static void AddRow(StringBuilder builder, string name, string value) => builder.Append("| ").Append(name.Replace("|", "\\|", StringComparison.Ordinal)).Append(" | ").Append(value.Replace("|", "\\|", StringComparison.Ordinal)).AppendLine(" |");
}

public static class Program
{
    private static readonly JsonSerializerOptions DiagnosisJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task<int> Main(string[] args)
    {
        var parse = HeadlessCommandLine.Parse(args);
        if (!parse.Succeeded)
        {
            if (!string.IsNullOrEmpty(parse.Error)) Console.Error.WriteLine($"error: {parse.Error}");
            Console.WriteLine(HelpText);
            return string.IsNullOrEmpty(parse.Error) ? 0 : 2;
        }

        try
        {
            var options = parse.Options!;
            if (options.Command == HeadlessCommand.Diagnose)
            {
                var diagnosis = PopulationDiagnosis.Run(options);
                var json = JsonSerializer.Serialize(diagnosis, DiagnosisJsonOptions);
                if (options.Output is { } output)
                {
                    Directory.CreateDirectory(output);
                    await File.WriteAllTextAsync(Path.Combine(output, $"population-{options.Rules}-seed-{options.Seed}-years-{options.Years}.json"), json);
                }
                Console.WriteLine(json);
                return 0;
            }
            var report = options.Command == HeadlessCommand.Acceptance
                ? await HeadlessRunner.RunAcceptanceAsync(options)
                : HeadlessRunner.Run(options);
            HeadlessReportSerialization.WriteArtifacts(report, options);
            Console.WriteLine(HeadlessReportSerialization.Serialize(report));
            return options.Command == HeadlessCommand.Acceptance && !report.MandatoryInvariantsPassed ? 1 : 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static readonly string HelpText = $"""
Little Ages headless deterministic runner

Usage:
  LittleAges.Headless <run|benchmark|acceptance|diagnose> [options]

Options:
  --seed <uint64>       World seed (default: 42)
  --years <1|10|100|500>  Simulation horizon (default: 1)
  --rules <version>     Rules version (default: {SimulationEngine.CurrentSimulationRulesVersion})
  --output <path>       Output directory or .json/.md artifact path
  --chunk-minutes <n>   Deterministic reporting cadence (default: one year)
  --chunk-size <n>     Alias for --chunk-minutes
  --checkpoint-year <n> Acceptance checkpoint year (for example, 37 with --years 100)
  --database <path>     Acceptance SQLite path (default: safe temporary path)
  --help                Show this help

Timing fields are operational; engine canonical fingerprints are state-derived, while the deterministic report fingerprint hashes the stable non-timing report projection.
""";
}
