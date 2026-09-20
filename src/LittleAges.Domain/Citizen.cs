namespace LittleAges.Domain;

/// <summary>Persisted action values. M2 values are compatibility data and must not be renumbered.</summary>
public enum CitizenAction : int
{
    None = 0,
    Idle = 1,
    Rest = 2,
    Wander = 3,
    Explore = 4,
    Eat = 5,
    GatherFood = 6,
    GatherWood = 7,
    GatherStone = 8,
    Dead = 9,
    HaulConstruction = 10,
    Build = 11,
    Socialize = 12,
    LivingWork = 13
}

public enum CitizenActionPhase : int
{
    None = 0,
    TravelToTarget = 1,
    Perform = 2,
    ReturnToStockpile = 3,
    TravelToStockpile = 4,
    TransportToConstruction = 5,
    WaitingForStorage = 6
}

public enum CitizenTrait : int
{
    Industriousness = 1,
    Sociability = 2,
    Curiosity = 3,
    Cooperativeness = 4,
    RiskTolerance = 5,
    Resilience = 6
}

public enum CitizenSkill : int
{
    Foraging = 1,
    Woodcutting = 2,
    Stoneworking = 3,
    Construction = 4,
    Hauling = 5,
    Domestic = 6
}

/// <summary>Fixed-point needs. Zero is satisfied and 10000 is critical.</summary>
public sealed record CitizenNeeds
{
    public const int Maximum = 10000;
    public int Hunger { get; init; }
    public int Rest { get; init; }
    public int Shelter { get; init; }
    public int Social { get; init; }
    public CitizenNeeds(int hunger = 0, int rest = 0, int shelter = 0, int social = 0)
    {
        Hunger = Require(hunger, nameof(hunger)); Rest = Require(rest, nameof(rest));
        Shelter = Require(shelter, nameof(shelter)); Social = Require(social, nameof(social));
    }
    private static int Require(int value, string name) => value is < 0 or > Maximum ? throw new ArgumentOutOfRangeException(name) : value;
}

public sealed record CitizenTraits
{
    public int Industriousness { get; init; }
    public int Sociability { get; init; }
    public int Curiosity { get; init; }
    public int Cooperativeness { get; init; }
    public int RiskTolerance { get; init; }
    public int Resilience { get; init; }
    public CitizenTraits(int industriousness, int sociability, int curiosity, int cooperativeness, int riskTolerance, int resilience)
    {
        Industriousness = Require(industriousness, nameof(industriousness)); Sociability = Require(sociability, nameof(sociability)); Curiosity = Require(curiosity, nameof(curiosity));
        Cooperativeness = Require(cooperativeness, nameof(cooperativeness)); RiskTolerance = Require(riskTolerance, nameof(riskTolerance)); Resilience = Require(resilience, nameof(resilience));
    }
    private static int Require(int value, string name) => value is < 0 or > 10000 ? throw new ArgumentOutOfRangeException(name) : value;
}

public sealed record CitizenSkills
{
    public int Foraging { get; set; }
    public int Woodcutting { get; set; }
    public int Stoneworking { get; set; }
    public int Construction { get; set; }
    public int Hauling { get; set; }
    public int Domestic { get; set; }
    public CitizenSkills(int foraging, int woodcutting, int stoneworking, int construction, int hauling, int domestic)
    {
        Foraging = Require(foraging, nameof(foraging)); Woodcutting = Require(woodcutting, nameof(woodcutting)); Stoneworking = Require(stoneworking, nameof(stoneworking));
        Construction = Require(construction, nameof(construction)); Hauling = Require(hauling, nameof(hauling)); Domestic = Require(domestic, nameof(domestic));
    }
    private static int Require(int value, string name) => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
}

/// <summary>Canonical citizen state. Age and life stage are derived from BirthMinute.</summary>
public sealed class Citizen : IEquatable<Citizen>
{
    public Citizen(CitizenId id, int founderOrdinal, string givenName, string familyName, long birthMinute,
        TileCoordinate location, CitizenTraits traits, CitizenSkills skills, CitizenNeeds? needs = null)
        : this(id, (int?)founderOrdinal, givenName, familyName, birthMinute, location, traits, skills, needs)
    {
    }

