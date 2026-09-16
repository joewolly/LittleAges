namespace LittleAges.Persistence;

public sealed class HouseholdRow
{
    public long Id { get; set; }
    public long CreatedMinute { get; set; }
    public long? DissolvedMinute { get; set; }
    public long? DwellingStructureId { get; set; }
}
