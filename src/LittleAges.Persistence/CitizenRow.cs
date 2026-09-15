namespace LittleAges.Persistence;

public sealed class CitizenRow
{
    public long Id { get; set; }
    public int? FounderOrdinal { get; set; }
    public long? TargetCitizenId { get; set; }
    public string GivenName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public long BirthMinute { get; set; }
    public long? DeathMinute { get; set; }
    public string? DeathCause { get; set; }
    public long? ParentAId { get; set; }
    public long? ParentBId { get; set; }
    public long? PartnerId { get; set; }
    public long? HouseholdId { get; set; }
    public long? HomeStructureId { get; set; }
    public int LocationX { get; set; }
    public int LocationY { get; set; }
    public int Health { get; set; }
    public int Hunger { get; set; }
    public int Rest { get; set; }
    public int Shelter { get; set; }
    public int Social { get; set; }
    public int Industriousness { get; set; }
    public int Sociability { get; set; }
    public int Curiosity { get; set; }
    public int Cooperativeness { get; set; }
    public int RiskTolerance { get; set; }
    public int Resilience { get; set; }
    public int Foraging { get; set; }
    public int Woodcutting { get; set; }
    public int Stoneworking { get; set; }
    public int Construction { get; set; }
    public int Hauling { get; set; }
    public int Domestic { get; set; }
    public int CurrentAction { get; set; }
    public long ActionSequence { get; set; }
    public long? ActionStartedMinute { get; set; }
    public long? ActionCompletesMinute { get; set; }
    public int? ActionTargetX { get; set; }
    public int? ActionTargetY { get; set; }
    public long NeedsUpdatedMinute { get; set; }
    public long LifetimeMovementSteps { get; set; }
    public long LifetimeMovementCost { get; set; }
    public long HealthUpdatedMinute { get; set; }
    public int ActionPhase { get; set; }
    public long? TargetResourceNodeId { get; set; }
    public int? CarriedResourceType { get; set; }
    public int CarriedResourceQuantity { get; set; }
    public long? TargetStructureId { get; set; }
    public long LifetimeForagingMinutes { get; set; }
    public long LifetimeWoodcuttingMinutes { get; set; }
    public long LifetimeStoneworkingMinutes { get; set; }
    public long LifetimeConstructionMinutes { get; set; }
    public long LifetimeHaulingMinutes { get; set; }
}
