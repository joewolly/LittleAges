namespace LittleAges.Persistence;

public sealed class RelationshipRow
{
    public long CitizenAId { get; set; }
    public long CitizenBId { get; set; }
    public int Familiarity { get; set; }
    public int Affinity { get; set; }
    public int Trust { get; set; }
    public int Conflict { get; set; }
    public long LastInteractionMinute { get; set; }
    public long InteractionCount { get; set; }
}
