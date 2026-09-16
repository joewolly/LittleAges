namespace LittleAges.Persistence;

public sealed class HistoricalEventRow
{
    public long Id { get; set; }
    public long WorldMinute { get; set; }
    public int EventType { get; set; }
    public int Importance { get; set; }
    public int Origin { get; set; }
    public int? LocationX { get; set; }
    public int? LocationY { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
}

public sealed class HistoricalEventCitizenLinkRow
{
    public long EventId { get; set; }
    public long CitizenId { get; set; }
    public string Role { get; set; } = string.Empty;
}

public sealed class HistoricalEventStructureLinkRow
{
    public long EventId { get; set; }
    public long StructureId { get; set; }
    public string Role { get; set; } = string.Empty;
}

public sealed class HistoryStateRow
{
    public int Id { get; set; }
    public long HistoryStartMinute { get; set; }
    public long HistoryStartEventId { get; set; }
    public long PeriodStartMinute { get; set; }
    public long BirthsSinceSample { get; set; }
    public long DeathsSinceSample { get; set; }
    public long FoodProducedSinceSample { get; set; }
    public long FoodConsumedSinceSample { get; set; }
    public bool ActiveFoodShortage { get; set; }
    public int PopulationMilestoneWatermark { get; set; }
}

public sealed class StatisticsSampleRow
{
    public long WorldMinute { get; set; }
    public long PeriodStartMinute { get; set; }
    public int Population { get; set; }
    public long BirthsPeriod { get; set; }
    public long DeathsPeriod { get; set; }
    public int FoodStored { get; set; }
    public long FoodProducedPeriod { get; set; }
    public long FoodConsumedPeriod { get; set; }
    public int WoodStored { get; set; }
    public int StoneStored { get; set; }
    public int ShelterCapacity { get; set; }
    public int AverageHealth { get; set; }
    public int AverageHunger { get; set; }
}

public sealed class CitizenMemoryRow
{
    public long CitizenId { get; set; }
    public long EventId { get; set; }
    public int MemoryType { get; set; }
    public int Importance { get; set; }
    public int EmotionalValence { get; set; }
    public long CreatedMinute { get; set; }
}
