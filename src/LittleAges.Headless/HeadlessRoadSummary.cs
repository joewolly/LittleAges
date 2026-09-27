using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Headless;

/// <summary>M15 calibration evidence: the road overlay and trade history at the end of a run. Operational, not fingerprinted.</summary>
public sealed record HeadlessRoadSummary
{
    public int TrackTiles { get; init; }
    public int TrailTiles { get; init; }
    public int RoadTiles { get; init; }
    public IReadOnlyDictionary<long, int> RoadTilesBuiltBySettlement { get; init; } = new Dictionary<long, int>();
    public long? TrailConnectedMinute { get; init; }
    public long? RoadConnectedMinute { get; init; }
    public int TradesDeparted { get; init; }
    public int TradesCompleted { get; init; }
    public int TradesLost { get; init; }
    public double MeanOutboundQuantity { get; init; }
    public int MaxOutboundQuantity { get; init; }
    public IReadOnlyDictionary<string, int> TradePairs { get; init; } = new Dictionary<string, int>();

    internal static HeadlessRoadSummary Build(SimulationEngine engine, IReadOnlyList<HistoricalEvent> history)
    {
        var grades = engine.RoadGrades;
        var built = new SortedDictionary<long, int>();
        var outbound = new List<int>();
        var pairs = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long? trail = null, road = null;
        foreach (var item in history)
        {
            using var payload = JsonDocument.Parse(item.PayloadJson);
            var root = payload.RootElement;
            switch (item.EventType)
            {
                case HistoricalEventType.RoadWorkSeason:
                    var site = long.Parse(root.GetProperty("settlementId").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    built[site] = built.GetValueOrDefault(site) + root.GetProperty("tilesBuilt").GetInt32();
                    break;
                case HistoricalEventType.RouteConnected:
                    if (root.GetProperty("grade").GetString() == nameof(RoadGrade.Trail)) trail ??= item.WorldMinute;
                    else road ??= item.WorldMinute;
                    break;
                case HistoricalEventType.TradeDeparted:
                    outbound.Add(root.GetProperty("outboundQuantity").GetInt32());
                    var key = $"{root.GetProperty("outboundGood")} > {root.GetProperty("returnGood")}";
                    pairs[key] = pairs.GetValueOrDefault(key) + 1;
                    break;
            }
        }
        return new HeadlessRoadSummary
        {
            TrackTiles = grades.Count(x => x.Grade == RoadGrade.Track),
            TrailTiles = grades.Count(x => x.Grade == RoadGrade.Trail),
            RoadTiles = grades.Count(x => x.Grade == RoadGrade.Road),
            RoadTilesBuiltBySettlement = built,
            TrailConnectedMinute = trail,
            RoadConnectedMinute = road,
            TradesDeparted = outbound.Count,
            TradesCompleted = history.Count(x => x.EventType == HistoricalEventType.TradeCompleted),
            TradesLost = history.Count(x => x.EventType == HistoricalEventType.TradeLost),
            MeanOutboundQuantity = outbound.Count == 0 ? 0 : outbound.Average(),
            MaxOutboundQuantity = outbound.DefaultIfEmpty(0).Max(),
            TradePairs = pairs
        };
    }
}
