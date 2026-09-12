namespace LittleAges.Simulation;

/// <summary>Single home for M2 canonical tuning values.</summary>
public static class CitizenSimulationRules
{
    public const int FounderCount = 20;
    public const int IdleMinimumMinutes = 30;
    public const int IdleMaximumMinutes = 90;
    public const int RestDurationMinutes = 120;
    public const int WanderRadius = 4;
    public const int ExploreRadius = 12;
    public const int RestUtilityWeight = 2;
    public const int ExploreBaseUtility = 1000;
    public const int WanderBaseUtility = 700;
    public const int IdleBaseUtility = 500;
}
