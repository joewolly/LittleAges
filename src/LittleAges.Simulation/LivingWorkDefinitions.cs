using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Rules-versioned recipes. Checkpoints cannot redefine the cost of production.</summary>
public static class LivingWorkDefinitions
{
    public static int Work(LivingWorkKind kind) => kind switch
    {
        LivingWorkKind.EstablishField => 360,
        LivingWorkKind.Sow or LivingWorkKind.MakeTool or LivingWorkKind.Weave or LivingWorkKind.Teach or LivingWorkKind.Hunt => 240,
        LivingWorkKind.BuildHearth or LivingWorkKind.BuildLoom or LivingWorkKind.BuildCareHouse or LivingWorkKind.Experiment => 480,
        _ => 120
    };

    public static IReadOnlyList<LivingIngredient> Ingredients(LivingWorkKind kind) => kind switch
    {
        LivingWorkKind.EstablishField => [new("Wood", 4)],
        LivingWorkKind.Cook => [new("Grain", 20), new("Fuel", 1)],
        LivingWorkKind.Preserve => [new("Grain", 20), new("Fuel", 2)],
        LivingWorkKind.CutFuel => [new("Wood", 10)],
        LivingWorkKind.MakeTool => [new("Wood", 4), new("Stone", 2)],
        LivingWorkKind.Weave => [new("Fiber", 8)],
        LivingWorkKind.PrepareMedicine => [new("Food", 5), new("Fiber", 2)],
        LivingWorkKind.BuildHearth or LivingWorkKind.BuildLoom or LivingWorkKind.BuildCareHouse => [new("Wood", 12), new("Stone", 6)],
        LivingWorkKind.EquipTool => [new("Tool", 1)],
        LivingWorkKind.EquipClothing => [new("Clothing", 1)],
        _ => []
    };

    public static bool ValidIngredients(LivingWorkOrder order) => order.Kind == LivingWorkKind.Care
        ? order.Ingredients.All(x => x is { Resource: "Medicine", Quantity: 1 } or { Resource: "Food", Quantity: 2 })
        : order.Ingredients.SequenceEqual(Ingredients(order.Kind));

    public static bool ValidCargo(LivingWorkOrder order)
    {
        if (!order.Produced) return order.Cargo.Count == 0;
        return order.Kind switch
        {
            LivingWorkKind.Harvest => order.Cargo.Count == 2 && order.Cargo[0] is { Good: LivingGood.Grain, Quantity: > 0 and <= 60 } && order.Cargo[1] is { Good: LivingGood.Fiber, Quantity: 5 },
            LivingWorkKind.Hunt => order.Cargo.Count == 0 || order.Cargo.SequenceEqual([new LivingStock(LivingGood.Meal, 40), new LivingStock(LivingGood.Hide, 5)]),
            LivingWorkKind.Cook => Single(LivingGood.Meal, 30),
            LivingWorkKind.Preserve => Single(LivingGood.PreservedFood, 20),
            LivingWorkKind.CutFuel => Single(LivingGood.Fuel, 10),
            LivingWorkKind.MakeTool => Single(LivingGood.Tool, 1),
            LivingWorkKind.Weave => Single(LivingGood.Clothing, 1),
            LivingWorkKind.PrepareMedicine => Single(LivingGood.Medicine, 5),
            _ => order.Cargo.Count == 0
        };
        bool Single(LivingGood good, int quantity) => order.Cargo.Count == 1 && order.Cargo[0] == new LivingStock(good, quantity);
    }
}
