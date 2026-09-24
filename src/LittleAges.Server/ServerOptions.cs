using System.Globalization;
using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Server;

public sealed record ServerOptions
{
    public const string DefaultActiveWorld = "default-world";
    public const string DefaultListenUrls = "http://127.0.0.1:5274";
    public const int DefaultCheckpointSimulationMinutes = 360;
    public const int DefaultCheckpointMinimumRealSeconds = 30;
    public const int DefaultCheckpointRetryCount = 3;
    public const int DefaultCheckpointRetryDelaySeconds = 2;
    public const int DefaultBrowserUpdateIntervalMilliseconds = 500;
    public const int DefaultObserverStreamIntervalMilliseconds = 100;
    public const int DefaultRealSecondsPerSimulationDay = 1_000;
    public const double DefaultSimulationMinutesPerSecond = (double)WorldCalendar.MinutesPerDay / DefaultRealSecondsPerSimulationDay;
    public const double MaximumSimulationMinutesPerSecond = 1_000;

    public required string DataRoot { get; init; }
    public required string ActiveWorld { get; init; }
    public required WorldSeed WorldSeed { get; init; }
    public required string ListenUrls { get; init; }
    public string NewWorldRules { get; init; } = SimulationEngine.CurrentSimulationRulesVersion;
    public double SimulationMinutesPerSecond { get; init; } = DefaultSimulationMinutesPerSecond;
    public int CheckpointSimulationMinutes { get; init; } = DefaultCheckpointSimulationMinutes;
    public int CheckpointMinimumRealSeconds { get; init; } = DefaultCheckpointMinimumRealSeconds;
    public int CheckpointRetryCount { get; init; } = DefaultCheckpointRetryCount;
    public int CheckpointRetryDelaySeconds { get; init; } = DefaultCheckpointRetryDelaySeconds;
    public int BrowserUpdateIntervalMilliseconds { get; init; } = DefaultBrowserUpdateIntervalMilliseconds;
    public int ObserverStreamIntervalMilliseconds { get; init; } = DefaultObserverStreamIntervalMilliseconds;

    public string DatabasePath => Path.Combine(DataRoot, ActiveWorld + ".db");

