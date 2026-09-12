using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
sealed partial class LittleAgesDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

        modelBuilder.Entity("LittleAges.Persistence.ScheduledEventRow", b =>
        {
            b.Property<long>("Id").ValueGeneratedNever().HasColumnType("INTEGER").HasColumnName("id");
            b.Property<long>("DueWorldMinute").HasColumnType("INTEGER").HasColumnName("due_world_minute");
            b.Property<int>("Priority").HasColumnType("INTEGER").HasColumnName("priority");
            b.Property<long>("EntitySortKey").HasColumnType("INTEGER").HasColumnName("entity_sort_key");
            b.Property<long>("Sequence").HasColumnType("INTEGER").HasColumnName("sequence");
            b.Property<string>("EventName").IsRequired().HasColumnType("TEXT").HasColumnName("event_name");
            b.Property<string>("EventPayloadJson").IsRequired().HasColumnType("TEXT").HasColumnName("event_payload_json");
            b.HasKey("Id");
            b.HasIndex("Sequence").IsUnique();
            b.ToTable("scheduled_events", t => t.HasCheckConstraint("CK_scheduled_events_identity", "id = sequence"));
        });

        modelBuilder.Entity("LittleAges.Persistence.WorldMetaRow", b =>
        {
            b.Property<int>("Id").ValueGeneratedNever().HasColumnType("INTEGER").HasColumnName("id");
            b.Property<string>("WorldSeedValue").IsRequired().HasColumnType("TEXT").HasColumnName("world_seed");
            b.Property<long>("WorldMinute").HasColumnType("INTEGER").HasColumnName("world_minute");
            b.Property<string>("WorldSchemaVersion").IsRequired().HasColumnType("TEXT").HasColumnName("world_schema_version");
            b.Property<string>("SimulationRulesVersion").IsRequired().HasColumnType("TEXT").HasColumnName("simulation_rules_version");
            b.Property<string>("ApplicationVersion").IsRequired().HasColumnType("TEXT").HasColumnName("application_version");
            b.Property<string>("WorldConfigurationJson").IsRequired().HasColumnType("TEXT").HasColumnName("world_configuration_json");
            b.Property<int>("GenerationVersion").HasColumnType("INTEGER").HasColumnName("generation_version");
            b.Property<int>("GenerationAttempt").HasColumnType("INTEGER").HasColumnName("generation_attempt");
            b.Property<int>("StartingX").HasColumnType("INTEGER").HasColumnName("starting_x");
            b.Property<int>("StartingY").HasColumnType("INTEGER").HasColumnName("starting_y");
            b.Property<string>("WorldFingerprint").IsRequired().HasColumnType("TEXT").HasColumnName("world_fingerprint");
            b.Property<int>("CitizenGenerationVersion").HasColumnType("INTEGER").HasColumnName("citizen_generation_version");
            b.Property<long>("NextEntityId").HasColumnType("INTEGER").HasColumnName("next_entity_id");
            b.Property<long>("NextHistoricalEventId").HasColumnType("INTEGER").HasColumnName("next_historical_event_id");
            b.Property<long>("NextScheduledEventSequence").HasColumnType("INTEGER").HasColumnName("next_scheduled_event_sequence");
            b.Property<DateTime>("CreatedUtc").HasColumnType("TEXT").HasColumnName("created_utc");
            b.Property<DateTime>("LastCheckpointUtc").HasColumnType("TEXT").HasColumnName("last_checkpoint_utc");
            b.HasKey("Id");
            b.ToTable("world_meta", t => t.HasCheckConstraint("CK_world_meta_singleton", "id = 1"));
        });

        modelBuilder.Entity("LittleAges.Persistence.WorldTileRow", b =>
        {
            b.Property<long>("TileIndex").ValueGeneratedNever().HasColumnType("INTEGER").HasColumnName("tile_index");
            b.Property<int>("X").HasColumnType("INTEGER").HasColumnName("x");
            b.Property<int>("Y").HasColumnType("INTEGER").HasColumnName("y");
            b.Property<int>("Terrain").HasColumnType("INTEGER").HasColumnName("terrain");
            b.Property<int>("Elevation").HasColumnType("INTEGER").HasColumnName("elevation");
            b.Property<int>("Fertility").HasColumnType("INTEGER").HasColumnName("fertility");
            b.Property<int>("WaterAccess").HasColumnType("INTEGER").HasColumnName("water_access");
            b.Property<bool>("Walkable").HasColumnType("INTEGER").HasColumnName("walkable");
            b.Property<int>("MovementCost").HasColumnType("INTEGER").HasColumnName("movement_cost");
            b.HasKey("TileIndex");
            b.HasIndex("X", "Y").IsUnique();
            b.ToTable("world_tiles", t =>
            {
                t.HasCheckConstraint("CK_world_tiles_index", "tile_index >= 0");
                t.HasCheckConstraint("CK_world_tiles_coordinates", "x >= 0 AND y >= 0");
                t.HasCheckConstraint("CK_world_tiles_values", "elevation BETWEEN 0 AND 10000 AND fertility BETWEEN 0 AND 10000 AND water_access BETWEEN 0 AND 10000");
                t.HasCheckConstraint("CK_world_tiles_walkability", "((walkable = 1 AND movement_cost > 0) OR (walkable = 0 AND movement_cost = 0))");
                t.HasCheckConstraint("CK_world_tiles_terrain", "terrain IN (1, 2, 3, 4, 5)");
            });
        });

        modelBuilder.Entity("LittleAges.Persistence.ResourceNodeRow", b =>
        {
            b.Property<long>("Id").ValueGeneratedNever().HasColumnType("INTEGER").HasColumnName("id");
            b.Property<long>("TileIndex").HasColumnType("INTEGER").HasColumnName("tile_index");
            b.Property<int>("X").HasColumnType("INTEGER").HasColumnName("x");
            b.Property<int>("Y").HasColumnType("INTEGER").HasColumnName("y");
            b.Property<int>("Resource").HasColumnType("INTEGER").HasColumnName("resource");
            b.Property<int>("InitialQuantity").HasColumnType("INTEGER").HasColumnName("initial_quantity");
            b.Property<int>("MaximumQuantity").HasColumnType("INTEGER").HasColumnName("maximum_quantity");
            b.Property<int>("RegenerationPotential").HasColumnType("INTEGER").HasColumnName("regeneration_potential");
            b.HasKey("Id");
            b.HasIndex("TileIndex");
            b.ToTable("resource_nodes", t =>
            {
                t.HasCheckConstraint("CK_resource_nodes_identity", "id > 0 AND tile_index >= 0 AND x >= 0 AND y >= 0");
                t.HasCheckConstraint("CK_resource_nodes_type", "resource IN (1, 2, 3)");
                t.HasCheckConstraint("CK_resource_nodes_quantities", "initial_quantity > 0 AND maximum_quantity >= initial_quantity AND regeneration_potential BETWEEN 0 AND 10000");
            });
            b.HasOne("LittleAges.Persistence.WorldTileRow", "Tile")
                .WithMany("Resources")
                .HasForeignKey("TileIndex")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("LittleAges.Persistence.CitizenRow", b =>
        {
            b.Property<long>("Id").ValueGeneratedNever().HasColumnType("INTEGER").HasColumnName("id");
            b.Property<int>("FounderOrdinal").HasColumnType("INTEGER").HasColumnName("founder_ordinal");
            b.Property<string>("GivenName").IsRequired().HasColumnType("TEXT").HasColumnName("given_name");
            b.Property<string>("FamilyName").IsRequired().HasColumnType("TEXT").HasColumnName("family_name");
            b.Property<long>("BirthMinute").HasColumnType("INTEGER").HasColumnName("birth_minute"); b.Property<long?>("DeathMinute").HasColumnType("INTEGER").HasColumnName("death_minute"); b.Property<string>("DeathCause").HasColumnType("TEXT").HasColumnName("death_cause");
            b.Property<long?>("ParentAId").HasColumnType("INTEGER").HasColumnName("parent_a_id"); b.Property<long?>("ParentBId").HasColumnType("INTEGER").HasColumnName("parent_b_id"); b.Property<long?>("PartnerId").HasColumnType("INTEGER").HasColumnName("partner_id"); b.Property<long?>("HouseholdId").HasColumnType("INTEGER").HasColumnName("household_id"); b.Property<long?>("HomeStructureId").HasColumnType("INTEGER").HasColumnName("home_structure_id");
            b.Property<int>("LocationX").HasColumnType("INTEGER").HasColumnName("location_x"); b.Property<int>("LocationY").HasColumnType("INTEGER").HasColumnName("location_y"); b.Property<int>("Health").HasColumnType("INTEGER").HasColumnName("health");
            b.Property<int>("Hunger").HasColumnType("INTEGER").HasColumnName("hunger"); b.Property<int>("Rest").HasColumnType("INTEGER").HasColumnName("rest"); b.Property<int>("Shelter").HasColumnType("INTEGER").HasColumnName("shelter"); b.Property<int>("Social").HasColumnType("INTEGER").HasColumnName("social");
            b.Property<int>("Industriousness").HasColumnType("INTEGER").HasColumnName("industriousness"); b.Property<int>("Sociability").HasColumnType("INTEGER").HasColumnName("sociability"); b.Property<int>("Curiosity").HasColumnType("INTEGER").HasColumnName("curiosity"); b.Property<int>("Cooperativeness").HasColumnType("INTEGER").HasColumnName("cooperativeness"); b.Property<int>("RiskTolerance").HasColumnType("INTEGER").HasColumnName("risk_tolerance"); b.Property<int>("Resilience").HasColumnType("INTEGER").HasColumnName("resilience");
            b.Property<int>("Foraging").HasColumnType("INTEGER").HasColumnName("foraging"); b.Property<int>("Woodcutting").HasColumnType("INTEGER").HasColumnName("woodcutting"); b.Property<int>("Stoneworking").HasColumnType("INTEGER").HasColumnName("stoneworking"); b.Property<int>("Construction").HasColumnType("INTEGER").HasColumnName("construction"); b.Property<int>("Hauling").HasColumnType("INTEGER").HasColumnName("hauling"); b.Property<int>("Domestic").HasColumnType("INTEGER").HasColumnName("domestic");
            b.Property<int>("CurrentAction").HasColumnType("INTEGER").HasColumnName("current_action"); b.Property<long>("ActionSequence").HasColumnType("INTEGER").HasColumnName("action_sequence"); b.Property<long?>("ActionStartedMinute").HasColumnType("INTEGER").HasColumnName("action_started_minute"); b.Property<long?>("ActionCompletesMinute").HasColumnType("INTEGER").HasColumnName("action_completes_minute"); b.Property<int?>("ActionTargetX").HasColumnType("INTEGER").HasColumnName("action_target_x"); b.Property<int?>("ActionTargetY").HasColumnType("INTEGER").HasColumnName("action_target_y"); b.Property<long>("NeedsUpdatedMinute").HasColumnType("INTEGER").HasColumnName("needs_updated_minute"); b.Property<long>("LifetimeMovementSteps").HasColumnType("INTEGER").HasColumnName("lifetime_movement_steps"); b.Property<long>("LifetimeMovementCost").HasColumnType("INTEGER").HasColumnName("lifetime_movement_cost");
            b.HasKey("Id"); b.HasIndex("FounderOrdinal").IsUnique(); b.ToTable("citizens", t => { t.HasCheckConstraint("CK_citizens_id", "id > 0"); t.HasCheckConstraint("CK_citizens_founder", "founder_ordinal BETWEEN 0 AND 19"); t.HasCheckConstraint("CK_citizens_health", "health BETWEEN 0 AND 10000"); t.HasCheckConstraint("CK_citizens_needs", "hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000"); t.HasCheckConstraint("CK_citizens_action", "current_action IN (0,1,2,3,4)"); });
        });

        modelBuilder.Entity("LittleAges.Persistence.ResourceNodeRow", b =>
        {
            b.HasOne("LittleAges.Persistence.WorldTileRow", "Tile")
                .WithMany("Resources")
                .HasForeignKey("TileIndex")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("LittleAges.Persistence.WorldTileRow", b =>
        {
            b.Navigation("Resources");
        });
    }
}
