namespace LittleAges.Domain;

/// <summary>Persistent M4 settlement structure kinds. Values are compatibility data.</summary>
public enum StructureType : int
{
    Shelter = 1,
    Stockpile = 2,
    Workshop = 3,
    Farm = 4,
    Granary = 5,
    Marketplace = 6
}

/// <summary>Persistent M4 construction lifecycle values. Values are compatibility data.</summary>
public enum StructureStatus : int
{
    UnderConstruction = 1,
    Complete = 2
}

/// <summary>Immutable construction requirements for each persistent M4 structure type.</summary>
public static class StructureDefinitions
{
    public const int ShelterRequiredWood = 40;
    public const int ShelterRequiredStone = 10;
    public const int ShelterRequiredWork = 600;
    public const int StockpileRequiredWood = 60;
    public const int StockpileRequiredStone = 30;
    public const int StockpileRequiredWork = 900;
    public const int WorkshopRequiredWood = 80;
    public const int WorkshopRequiredStone = 50;
    public const int WorkshopRequiredWork = 1200;

    public static bool HasCanonicalRequirements(StructureType type, int requiredWood, int requiredStone, int requiredWork) =>
        (type, requiredWood, requiredStone, requiredWork) switch
        {
            (StructureType.Shelter, ShelterRequiredWood, ShelterRequiredStone, ShelterRequiredWork) => true,
            (StructureType.Stockpile, StockpileRequiredWood, StockpileRequiredStone, StockpileRequiredWork) => true,
            (StructureType.Workshop, WorkshopRequiredWood, WorkshopRequiredStone, WorkshopRequiredWork) => true,
            (StructureType.Farm, AgricultureRules.FarmWood, AgricultureRules.FarmStone, AgricultureRules.FarmWork) => true,
            (StructureType.Granary, AgricultureRules.GranaryWood, AgricultureRules.GranaryStone, AgricultureRules.GranaryWork) => true,
            (StructureType.Marketplace, EconomyRules.MarketWood, EconomyRules.MarketStone, EconomyRules.MarketWork) => true,
            _ => false
        };
}

/// <summary>Canonical persistent construction project or completed structure.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not contain type names", Justification = "Structure is the canonical public M4 domain entity.")]
public sealed class Structure
{
    public Structure(StructureId id, StructureType type, TileCoordinate location, long constructionStartedMinute,
        int requiredWood, int requiredStone, int requiredWork)
    {
        if (!Enum.IsDefined(type) || constructionStartedMinute < 0 || requiredWood < 0 || requiredStone < 0 || requiredWork <= 0)
            throw new ArgumentOutOfRangeException(nameof(type));
        Id = id; Type = type; Location = location; ConstructionStartedMinute = constructionStartedMinute;
        RequiredWood = requiredWood; RequiredStone = requiredStone; RequiredWork = requiredWork;
        Status = StructureStatus.UnderConstruction;
    }

    public StructureId Id { get; }
    public StructureId StructureId => Id;
    public StructureType Type { get; }
    public StructureStatus Status { get; set; }
    public TileCoordinate Location { get; }
    public long ConstructionStartedMinute { get; }
    public long? CompletedMinute { get; set; }
    public int RequiredWood { get; }
    public int DeliveredWood { get; set; }
    public int RequiredStone { get; }
    public int DeliveredStone { get; set; }
    public int RequiredWork { get; }
    public int CompletedWork { get; set; }
    public int Condition => Status == StructureStatus.Complete ? 10000 : 0;

    public Structure Validate()
    {
        if (!Enum.IsDefined(Type) || !Enum.IsDefined(Status) || !StructureDefinitions.HasCanonicalRequirements(Type, RequiredWood, RequiredStone, RequiredWork) || ConstructionStartedMinute < 0 || DeliveredWood < 0 || DeliveredWood > RequiredWood || DeliveredStone < 0 || DeliveredStone > RequiredStone || CompletedWork < 0 || CompletedWork > RequiredWork || (CompletedWork > 0 && (DeliveredWood != RequiredWood || DeliveredStone != RequiredStone)) || (Status == StructureStatus.UnderConstruction && (CompletedMinute is not null || CompletedWork == RequiredWork)) || (Status == StructureStatus.Complete && (CompletedMinute is null || DeliveredWood != RequiredWood || DeliveredStone != RequiredStone || CompletedWork != RequiredWork)))
            throw new ArgumentException("Structure canonical state is invalid.");
        return this;
    }
}

/// <summary>Cumulative, per-citizen material and work contribution to one structure.</summary>
public sealed class StructureContribution
{
    public StructureContribution(StructureId structureId, CitizenId citizenId, int constructionWork = 0, int woodDelivered = 0, int stoneDelivered = 0)
    {
        if (constructionWork < 0 || woodDelivered < 0 || stoneDelivered < 0) throw new ArgumentOutOfRangeException(nameof(constructionWork));
        StructureId = structureId; CitizenId = citizenId; ConstructionWork = constructionWork; WoodDelivered = woodDelivered; StoneDelivered = stoneDelivered;
    }
    public StructureId StructureId { get; }
    public CitizenId CitizenId { get; }
    public int ConstructionWork { get; set; }
    public int WoodDelivered { get; set; }
    public int StoneDelivered { get; set; }
    public StructureContribution Validate()
    {
        if (ConstructionWork < 0 || WoodDelivered < 0 || StoneDelivered < 0) throw new ArgumentException("Structure contribution cannot be negative.");
        return this;
    }
}

public static class CitizenOccupation
{
    public const string Generalist = "Generalist";
    public static string Derive(Citizen citizen)
    {
        ArgumentNullException.ThrowIfNull(citizen);
        var values = new[] { (citizen.LifetimeForagingMinutes, "Forager", 0), (citizen.LifetimeWoodcuttingMinutes, "Lumberjack", 1), (citizen.LifetimeStoneworkingMinutes, "Stoneworker", 2), (citizen.LifetimeConstructionMinutes, "Builder", 3), (citizen.LifetimeHaulingMinutes, "Hauler", 4) };
        var total = values.Sum(static x => x.Item1);
        if (total < 360) return Generalist;
        var selected = values.OrderByDescending(static x => x.Item1).ThenBy(static x => x.Item3).First();
        return selected.Item1 * 100 < total * 40 ? Generalist : selected.Item2;
    }
}
