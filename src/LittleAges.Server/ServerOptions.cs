using System.Globalization;
using LittleAges.Domain;

namespace LittleAges.Server;

public sealed record ServerOptions
{
    public const string DefaultActiveWorld = "default-world";
    public const string DefaultListenUrls = "http://127.0.0.1:5274";

    public required string DataRoot { get; init; }
    public required string ActiveWorld { get; init; }
    public required WorldSeed WorldSeed { get; init; }
    public required string ListenUrls { get; init; }
    public double SimulationMinutesPerSecond { get; init; } = 10;

    public string DatabasePath => Path.Combine(DataRoot, ActiveWorld + ".db");

    public static ServerOptions FromConfiguration(IConfiguration configuration)
    {
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
        var rate = 10d;
        if (configuredRate is not null && (!double.TryParse(configuredRate, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || !double.IsFinite(rate) || rate < 0)) throw new ArgumentException("SimulationMinutesPerSecond must be a finite non-negative number.", nameof(configuration));
        return new ServerOptions
        {
            DataRoot = Path.GetFullPath(dataRoot),
            ActiveWorld = activeWorld,
            WorldSeed = new WorldSeed(seed),
            ListenUrls = configuration["ListenUrls"] ?? DefaultListenUrls,
            SimulationMinutesPerSecond = rate
        };
    }
}
