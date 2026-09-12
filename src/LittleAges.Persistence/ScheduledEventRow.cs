namespace LittleAges.Persistence;

/// <summary>Pure data representation of an M0 synthetic scheduled event.</summary>
public sealed class ScheduledEventRow
{
    public long Id { get; set; }
    public long DueWorldMinute { get; set; }
    public int Priority { get; set; }
    public long EntitySortKey { get; set; }
    public long Sequence { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventPayloadJson { get; set; } = "{}";
}
