namespace LittleAges.Persistence;

/// <summary>
/// EF storage row for the singleton world metadata record. WorldSeedValue is decimal text
/// deliberately: SQLite INTEGER is signed Int64, while WorldSeed is the complete UInt64 domain.
/// </summary>
public sealed class WorldMetaRow
{
    public int Id { get; set; }
    public string WorldSeedValue { get; set; } = string.Empty;
    public long WorldMinute { get; set; }
    public string WorldSchemaVersion { get; set; } = string.Empty;
    public string SimulationRulesVersion { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public string WorldConfigurationJson { get; set; } = string.Empty;
    public int GenerationVersion { get; set; }
    public int GenerationAttempt { get; set; }
    public int StartingX { get; set; }
    public int StartingY { get; set; }
    public string WorldFingerprint { get; set; } = string.Empty;
    public int CitizenGenerationVersion { get; set; }
    public int SurvivalVersion { get; set; }
    public int SettlementVersion { get; set; }
    public int SocialVersion { get; set; }
    public int HistoryVersion { get; set; }
    public long NextEntityId { get; set; }
    public long NextHistoricalEventId { get; set; }
    public long NextScheduledEventSequence { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastCheckpointUtc { get; set; }
}
