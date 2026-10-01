using LittleAges.Simulation;

namespace LittleAges.Headless;

/// <summary>M16 calibration evidence: how tidy each settlement's layout is at the end of a run. Deterministic, derived from canonical state.</summary>
public sealed record HeadlessLayoutSummary
{
    public long SettlementId { get; init; }
    public IReadOnlyDictionary<string, int> BuildingsByKind { get; init; } = new Dictionary<string, int>();
    public int StorageSpread { get; init; }
    public double StorageNearestNeighbour { get; init; }
    public int MixedNeighbourPercent { get; init; }
    public long StorageCapacity { get; init; }

    internal static IReadOnlyList<HeadlessLayoutSummary> Build(SimulationEngine engine) =>
        engine.CaptureLayouts().Select(layout => new HeadlessLayoutSummary
        {
            SettlementId = layout.SettlementId,
            BuildingsByKind = layout.CountsByKind,
            StorageSpread = layout.StorageSpread,
            StorageNearestNeighbour = layout.StorageNearestNeighbour,
            MixedNeighbourPercent = layout.MixedNeighbourPercent,
            StorageCapacity = layout.StorageCapacity
        }).ToArray();
}
