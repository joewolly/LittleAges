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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldMetaRow>(entity =>
        {
            entity.ToTable("world_meta", table =>
            {
                table.HasCheckConstraint("CK_world_meta_singleton", "id = 1");
                table.HasCheckConstraint("CK_world_meta_survival_version", "survival_version IN (0, 1)");
                table.HasCheckConstraint("CK_world_meta_settlement_version", "settlement_version IN (0, 1)");
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
                table.HasCheckConstraint("CK_citizens_founder", "founder_ordinal BETWEEN 0 AND 19");
                table.HasCheckConstraint("CK_citizens_health", "health BETWEEN 0 AND 10000");
                table.HasCheckConstraint("CK_citizens_needs", "hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000");
                table.HasCheckConstraint("CK_citizens_action", "current_action IN (0,1,2,3,4,5,6,7,8,9,10,11)");
                table.HasCheckConstraint("CK_citizens_m4_state", "health_updated_minute >= 0 AND action_phase IN (0,1,2,3,4,5,6) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND (target_structure_id IS NULL OR target_structure_id > 0) AND carried_resource_quantity >= 0 AND lifetime_foraging_minutes >= 0 AND lifetime_woodcutting_minutes >= 0 AND lifetime_stoneworking_minutes >= 0 AND lifetime_construction_minutes >= 0 AND lifetime_hauling_minutes >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3)))");
            });
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.FounderOrdinal).HasColumnName("founder_ordinal").IsRequired();
            entity.Property(row => row.GivenName).HasColumnName("given_name").HasColumnType("TEXT").IsRequired(); entity.Property(row => row.FamilyName).HasColumnName("family_name").HasColumnType("TEXT").IsRequired();
            entity.Property(row => row.BirthMinute).HasColumnName("birth_minute").IsRequired(); entity.Property(row => row.LocationX).HasColumnName("location_x").IsRequired(); entity.Property(row => row.LocationY).HasColumnName("location_y").IsRequired();
            entity.Property(row => row.DeathMinute).HasColumnName("death_minute"); entity.Property(row => row.DeathCause).HasColumnName("death_cause").HasColumnType("TEXT"); entity.Property(row => row.ParentAId).HasColumnName("parent_a_id"); entity.Property(row => row.ParentBId).HasColumnName("parent_b_id"); entity.Property(row => row.PartnerId).HasColumnName("partner_id"); entity.Property(row => row.HouseholdId).HasColumnName("household_id"); entity.Property(row => row.HomeStructureId).HasColumnName("home_structure_id");
            entity.Property(row => row.Health).HasColumnName("health"); entity.Property(row => row.Hunger).HasColumnName("hunger"); entity.Property(row => row.Rest).HasColumnName("rest"); entity.Property(row => row.Shelter).HasColumnName("shelter"); entity.Property(row => row.Social).HasColumnName("social");
            entity.Property(row => row.Industriousness).HasColumnName("industriousness"); entity.Property(row => row.Sociability).HasColumnName("sociability"); entity.Property(row => row.Curiosity).HasColumnName("curiosity"); entity.Property(row => row.Cooperativeness).HasColumnName("cooperativeness"); entity.Property(row => row.RiskTolerance).HasColumnName("risk_tolerance"); entity.Property(row => row.Resilience).HasColumnName("resilience");
            entity.Property(row => row.Foraging).HasColumnName("foraging"); entity.Property(row => row.Woodcutting).HasColumnName("woodcutting"); entity.Property(row => row.Stoneworking).HasColumnName("stoneworking"); entity.Property(row => row.Construction).HasColumnName("construction"); entity.Property(row => row.Hauling).HasColumnName("hauling"); entity.Property(row => row.Domestic).HasColumnName("domestic");
            entity.Property(row => row.CurrentAction).HasColumnName("current_action"); entity.Property(row => row.ActionSequence).HasColumnName("action_sequence"); entity.Property(row => row.ActionStartedMinute).HasColumnName("action_started_minute"); entity.Property(row => row.ActionCompletesMinute).HasColumnName("action_completes_minute"); entity.Property(row => row.ActionTargetX).HasColumnName("action_target_x"); entity.Property(row => row.ActionTargetY).HasColumnName("action_target_y"); entity.Property(row => row.NeedsUpdatedMinute).HasColumnName("needs_updated_minute"); entity.Property(row => row.LifetimeMovementSteps).HasColumnName("lifetime_movement_steps"); entity.Property(row => row.LifetimeMovementCost).HasColumnName("lifetime_movement_cost"); entity.Property(row => row.HealthUpdatedMinute).HasColumnName("health_updated_minute"); entity.Property(row => row.ActionPhase).HasColumnName("action_phase"); entity.Property(row => row.TargetResourceNodeId).HasColumnName("target_resource_node_id"); entity.Property(row => row.CarriedResourceType).HasColumnName("carried_resource_type"); entity.Property(row => row.CarriedResourceQuantity).HasColumnName("carried_resource_quantity"); entity.Property(row => row.TargetStructureId).HasColumnName("target_structure_id"); entity.Property(row => row.LifetimeForagingMinutes).HasColumnName("lifetime_foraging_minutes"); entity.Property(row => row.LifetimeWoodcuttingMinutes).HasColumnName("lifetime_woodcutting_minutes"); entity.Property(row => row.LifetimeStoneworkingMinutes).HasColumnName("lifetime_stoneworking_minutes"); entity.Property(row => row.LifetimeConstructionMinutes).HasColumnName("lifetime_construction_minutes"); entity.Property(row => row.LifetimeHaulingMinutes).HasColumnName("lifetime_hauling_minutes");
            entity.HasIndex(row => row.FounderOrdinal).IsUnique();
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
