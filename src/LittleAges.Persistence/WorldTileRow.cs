namespace LittleAges.Persistence;

/// <summary>Canonical persisted geography row. TileIndex is the stable row-major identity.</summary>
public sealed class WorldTileRow
{
    public long TileIndex { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Terrain { get; set; }
    public int Elevation { get; set; }
    public int Fertility { get; set; }
    public int WaterAccess { get; set; }
    public bool Walkable { get; set; }
    public int MovementCost { get; set; }
    public List<ResourceNodeRow> Resources { get; } = [];
}
