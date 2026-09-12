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
