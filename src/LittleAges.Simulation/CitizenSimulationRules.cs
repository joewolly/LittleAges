using LittleAges.Domain;

namespace LittleAges.Simulation;

/// <summary>Single home for canonical survival tuning values.</summary>
public static class CitizenSimulationRules
{
    public const int SettlementVersion = 1;
    public const string CurrentRulesVersion = "m5-rng1-social1";
    public const int SocialVersion = 1;
    public const int SocializeDurationMinutes = 60;
    public const int SocialRadius = 2;
    // M5 compatibility data: a recent relationship is temporarily deprioritized so nearby
    // candidates remain socially diverse without category-specific matchmaking rules.
    public const int SocialRecentInteractionPenalty = 4000;
    public const int SocialRecentInteractionPenaltyDecayMinutesPerPoint = 10;
    public const int FamilyCheckPriority = 8;
    public const int LifecycleCheckPriority = 17;
    public const int MaximumPopulation = 2000;
    public const int MaximumHouseholdSize = 4;
    public const long BirthCooldownMinutes = 2L * WorldCalendar.MinutesPerYear;
    public const int DesiredSpareShelterSlots = 4;
    public const int BaseStorageCapacity = 800;
    public const int StockpileStorageBonus = 800;
    public const int ShelterCapacityPerBuilding = 4;
    public const int ShelterRequiredWood = 40;
    public const int ShelterRequiredStone = 10;
    public const int ShelterRequiredWork = 600;
    public const int StockpileRequiredWood = 60;
    public const int StockpileRequiredStone = 30;
    public const int StockpileRequiredWork = 900;
    public const int WorkshopRequiredWood = 80;
    public const int WorkshopRequiredStone = 50;
    public const int WorkshopRequiredWork = 1200;
    public const int SettlementDemandIntervalMinutes = 360;
    public const int StorageRetryIntervalMinutes = 60;
    public const int BaseConstructionCarryCapacity = 20;
    public const int MaximumConstructionCarrySkillBonus = 20;
    public const int ConstructionShiftDurationMinutes = 180;
    public const int BaseConstructionWorkPerShift = 100;
    public const int WorkshopConstructionMultiplierBasisPoints = 12500;
    public const int ConstructionExperienceGain = 25;
    public const int HaulingExperienceGain = 15;
    public const int ExposureGraceDurationMinutes = 7 * WorldCalendar.MinutesPerDay;
    public const int ShelterCriticalThreshold = 9000;
    public const int ExposureDamagePerCheck = 150;
    public const int WinterExposureDamagePerCheck = 300;
    public const int RecoveryShelterThreshold = 8000;
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
    public const int FoodSecurityGatherContributionMaximum = 4000;
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
