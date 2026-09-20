using System.Globalization;
using LittleAges.Domain;

namespace LittleAges.Server;

public sealed record FarmObservation(string StructureId, long Year, string Stage, int PlantingWork,
    int TendingWork, int Yield, int Remaining, int Harvested);
public sealed record HarvestObservation(string StructureId, long Year, int Yield, int Harvested, int Lost);
public sealed record AgricultureObservation(int Version, int Food, int DedicatedFoodCapacity,
    int WinterReserveTarget, int ProjectedCoverageDays, IReadOnlyList<FarmObservation> Farms,
    IReadOnlyList<HarvestObservation> Harvests)
{
    public static AgricultureObservation? Create(AgricultureState? state, int food, int granaryCapacity, int population)
    {
        if (state is null) return null;
        return new(1, food, granaryCapacity, checked(population * 810), population == 0 ? 0 : food / Math.Max(1, checked(population * 9)),
            Array.AsReadOnly(state.Farms.Select(f => new FarmObservation(f.StructureId.ToString(CultureInfo.InvariantCulture), f.Year, f.Stage.ToString(), f.PlantingWork, f.TendingWork, f.Yield, f.Remaining, f.Harvested)).ToArray()),
            Array.AsReadOnly(state.Harvests.Select(h => new HarvestObservation(h.StructureId.ToString(CultureInfo.InvariantCulture), h.Year, h.Yield, h.Harvested, h.Lost)).ToArray()));
    }
}
