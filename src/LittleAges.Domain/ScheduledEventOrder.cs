namespace LittleAges.Domain;

/// <summary>The complete deterministic ordering tuple for scheduled simulation work.</summary>
public readonly record struct ScheduledEventOrder : IComparable<ScheduledEventOrder>
{
    public ScheduledEventOrder(WorldMinute dueWorldMinute, int priority, long entitySortKey, long sequence)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Scheduled event sequence must be positive.");
        }

        DueWorldMinute = dueWorldMinute;
        Priority = priority;
        EntitySortKey = entitySortKey;
        Sequence = sequence;
    }

    public WorldMinute DueWorldMinute { get; }
    public int Priority { get; }
    public long EntitySortKey { get; }
    public long Sequence { get; }

    public int CompareTo(ScheduledEventOrder other)
    {
        var result = DueWorldMinute.CompareTo(other.DueWorldMinute);
        if (result != 0) return result;
        result = Priority.CompareTo(other.Priority);
        if (result != 0) return result;
        result = EntitySortKey.CompareTo(other.EntitySortKey);
        if (result != 0) return result;
        return Sequence.CompareTo(other.Sequence);
    }

    public static bool operator <(ScheduledEventOrder left, ScheduledEventOrder right) => left.CompareTo(right) < 0;
    public static bool operator <=(ScheduledEventOrder left, ScheduledEventOrder right) => left.CompareTo(right) <= 0;
    public static bool operator >(ScheduledEventOrder left, ScheduledEventOrder right) => left.CompareTo(right) > 0;
    public static bool operator >=(ScheduledEventOrder left, ScheduledEventOrder right) => left.CompareTo(right) >= 0;
}
