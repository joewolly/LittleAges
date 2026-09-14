namespace LittleAges.Persistence;

public sealed class StructureRow
{
    public long Id { get; set; }
    public int Type { get; set; }
    public int Status { get; set; }
    public int Condition { get; set; }
    public int LocationX { get; set; }
    public int LocationY { get; set; }
    public long ConstructionStartedMinute { get; set; }
    public long? CompletedMinute { get; set; }
    public int RequiredWood { get; set; }
    public int DeliveredWood { get; set; }
    public int RequiredStone { get; set; }
    public int DeliveredStone { get; set; }
    public int RequiredWork { get; set; }
    public int CompletedWork { get; set; }
}
