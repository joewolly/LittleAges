using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

public sealed class LittleAgesDbContext(DbContextOptions<LittleAgesDbContext> options) : DbContext(options)
{
    public DbSet<WorldMetaRow> WorldMeta => Set<WorldMetaRow>();
    public DbSet<ScheduledEventRow> ScheduledEvents => Set<ScheduledEventRow>();
    public DbSet<WorldTileRow> WorldTiles => Set<WorldTileRow>();
    public DbSet<ResourceNodeRow> ResourceNodes => Set<ResourceNodeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldMetaRow>(entity =>
        {
            entity.ToTable("world_meta", table => table.HasCheckConstraint("CK_world_meta_singleton", "id = 1"));
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
    }
}
