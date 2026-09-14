namespace LittleAges.Domain;

/// <summary>Mutable quantity for an immutable M1 resource definition.</summary>
public sealed record ResourceState
{
    public ResourceState(ResourceNodeId resourceNodeId, int currentQuantity)
    {
        WorldIdValidation.RequirePositive(resourceNodeId.Value, nameof(resourceNodeId));
        ArgumentOutOfRangeException.ThrowIfNegative(currentQuantity);
        ResourceNodeId = resourceNodeId;
        CurrentQuantity = currentQuantity;
    }

    public ResourceNodeId ResourceNodeId { get; }
    public int CurrentQuantity { get; set; }
    public ResourceState Validate(ResourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Id != ResourceNodeId || CurrentQuantity is < 0 || CurrentQuantity > node.MaximumQuantity)
            throw new ArgumentException("Resource state is outside the immutable node bounds.", nameof(node));
        return this;
    }
}

/// <summary>Singleton shared settlement stockpile and M4 settlement boundaries.</summary>
public sealed record SettlementState
{
    public const int SingletonId = 1;
    public int FoodStored { get; set; }
    public int WoodStored { get; set; }
    public int StoneStored { get; set; }
    public int BaseStorageCapacity { get; set; }
    public long DemandUpdatedMinute { get; set; }
    public long ExposureConsequencesStartMinute { get; set; }
    public int StorageUsed => checked(FoodStored + WoodStored + StoneStored);
    public SettlementState(int foodStored = 400, int woodStored = 0, int stoneStored = 0, int baseStorageCapacity = 0, long demandUpdatedMinute = 0, long exposureConsequencesStartMinute = long.MaxValue)
    {
        FoodStored = Require(foodStored, nameof(foodStored));
        WoodStored = Require(woodStored, nameof(woodStored));
        StoneStored = Require(stoneStored, nameof(stoneStored));
        BaseStorageCapacity = Require(baseStorageCapacity, nameof(baseStorageCapacity));
        DemandUpdatedMinute = RequireMinute(demandUpdatedMinute, nameof(demandUpdatedMinute));
        ExposureConsequencesStartMinute = RequireMinute(exposureConsequencesStartMinute, nameof(exposureConsequencesStartMinute));
    }
    public SettlementState Validate()
    {
        Require(FoodStored, nameof(FoodStored)); Require(WoodStored, nameof(WoodStored)); Require(StoneStored, nameof(StoneStored)); Require(BaseStorageCapacity, nameof(BaseStorageCapacity)); RequireMinute(DemandUpdatedMinute, nameof(DemandUpdatedMinute)); RequireMinute(ExposureConsequencesStartMinute, nameof(ExposureConsequencesStartMinute));
        return this;
    }
    private static int Require(int value, string name) => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
    private static long RequireMinute(long value, string name) => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
}
