using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

public sealed class LittleAgesDbContext(DbContextOptions<LittleAgesDbContext> options) : DbContext(options)
{
    public DbSet<WorldMetaRow> WorldMeta => Set<WorldMetaRow>();
    public DbSet<ScheduledEventRow> ScheduledEvents => Set<ScheduledEventRow>();

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
    }
}
