using System.Text.Json.Serialization;

namespace LittleAges.Domain;

public static class FestivalRules
{
    public const int DurationMinutes = 360;
    public const int AttendanceMinutes = 60;
    public static long Start(long settlementId, long year) => new WorldCalendarDate(year, 9, settlementId == 1 ? 1 : 8, 12, 0).ToWorldMinute().Value;
}

public enum FestivalMode { Gathering = 1, Feast }

/// <summary>Only the current/latest occurrence per site is retained; completed history is append-only.</summary>
public sealed class FestivalState
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long SettlementId { get; set; }
    public long Year { get; set; }
    public TileCoordinate Location { get; set; }
    public long StartMinute { get; set; }
    public long EndMinute { get; set; }
    public bool Started { get; set; }
    public bool Finished { get; set; }
    public FestivalMode Mode { get; set; } = FestivalMode.Gathering;
    public int InitialFood { get; set; }
    public int ReservedFood { get; set; }
    public int ConsumedFood { get; set; }
    public List<FestivalAttendance> Attendance { get; set; } = [];
    public List<FestivalPair> StrengthenedPairs { get; set; } = [];
}

public sealed class FestivalAttendance
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long CitizenId { get; set; }
    public int Minutes { get; set; }
    public bool BenefitsGranted { get; set; }
    public bool PortionConsumed { get; set; }
    public bool RelationshipGranted { get; set; }
}

public sealed record FestivalPair(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long FirstId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long SecondId);

public sealed class FestivalVisitPlan
{
    public long Year { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long CitizenId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long RelativeId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long SettlementId { get; set; }
    public long StartMinute { get; set; }
    public long DepartMinute { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long? PartyId { get; set; }
    public long LastAttendanceMinute { get; set; }
}
