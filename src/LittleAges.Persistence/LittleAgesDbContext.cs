using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

public sealed class LittleAgesDbContext(DbContextOptions<LittleAgesDbContext> options) : DbContext(options)
{
    public DbSet<WorldMetaRow> WorldMeta => Set<WorldMetaRow>();
    public DbSet<ScheduledEventRow> ScheduledEvents => Set<ScheduledEventRow>();
    public DbSet<WorldTileRow> WorldTiles => Set<WorldTileRow>();
    public DbSet<ResourceNodeRow> ResourceNodes => Set<ResourceNodeRow>();
    public DbSet<ResourceStateRow> ResourceStates => Set<ResourceStateRow>();
    public DbSet<SettlementStateRow> SettlementStates => Set<SettlementStateRow>();
    public DbSet<CitizenRow> Citizens => Set<CitizenRow>();
    public DbSet<StructureRow> Structures => Set<StructureRow>();
    public DbSet<StructureContributionRow> StructureContributions => Set<StructureContributionRow>();
    public DbSet<RelationshipRow> Relationships => Set<RelationshipRow>();
    public DbSet<HouseholdRow> Households => Set<HouseholdRow>();
    public DbSet<HistoricalEventRow> HistoricalEvents => Set<HistoricalEventRow>();
    public DbSet<HistoricalEventCitizenLinkRow> HistoricalEventCitizens => Set<HistoricalEventCitizenLinkRow>();
    public DbSet<HistoricalEventStructureLinkRow> HistoricalEventStructures => Set<HistoricalEventStructureLinkRow>();
    public DbSet<HistoryStateRow> HistoryStates => Set<HistoryStateRow>();
    public DbSet<StatisticsSampleRow> StatisticsSamples => Set<StatisticsSampleRow>();
    public DbSet<CitizenMemoryRow> Memories => Set<CitizenMemoryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldMetaRow>(entity =>
        {
            entity.ToTable("world_meta", table =>
            {
                table.HasCheckConstraint("CK_world_meta_singleton", "id = 1");
                table.HasCheckConstraint("CK_world_meta_survival_version", "survival_version IN (0, 1)");
                table.HasCheckConstraint("CK_world_meta_settlement_version", "settlement_version IN (0, 1)");
                table.HasCheckConstraint("CK_world_meta_social_version", "social_version IN (0, 1)");
                table.HasCheckConstraint("CK_world_meta_history_version", "history_version IN (0, 1)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.WorldSeedValue).HasColumnName("world_seed").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.WorldMinute).HasColumnName("world_minute").IsRequired();
            entity.Property(row => row.WorldSchemaVersion).HasColumnName("world_schema_version").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.SimulationRulesVersion).HasColumnName("simulation_rules_version").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.ApplicationVersion).HasColumnName("application_version").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.WorldConfigurationJson).HasColumnName("world_configuration_json").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.GenerationVersion).HasColumnName("generation_version").IsRequired();
            entity.Property(row => row.GenerationAttempt).HasColumnName("generation_attempt").IsRequired();
            entity.Property(row => row.StartingX).HasColumnName("starting_x").IsRequired();
            entity.Property(row => row.StartingY).HasColumnName("starting_y").IsRequired();
            entity.Property(row => row.WorldFingerprint).HasColumnName("world_fingerprint").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.CitizenGenerationVersion).HasColumnName("citizen_generation_version").IsRequired();
            entity.Property(row => row.SurvivalVersion).HasColumnName("survival_version").IsRequired();
            entity.Property(row => row.SettlementVersion).HasColumnName("settlement_version").IsRequired();
            entity.Property(row => row.SocialVersion).HasColumnName("social_version").IsRequired();
            entity.Property(row => row.LivingStateJson).HasColumnName("living_state_json").HasColumnType("TEXT");
            entity.Property(row => row.HistoryVersion).HasColumnName("history_version").IsRequired();
            entity.Property(row => row.NextEntityId).HasColumnName("next_entity_id").IsRequired();
            entity.Property(row => row.NextHistoricalEventId).HasColumnName("next_historical_event_id").IsRequired();
            entity.Property(row => row.NextScheduledEventSequence).HasColumnName("next_scheduled_event_sequence").IsRequired();
            entity.Property(row => row.CreatedUtc).HasColumnName("created_utc").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.LastCheckpointUtc).HasColumnName("last_checkpoint_utc").HasColumnType("TEXT").IsRequired();
        });

        modelBuilder.Entity<ScheduledEventRow>(entity =>
        {
            entity.ToTable("scheduled_events", table => table.HasCheckConstraint("CK_scheduled_events_identity", "id = sequence"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.DueWorldMinute).HasColumnName("due_world_minute").IsRequired();
            entity.Property(row => row.Priority).HasColumnName("priority").IsRequired();
            entity.Property(row => row.EntitySortKey).HasColumnName("entity_sort_key").IsRequired();
            entity.Property(row => row.Sequence).HasColumnName("sequence").IsRequired();
            entity.Property(row => row.EventName).HasColumnName("event_name").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.EventPayloadJson).HasColumnName("event_payload_json").HasColumnType("TEXT").IsRequired();
            entity.HasIndex(row => row.Sequence).IsUnique();
        });

        modelBuilder.Entity<WorldTileRow>(entity =>
        {
            entity.ToTable("world_tiles", table =>
            {
                table.HasCheckConstraint("CK_world_tiles_index", "tile_index >= 0");
                table.HasCheckConstraint("CK_world_tiles_coordinates", "x >= 0 AND y >= 0");
                table.HasCheckConstraint("CK_world_tiles_values", "elevation BETWEEN 0 AND 10000 AND fertility BETWEEN 0 AND 10000 AND water_access BETWEEN 0 AND 10000");
                table.HasCheckConstraint("CK_world_tiles_walkability", "((walkable = 1 AND movement_cost > 0) OR (walkable = 0 AND movement_cost = 0))");
                table.HasCheckConstraint("CK_world_tiles_terrain", "terrain IN (1, 2, 3, 4, 5)");
            });
            entity.HasKey(row => row.TileIndex);
            entity.Property(row => row.TileIndex).HasColumnName("tile_index").ValueGeneratedNever();
            entity.Property(row => row.X).HasColumnName("x").IsRequired();
            entity.Property(row => row.Y).HasColumnName("y").IsRequired();
            entity.Property(row => row.Terrain).HasColumnName("terrain").IsRequired();
            entity.Property(row => row.Elevation).HasColumnName("elevation").IsRequired();
            entity.Property(row => row.Fertility).HasColumnName("fertility").IsRequired();
            entity.Property(row => row.WaterAccess).HasColumnName("water_access").IsRequired();
            entity.Property(row => row.Walkable).HasColumnName("walkable").IsRequired();
            entity.Property(row => row.MovementCost).HasColumnName("movement_cost").IsRequired();
            entity.HasIndex(row => new { row.X, row.Y }).IsUnique();
        });

        modelBuilder.Entity<ResourceNodeRow>(entity =>
        {
            entity.ToTable("resource_nodes", table =>
            {
                table.HasCheckConstraint("CK_resource_nodes_identity", "id > 0 AND tile_index >= 0 AND x >= 0 AND y >= 0");
                table.HasCheckConstraint("CK_resource_nodes_type", "resource IN (1, 2, 3)");
                table.HasCheckConstraint("CK_resource_nodes_quantities", "initial_quantity > 0 AND maximum_quantity >= initial_quantity AND regeneration_potential BETWEEN 0 AND 10000");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.TileIndex).HasColumnName("tile_index").IsRequired();
            entity.Property(row => row.X).HasColumnName("x").IsRequired();
            entity.Property(row => row.Y).HasColumnName("y").IsRequired();
            entity.Property(row => row.Resource).HasColumnName("resource").IsRequired();
            entity.Property(row => row.InitialQuantity).HasColumnName("initial_quantity").IsRequired();
            entity.Property(row => row.MaximumQuantity).HasColumnName("maximum_quantity").IsRequired();
            entity.Property(row => row.RegenerationPotential).HasColumnName("regeneration_potential").IsRequired();
            entity.HasOne(row => row.Tile)
                .WithMany(tile => tile.Resources)
                .HasForeignKey(row => row.TileIndex)
                .HasPrincipalKey(tile => tile.TileIndex)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(row => row.TileIndex);
        });

        modelBuilder.Entity<CitizenRow>(entity =>
        {
            entity.ToTable("citizens", table =>
            {
                table.HasCheckConstraint("CK_citizens_id", "id > 0");
                table.HasCheckConstraint("CK_citizens_founder", "founder_ordinal IS NULL OR founder_ordinal BETWEEN 0 AND 19");
                table.HasCheckConstraint("CK_citizens_health", "health BETWEEN 0 AND 10000");
                table.HasCheckConstraint("CK_citizens_needs", "hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000");
                table.HasCheckConstraint("CK_citizens_action", "current_action IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13)");
                table.HasCheckConstraint("CK_citizens_m4_state", "health_updated_minute >= 0 AND action_phase IN (0,1,2,3,4,5,6) AND (target_citizen_id IS NULL OR target_citizen_id > 0) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND (target_structure_id IS NULL OR target_structure_id > 0) AND carried_resource_quantity >= 0 AND lifetime_foraging_minutes >= 0 AND lifetime_woodcutting_minutes >= 0 AND lifetime_stoneworking_minutes >= 0 AND lifetime_construction_minutes >= 0 AND lifetime_hauling_minutes >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3)))");
            });
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.FounderOrdinal).HasColumnName("founder_ordinal");
            entity.Property(row => row.TargetCitizenId).HasColumnName("target_citizen_id");
            entity.Property(row => row.GivenName).HasColumnName("given_name").HasColumnType("TEXT").IsRequired(); entity.Property(row => row.FamilyName).HasColumnName("family_name").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.BirthMinute).HasColumnName("birth_minute").IsRequired(); entity.Property(row => row.LocationX).HasColumnName("location_x").IsRequired(); entity.Property(row => row.LocationY).HasColumnName("location_y").IsRequired();
            entity.Property(row => row.DeathMinute).HasColumnName("death_minute"); entity.Property(row => row.DeathCause).HasColumnName("death_cause").HasColumnType("TEXT"); entity.Property(row => row.ParentAId).HasColumnName("parent_a_id"); entity.Property(row => row.ParentBId).HasColumnName("parent_b_id"); entity.Property(row => row.PartnerId).HasColumnName("partner_id"); entity.Property(row => row.HouseholdId).HasColumnName("household_id"); entity.Property(row => row.HomeStructureId).HasColumnName("home_structure_id");
            entity.Property(row => row.Health).HasColumnName("health"); entity.Property(row => row.Hunger).HasColumnName("hunger"); entity.Property(row => row.Rest).HasColumnName("rest"); entity.Property(row => row.Shelter).HasColumnName("shelter"); entity.Property(row => row.Social).HasColumnName("social");
            entity.Property(row => row.Industriousness).HasColumnName("industriousness"); entity.Property(row => row.Sociability).HasColumnName("sociability"); entity.Property(row => row.Curiosity).HasColumnName("curiosity"); entity.Property(row => row.Cooperativeness).HasColumnName("cooperativeness"); entity.Property(row => row.RiskTolerance).HasColumnName("risk_tolerance"); entity.Property(row => row.Resilience).HasColumnName("resilience");
            entity.Property(row => row.Foraging).HasColumnName("foraging"); entity.Property(row => row.Woodcutting).HasColumnName("woodcutting"); entity.Property(row => row.Stoneworking).HasColumnName("stoneworking"); entity.Property(row => row.Construction).HasColumnName("construction"); entity.Property(row => row.Hauling).HasColumnName("hauling"); entity.Property(row => row.Domestic).HasColumnName("domestic");
            entity.Property(row => row.CurrentAction).HasColumnName("current_action"); entity.Property(row => row.ActionSequence).HasColumnName("action_sequence"); entity.Property(row => row.ActionStartedMinute).HasColumnName("action_started_minute"); entity.Property(row => row.ActionCompletesMinute).HasColumnName("action_completes_minute"); entity.Property(row => row.ActionTargetX).HasColumnName("action_target_x"); entity.Property(row => row.ActionTargetY).HasColumnName("action_target_y"); entity.Property(row => row.NeedsUpdatedMinute).HasColumnName("needs_updated_minute"); entity.Property(row => row.LifetimeMovementSteps).HasColumnName("lifetime_movement_steps"); entity.Property(row => row.LifetimeMovementCost).HasColumnName("lifetime_movement_cost"); entity.Property(row => row.HealthUpdatedMinute).HasColumnName("health_updated_minute"); entity.Property(row => row.ActionPhase).HasColumnName("action_phase"); entity.Property(row => row.TargetResourceNodeId).HasColumnName("target_resource_node_id"); entity.Property(row => row.CarriedResourceType).HasColumnName("carried_resource_type"); entity.Property(row => row.CarriedResourceQuantity).HasColumnName("carried_resource_quantity"); entity.Property(row => row.TargetStructureId).HasColumnName("target_structure_id"); entity.Property(row => row.LifetimeForagingMinutes).HasColumnName("lifetime_foraging_minutes"); entity.Property(row => row.LifetimeWoodcuttingMinutes).HasColumnName("lifetime_woodcutting_minutes"); entity.Property(row => row.LifetimeStoneworkingMinutes).HasColumnName("lifetime_stoneworking_minutes"); entity.Property(row => row.LifetimeConstructionMinutes).HasColumnName("lifetime_construction_minutes"); entity.Property(row => row.LifetimeHaulingMinutes).HasColumnName("lifetime_hauling_minutes");
            entity.HasIndex(row => row.FounderOrdinal).IsUnique();
        });

        modelBuilder.Entity<RelationshipRow>(entity =>
        {
            entity.ToTable("relationships", table =>
                table.HasCheckConstraint("CK_relationships_values", "citizen_a_id > 0 AND citizen_b_id > citizen_a_id AND familiarity BETWEEN 0 AND 10000 AND affinity BETWEEN -10000 AND 10000 AND trust BETWEEN 0 AND 10000 AND conflict BETWEEN 0 AND 10000 AND last_interaction_minute >= 0 AND interaction_count >= 1"));
            entity.HasKey(row => new { row.CitizenAId, row.CitizenBId });
            entity.Property(row => row.CitizenAId).HasColumnName("citizen_a_id"); entity.Property(row => row.CitizenBId).HasColumnName("citizen_b_id");
            entity.Property(row => row.Familiarity).HasColumnName("familiarity"); entity.Property(row => row.Affinity).HasColumnName("affinity"); entity.Property(row => row.Trust).HasColumnName("trust"); entity.Property(row => row.Conflict).HasColumnName("conflict"); entity.Property(row => row.LastInteractionMinute).HasColumnName("last_interaction_minute"); entity.Property(row => row.InteractionCount).HasColumnName("interaction_count");
        });

        modelBuilder.Entity<HouseholdRow>(entity =>
        {
            entity.ToTable("households", table => table.HasCheckConstraint("CK_households_minutes", "id > 0 AND created_minute >= 0 AND (dissolved_minute IS NULL OR dissolved_minute >= created_minute) AND (dwelling_structure_id IS NULL OR dwelling_structure_id > 0)"));
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever(); entity.Property(row => row.CreatedMinute).HasColumnName("created_minute"); entity.Property(row => row.DissolvedMinute).HasColumnName("dissolved_minute"); entity.Property(row => row.DwellingStructureId).HasColumnName("dwelling_structure_id");
        });

        modelBuilder.Entity<HistoricalEventRow>(entity =>
        {
            entity.ToTable("historical_events", table =>
            {
                table.HasCheckConstraint("CK_historical_events_id", "id > 0");
                table.HasCheckConstraint("CK_historical_events_values", "world_minute >= 0 AND event_type BETWEEN 1 AND 15 AND importance BETWEEN 0 AND 5 AND origin IN (1,2) AND schema_version = 1 AND ((location_x IS NULL AND location_y IS NULL) OR (location_x >= 0 AND location_y >= 0))");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.WorldMinute).HasColumnName("world_minute").IsRequired();
            entity.Property(row => row.EventType).HasColumnName("event_type").IsRequired();
            entity.Property(row => row.Importance).HasColumnName("importance").IsRequired();
            entity.Property(row => row.Origin).HasColumnName("origin").IsRequired();
            entity.Property(row => row.LocationX).HasColumnName("location_x");
            entity.Property(row => row.LocationY).HasColumnName("location_y");
            entity.Property(row => row.PayloadJson).HasColumnName("payload_json").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.SchemaVersion).HasColumnName("schema_version").IsRequired();
            entity.HasIndex(row => new { row.WorldMinute, row.Id });
            entity.HasIndex(row => row.EventType);
            entity.HasIndex(row => row.Importance);
        });

        modelBuilder.Entity<HistoricalEventCitizenLinkRow>(entity =>
        {
            entity.ToTable("historical_event_citizens", table => table.HasCheckConstraint("CK_historical_event_citizens_values", "event_id > 0 AND citizen_id > 0 AND role IN ('subject','parent','partner','founder','participant','member','contributor')"));
            entity.HasKey(row => new { row.EventId, row.CitizenId, row.Role });
            entity.Property(row => row.EventId).HasColumnName("event_id");
            entity.Property(row => row.CitizenId).HasColumnName("citizen_id");
            entity.Property(row => row.Role).HasColumnName("role").HasColumnType("TEXT").IsRequired();
            entity.HasOne<HistoricalEventRow>().WithMany().HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne<CitizenRow>().WithMany().HasForeignKey(row => row.CitizenId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasIndex(row => row.CitizenId);
            entity.HasIndex(row => row.EventId);
        });

        modelBuilder.Entity<HistoricalEventStructureLinkRow>(entity =>
        {
            entity.ToTable("historical_event_structures", table => table.HasCheckConstraint("CK_historical_event_structures_values", "event_id > 0 AND structure_id > 0 AND role = 'subject'"));
            entity.HasKey(row => new { row.EventId, row.StructureId, row.Role });
            entity.Property(row => row.EventId).HasColumnName("event_id");
            entity.Property(row => row.StructureId).HasColumnName("structure_id");
            entity.Property(row => row.Role).HasColumnName("role").HasColumnType("TEXT").IsRequired();
            entity.HasOne<HistoricalEventRow>().WithMany().HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne<StructureRow>().WithMany().HasForeignKey(row => row.StructureId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasIndex(row => row.StructureId);
            entity.HasIndex(row => row.EventId);
        });

        modelBuilder.Entity<HistoryStateRow>(entity =>
        {
            entity.ToTable("history_state", table => table.HasCheckConstraint("CK_history_state_singleton", "id = 1"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.HistoryStartMinute).HasColumnName("history_start_minute").IsRequired();
            entity.Property(row => row.HistoryStartEventId).HasColumnName("history_start_event_id").IsRequired();
            entity.Property(row => row.PeriodStartMinute).HasColumnName("period_start_minute").IsRequired();
            entity.Property(row => row.BirthsSinceSample).HasColumnName("births_since_sample").IsRequired();
            entity.Property(row => row.DeathsSinceSample).HasColumnName("deaths_since_sample").IsRequired();
            entity.Property(row => row.FoodProducedSinceSample).HasColumnName("food_produced_since_sample").IsRequired();
            entity.Property(row => row.FoodConsumedSinceSample).HasColumnName("food_consumed_since_sample").IsRequired();
            entity.Property(row => row.ActiveFoodShortage).HasColumnName("active_food_shortage").IsRequired();
            entity.Property(row => row.PopulationMilestoneWatermark).HasColumnName("population_milestone_watermark").IsRequired();
        });

        modelBuilder.Entity<StatisticsSampleRow>(entity =>
        {
            entity.ToTable("statistics_samples", table => table.HasCheckConstraint("CK_statistics_samples_values", "world_minute >= 0 AND period_start_minute >= 0 AND period_start_minute <= world_minute AND population >= 0 AND births_period >= 0 AND deaths_period >= 0 AND food_stored >= 0 AND food_produced_period >= 0 AND food_consumed_period >= 0 AND wood_stored >= 0 AND stone_stored >= 0 AND shelter_capacity >= 0 AND average_health BETWEEN 0 AND 10000 AND average_hunger BETWEEN 0 AND 10000"));
            entity.HasKey(row => row.WorldMinute);
            entity.Property(row => row.WorldMinute).HasColumnName("world_minute").ValueGeneratedNever();
            entity.Property(row => row.PeriodStartMinute).HasColumnName("period_start_minute").IsRequired();
            entity.Property(row => row.Population).HasColumnName("population").IsRequired();
            entity.Property(row => row.BirthsPeriod).HasColumnName("births_period").IsRequired();
            entity.Property(row => row.DeathsPeriod).HasColumnName("deaths_period").IsRequired();
            entity.Property(row => row.FoodStored).HasColumnName("food_stored").IsRequired();
            entity.Property(row => row.FoodProducedPeriod).HasColumnName("food_produced_period").IsRequired();
            entity.Property(row => row.FoodConsumedPeriod).HasColumnName("food_consumed_period").IsRequired();
            entity.Property(row => row.WoodStored).HasColumnName("wood_stored").IsRequired();
            entity.Property(row => row.StoneStored).HasColumnName("stone_stored").IsRequired();
            entity.Property(row => row.ShelterCapacity).HasColumnName("shelter_capacity").IsRequired();
            entity.Property(row => row.AverageHealth).HasColumnName("average_health").IsRequired();
            entity.Property(row => row.AverageHunger).HasColumnName("average_hunger").IsRequired();
            entity.HasIndex(row => row.WorldMinute);
        });

        modelBuilder.Entity<CitizenMemoryRow>(entity =>
        {
            entity.ToTable("memories", table => table.HasCheckConstraint("CK_memories_values", "citizen_id > 0 AND event_id > 0 AND memory_type BETWEEN 1 AND 6 AND importance BETWEEN 0 AND 5 AND emotional_valence BETWEEN -10000 AND 10000 AND created_minute >= 0"));
            entity.HasKey(row => new { row.CitizenId, row.EventId, row.MemoryType });
            entity.Property(row => row.CitizenId).HasColumnName("citizen_id");
            entity.Property(row => row.EventId).HasColumnName("event_id");
            entity.Property(row => row.MemoryType).HasColumnName("memory_type");
            entity.Property(row => row.Importance).HasColumnName("importance");
            entity.Property(row => row.EmotionalValence).HasColumnName("emotional_valence");
            entity.Property(row => row.CreatedMinute).HasColumnName("created_minute");
            entity.HasOne<HistoricalEventRow>().WithMany().HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne<CitizenRow>().WithMany().HasForeignKey(row => row.CitizenId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasIndex(row => row.CitizenId);
        });

        modelBuilder.Entity<ResourceStateRow>(entity =>
        {
            entity.ToTable("resource_state", table => table.HasCheckConstraint("CK_resource_state_quantity", "current_quantity >= 0"));
            entity.HasKey(row => row.ResourceNodeId);
            entity.Property(row => row.ResourceNodeId).HasColumnName("resource_node_id").ValueGeneratedNever();
            entity.Property(row => row.CurrentQuantity).HasColumnName("current_quantity").IsRequired();
            entity.HasOne(row => row.ResourceNode).WithMany().HasForeignKey(row => row.ResourceNodeId).OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity<SettlementStateRow>(entity =>
        {
            entity.ToTable("settlement_state", table =>
            {
                table.HasCheckConstraint("CK_settlement_state_singleton", "id = 1");
                table.HasCheckConstraint("CK_settlement_state_quantities", "food_stored >= 0 AND wood_stored >= 0 AND stone_stored >= 0 AND base_storage_capacity >= 0 AND demand_updated_minute >= 0 AND exposure_consequences_start_minute >= 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.FoodStored).HasColumnName("food_stored").IsRequired();
            entity.Property(row => row.WoodStored).HasColumnName("wood_stored").IsRequired();
            entity.Property(row => row.StoneStored).HasColumnName("stone_stored").IsRequired();
            entity.Property(row => row.BaseStorageCapacity).HasColumnName("base_storage_capacity").IsRequired();
            entity.Property(row => row.DemandUpdatedMinute).HasColumnName("demand_updated_minute").IsRequired();
            entity.Property(row => row.ExposureConsequencesStartMinute).HasColumnName("exposure_consequences_start_minute").IsRequired();
        });

        modelBuilder.Entity<StructureRow>(entity =>
        {
            entity.ToTable("structures", table =>
            {
                table.HasCheckConstraint("CK_structures_id", "id > 0");
                table.HasCheckConstraint("CK_structures_type", "type IN (1,2,3)");
                table.HasCheckConstraint("CK_structures_status", "status IN (1,2)");
                table.HasCheckConstraint("CK_structures_coordinates", "location_x >= 0 AND location_y >= 0");
                table.HasCheckConstraint("CK_structures_values", "construction_started_minute >= 0 AND ((type = 1 AND required_wood = 40 AND required_stone = 10 AND required_work = 600) OR (type = 2 AND required_wood = 60 AND required_stone = 30 AND required_work = 900) OR (type = 3 AND required_wood = 80 AND required_stone = 50 AND required_work = 1200)) AND delivered_wood BETWEEN 0 AND required_wood AND delivered_stone BETWEEN 0 AND required_stone AND completed_work BETWEEN 0 AND required_work AND ((status = 1 AND completed_minute IS NULL AND condition = 0) OR (status = 2 AND completed_minute IS NOT NULL AND delivered_wood = required_wood AND delivered_stone = required_stone AND completed_work = required_work AND condition = 10000))");
            });
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Type).HasColumnName("type"); entity.Property(row => row.Status).HasColumnName("status"); entity.Property(row => row.Condition).HasColumnName("condition"); entity.Property(row => row.LocationX).HasColumnName("location_x"); entity.Property(row => row.LocationY).HasColumnName("location_y"); entity.Property(row => row.ConstructionStartedMinute).HasColumnName("construction_started_minute"); entity.Property(row => row.CompletedMinute).HasColumnName("completed_minute"); entity.Property(row => row.RequiredWood).HasColumnName("required_wood"); entity.Property(row => row.DeliveredWood).HasColumnName("delivered_wood"); entity.Property(row => row.RequiredStone).HasColumnName("required_stone"); entity.Property(row => row.DeliveredStone).HasColumnName("delivered_stone"); entity.Property(row => row.RequiredWork).HasColumnName("required_work"); entity.Property(row => row.CompletedWork).HasColumnName("completed_work");
            entity.HasIndex(row => new { row.LocationX, row.LocationY }).IsUnique();
        });

        modelBuilder.Entity<StructureContributionRow>(entity =>
        {
            entity.ToTable("structure_contributions", table => table.HasCheckConstraint("CK_structure_contributions_values", "construction_work >= 0 AND wood_delivered >= 0 AND stone_delivered >= 0"));
            entity.HasKey(row => new { row.StructureId, row.CitizenId });
            entity.Property(row => row.StructureId).HasColumnName("structure_id"); entity.Property(row => row.CitizenId).HasColumnName("citizen_id"); entity.Property(row => row.ConstructionWork).HasColumnName("construction_work"); entity.Property(row => row.WoodDelivered).HasColumnName("wood_delivered"); entity.Property(row => row.StoneDelivered).HasColumnName("stone_delivered");
            entity.HasOne<StructureRow>().WithMany().HasForeignKey(row => row.StructureId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne<CitizenRow>().WithMany().HasForeignKey(row => row.CitizenId).OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
    }
}
