namespace LittleAges.Simulation;

/// <summary>Single home for canonical survival tuning values.</summary>
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
    public const int SurvivalCheckIntervalMinutes = 360;
    public const int BaseGatherDurationMinutes = 180;
    public const int MinimumGatherDurationMinutes = 60;
    public const int FoodBaseYield = 12;
    public const int WoodBaseYield = 10;
    public const int StoneBaseYield = 8;
    public const int GatherExperienceGain = 25;
    public const int EatDurationMinutes = 30;
    public const int MealFoodUnits = 10;
    public const int FullHungerReduction = 5000;
    public const int RestNeedReduction = 4000;
    public const int FoodTarget = 400;
    public const int WoodTarget = 120;
    public const int StoneTarget = 100;
    public const int StarvationThreshold = 9000;
    public const int ExhaustionThreshold = 9500;
    public const int StarvationDamagePerCheck = 300;
    public const int ExhaustionDamagePerCheck = 120;
    public const int RecoveryHungerThreshold = 6000;
    public const int RecoveryRestThreshold = 7000;
    public const int HealthRecoveryPerCheck = 120;
    public const int FoodSpringBasisPoints = 12500;
    public const int FoodSummerBasisPoints = 15000;
    public const int FoodAutumnBasisPoints = 10000;
    public const int FoodWinterBasisPoints = 2500;
    public const int WoodRegenerationDivisor = 8;
}
