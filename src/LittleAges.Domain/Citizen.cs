namespace LittleAges.Domain;

/// <summary>Persisted M2 action values. Do not renumber these values.</summary>
public enum CitizenAction : int
{
    None = 0,
    Idle = 1,
    Rest = 2,
    Wander = 3,
    Explore = 4
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
    public int Foraging { get; init; }
    public int Woodcutting { get; init; }
    public int Stoneworking { get; init; }
    public int Construction { get; init; }
    public int Hauling { get; init; }
    public int Domestic { get; init; }
    public CitizenSkills(int foraging, int woodcutting, int stoneworking, int construction, int hauling, int domestic)
    {
        Foraging = Require(foraging, nameof(foraging)); Woodcutting = Require(woodcutting, nameof(woodcutting)); Stoneworking = Require(stoneworking, nameof(stoneworking));
        Construction = Require(construction, nameof(construction)); Hauling = Require(hauling, nameof(hauling)); Domestic = Require(domestic, nameof(domestic));
    }
    private static int Require(int value, string name) => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
}

/// <summary>Canonical citizen state. Age and life stage are derived from BirthMinute.</summary>
public sealed class Citizen
{
    public Citizen(CitizenId id, int founderOrdinal, string givenName, string familyName, long birthMinute,
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
    public int FounderOrdinal { get; }
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
    public TileCoordinate Location { get; set; }
    public int Health { get; private set; }
    public CitizenNeeds Needs { get; set; }
    public CitizenTraits Traits { get; }
    public CitizenSkills Skills { get; }
    public CitizenAction CurrentAction { get; set; }
    public long ActionSequence { get; set; }
    public WorldMinute? ActionStartedMinute { get; set; }
    public WorldMinute? ActionCompletesMinute { get; set; }
    public TileCoordinate? ActionTarget { get; set; }
    public long NeedsUpdatedMinute { get; set; }
    public long LifetimeMovementSteps { get; set; }
    public long LifetimeMovementCost { get; set; }
    public int AgeYears(WorldMinute worldMinute)
    {
        if (worldMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(worldMinute));
        var elapsed = checked(worldMinute.Value - BirthMinute);
        return checked((int)(elapsed / WorldCalendar.MinutesPerYear));
    }
    public int GetAgeYears(WorldMinute worldMinute) => AgeYears(worldMinute);
    public int AgeAt(WorldMinute worldMinute) => AgeYears(worldMinute);
    public string LifeStage(WorldMinute worldMinute) => AgeYears(worldMinute) switch { < 13 => "Child", < 18 => "Adolescent", < 60 => "Adult", _ => "Elder" };
    public CitizenNeeds GetProjectedNeeds(WorldMinute worldMinute) => NeedsProjection.Project(Needs, NeedsUpdatedMinute, worldMinute);
    public Citizen Validate(WorldMap? world = null)
    {
        if (Health != 10000 || CurrentAction is < CitizenAction.None or > CitizenAction.Explore || ActionSequence < 0 || NeedsUpdatedMinute < 0) throw new ArgumentException("Citizen canonical state is invalid.");
        if (world is not null && (!world.GetTile(Location).Walkable || (ActionTarget is { } target && !world.GetTile(target).Walkable))) throw new ArgumentException("Citizen location or target is not walkable.");
        if (CurrentAction == CitizenAction.None && (ActionTarget is not null || ActionStartedMinute is not null || ActionCompletesMinute is not null)) throw new ArgumentException("An undecided citizen cannot have action timing or a target.");
        if (CurrentAction is CitizenAction.Idle or CitizenAction.Rest && (ActionTarget is not null || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("An active non-moving citizen requires timing and no target.");
        if (CurrentAction is CitizenAction.Wander or CitizenAction.Explore && (ActionTarget is null || ActionStartedMinute is null || ActionCompletesMinute is null)) throw new ArgumentException("A moving citizen requires a target and timing.");
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
        return new CitizenNeeds(Add(needs.Hunger, HungerRatePerMinute, elapsed), Add(needs.Rest, RestRatePerMinute, elapsed), Add(needs.Shelter, ShelterRatePerMinute, elapsed), Add(needs.Social, SocialRatePerMinute, elapsed));
    }
}
