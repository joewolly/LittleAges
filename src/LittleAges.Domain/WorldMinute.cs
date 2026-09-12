namespace LittleAges.Domain;

/// <summary>
/// Canonical simulation time: non-negative simulated minutes since world creation.
/// It deliberately has no relationship to wall-clock time.
/// </summary>
public readonly record struct WorldMinute : IComparable<WorldMinute>
{
    public WorldMinute(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "World minutes cannot be negative.");
        }

        Value = value;
    }

    public long Value { get; }
    public static WorldMinute Zero => new(0);

    public WorldMinute Add(long minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Canonical time may only advance.");
        }

        try
        {
            return new WorldMinute(checked(Value + minutes));
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "The world minute would overflow Int64.");
        }
    }

    public WorldMinute AdvanceBy(long minutes) => Add(minutes);

    public WorldMinute AdvanceTo(WorldMinute target)
    {
        if (target < this)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Canonical time may not move backward.");
        }

        return target;
    }

    public WorldCalendarDate ToCalendar() => WorldCalendar.FromMinute(this);

    public int CompareTo(WorldMinute other) => Value.CompareTo(other.Value);

    public static WorldMinute operator +(WorldMinute minute, long delta) => minute.Add(delta);
    public static bool operator <(WorldMinute left, WorldMinute right) => left.Value < right.Value;
    public static bool operator <=(WorldMinute left, WorldMinute right) => left.Value <= right.Value;
    public static bool operator >(WorldMinute left, WorldMinute right) => left.Value > right.Value;
    public static bool operator >=(WorldMinute left, WorldMinute right) => left.Value >= right.Value;
    public static WorldMinute operator -(WorldMinute minute, WorldMinute other)
    {
        if (minute < other)
        {
            throw new ArgumentOutOfRangeException(nameof(other), other, "Canonical time subtraction may not be negative.");
        }

        return new WorldMinute(minute.Value - other.Value);
    }
}

public enum WorldSeason
{
    Spring = 1,
    Summer = 2,
    Autumn = 3,
    Winter = 4
}

/// <summary>Calendar values derived from canonical world minutes.</summary>
public readonly record struct WorldCalendarDate
{
    public WorldCalendarDate(long year, int month, int day, int hour, int minute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(year);
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        if (day is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(day));
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(minute));

        Year = year;
        Month = month;
        Day = day;
        Hour = hour;
        Minute = minute;
    }

    public long Year { get; }
    public int Month { get; }
    public int Day { get; }
    public int Hour { get; }
    public int Minute { get; }
    public int DayOfYear => ((Month - 1) * WorldCalendar.DaysPerMonth) + Day;
    public int DayOfWeek => (int)(((Year % WorldCalendar.DaysPerWeek) * (WorldCalendar.DaysPerYear % WorldCalendar.DaysPerWeek) + DayOfYear - 1) % WorldCalendar.DaysPerWeek);
    public WorldSeason Season => (WorldSeason)(((Month - 1) / 3) + 1);

    public WorldMinute ToWorldMinute() => WorldCalendar.ToMinute(this);
}

public static class WorldCalendar
{
    public const int HoursPerDay = 24;
    public const int MinutesPerHour = 60;
    public const int MinutesPerDay = HoursPerDay * MinutesPerHour;
    public const int DaysPerWeek = 7;
    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerSeason = 90;
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;
    public const long MinutesPerYear = (long)DaysPerYear * MinutesPerDay;

    public static WorldCalendarDate FromMinute(WorldMinute minute)
    {
        var remaining = minute.Value;
        var year = remaining / MinutesPerYear;
        remaining %= MinutesPerYear;
        var dayOfYear = checked((int)(remaining / MinutesPerDay));
        remaining %= MinutesPerDay;
        var hour = checked((int)(remaining / MinutesPerHour));
        var calendarMinute = checked((int)(remaining % MinutesPerHour));

        return new WorldCalendarDate(year, (dayOfYear / DaysPerMonth) + 1, (dayOfYear % DaysPerMonth) + 1, hour, calendarMinute);
    }

    public static WorldMinute ToMinute(WorldCalendarDate date)
    {
        var days = checked(((long)date.Year * DaysPerYear) + ((date.Month - 1L) * DaysPerMonth) + (date.Day - 1L));
        return new WorldMinute(checked((days * MinutesPerDay) + (date.Hour * MinutesPerHour) + date.Minute));
    }
}