    public Citizen(CitizenId id, int? founderOrdinal, string givenName, string familyName, long birthMinute,
        TileCoordinate location, CitizenTraits traits, CitizenSkills skills, CitizenNeeds? needs = null)
    {
        if (founderOrdinal is < 0 or > 19) throw new ArgumentOutOfRangeException(nameof(founderOrdinal));
        if (string.IsNullOrWhiteSpace(givenName) || string.IsNullOrWhiteSpace(familyName)) throw new ArgumentException("Citizen names are required.");
        Id = id; FounderOrdinal = founderOrdinal; GivenName = givenName; FamilyName = familyName; BirthMinute = birthMinute;
        Location = location; Traits = traits; Skills = skills; Needs = needs ?? new CitizenNeeds();
        Health = 10000; CurrentAction = CitizenAction.None; NeedsUpdatedMinute = 0;
    }

    public CitizenId Id { get; }
    public CitizenId CitizenId => Id;
    public int? FounderOrdinal { get; }
    public string GivenName { get; }
    public string FamilyName { get; }
    public string Name => $"{GivenName} {FamilyName}";
    public long BirthMinute { get; }
    public long? DeathMinute { get; set; }
    public string? DeathCause { get; set; }
    public CitizenId? ParentAId { get; set; }
    public CitizenId? ParentBId { get; set; }
    public CitizenId? PartnerId { get; set; }
    public HouseholdId? HouseholdId { get; set; }
    public StructureId? HomeStructureId { get; set; }
    public StructureId? TargetStructureId { get; set; }
    /// <summary>Only Socialize actions may retain a target citizen.</summary>
    public CitizenId? TargetCitizenId { get; set; }
    public TileCoordinate Location { get; set; }
    public int Health { get; set; }
    public bool IsAlive => DeathMinute is null && CurrentAction != CitizenAction.Dead;
    public CitizenNeeds Needs { get; set; }
    public CitizenTraits Traits { get; }
    public CitizenSkills Skills { get; }
    public CitizenAction CurrentAction { get; set; }
    public CitizenActionPhase ActionPhase { get; set; }
    public long ActionSequence { get; set; }
    public WorldMinute? ActionStartedMinute { get; set; }
    public WorldMinute? ActionCompletesMinute { get; set; }
    public TileCoordinate? ActionTarget { get; set; }
    public ResourceNodeId? TargetResourceNodeId { get; set; }
    public ResourceType? CarriedResourceType { get; set; }
    public int CarriedResourceQuantity { get; set; }
    public long NeedsUpdatedMinute { get; set; }
    public long HealthUpdatedMinute { get; set; }
    public long LifetimeMovementSteps { get; set; }
    public long LifetimeMovementCost { get; set; }
    public long LifetimeForagingMinutes { get; set; }
    public long LifetimeWoodcuttingMinutes { get; set; }
    public long LifetimeStoneworkingMinutes { get; set; }
    public long LifetimeConstructionMinutes { get; set; }
    public long LifetimeHaulingMinutes { get; set; }
    public string Occupation => CitizenOccupation.Derive(this);
    public int AgeYears(WorldMinute worldMinute)
    {
        if (worldMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(worldMinute));
        var elapsed = checked((DeathMinute ?? worldMinute.Value) - BirthMinute);
        return checked((int)(elapsed / WorldCalendar.MinutesPerYear));
    }
    public int GetAgeYears(WorldMinute worldMinute) => AgeYears(worldMinute);
    public int AgeAt(WorldMinute worldMinute) => AgeYears(worldMinute);
    public bool Equals(Citizen? other) => other is not null && Id == other.Id && FounderOrdinal == other.FounderOrdinal && GivenName == other.GivenName && FamilyName == other.FamilyName && BirthMinute == other.BirthMinute && ParentAId == other.ParentAId && ParentBId == other.ParentBId && PartnerId == other.PartnerId && HouseholdId == other.HouseholdId && Location == other.Location && Health == other.Health && Equals(Needs, other.Needs) && Equals(Traits, other.Traits) && Equals(Skills, other.Skills) && CurrentAction == other.CurrentAction && ActionPhase == other.ActionPhase && ActionSequence == other.ActionSequence && ActionStartedMinute == other.ActionStartedMinute && ActionCompletesMinute == other.ActionCompletesMinute && ActionTarget == other.ActionTarget && TargetResourceNodeId == other.TargetResourceNodeId && TargetStructureId == other.TargetStructureId && TargetCitizenId == other.TargetCitizenId && HomeStructureId == other.HomeStructureId && CarriedResourceType == other.CarriedResourceType && CarriedResourceQuantity == other.CarriedResourceQuantity && NeedsUpdatedMinute == other.NeedsUpdatedMinute && HealthUpdatedMinute == other.HealthUpdatedMinute && LifetimeMovementSteps == other.LifetimeMovementSteps && LifetimeMovementCost == other.LifetimeMovementCost && LifetimeForagingMinutes == other.LifetimeForagingMinutes && LifetimeWoodcuttingMinutes == other.LifetimeWoodcuttingMinutes && LifetimeStoneworkingMinutes == other.LifetimeStoneworkingMinutes && LifetimeConstructionMinutes == other.LifetimeConstructionMinutes && LifetimeHaulingMinutes == other.LifetimeHaulingMinutes && DeathMinute == other.DeathMinute && DeathCause == other.DeathCause;
    public override bool Equals(object? obj) => Equals(obj as Citizen);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(FounderOrdinal); hash.Add(GivenName); hash.Add(FamilyName); hash.Add(BirthMinute); hash.Add(ParentAId); hash.Add(ParentBId); hash.Add(PartnerId); hash.Add(HouseholdId); hash.Add(Location); hash.Add(Health); hash.Add(Needs); hash.Add(Traits); hash.Add(Skills); hash.Add(CurrentAction); hash.Add(ActionPhase); hash.Add(ActionSequence); hash.Add(ActionStartedMinute); hash.Add(ActionCompletesMinute); hash.Add(ActionTarget); hash.Add(TargetResourceNodeId); hash.Add(TargetStructureId); hash.Add(TargetCitizenId); hash.Add(HomeStructureId); hash.Add(CarriedResourceType); hash.Add(CarriedResourceQuantity); hash.Add(NeedsUpdatedMinute); hash.Add(HealthUpdatedMinute); hash.Add(LifetimeMovementSteps); hash.Add(LifetimeMovementCost); hash.Add(LifetimeForagingMinutes); hash.Add(LifetimeWoodcuttingMinutes); hash.Add(LifetimeStoneworkingMinutes); hash.Add(LifetimeConstructionMinutes); hash.Add(LifetimeHaulingMinutes); hash.Add(DeathMinute); hash.Add(DeathCause);
        return hash.ToHashCode();
    }
    public string LifeStage(WorldMinute worldMinute) => AgeYears(worldMinute) switch { <= 5 => "Young Child", <= 12 => "Child", <= 17 => "Adolescent", <= 59 => "Adult", _ => "Elder" };
    public CitizenNeeds GetProjectedNeeds(WorldMinute worldMinute) => NeedsProjection.Project(Needs, NeedsUpdatedMinute, worldMinute);
    public Citizen Validate(WorldMap? world = null)
    {
        if (Health is < 0 or > 10000 || (DeathMinute is null && Health < 1) || (DeathMinute is not null && Health != 0) || CurrentAction is < CitizenAction.None or > CitizenAction.LivingWork || ActionPhase is < CitizenActionPhase.None or > CitizenActionPhase.WaitingForStorage || ActionSequence < 0 || NeedsUpdatedMinute < 0 || HealthUpdatedMinute < 0 || CarriedResourceQuantity < 0 || LifetimeForagingMinutes < 0 || LifetimeWoodcuttingMinutes < 0 || LifetimeStoneworkingMinutes < 0 || LifetimeConstructionMinutes < 0 || LifetimeHaulingMinutes < 0 || (CarriedResourceQuantity == 0 && CarriedResourceType is not null) || (CarriedResourceQuantity > 0 && CarriedResourceType is null)) throw new ArgumentException("Citizen canonical state is invalid.");
        if (world is not null && (!world.GetTile(Location).Walkable || (ActionTarget is { } target && !world.GetTile(target).Walkable))) throw new ArgumentException("Citizen location or target is not walkable.");
        if (CurrentAction == CitizenAction.Dead && (DeathMinute is null || Health != 0 || string.IsNullOrWhiteSpace(DeathCause) || ActionPhase != CitizenActionPhase.None || ActionTarget is not null || ActionStartedMinute is not null || ActionCompletesMinute is not null || TargetResourceNodeId is not null || CarriedResourceQuantity != 0)) throw new ArgumentException("A dead citizen must have no active state.");
        if (DeathMinute is not null && (Health != 0 || string.IsNullOrWhiteSpace(DeathCause) || DeathCause is not ("starvation" or "exhaustion" or "exposure" or "deprivation" or "natural"))) throw new ArgumentException("A dead citizen must have zero health and a canonical death cause.");
        if (DeathMinute is not null && CurrentAction != CitizenAction.Dead) throw new ArgumentException("A citizen with a death minute must use the Dead action.");
        var gathering = CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone;
        if (!gathering && TargetResourceNodeId is not null) throw new ArgumentException("Only gathering may target a resource node.");
        if (gathering && TargetResourceNodeId is null) throw new ArgumentException("A gathering citizen must target a resource node.");
        if (CarriedResourceQuantity > 0 && (ActionPhase is not (CitizenActionPhase.ReturnToStockpile or CitizenActionPhase.TransportToConstruction or CitizenActionPhase.WaitingForStorage) || (CurrentAction == CitizenAction.GatherFood && CarriedResourceType != ResourceType.Food) || (CurrentAction == CitizenAction.GatherWood && CarriedResourceType != ResourceType.Wood) || (CurrentAction == CitizenAction.GatherStone && CarriedResourceType != ResourceType.Stone))) throw new ArgumentException("Carried goods must match a returning, blocked, or construction transport phase.");
        if (CurrentAction == CitizenAction.None && (ActionPhase != CitizenActionPhase.None || ActionTarget is not null || ActionStartedMinute is not null || ActionCompletesMinute is not null)) throw new ArgumentException("An undecided citizen cannot have action timing or a target.");
        if (CurrentAction == CitizenAction.Idle && (ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.Perform) || ActionTarget is not null || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("Idle cannot travel or retain a target.");
        if (CurrentAction == CitizenAction.Rest && (ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.TravelToTarget or CitizenActionPhase.Perform) || (ActionPhase == CitizenActionPhase.TravelToTarget && (ActionTarget is null || TargetStructureId != HomeStructureId)) || (ActionPhase == CitizenActionPhase.Perform && ActionTarget is not null) || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("Rest phase state is invalid.");
        if (TargetCitizenId is { } targetCitizenId && targetCitizenId.Value <= 0) throw new ArgumentException("A citizen target must be positive.");
        if (CurrentAction == CitizenAction.Socialize && (ActionPhase != CitizenActionPhase.Perform || TargetCitizenId is null || ActionTarget is not null || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("Socialize requires a citizen target and perform timing.");
        if (CurrentAction != CitizenAction.Socialize && TargetCitizenId is not null) throw new ArgumentException("Only Socialize may target a citizen.");
        if (CurrentAction is CitizenAction.Wander or CitizenAction.Explore && (ActionPhase is not (CitizenActionPhase.None or CitizenActionPhase.TravelToTarget) || ActionTarget is null || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("A moving citizen requires a target and timing.");
        if (CurrentAction == CitizenAction.LivingWork && (ActionPhase is not (CitizenActionPhase.TravelToTarget or CitizenActionPhase.Perform or CitizenActionPhase.WaitingForStorage) || ActionStartedMinute is null || ActionCompletesMinute is null || (ActionPhase == CitizenActionPhase.TravelToTarget) != (ActionTarget is not null) || TargetResourceNodeId is not null || TargetStructureId is not null || TargetCitizenId is not null || CarriedResourceQuantity != 0 || CarriedResourceType is not null)) throw new ArgumentException("Living work phase is invalid.");
        if (CurrentAction == CitizenAction.Eat && (ActionPhase is not (CitizenActionPhase.TravelToTarget or CitizenActionPhase.Perform) || (ActionPhase == CitizenActionPhase.TravelToTarget && ActionTarget is null) || (ActionPhase == CitizenActionPhase.Perform && ActionTarget is not null) || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("Eating phase state is invalid.");
        if (CurrentAction is CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone)
        {
            if (ActionPhase is not (CitizenActionPhase.TravelToTarget or CitizenActionPhase.Perform or CitizenActionPhase.ReturnToStockpile or CitizenActionPhase.WaitingForStorage) || ActionStartedMinute is null || ActionCompletesMinute is null || (ActionPhase == CitizenActionPhase.TravelToTarget && (ActionTarget is null || CarriedResourceQuantity != 0)) || (ActionPhase == CitizenActionPhase.Perform && (ActionTarget is not null || CarriedResourceQuantity != 0)) || (ActionPhase == CitizenActionPhase.ReturnToStockpile && (ActionTarget is null || CarriedResourceQuantity <= 0)) || (ActionPhase == CitizenActionPhase.WaitingForStorage && (ActionTarget is not null || CarriedResourceQuantity <= 0))) throw new ArgumentException("Gathering phase state is invalid.");
        }
        if (ActionPhase == CitizenActionPhase.ReturnToStockpile && (CurrentAction is not (CitizenAction.GatherFood or CitizenAction.GatherWood or CitizenAction.GatherStone) || CarriedResourceQuantity <= 0 || ActionTarget is null)) throw new ArgumentException("A returning gatherer requires carried goods and a target.");
        if (CurrentAction == CitizenAction.HaulConstruction && (ActionPhase is not (CitizenActionPhase.TravelToStockpile or CitizenActionPhase.TransportToConstruction) || TargetStructureId is null || ActionStartedMinute is null || ActionCompletesMinute is null || (ActionPhase == CitizenActionPhase.TravelToStockpile && (ActionTarget is null || CarriedResourceQuantity != 0)) || (ActionPhase == CitizenActionPhase.TransportToConstruction && (ActionTarget is null || CarriedResourceQuantity <= 0)))) throw new ArgumentException("Construction hauling phase state is invalid.");
        if (CurrentAction == CitizenAction.Build && (ActionPhase is not (CitizenActionPhase.TravelToTarget or CitizenActionPhase.Perform) || TargetStructureId is null || ActionStartedMinute is null || ActionCompletesMinute is null || (ActionPhase == CitizenActionPhase.TravelToTarget && ActionTarget is null) || (ActionPhase == CitizenActionPhase.Perform && ActionTarget is not null))) throw new ArgumentException("Construction building phase state is invalid.");
        if (ActionStartedMinute is { } started && ActionCompletesMinute is { } completes && (started.Value < 0 || completes.Value < started.Value)) throw new ArgumentException("Citizen action timing is incoherent.");
        return this;
    }
}

public static class NeedsProjection
{
    public const int HungerRatePerMinute = 2;
    public const int RestRatePerMinute = 3;
    public const int ShelterRatePerMinute = 1;
    public const int SocialRatePerMinute = 1;
    public const int WinterHungerRatePerMinute = 3;
    public const int MinutesPerSeason = WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay;
    public static CitizenNeeds Project(CitizenNeeds needs, long updatedMinute, WorldMinute worldMinute)
    {
        ArgumentNullException.ThrowIfNull(needs);
        if (updatedMinute < 0 || worldMinute.Value < updatedMinute) throw new ArgumentOutOfRangeException(nameof(worldMinute));
        var elapsed = checked(worldMinute.Value - updatedMinute);
        static int Add(int value, int rate, long elapsed)
        {
            if (elapsed > (CitizenNeeds.Maximum - value) / rate) return CitizenNeeds.Maximum;
            return value + checked(rate * (int)elapsed);
        }
        var hunger = ProjectHunger(needs.Hunger, updatedMinute, worldMinute.Value);
        return new CitizenNeeds(hunger, Add(needs.Rest, RestRatePerMinute, elapsed), Add(needs.Shelter, ShelterRatePerMinute, elapsed), Add(needs.Social, SocialRatePerMinute, elapsed));
    }

    public static int ProjectHunger(int hunger, long fromMinute, long toMinute)
    {
        if (hunger is < 0 or > CitizenNeeds.Maximum) throw new ArgumentOutOfRangeException(nameof(hunger));
        if (fromMinute < 0 || toMinute < fromMinute) throw new ArgumentOutOfRangeException(nameof(toMinute));
        var value = hunger;
        var cursor = fromMinute;
        while (cursor < toMinute && value < CitizenNeeds.Maximum)
        {
            var seasonOffset = cursor % (WorldCalendar.MinutesPerYear);
            var seasonIndex = seasonOffset / MinutesPerSeason;
            var nextBoundary = checked(cursor + (MinutesPerSeason - (seasonOffset % MinutesPerSeason)));
            var segmentEnd = Math.Min(toMinute, nextBoundary);
            var rate = seasonIndex == 3 ? WinterHungerRatePerMinute : HungerRatePerMinute;
            var minutes = segmentEnd - cursor;
            value = minutes > (CitizenNeeds.Maximum - value) / rate ? CitizenNeeds.Maximum : checked(value + checked((int)(minutes * rate)));
            cursor = segmentEnd;
        }
        return value;
    }
}
