using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class WorldMinuteTests
{
    [Fact]
    public void CalendarConvertsEveryBoundaryInThe360DayYear()
    {
        Assert.Equal(new WorldCalendarDate(0, 1, 1, 0, 0), new WorldMinute(0).ToCalendar());
        Assert.Equal(new WorldCalendarDate(0, 3, 30, 23, 59), new WorldMinute((90L * WorldCalendar.MinutesPerDay) - 1).ToCalendar());
        Assert.Equal(new WorldCalendarDate(0, 4, 1, 0, 0), new WorldMinute(90L * WorldCalendar.MinutesPerDay).ToCalendar());
        Assert.Equal(new WorldCalendarDate(0, 12, 30, 23, 59), new WorldMinute(WorldCalendar.MinutesPerYear - 1).ToCalendar());
        Assert.Equal(new WorldCalendarDate(1, 1, 1, 0, 0), new WorldMinute(WorldCalendar.MinutesPerYear).ToCalendar());
        Assert.Equal(3, new WorldMinute(WorldCalendar.MinutesPerYear).ToCalendar().DayOfWeek);
    }

    [Fact]
    public void CalendarConversionRoundTrips()
    {
        var date = new WorldCalendarDate(37, 8, 17, 19, 43);
        Assert.Equal(date, date.ToWorldMinute().ToCalendar());
        Assert.Equal(WorldSeason.Autumn, date.Season);
        Assert.Equal(long.MaxValue, WorldCalendar.FromMinute(new WorldMinute(long.MaxValue)).ToWorldMinute().Value);
    }

    [Fact]
    public void WorldMinuteRejectsBackwardArithmetic()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMinute(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMinute(10).Add(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMinute(10).AdvanceTo(new WorldMinute(9)));
    }
}

public sealed class IdentityAndCounterTests
{
    [Fact]
    public void CountersAreMonotonicAndRestorable()
    {
        var counters = new DeterministicCounters();
        var first = counters.AllocateCitizenId();
        var second = counters.AllocateStructureId();
        var sequence = counters.AllocateScheduledEventSequence();

        Assert.Equal(1L, first.Value);
        Assert.Equal(2L, second.Value);
        Assert.Equal(1L, sequence);

        var restored = new DeterministicCounters(counters.Snapshot);
        Assert.Equal(3L, restored.AllocateHouseholdId().Value);
        Assert.Equal(2L, restored.AllocateScheduledEventSequence());
    }

    [Fact]
    public void IDTypesDoNotCompareEqualAcrossEntityKinds()
    {
        Assert.NotEqual<object>(new CitizenId(1), new StructureId(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicCounters(new DeterministicCountersSnapshot(0, 1, 1)));
    }

    [Fact]
    public void SignedCountersRejectExhaustionWithoutConsumingState()
    {
        var counter = new MonotonicCounter(long.MaxValue - 1);
        Assert.Equal(long.MaxValue - 1, counter.Allocate());
        Assert.Equal(long.MaxValue, counter.NextValue);
        Assert.Throws<InvalidOperationException>(() => counter.Allocate());
        Assert.Equal(long.MaxValue, counter.NextValue);

        var counters = new DeterministicCounters(new DeterministicCountersSnapshot(long.MaxValue, long.MaxValue, long.MaxValue));
        var before = counters.Snapshot;
        Assert.Throws<InvalidOperationException>(() => counters.AllocateCitizenId());
        Assert.Throws<InvalidOperationException>(() => counters.AllocateHistoricalEventId());
        Assert.Throws<InvalidOperationException>(() => counters.AllocateScheduledEventSequence());
        Assert.Equal(before, counters.Snapshot);
        Assert.Equal(before, new DeterministicCounters(before).Snapshot);
    }
}

public sealed class RandomnessTests
{
    [Fact]
    public void GoldenVectorLocksVersionOneDerivation()
    {
        Assert.Equal(1, DeterministicRandom.AlgorithmVersion);
        var random = new DeterministicRandom(new WorldSeed(0x0123456789ABCDEFUL));

        Assert.Equal(0x165253D2BA50D131UL, random.NextUInt64(RandomDomain.WorldGeneration, 1, 2, 3));
        Assert.Equal(0x4EAC0CAF529D7777UL, random.NextUInt64(RandomDomain.Relationships, 1, 2, 3));
    }

