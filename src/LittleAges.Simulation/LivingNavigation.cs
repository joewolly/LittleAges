using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private Dictionary<ResourceType, ResourceNode[]>? _livingResourceNodes;

    private ResourceNode? SelectLivingResourceTarget(ResourceType type, IReadOnlyDictionary<TileCoordinate, long> costs, long? settlementId = null)
    {
        _livingResourceNodes ??= World.Resources.GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.ToArray());
        if (!_livingResourceNodes.TryGetValue(type, out var nodes)) return null;
        ResourceNode? best = null;
        long bestCost = long.MaxValue;
        var bestQuantity = -1;
        foreach (var node in nodes)
        {
            var quantity = _resourceStates[node.Id.Value].CurrentQuantity;
            if (quantity <= 0 || settlementId is { } site && SiteIdForResource(node) != site || !costs.TryGetValue(node.Coordinate, out var cost)) continue;
            if (best is not null && (cost > bestCost || cost == bestCost && (quantity < bestQuantity || quantity == bestQuantity && node.Id.Value >= best.Id.Value))) continue;
            best = node;
            bestCost = cost;
            bestQuantity = quantity;
        }
        return best;
    }
}
