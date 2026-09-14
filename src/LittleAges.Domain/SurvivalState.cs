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

/// <summary>Singleton shared settlement stockpile. M3 has no capacity limit.</summary>
public sealed record SettlementState
{
    public const int SingletonId = 1;
    public int FoodStored { get; set; }
    public int WoodStored { get; set; }
    public int StoneStored { get; set; }
    public SettlementState(int foodStored = 400, int woodStored = 0, int stoneStored = 0)
    {
        FoodStored = Require(foodStored, nameof(foodStored));
        WoodStored = Require(woodStored, nameof(woodStored));
        StoneStored = Require(stoneStored, nameof(stoneStored));
    }
    public SettlementState Validate()
    {
        Require(FoodStored, nameof(FoodStored)); Require(WoodStored, nameof(WoodStored)); Require(StoneStored, nameof(StoneStored));
        return this;
    }
    private static int Require(int value, string name) => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
}