    [Fact]
    public void UnitDoubleBitPatternsLockDefaultAndBoundaryInputs()
    {
        var zeroSeed = new DeterministicRandom(new WorldSeed(0));
        Assert.Equal(0xBB28B580F325675BUL, zeroSeed.NextUInt64(RandomDomain.WorldGeneration));
        var zeroDouble = zeroSeed.NextUnitDouble(RandomDomain.WorldGeneration);
        Assert.Equal(BitConverter.Int64BitsToDouble(0x3FE76516B01E64ACL), zeroDouble);
        Assert.Equal(0x3FE76516B01E64ACL, BitConverter.DoubleToInt64Bits(zeroDouble));

        var maximumSeed = new DeterministicRandom(new WorldSeed(ulong.MaxValue));
        Assert.Equal(0xA16E0DD4A3BC86C3UL, maximumSeed.NextUInt64(RandomDomain.WorldGeneration));
        var maximumSeedDouble = maximumSeed.NextUnitDouble(RandomDomain.WorldGeneration);
        Assert.Equal(BitConverter.Int64BitsToDouble(0x3FE42DC1BA947790L), maximumSeedDouble);
        Assert.Equal(0x3FE42DC1BA947790L, BitConverter.DoubleToInt64Bits(maximumSeedDouble));

        var maximumInputs = new DeterministicRandom(new WorldSeed(ulong.MaxValue));
        Assert.Equal(0x3A8123A19F81310CUL, maximumInputs.NextUInt64(RandomDomain.ResourceRegeneration, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue));
        var maximumInputsDouble = maximumInputs.NextUnitDouble(RandomDomain.ResourceRegeneration, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);
        Assert.Equal(BitConverter.Int64BitsToDouble(0x3FCD4091D0CFC098L), maximumInputsDouble);
        Assert.Equal(0x3FCD4091D0CFC098L, BitConverter.DoubleToInt64Bits(maximumInputsDouble));

        var boundaryKeys = new DeterministicRandom(new WorldSeed(0x0123456789ABCDEFUL));
        Assert.Equal(0x7F91404FFE7C254FUL, boundaryKeys.NextUInt64(RandomDomain.DecisionVariation, ulong.MaxValue, 0, ulong.MaxValue));
        var boundaryKeysDouble = boundaryKeys.NextUnitDouble(RandomDomain.DecisionVariation, ulong.MaxValue, 0, ulong.MaxValue);
        Assert.Equal(BitConverter.Int64BitsToDouble(0x3FDFE45013FF9F08L), boundaryKeysDouble);
        Assert.Equal(0x3FDFE45013FF9F08L, BitConverter.DoubleToInt64Bits(boundaryKeysDouble));
    }

    [Fact]
    public void DomainsAndKeysAreSeparatedAndRepeatable()
    {
        var one = new DeterministicRandom(new WorldSeed(42));
        var two = new DeterministicRandom(new WorldSeed(42));

        Assert.Equal(one.NextUInt64(RandomDomain.Mortality, 9), two.NextUInt64(RandomDomain.Mortality, 9));
        Assert.NotEqual(one.NextUInt64(RandomDomain.Mortality, 9), one.NextUInt64(RandomDomain.Reproduction, 9));
        Assert.InRange(one.NextUnitDouble(RandomDomain.DecisionVariation, 1), 0d, 1d);
    }
}

public sealed class StructureValidationTests
{
    [Theory]
    [InlineData(StructureType.Shelter, 41, 10, 600)]
    [InlineData(StructureType.Stockpile, 60, 31, 900)]
    [InlineData(StructureType.Workshop, 80, 50, 1201)]
    public void ValidateRejectsNonCanonicalTypeRequirements(StructureType type, int wood, int stone, int work)
    {
        var structure = new Structure(new StructureId(1), type, new TileCoordinate(0, 0), 0, wood, stone, work);

        Assert.Throws<ArgumentException>(structure.Validate);
    }

    [Theory]
    [InlineData(39, 10, 600)]
    [InlineData(40, 9, 600)]
    [InlineData(40, 10, 599)]
    [InlineData(41, 10, 600)]
    [InlineData(40, 11, 600)]
    [InlineData(40, 10, 601)]
    public void ValidateRejectsIncompleteOrOverdeliveredCompletedStructure(int wood, int stone, int work)
    {
        var structure = new Structure(new StructureId(1), StructureType.Shelter, new TileCoordinate(0, 0), 0, 40, 10, 600)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = 1,
            DeliveredWood = wood,
            DeliveredStone = stone,
            CompletedWork = work
        };

        Assert.Throws<ArgumentException>(structure.Validate);
    }
}
