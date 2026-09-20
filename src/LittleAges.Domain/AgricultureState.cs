using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleAges.Domain;

public enum CropStage { Fallow = 0, Planted = 1, Growing = 2, Harvest = 3, Dormant = 4 }

public static class AgricultureRules
{
    public const int PlantingWork = 1200;
    public const int TendingWork = 2400;
    public const int WorkPerShift = 100;
    public const int HarvestPerShift = 40;
    public const int GranaryFoodCapacity = 10000;
    public const int FarmWood = 60;
    public const int FarmStone = 10;
    public const int FarmWork = 900;
    public const int GranaryWood = 100;
    public const int GranaryStone = 60;
    public const int GranaryWork = 1500;
    public static bool Suitable(WorldTile tile) => tile.Buildable && tile.Fertility >= 3000 && tile.WaterAccess >= 2000;
    public static int PotentialYield(WorldTile tile) => 3000 + (tile.Fertility + tile.WaterAccess) * 9000 / 20000;
    public static int LaborYield(WorldTile tile, int planting, int tending) => checked((int)((long)PotentialYield(tile) * planting / PlantingWork * (2500 + 7500 * tending / TendingWork) / 10000));
}

public sealed record FarmCrop(long StructureId, long Year, CropStage Stage, int PlantingWork,
    int TendingWork, int Yield, int Remaining, int Harvested);
public sealed record HarvestRecord(long StructureId, long Year, int PlantingWork, int TendingWork,
    int Yield, int Harvested, int Lost);

public sealed record AgricultureState(int Version, long SeasonIndex, IReadOnlyList<FarmCrop> Farms,
    IReadOnlyList<HarvestRecord> Harvests)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public AgricultureState Copy() => new(Version, SeasonIndex,
        Array.AsReadOnly(Farms.ToArray()), Array.AsReadOnly(Harvests.ToArray()));
    public string ToCanonicalJson() => JsonSerializer.Serialize(this, Options);
    public static AgricultureState Parse(string json)
    {
        var value = JsonSerializer.Deserialize<AgricultureState>(json, Options) ?? throw new InvalidDataException("Missing agriculture state.");
        if (value.Farms is null || value.Harvests is null || value.ToCanonicalJson() != json)
            throw new InvalidDataException("Agriculture state is not canonical JSON.");
        return value;
    }

    public void Validate(WorldMap world, IReadOnlyList<Structure> structures, IReadOnlyList<Citizen> citizens, long demandMinute, long? currentMinute = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(citizens);
        if (Version != 1 || SeasonIndex != demandMinute / (WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay) || Farms is null || Harvests is null)
            throw new ArgumentException("Invalid agriculture version or seasonal boundary.");
        var farmStructures = structures.Where(s => s.Type == StructureType.Farm).OrderBy(s => s.Id.Value).ToArray();
        if (!Farms.Select(f => f.StructureId).SequenceEqual(farmStructures.Select(s => s.Id.Value)))
            throw new ArgumentException("Every farm requires exactly one ordered crop state.");
        foreach (var farm in Farms)
        {
            var structure = farmStructures.Single(s => s.Id.Value == farm.StructureId);
            if (!AgricultureRules.Suitable(world.GetTile(structure.Location)) || farm.Year != SeasonIndex / 4 ||
                !Enum.IsDefined(farm.Stage) || farm.PlantingWork is < 0 or > AgricultureRules.PlantingWork ||
                farm.TendingWork is < 0 or > AgricultureRules.TendingWork || farm.Yield < 0 || farm.Remaining < 0 || farm.Harvested < 0 ||
                farm.Remaining + farm.Harvested > farm.Yield || farm.Yield > AgricultureRules.PotentialYield(world.GetTile(structure.Location)) ||
                (structure.Status != StructureStatus.Complete && (farm.PlantingWork != 0 || farm.TendingWork != 0 || farm.Yield != 0)))
                throw new ArgumentException("Invalid canonical crop state.");
            var season = SeasonIndex % 4;
            var stageValid = farm.Stage switch
            {
                CropStage.Fallow => farm.PlantingWork == 0 && farm.TendingWork == 0 && farm.Yield == 0,
                CropStage.Planted => season == 0 && farm.PlantingWork > 0 && farm.TendingWork == 0 && farm.Yield == 0,
                CropStage.Growing => season == 1 && farm.Yield == 0,
                CropStage.Harvest => season == 2 && farm.Remaining + farm.Harvested == farm.Yield,
                CropStage.Dormant => season == 3 && farm.Remaining == 0,
                _ => false
            };
            if (!stageValid || farm.Stage is CropStage.Harvest or CropStage.Dormant && farm.Yield != AgricultureRules.LaborYield(world.GetTile(structure.Location), farm.PlantingWork, farm.TendingWork))
                throw new ArgumentException("Crop stage or yield does not match season and completed labor.");
        }
        if (!Harvests.SequenceEqual(Harvests.OrderBy(h => h.Year).ThenBy(h => h.StructureId)) ||
            Harvests.Select(h => (h.StructureId, h.Year)).Distinct().Count() != Harvests.Count)
            throw new ArgumentException("Harvest records must be unique and ordered.");
        foreach (var harvest in Harvests)
            if (!farmStructures.Any(s => s.Id.Value == harvest.StructureId) || harvest.Year < 0 || harvest.Year > (SeasonIndex >= 3 ? (SeasonIndex - 3) / 4 : -1) ||
                harvest.PlantingWork is < 0 or > AgricultureRules.PlantingWork || harvest.TendingWork is < 0 or > AgricultureRules.TendingWork ||
                harvest.Yield < 0 || harvest.Harvested < 0 || harvest.Lost < 0 || harvest.Harvested + harvest.Lost != harvest.Yield)
                throw new ArgumentException("Invalid harvest conservation record.");
            else if (harvest.Yield != AgricultureRules.LaborYield(world.GetTile(farmStructures.Single(s => s.Id.Value == harvest.StructureId).Location), harvest.PlantingWork, harvest.TendingWork))
                throw new ArgumentException("Harvest yield does not match its land and labor.");
        foreach (var citizen in citizens.Where(c => c.CurrentAction is CitizenAction.WorkFarm or CitizenAction.HaulHarvest))
        {
            var farm = farmStructures.SingleOrDefault(s => s.Id == citizen.TargetStructureId);
            if (farm is null || farm.Status != StructureStatus.Complete || !citizen.IsAlive || citizen.AgeYears(new WorldMinute(currentMinute ?? demandMinute)) < 13 ||
                (citizen.ActionPhase == CitizenActionPhase.TravelToTarget && citizen.ActionTarget != farm.Location) ||
                (citizen.ActionPhase == CitizenActionPhase.Perform && citizen.Location != farm.Location) ||
                (citizen.ActionPhase == CitizenActionPhase.ReturnToStockpile && citizen.ActionTarget != world.StartingSite))
                throw new ArgumentException("Farm work must reference a completed reachable farm.");
        }
    }
}
