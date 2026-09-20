using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleAges.Domain;

public enum LivingGoal { FamilySecurity = 1, Comfort, Mastery, Exploration }
public enum LivingGood { Grain = 1, Meal, PreservedFood, Fuel, Tool, Clothing, Medicine, Fiber, Hide }
public enum LivingTechnique { Cultivation = 1, Preservation, Toolmaking, Textiles, Care }
public enum LivingWorkKind { EstablishField = 1, Sow, Tend, Harvest, Cook, Preserve, CutFuel, MakeTool, Weave, PrepareMedicine, BuildHearth, BuildLoom, BuildCareHouse, Care, Recreate, Teach, Experiment, Hunt, RepairRelationship, EquipTool, EquipClothing }
public enum LivingWorkPhase { Collect = 1, Travel, Work, Deliver }
public enum LivingFacilityKind { Hearth = 1, Loom, CareHouse }
public enum LivingWeatherKind { Fair = 1, Rain, Drought, ColdSpell }
public enum LivingExperienceKind { Helped = 1, Bereavement, Scarcity, SharedWork, Recreation, Learned }
public enum LivingFactKind { FieldEstablished = 1, FacilityCompleted, FirstHarvest, TechniqueDiscovered, TechniqueTaught, Injury, Recovery, WeatherChanged, Bereavement }

/// <summary>Canonical v0.2 state. Lists have stable order; IDs use an independent, persisted living-world namespace.</summary>
public sealed class LivingWorldState
{
    public int Version { get; set; } = 1;
    public long NextId { get; set; } = 1;
    public long NextFactId { get; set; } = 1;
    public long UpdatedMinute { get; set; }
    public long LastDailyMinute { get; set; }
    public LivingWeatherKind Weather { get; set; } = LivingWeatherKind.Fair;
    public int Temperature { get; set; } = 15;
    public int Rainfall { get; set; } = 50;
    public long CompletedOrders { get; set; }
    public long FoodHarvested { get; set; }
    public long FoodPrepared { get; set; }
    public long GoodsSpoiled { get; set; }
    public long CareGiven { get; set; }
    public List<LivingStock> Stock { get; set; } = [];
    public List<LivingPerson> People { get; set; } = [];
    public List<LivingWorkOrder> Orders { get; set; } = [];
    public List<LivingField> Fields { get; set; } = [];
    public List<LivingFacility> Facilities { get; set; } = [];
    public List<LivingAnimal> Animals { get; set; } = [];
    public List<LivingFact> Facts { get; set; } = [];
}

public sealed class LivingPerson
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long CitizenId { get; set; }
    public LivingGoal Goal { get; set; }
    public int Mood { get; set; } = 6000;
    public int Stress { get; set; }
    public int Injury { get; set; }
    public int Illness { get; set; }
    public int ToolCondition { get; set; }
    public int ClothingCondition { get; set; }
    public int Practice { get; set; }
    public long LastLeisureMinute { get; set; } = -1440;
    public long LastTeachingMinute { get; set; } = -1440;
    public long LastCareMinute { get; set; } = -1440;
    public bool DeathObserved { get; set; }
    public List<LivingTechnique> Knowledge { get; set; } = [];
    public List<LivingExperience> Experiences { get; set; } = [];
}

public sealed record LivingExperience(LivingExperienceKind Kind, long Minute,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? OtherCitizenId = null);
public sealed record LivingStock(LivingGood Good, int Quantity);
public sealed record LivingIngredient(string Resource, int Quantity);

public sealed class LivingWorkOrder
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Id { get; set; }
    public LivingWorkKind Kind { get; set; }
    public TileCoordinate Location { get; set; }
    public TileCoordinate SupplyLocation { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long? SubjectId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long? CitizenId { get; set; }
    public LivingTechnique? Technique { get; set; }
    public long CreatedMinute { get; set; }
    public long ClaimedMinute { get; set; }
    public int Priority { get; set; }
    public int RequiredWork { get; set; } = 120;
    public int WorkDone { get; set; }
    public LivingWorkPhase Phase { get; set; } = LivingWorkPhase.Collect;
    public bool Reserved { get; set; }
    public bool SuppliesDelivered { get; set; }
    public bool Produced { get; set; }
    public bool CargoInTransit { get; set; }
    public List<LivingIngredient> Ingredients { get; set; } = [];
    public List<LivingStock> Cargo { get; set; } = [];
    public string BlockedReason { get; set; } = "";
}

public sealed class LivingField
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Id { get; set; }
    public TileCoordinate Location { get; set; }
    public long SownMinute { get; set; } = -1;
    public int Growth { get; set; }
    public int Moisture { get; set; } = 5000;
    public int Condition { get; set; } = 10000;
    public long LastTendedMinute { get; set; }
    public int Harvests { get; set; }
    public int YieldRemaining { get; set; }
}
public sealed record LivingFacility(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Id,
    LivingFacilityKind Kind, TileCoordinate Location, long CompletedMinute);
public sealed class LivingAnimal
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Id { get; set; }
    public bool Predator { get; set; }
    public TileCoordinate Location { get; set; }
    public int Energy { get; set; } = 7000;
    public long BornMinute { get; set; }
}
public sealed record LivingFact(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Id,
    long Minute, LivingFactKind Kind,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? CitizenId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? RelatedId,
    TileCoordinate? Location, int Value);

/// <summary>Explicit JSON contract for a canonical checkpoint component, never arbitrary extension data.</summary>
public static class LivingWorldCodec
{
    private static readonly string[] Capabilities = ["work", "production", "personal-life", "environment", "knowledge"];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Serialize(LivingWorldState state) => JsonSerializer.Serialize(state, Options);
    public static LivingWorldState Deserialize(string json) => JsonSerializer.Deserialize<LivingWorldState>(json, Options)
        ?? throw new ArgumentException("Living state cannot be null.", nameof(json));

    public static JsonElement Observe(LivingWorldState state, long worldMinute, string rulesVersion) => JsonSerializer.SerializeToElement(new
    {
        state.Version, RulesVersion = rulesVersion, WorldMinute = worldMinute,
        Capabilities,
        Age = state.People.Any(x => !x.DeathObserved && x.Knowledge.Contains(LivingTechnique.Cultivation)) && state.FoodHarvested > 0 ? "Agrarian" : "Foraging",
        state.Weather, state.Temperature, state.Rainfall, state.CompletedOrders, state.FoodHarvested, state.FoodPrepared, state.GoodsSpoiled, state.CareGiven,
        state.Stock, state.People, state.Orders, state.Fields, state.Facilities, state.Animals,
        Facts = state.Facts.TakeLast(100).Reverse().ToArray(), TotalFacts = state.Facts.Count
    }, Options);
}
