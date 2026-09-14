namespace LittleAges.Persistence;

public sealed class StructureContributionRow
{
    public long StructureId { get; set; }
    public long CitizenId { get; set; }
    public int ConstructionWork { get; set; }
    public int WoodDelivered { get; set; }
    public int StoneDelivered { get; set; }
}
