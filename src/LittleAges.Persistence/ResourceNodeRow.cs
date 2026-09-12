namespace LittleAges.Persistence;

/// <summary>Canonical persisted deterministic resource node.</summary>
public sealed class ResourceNodeRow
{
    public long Id { get; set; }
    public long TileIndex { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Resource { get; set; }
    public int InitialQuantity { get; set; }
    public int MaximumQuantity { get; set; }
    public int RegenerationPotential { get; set; }
    public WorldTileRow? Tile { get; set; }
}