    public static ServerOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var dataRoot = configuration["DataRoot"];
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
        }

        var activeWorld = configuration["ActiveWorld"] ?? DefaultActiveWorld;
        if (string.IsNullOrWhiteSpace(activeWorld) || activeWorld != Path.GetFileName(activeWorld) || activeWorld.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("ActiveWorld must be a simple file-safe world name.", nameof(configuration));
        }

        var seedText = configuration["WorldSeed"] ?? "0";
        if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
        {
            throw new ArgumentException("WorldSeed must be an invariant UInt64 value.", nameof(configuration));
        }

        var configuredRate = configuration["SimulationMinutesPerSecond"];
        var rate = DefaultSimulationMinutesPerSecond;
        if (configuredRate is not null && (!double.TryParse(configuredRate, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || !double.IsFinite(rate) || rate < 0 || rate > MaximumSimulationMinutesPerSecond)) throw new ArgumentException($"SimulationMinutesPerSecond must be a finite invariant number between zero and {MaximumSimulationMinutesPerSecond.ToString(CultureInfo.InvariantCulture)}.", nameof(configuration));
        var options = new ServerOptions
        {
            DataRoot = Path.GetFullPath(dataRoot),
            ActiveWorld = activeWorld,
            WorldSeed = new WorldSeed(seed),
            ListenUrls = configuration["ListenUrls"] ?? DefaultListenUrls,
            NewWorldRules = configuration["NewWorldRules"] ?? SimulationEngine.CurrentSimulationRulesVersion,
            SimulationMinutesPerSecond = rate,
            CheckpointSimulationMinutes = ParseNonNegativeInt(configuration, "CheckpointSimulationMinutes", DefaultCheckpointSimulationMinutes),
            CheckpointMinimumRealSeconds = ParseNonNegativeInt(configuration, "CheckpointMinimumRealSeconds", DefaultCheckpointMinimumRealSeconds),
            CheckpointRetryCount = ParseNonNegativeInt(configuration, "CheckpointRetryCount", DefaultCheckpointRetryCount),
            CheckpointRetryDelaySeconds = ParseNonNegativeInt(configuration, "CheckpointRetryDelaySeconds", DefaultCheckpointRetryDelaySeconds),
            BrowserUpdateIntervalMilliseconds = ParsePositiveInt(configuration, "BrowserUpdateIntervalMilliseconds", DefaultBrowserUpdateIntervalMilliseconds),
            ObserverStreamIntervalMilliseconds = ParsePositiveInt(configuration, "ObserverStreamIntervalMilliseconds", DefaultObserverStreamIntervalMilliseconds)
        };
        options.Validate();
        return options;
    }

    public IReadOnlyList<Uri> GetListenUris()
    {
        ValidateListenUrls(ListenUrls);
        return ListenUrls.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => new Uri(value, UriKind.Absolute))
            .ToArray();
    }

    internal void Validate()
    {
        if (!SimulationEngine.IsHistoryRulesVersion(NewWorldRules) && !SimulationEngine.MigrationSystemsEnabled(NewWorldRules)) throw new ArgumentException("NewWorldRules must select a supported history or migration rules version.", nameof(NewWorldRules));
        if (string.IsNullOrWhiteSpace(DataRoot)) throw new ArgumentException("DataRoot is required.", nameof(DataRoot));
        if (string.IsNullOrWhiteSpace(ActiveWorld) || ActiveWorld != Path.GetFileName(ActiveWorld) || ActiveWorld.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("ActiveWorld must be a simple file-safe world name.", nameof(ActiveWorld));
        if (!double.IsFinite(SimulationMinutesPerSecond) || SimulationMinutesPerSecond < 0 || SimulationMinutesPerSecond > MaximumSimulationMinutesPerSecond) throw new ArgumentOutOfRangeException(nameof(SimulationMinutesPerSecond), $"Simulation advancement must be finite, non-negative, and no greater than {MaximumSimulationMinutesPerSecond.ToString(CultureInfo.InvariantCulture)}.");
        if (CheckpointSimulationMinutes < 0) throw new ArgumentOutOfRangeException(nameof(CheckpointSimulationMinutes), "CheckpointSimulationMinutes must be non-negative.");
        if (CheckpointMinimumRealSeconds < 0) throw new ArgumentOutOfRangeException(nameof(CheckpointMinimumRealSeconds), "CheckpointMinimumRealSeconds must be non-negative.");
        if (CheckpointRetryCount < 0 || CheckpointRetryCount == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(CheckpointRetryCount), "CheckpointRetryCount must be between zero and Int32.MaxValue - 1 so the total attempt count can be represented safely.");
        if (CheckpointRetryDelaySeconds < 0) throw new ArgumentOutOfRangeException(nameof(CheckpointRetryDelaySeconds), "CheckpointRetryDelaySeconds must be non-negative.");
        if (BrowserUpdateIntervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(BrowserUpdateIntervalMilliseconds), "BrowserUpdateIntervalMilliseconds must be positive.");
        if (ObserverStreamIntervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(ObserverStreamIntervalMilliseconds), "ObserverStreamIntervalMilliseconds must be positive.");
        ValidateListenUrls(ListenUrls);
    }

    private static int ParseNonNegativeInt(IConfiguration configuration, string key, int fallback)
    {
        var text = configuration[key];
        if (text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0) throw new ArgumentException($"{key} must be a non-negative invariant Int32 value.", nameof(configuration));
        return value;
    }

    private static int ParsePositiveInt(IConfiguration configuration, string key, int fallback)
    {
        var value = ParseNonNegativeInt(configuration, key, fallback);
        if (value == 0) throw new ArgumentException($"{key} must be a positive invariant Int32 value.", nameof(configuration));
        return value;
    }

    private static void ValidateListenUrls(string? listenUrls)
    {
        if (string.IsNullOrWhiteSpace(listenUrls)) throw new ArgumentException("ListenUrls must contain one or more absolute HTTP(S) URLs.", nameof(listenUrls));
        var values = listenUrls.Split(';', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("ListenUrls must be semicolon-separated absolute HTTP(S) URLs.", nameof(listenUrls));
        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri is null || uri.IsFile || uri.Host.Length == 0 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("ListenUrls must be semicolon-separated absolute HTTP(S) URLs.", nameof(listenUrls));
            }
        }
    }
}
