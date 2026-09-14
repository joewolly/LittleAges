namespace LittleAges.Persistence;

/// <summary>Singleton shared M3 settlement stockpile.</summary>
public sealed class SettlementStateRow
{
    public int Id { get; set; }
    public int FoodStored { get; set; }
    public int WoodStored { get; set; }
    public int StoneStored { get; set; }
}
