using System.Text;
using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>One building on the map, grouped by the part of village life it serves.</summary>
public sealed record LayoutBuilding(TileCoordinate Location, string Kind, string Group);

/// <summary>
/// Operational evidence for how tidy a settlement's layout is. Derived from canonical state for
/// observers and calibration only; never fingerprinted or checkpointed.
/// </summary>
public sealed record SettlementLayout(long SettlementId, TileCoordinate Site, IReadOnlyList<LayoutBuilding> Buildings)
{
    /// <summary>What fills the settlement's storage, by good, and its total capacity.</summary>
    public IReadOnlyDictionary<string, long> Stored { get; init; } = new Dictionary<string, long>();
    public long StorageCapacity { get; init; }

    /// <summary>Largest tile distance between two storage buildings (stockpiles, storehouses, granaries).</summary>
    public int StorageSpread => Spread("Storage");

    /// <summary>Mean tile distance from each storage building to its nearest other storage building.</summary>
    public double StorageNearestNeighbour => NearestSameGroup("Storage");

    /// <summary>Percent of buildings whose closest neighbour serves a different part of village life.</summary>
    public int MixedNeighbourPercent
    {
        get
        {
            var scored = 0;
            var mixed = 0;
            foreach (var building in Buildings)
            {
                var nearest = Buildings.Where(x => !ReferenceEquals(x, building))
                    .OrderBy(x => SettlementPlan.Chebyshev(x.Location, building.Location)).ThenBy(x => x.Location).FirstOrDefault();
                if (nearest is null) continue;
                scored++;
                if (nearest.Group != building.Group) mixed++;
            }
            return scored == 0 ? 0 : mixed * 100 / scored;
        }
    }

    public IReadOnlyDictionary<string, int> CountsByKind => Buildings.GroupBy(x => x.Kind, StringComparer.Ordinal)
        .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

    private int Spread(string group)
    {
        var members = Buildings.Where(x => x.Group == group).ToArray();
        var spread = 0;
        foreach (var first in members)
            foreach (var second in members)
                spread = Math.Max(spread, SettlementPlan.Chebyshev(first.Location, second.Location));
        return spread;
    }

    private double NearestSameGroup(string group)
    {
        var members = Buildings.Where(x => x.Group == group).ToArray();
        if (members.Length < 2) return 0;
        return Math.Round(members.Average(first => members.Where(x => !ReferenceEquals(x, first)).Min(x => SettlementPlan.Chebyshev(x.Location, first.Location))), 2);
    }

    private static readonly Dictionary<string, char> Glyphs = new(StringComparer.Ordinal)
    {
        ["Shelter"] = 'h', ["Stockpile"] = 's', ["Storehouse"] = 'S', ["Granary"] = 'G', ["Workshop"] = 'W', ["Marketplace"] = 'M',
        ["Farm"] = 'F', ["Field"] = 'f', ["Hearth"] = '*', ["Loom"] = 'L', ["CareHouse"] = 'C', ["Construction"] = '?'
    };

    /// <summary>A plain-text map around the site: letters for buildings, '#' for roads, '=' for trails, '~' for water, '^' for wild land.</summary>
    public string RenderMap(WorldMap world, IReadOnlyCollection<(TileCoordinate Coordinate, RoadGrade Grade)> roads, int radius)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(roads);
        var grades = roads.ToDictionary(x => x.Coordinate, x => x.Grade);
        var byTile = Buildings.GroupBy(x => x.Location).ToDictionary(x => x.Key, x => x.First());
        var text = new StringBuilder();
        for (var y = Math.Max(0, Site.Y - radius); y <= Math.Min(world.Height - 1, Site.Y + radius); y++)
        {
            for (var x = Math.Max(0, Site.X - radius); x <= Math.Min(world.Width - 1, Site.X + radius); x++)
            {
                var tile = new TileCoordinate(x, y);
                var terrain = world.GetTile(tile).Terrain;
                text.Append(tile == Site ? '@'
                    : byTile.TryGetValue(tile, out var building) ? Glyphs.GetValueOrDefault(building.Kind, '?')
                    : grades.GetValueOrDefault(tile) == RoadGrade.Road ? '#'
                    : grades.GetValueOrDefault(tile) == RoadGrade.Trail ? '='
                    : terrain == TerrainType.Freshwater ? '~'
                    : terrain is TerrainType.Forest or TerrainType.DenseWilderness ? '^'
                    : terrain == TerrainType.RockyGround ? ':' : '.');
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}

public sealed partial class SimulationEngine
{
    /// <summary>The built layout of every settlement site, for observers and calibration.</summary>
    public IReadOnlyList<SettlementLayout> CaptureLayouts()
    {
        var sites = MigrationSystemsEnabled(SimulationRulesVersion) ? MigrationSettlementIds : [1L];
        var result = new List<SettlementLayout>();
        foreach (var siteId in sites)
        {
            var buildings = new List<LayoutBuilding>();
            foreach (var structure in (MigrationSystemsEnabled(SimulationRulesVersion) ? StructuresAt(siteId) : _structures.Values).OrderBy(x => x.Id.Value))
                buildings.Add(new(structure.Location, structure.Type.ToString(), GroupOf(structure.Type)));
            if (_living is { } living)
            {
                foreach (var facility in living.Facilities.Where(x => !MigrationSystemsEnabled(SimulationRulesVersion) || SiteIdForFacility(x) == siteId).OrderBy(x => x.Id))
                    buildings.Add(new(facility.Location, facility.Kind.ToString(), facility.Kind == LivingFacilityKind.Hearth ? "Homes" : "Crafts"));
                foreach (var field in living.Fields.Where(x => !MigrationSystemsEnabled(SimulationRulesVersion) || SiteIdForLocation(x.Location) == siteId).OrderBy(x => x.Id))
                    buildings.Add(new(field.Location, "Field", "Farmland"));
            }
            var settlement = SettlementFor(siteId);
            var stored = new SortedDictionary<string, long>(StringComparer.Ordinal)
            {
                ["Food"] = settlement.FoodStored, ["Wood"] = settlement.WoodStored, ["Stone"] = settlement.StoneStored
            };
            if (LivingEnabled)
                foreach (var item in LivingGoodsFor(siteId)) stored[item.Good.ToString()] = stored.GetValueOrDefault(item.Good.ToString()) + item.Quantity;
            if (EconomyEnabled)
            {
                var owned = OwnedStoredGoodsAt(siteId);
                stored["OwnedFood"] = owned.Food; stored["OwnedWood"] = owned.Wood; stored["OwnedStone"] = owned.Stone;
            }
            result.Add(new SettlementLayout(siteId, SiteLocation(siteId), buildings) { Stored = stored, StorageCapacity = StorageCapacityAt(siteId) });
        }
        return result;
    }

    private static string GroupOf(StructureType type) => type switch
    {
        StructureType.Shelter => "Homes",
        StructureType.Stockpile or StructureType.Storehouse or StructureType.Granary => "Storage",
        StructureType.Workshop => "Crafts",
        StructureType.Marketplace => "Core",
        StructureType.Farm => "Farmland",
        _ => type.ToString()
    };
}
