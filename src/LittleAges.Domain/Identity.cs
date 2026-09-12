namespace LittleAges.Domain;

/// <summary>Base contract shared by the strongly typed world-local identifiers.</summary>
public interface IWorldEntityId
{
    long Value { get; }
}

public readonly record struct CitizenId : IWorldEntityId
{
    public CitizenId(long value) => Value = WorldIdValidation.RequirePositive(value);
    public long Value { get; }
}

public readonly record struct StructureId : IWorldEntityId
{
    public StructureId(long value) => Value = WorldIdValidation.RequirePositive(value);
    public long Value { get; }
}

public readonly record struct HouseholdId : IWorldEntityId
{
    public HouseholdId(long value) => Value = WorldIdValidation.RequirePositive(value);
    public long Value { get; }
}

public readonly record struct HistoricalEventId : IWorldEntityId
{
    public HistoricalEventId(long value) => Value = WorldIdValidation.RequirePositive(value);
    public long Value { get; }
}

public readonly record struct ScheduledEventId : IWorldEntityId
{
    public ScheduledEventId(long value) => Value = WorldIdValidation.RequirePositive(value);
    public long Value { get; }
}

/// <summary>Validation shared by ID allocation and persisted ID restoration.</summary>
public static class WorldIdValidation
{
    public static long RequirePositive(long value, string parameterName = "value")
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "World-local identifiers must be positive.");
        }

        return value;
    }

    public static ulong RequirePositive(ulong value, string parameterName = "value")
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "World-local counters must be positive.");
        }

        return value;
    }
}
