using System.Globalization;
using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class M6HistoryContractTests
{
    private static readonly int[] HistoricalEventValues = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
    private static readonly int[] HistoricalImportanceValues = [0, 1, 2, 3, 4, 5];
    private static readonly int[] HistoricalOriginValues = [1, 2];

    [Fact]
    public void PersistedEnumsAndImportanceAreExplicitAndStable()
    {
        Assert.Equal(HistoricalEventValues,
            Enum.GetValues<HistoricalEventType>().Select(value => (int)value));
        Assert.Equal(HistoricalImportanceValues,
            Enum.GetValues<HistoricalImportance>().Select(value => (int)value));
        Assert.Equal(HistoricalOriginValues,
            Enum.GetValues<HistoricalEventOrigin>().Select(value => (int)value));
        Assert.Equal(1, (int)HistoricalEventType.WorldCreated);
        Assert.Equal(15, (int)HistoricalEventType.SeasonStarted);
        Assert.Equal(5, (int)HistoricalImportance.Historic);
        Assert.Equal(1, HistoricalEvent.CurrentSchemaVersion);
    }

    [Fact]
    public void EveryM6PayloadBuilderProducesTheLockedCanonicalShape()
    {
        Assert.Equal("{\"seed\":\"42\"}", HistoricalEventPayloads.WorldCreated(new WorldSeed(42)));
        Assert.Equal("{\"founderCount\":20}", HistoricalEventPayloads.SettlementFounded(20));
        Assert.Equal("{}", HistoricalEventPayloads.CitizenBorn(null));
        Assert.Equal("{\"householdId\":\"21\"}", HistoricalEventPayloads.CitizenBorn(new HouseholdId(21)));
        Assert.Equal("{\"cause\":\"natural\",\"ageYears\":80}", HistoricalEventPayloads.CitizenDied("natural", 80));
        Assert.Equal("{\"householdId\":\"21\"}", HistoricalEventPayloads.PartnershipFormed(new HouseholdId(21)));
        Assert.Equal("{\"householdId\":\"21\"}", HistoricalEventPayloads.HouseholdCreated(new HouseholdId(21)));
        Assert.Equal("{\"familiarity\":7000,\"affinity\":6000,\"trust\":5000,\"conflict\":0}", HistoricalEventPayloads.Relationship(7000, 6000, 5000, 0));
        Assert.Equal("{\"structureType\":\"Shelter\",\"requiredWood\":40,\"requiredStone\":10,\"requiredWork\":600}", HistoricalEventPayloads.StructureStarted(StructureType.Shelter, 40, 10, 600));
        Assert.Equal("{\"structureType\":\"Shelter\"}", HistoricalEventPayloads.StructureCompleted(StructureType.Shelter));
        Assert.Equal("{\"population\":25}", HistoricalEventPayloads.PopulationMilestone(25));
        Assert.Equal("{\"resourceType\":\"Food\",\"quantity\":199,\"livingPopulation\":20,\"preexistingAtHistoryStart\":false}", HistoricalEventPayloads.ResourceShortage(ResourceType.Food, 199, 20));
        Assert.Equal("{\"from\":\"Generalist\",\"to\":\"Forager\"}", HistoricalEventPayloads.SpecializationChanged(CitizenOccupation.Generalist, "Forager"));
        Assert.Equal("{\"season\":\"Autumn\",\"year\":7}", HistoricalEventPayloads.SeasonStarted(WorldSeason.Autumn, 7));

        foreach (var (type, payload) in new[]
        {
            (HistoricalEventType.WorldCreated, HistoricalEventPayloads.WorldCreated(new WorldSeed(42))),
            (HistoricalEventType.SettlementFounded, HistoricalEventPayloads.SettlementFounded(20)),
            (HistoricalEventType.CitizenBorn, HistoricalEventPayloads.CitizenBorn(null)),
            (HistoricalEventType.CitizenBorn, HistoricalEventPayloads.CitizenBorn(new HouseholdId(21))),
            (HistoricalEventType.CitizenDied, HistoricalEventPayloads.CitizenDied("natural", 80)),
            (HistoricalEventType.PartnershipFormed, HistoricalEventPayloads.PartnershipFormed(new HouseholdId(21))),
            (HistoricalEventType.FriendshipFormed, HistoricalEventPayloads.Relationship(7000, 6000, 5000, 0)),
            (HistoricalEventType.RivalryFormed, HistoricalEventPayloads.Relationship(7000, -6000, 5000, 4000)),
            (HistoricalEventType.HouseholdCreated, HistoricalEventPayloads.HouseholdCreated(new HouseholdId(21))),
            (HistoricalEventType.StructureStarted, HistoricalEventPayloads.StructureStarted(StructureType.Shelter, 40, 10, 600)),
            (HistoricalEventType.StructureCompleted, HistoricalEventPayloads.StructureCompleted(StructureType.Shelter)),
            (HistoricalEventType.PopulationMilestone, HistoricalEventPayloads.PopulationMilestone(25)),
            (HistoricalEventType.ResourceShortageStarted, HistoricalEventPayloads.ResourceShortage(ResourceType.Food, 199, 20)),
            (HistoricalEventType.ResourceShortageEnded, HistoricalEventPayloads.ResourceShortage(ResourceType.Food, 400, 20)),
            (HistoricalEventType.CitizenSpecializationChanged, HistoricalEventPayloads.SpecializationChanged(CitizenOccupation.Generalist, "Forager")),
            (HistoricalEventType.SeasonStarted, HistoricalEventPayloads.SeasonStarted(WorldSeason.Autumn, 7))
        })
        {
            Assert.True(HistoricalEventPayloads.IsCanonical(type, payload), $"{type}: {payload}");
        }
    }

    [Fact]
    public void CanonicalPayloadsAreInvariantAndRejectReorderingExtrasAndInvalidValues()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = custom;
            CultureInfo.CurrentUICulture = custom;
            Assert.Equal("{\"resourceType\":\"Food\",\"quantity\":199,\"livingPopulation\":20,\"preexistingAtHistoryStart\":false}", HistoricalEventPayloads.ResourceShortage(ResourceType.Food, 199, 20));
            Assert.True(HistoricalEventPayloads.IsCanonical(HistoricalEventType.WorldCreated, "{\"seed\":\"18446744073709551615\"}"));
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }

        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.WorldCreated, "{\"seed\":\"042\"}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.SettlementFounded, "{\"founderCount\":20,\"extra\":1}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.SettlementFounded, "{\"founderCount\":20.0}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.CitizenDied, "{\"ageYears\":80,\"cause\":\"natural\"}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.CitizenDied, "{\"cause\":\"unknown\",\"ageYears\":80}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.CitizenSpecializationChanged, "{\"from\":\"Forager\",\"to\":\"Forager\"}"));
        Assert.False(HistoricalEventPayloads.IsCanonical(HistoricalEventType.ResourceShortageStarted, "{\"resourceType\":\"Invalid\",\"quantity\":1,\"livingPopulation\":20,\"preexistingAtHistoryStart\":false}"));
    }

    [Fact]
    public void EventAndLinkValidationRejectsCorruptionAndRequiresEventSpecificRoles()
    {
        var worldCreated = new HistoricalEvent(new HistoricalEventId(1), 0, HistoricalEventType.WorldCreated,
            HistoricalImportance.Historic, HistoricalEventOrigin.Live, null, HistoricalEventPayloads.WorldCreated(new WorldSeed(42)));
        worldCreated.Validate(0);
        worldCreated.ValidateLinks([], []);

        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoricalEvent(new HistoricalEventId(0), 0, HistoricalEventType.WorldCreated,
            HistoricalImportance.Historic, HistoricalEventOrigin.Live, null, HistoricalEventPayloads.WorldCreated(new WorldSeed(42))).Validate(0));
        Assert.Throws<ArgumentException>(() => new HistoricalEvent(new HistoricalEventId(1), 1, HistoricalEventType.WorldCreated,
            HistoricalImportance.Historic, HistoricalEventOrigin.Live, null, HistoricalEventPayloads.WorldCreated(new WorldSeed(42))).Validate(0));
        Assert.Throws<NotSupportedException>(() => new HistoricalEvent(new HistoricalEventId(1), 0, HistoricalEventType.WorldCreated,
            HistoricalImportance.Historic, HistoricalEventOrigin.Live, null, HistoricalEventPayloads.WorldCreated(new WorldSeed(42)), 2).Validate(0));

        var born = new HistoricalEvent(new HistoricalEventId(2), 0, HistoricalEventType.CitizenBorn,
            HistoricalImportance.Notable, HistoricalEventOrigin.MigrationBackfill, null, HistoricalEventPayloads.CitizenBorn(new HouseholdId(21)));
        Assert.Throws<ArgumentException>(() => born.ValidateLinks(
            [new HistoricalEventCitizenLink(born.Id, new CitizenId(3), "subject")], []));
        Assert.Throws<ArgumentException>(() => born.ValidateLinks(
            [new HistoricalEventCitizenLink(born.Id, new CitizenId(3), "subject"), new HistoricalEventCitizenLink(born.Id, new CitizenId(1), "parent"), new HistoricalEventCitizenLink(born.Id, new CitizenId(2), "parent"), new HistoricalEventCitizenLink(born.Id, new CitizenId(2), "parent")], []));
        Assert.Throws<ArgumentException>(() => worldCreated.ValidateLinks([], [new HistoricalEventStructureLink(worldCreated.Id, new StructureId(4))]));

        var state = new HistoryState(historyStartMinute: 0, historyStartEventId: 7, periodStartMinute: 0);
        Assert.Same(state, state.Validate(0));
        Assert.Throws<ArgumentException>(() => new HistoryState(historyStartEventId: 0).Validate());
        Assert.Throws<ArgumentException>(() => new HistoryState(periodStartMinute: 1).Validate(0));
        Assert.Throws<ArgumentException>(() => new StatisticsSample(1, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).Validate(1));
        Assert.Throws<ArgumentException>(() => new CitizenMemory(new CitizenId(1), new HistoricalEventId(1), MemoryType.ChildBorn, HistoricalImportance.Personal, 10_001, 0).Validate(0));
    }
}
