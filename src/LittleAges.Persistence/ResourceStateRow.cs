namespace LittleAges.Persistence;

/// <summary>Mutable M3 quantity for an immutable resource_nodes definition.</summary>
public sealed class ResourceStateRow
{
    public long ResourceNodeId { get; set; }
    public int CurrentQuantity { get; set; }
    public ResourceNodeRow? ResourceNode { get; set; }
}
