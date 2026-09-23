using System.Globalization;
using System.Text;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;

namespace LittleAges.Headless;

public enum LivingDiagnosticCadence
{
    Monthly,
    Seasonal
}

public sealed record LivingSettlementDiagnosticReport
{
    public string Source { get; init; } = "SimulationPersistenceSnapshot + LivingWorldStateCodec";
    public ulong Seed { get; init; }
    public string Rules { get; init; } = string.Empty;
    public string Cadence { get; init; } = string.Empty;
    public int StartYear { get; init; }
    public int ThroughYear { get; init; }
    public long StartMinute { get; init; }
    public long FinalMinute { get; init; }
    public string? CheckpointPath { get; init; }
    public long CheckpointMinute { get; init; }
    public int ProcessedScheduledEvents { get; init; }
    public IReadOnlyList<string> UnavailableHistoricalMetrics { get; init; } = Array.Empty<string>();
    public LivingDiagnosticFingerprints FinalFingerprints { get; init; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    public IReadOnlyList<LivingSettlementDiagnosticSample> Samples { get; init; } = Array.Empty<LivingSettlementDiagnosticSample>();
}

public sealed record LivingDiagnosticFingerprints(string Survival, string Settlement, string Social, string History);

public sealed record LivingSettlementDiagnosticSample(
    long IntervalStartMinute,
    long IntervalEndMinute,
    int Year,
    int? Month,
    string Period,
    int LivingPopulation,
    int TotalPopulation,
    IReadOnlyDictionary<string, int> DeathsByCause,
    LivingFoodDiagnostic Food,
    IReadOnlyList<LivingFieldDiagnostic> Fields,
    int ReadyToHarvestFields,
    LivingWorkDiagnostic Work,
    LivingWeatherDiagnostic Weather);

public sealed record LivingFoodDiagnostic(
    long FoodHarvestedCumulative,
    long FoodHarvestedDuringInterval,
    long FoodPreparedCumulative,
    long FoodPreparedDuringInterval,
    long GoodsSpoiledCumulative,
    long GoodsSpoiledDuringInterval,
    long CompletedOrdersCumulative,
    long CompletedOrdersDuringInterval,
    long? EconomyFoodConsumedCumulative,
    long? EconomyFoodConsumedDuringInterval,
    int CommonsFoodStored,
    int CommonsFoodChange,
    int CommonsWoodStored,
    int CommonsWoodChange,
    IReadOnlyDictionary<string, int> LivingStockByGood,
    IReadOnlyDictionary<string, int> LivingStockChangeByGood,
    int LivingFoodStock,
    int LivingFoodStockChange,
    int FoodInActiveOrderCargo,
    int FuelStock);

public sealed record LivingFieldDiagnostic(
    long Id,
    int X,
    int Y,
    long SownMinute,
    int Growth,
    int Moisture,
    int Condition,
    long LastTendedMinute,
    int Harvests,
    int YieldRemaining,
    bool ReadyToHarvest);

public sealed record LivingWorkDiagnostic(
    int PendingOrders,
    int ClaimedOrders,
    int UnclaimedOrders,
    int BlockedOrders,
    long OldestOrderAgeDays,
    IReadOnlyDictionary<string, int> BlockedReasons,
    IReadOnlyList<LivingWorkKindDiagnostic> ByKind,
    IReadOnlyList<LivingOrderDiagnostic> Orders);

public sealed record LivingWorkKindDiagnostic(
    string Kind,
    int Pending,
    int Claimed,
    int Unclaimed,
    int Blocked,
    long WorkDone,
    long RequiredWork,
    long OldestOrderAgeDays);

public sealed record LivingOrderDiagnostic(
    long Id,
    string Kind,
    long? SubjectId,
    long? CitizenId,
    long CreatedMinute,
    long ClaimedMinute,
    long AgeDays,
    string Phase,
    string BlockedReason,
    int WorkDone,
    int RequiredWork,
    bool Reserved,
    bool SuppliesDelivered,
    bool Produced,
    bool CargoInTransit,
    IReadOnlyDictionary<string, int> Ingredients,
    IReadOnlyDictionary<string, int> Cargo);

public sealed record LivingWeatherDiagnostic(
    string AtIntervalEnd,
    int Temperature,
    int Rainfall,
    IReadOnlyList<LivingWeatherChangeDiagnostic> ChangesDuringInterval);

public sealed record LivingWeatherChangeDiagnostic(long Minute, int Value);

public static class LivingSettlementDiagnosticRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] UnavailableMetrics =
    [
        "The living snapshot has no explicit food-consumption counter; livingStockChangeByGood is a net stock change, not a measured consumption amount.",
        "CompletedOrders is cumulative but does not retain completion kind, so per-kind cooking and preservation completions are unavailable; current per-kind active orders and food-prepared deltas are reported.",
        "Work admission attempts are not persisted; claim and backlog counts describe current active orders only."
    ];

    public static async Task<LivingSettlementDiagnosticReport> RunAsync(HeadlessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.LivingDiagnosticCadence is not LivingDiagnosticCadence cadence || options.DiagnosticEndYear is not int throughYear)
            throw new ArgumentException("A living diagnostic cadence and end year are required.", nameof(options));
        if (!SimulationEngine.LivingSystemsEnabled(options.Rules))
            throw new ArgumentException("Living diagnostics require a living simulation rules version.", nameof(options));

        var startYear = options.DiagnosticStartYear ?? 0;
        if (startYear < 0 || throughYear <= startYear || throughYear > 500)
            throw new ArgumentOutOfRangeException(nameof(options), "The diagnostic year range must be positive, increasing, and end by year 500.");

        var startMinute = checked((long)startYear * WorldCalendar.MinutesPerYear);
        var (engine, checkpointMinute) = options.DiagnosticCheckpointPath is { } checkpointPath
            ? await LoadCheckpointAsync(checkpointPath, options.Seed, options.Rules, startMinute, cancellationToken)
            : (new SimulationEngine(new WorldSeed(options.Seed), simulationRulesVersion: options.Rules), 0L);

        if (engine.CurrentMinute.Value > startMinute)
            throw new InvalidDataException("The diagnostic checkpoint is later than the requested start year.");

        engine.AdvanceUntil(new WorldMinute(startMinute));
        var previousMinute = startMinute;
        var previousSnapshot = engine.CreatePersistenceSnapshot();
        var previousLiving = RequireLivingState(previousSnapshot);
        var periodsPerYear = cadence == LivingDiagnosticCadence.Monthly ? WorldCalendar.MonthsPerYear : 4;
        var minutesPerPeriod = cadence == LivingDiagnosticCadence.Monthly
            ? (long)WorldCalendar.DaysPerMonth * WorldCalendar.MinutesPerDay
            : (long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay;
        var firstPeriod = checked(startYear * periodsPerYear + 1);
        var lastPeriod = checked(throughYear * periodsPerYear);
        var samples = new List<LivingSettlementDiagnosticSample>(lastPeriod - firstPeriod + 1);

        for (var period = firstPeriod; period <= lastPeriod; period++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetMinute = checked((long)period * minutesPerPeriod);
            engine.AdvanceUntil(new WorldMinute(targetMinute));
            var snapshot = engine.CreatePersistenceSnapshot();
            var living = RequireLivingState(snapshot);
            var year = (period - 1) / periodsPerYear;
            var periodInYear = (period - 1) % periodsPerYear;
            samples.Add(BuildSample(previousSnapshot, previousLiving, snapshot, living, previousMinute, targetMinute, year, cadence, periodInYear));
            previousMinute = targetMinute;
            previousSnapshot = snapshot;
            previousLiving = living;
        }

        return new LivingSettlementDiagnosticReport
        {
            Seed = options.Seed,
            Rules = options.Rules,
            Cadence = cadence.ToString().ToLowerInvariant(),
            StartYear = startYear,
            ThroughYear = throughYear,
            StartMinute = startMinute,
            FinalMinute = engine.CurrentMinute.Value,
            CheckpointPath = options.DiagnosticCheckpointPath is null ? null : Path.GetFullPath(options.DiagnosticCheckpointPath),
            CheckpointMinute = checkpointMinute,
            ProcessedScheduledEvents = engine.ProcessedEventCount,
            UnavailableHistoricalMetrics = UnavailableMetrics,
            FinalFingerprints = new LivingDiagnosticFingerprints(engine.SurvivalFingerprint, engine.SettlementFingerprint, engine.SocialFingerprint, engine.HistoryFingerprint),
            Samples = samples
        };
    }

    public static string Serialize(LivingSettlementDiagnosticReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static async Task<string> WriteArtifactAsync(LivingSettlementDiagnosticReport report, string output)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("An output path is required.", nameof(output));
        var extension = Path.GetExtension(output);
        var path = string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(output)
            : Path.Combine(Path.GetFullPath(output), ArtifactFileName(report));
        var directory = Path.GetDirectoryName(path);
        if (directory is not null) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, Serialize(report) + Environment.NewLine, new UTF8Encoding(false));
        return path;
    }

    private static LivingSettlementDiagnosticSample BuildSample(
        SimulationPersistenceSnapshot previousSnapshot,
        LivingWorldState previousLiving,
        SimulationPersistenceSnapshot snapshot,
        LivingWorldState living,
        long intervalStartMinute,
        long intervalEndMinute,
        int year,
        LivingDiagnosticCadence cadence,
        int periodInYear)
    {
        var livingStock = StockByGood(living);
        var previousStock = StockByGood(previousLiving);
        var stockChanges = Enum.GetValues<LivingGood>().ToDictionary(
            good => good.ToString(),
            good => livingStock.GetValueOrDefault(good.ToString()) - previousStock.GetValueOrDefault(good.ToString()),
            StringComparer.Ordinal);
        var foodStock = FoodStock(livingStock);
        var previousFoodStock = FoodStock(previousStock);
        var foodConsumed = snapshot.Economy?.FoodConsumed;
        var previousFoodConsumed = previousSnapshot.Economy?.FoodConsumed;
        long? economyFoodConsumedDelta = foodConsumed is long currentConsumed && previousFoodConsumed is long priorConsumed
            ? currentConsumed - priorConsumed
            : null;
        var weatherChanges = living.Facts
            .Where(fact => fact.Kind == LivingFactKind.WeatherChanged && fact.Minute > intervalStartMinute && fact.Minute <= intervalEndMinute)
            .Select(fact => new LivingWeatherChangeDiagnostic(fact.Minute, fact.Value))
            .ToArray();
        var orders = living.Orders
            .OrderBy(order => order.Kind)
            .ThenBy(order => order.CreatedMinute)
            .ThenBy(order => order.Id)
            .ToArray();
        var orderGroups = orders.GroupBy(order => order.Kind)
            .OrderBy(group => group.Key)
            .Select(group => new LivingWorkKindDiagnostic(
                group.Key.ToString(),
                group.Count(),
                group.Count(order => order.CitizenId is not null),
                group.Count(order => order.CitizenId is null),
                group.Count(order => !string.IsNullOrWhiteSpace(order.BlockedReason)),
                group.Sum(order => (long)order.WorkDone),
                group.Sum(order => (long)order.RequiredWork),
                group.Max(order => (intervalEndMinute - order.CreatedMinute) / WorldCalendar.MinutesPerDay)))
            .ToArray();
        var blockedReasons = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.BlockedReason))
            .GroupBy(order => order.BlockedReason, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var fieldDiagnostics = living.Fields.OrderBy(field => field.Id)
            .Select(field => new LivingFieldDiagnostic(field.Id, field.Location.X, field.Location.Y, field.SownMinute, field.Growth, field.Moisture, field.Condition, field.LastTendedMinute, field.Harvests, field.YieldRemaining, field.YieldRemaining > 0))
            .ToArray();
        var orderDiagnostics = orders.Select(order => new LivingOrderDiagnostic(
            order.Id,
            order.Kind.ToString(),
            order.SubjectId,
            order.CitizenId,
            order.CreatedMinute,
            order.ClaimedMinute,
            (intervalEndMinute - order.CreatedMinute) / WorldCalendar.MinutesPerDay,
            order.Phase.ToString(),
            order.BlockedReason,
            order.WorkDone,
            order.RequiredWork,
            order.Reserved,
            order.SuppliesDelivered,
            order.Produced,
            order.CargoInTransit,
            order.Ingredients.GroupBy(ingredient => ingredient.Resource, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(ingredient => ingredient.Quantity), StringComparer.Ordinal),
            order.Cargo.GroupBy(item => item.Good).OrderBy(group => group.Key).ToDictionary(group => group.Key.ToString(), group => group.Sum(item => item.Quantity), StringComparer.Ordinal)))
            .ToArray();
        var deadThisInterval = CountDeathsByCause(previousSnapshot.Citizens, snapshot.Citizens, intervalStartMinute, intervalEndMinute);
        var period = cadence == LivingDiagnosticCadence.Monthly
            ? $"month-{periodInYear + 1:D2}"
            : ((WorldSeason)(periodInYear + 1)).ToString().ToLowerInvariant();

        return new LivingSettlementDiagnosticSample(
            intervalStartMinute,
            intervalEndMinute,
            year,
            cadence == LivingDiagnosticCadence.Monthly ? periodInYear + 1 : null,
            period,
            snapshot.Citizens.Count(citizen => citizen.IsAlive),
            snapshot.Citizens.Count,
            deadThisInterval,
            new LivingFoodDiagnostic(
                living.FoodHarvested,
                living.FoodHarvested - previousLiving.FoodHarvested,
                living.FoodPrepared,
                living.FoodPrepared - previousLiving.FoodPrepared,
                living.GoodsSpoiled,
                living.GoodsSpoiled - previousLiving.GoodsSpoiled,
                living.CompletedOrders,
                living.CompletedOrders - previousLiving.CompletedOrders,
                foodConsumed,
                economyFoodConsumedDelta,
                snapshot.Settlement?.FoodStored ?? 0,
                (snapshot.Settlement?.FoodStored ?? 0) - (previousSnapshot.Settlement?.FoodStored ?? 0),
                snapshot.Settlement?.WoodStored ?? 0,
                (snapshot.Settlement?.WoodStored ?? 0) - (previousSnapshot.Settlement?.WoodStored ?? 0),
                livingStock,
                stockChanges,
                foodStock,
                foodStock - previousFoodStock,
                orders.Sum(order => order.Cargo.Where(item => IsFood(item.Good)).Sum(item => item.Quantity)),
                livingStock.GetValueOrDefault(LivingGood.Fuel.ToString())),
            fieldDiagnostics,
            fieldDiagnostics.Count(field => field.ReadyToHarvest),
            new LivingWorkDiagnostic(
                orders.Length,
                orders.Count(order => order.CitizenId is not null),
                orders.Count(order => order.CitizenId is null),
                orders.Count(order => !string.IsNullOrWhiteSpace(order.BlockedReason)),
                orders.Length == 0 ? 0 : orders.Max(order => (intervalEndMinute - order.CreatedMinute) / WorldCalendar.MinutesPerDay),
                blockedReasons,
                orderGroups,
                orderDiagnostics),
            new LivingWeatherDiagnostic(living.Weather.ToString(), living.Temperature, living.Rainfall, weatherChanges));
    }

    private static async Task<(SimulationEngine Engine, long CheckpointMinute)> LoadCheckpointAsync(
        string sourcePath,
        ulong seed,
        string rules,
        long startMinute,
        CancellationToken cancellationToken)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourceFullPath)) throw new FileNotFoundException("The living diagnostic checkpoint does not exist.", sourceFullPath);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "LittleAges-Headless-LivingDiagnostic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var isolatedPath = Path.Combine(tempDirectory, "checkpoint-copy.db");
        try
        {
            await CopyDatabaseSnapshotAsync(sourceFullPath, isolatedPath, cancellationToken);
            await using var database = await WorldDatabase.OpenAsync(isolatedPath, cancellationToken);
            if (!await database.HasCheckpointAsync(cancellationToken)) throw new InvalidDataException("The living diagnostic database has no checkpoint.");
            var snapshot = await database.CreateCheckpointStore().LoadAsync(cancellationToken);
            if (snapshot.Seed.Value != seed) throw new InvalidDataException($"The checkpoint seed {snapshot.Seed.Value.ToString(CultureInfo.InvariantCulture)} does not match requested seed {seed.ToString(CultureInfo.InvariantCulture)}.");
            if (!string.Equals(snapshot.SimulationRulesVersion, rules, StringComparison.Ordinal)) throw new InvalidDataException($"The checkpoint rules '{snapshot.SimulationRulesVersion}' do not match requested rules '{rules}'.");
            if (snapshot.WorldMinute.Value > startMinute) throw new InvalidDataException("The diagnostic checkpoint is later than the requested start year.");
            return (SimulationEngine.FromPersistenceSnapshot(snapshot), snapshot.WorldMinute.Value);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static Task CopyDatabaseSnapshotAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Copy(sourcePath, destinationPath);
        var sourceWalPath = sourcePath + "-wal";
        if (File.Exists(sourceWalPath)) File.Copy(sourceWalPath, destinationPath + "-wal");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static LivingWorldState RequireLivingState(SimulationPersistenceSnapshot snapshot) => snapshot.LivingStateJson is { } json
        ? LivingWorldCodec.Deserialize(json)
        : throw new InvalidDataException("The diagnostic snapshot has no living-world state.");

    internal static IReadOnlyDictionary<string, int> CountDeathsByCause(
        IReadOnlyList<Citizen> previousCitizens,
        IReadOnlyList<Citizen> currentCitizens,
        long intervalStartMinute,
        long intervalEndMinute)
    {
        var previouslyAlive = previousCitizens.Where(citizen => citizen.IsAlive).Select(citizen => citizen.Id.Value).ToHashSet();
        return currentCitizens
            .Where(citizen => !citizen.IsAlive &&
                (citizen.DeathMinute is long deathMinute
                    ? deathMinute > intervalStartMinute && deathMinute <= intervalEndMinute
                    : previouslyAlive.Contains(citizen.Id.Value)))
            .GroupBy(citizen => citizen.DeathCause ?? "unknown", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static Dictionary<string, int> StockByGood(LivingWorldState state) => Enum.GetValues<LivingGood>()
        .ToDictionary(good => good.ToString(), good => state.Stock.FirstOrDefault(item => item.Good == good)?.Quantity ?? 0, StringComparer.Ordinal);

    private static int FoodStock(IReadOnlyDictionary<string, int> stock) =>
        stock.GetValueOrDefault(LivingGood.Grain.ToString()) +
        stock.GetValueOrDefault(LivingGood.Meal.ToString()) +
        stock.GetValueOrDefault(LivingGood.PreservedFood.ToString());

    private static bool IsFood(LivingGood good) => good is LivingGood.Grain or LivingGood.Meal or LivingGood.PreservedFood;

    private static string ArtifactFileName(LivingSettlementDiagnosticReport report) =>
        $"living-diagnostic-{report.Rules}-seed-{report.Seed.ToString(CultureInfo.InvariantCulture)}-years-{report.StartYear.ToString(CultureInfo.InvariantCulture)}-{report.ThroughYear.ToString(CultureInfo.InvariantCulture)}-{report.Cadence}.json";
}
