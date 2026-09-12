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
            b.Property<long>("NextEntityId").HasColumnType("INTEGER").HasColumnName("next_entity_id");
            b.Property<long>("NextHistoricalEventId").HasColumnType("INTEGER").HasColumnName("next_historical_event_id");
            b.Property<long>("NextScheduledEventSequence").HasColumnType("INTEGER").HasColumnName("next_scheduled_event_sequence");
            b.Property<DateTime>("CreatedUtc").HasColumnType("TEXT").HasColumnName("created_utc");
            b.Property<DateTime>("LastCheckpointUtc").HasColumnType("TEXT").HasColumnName("last_checkpoint_utc");
            b.HasKey("Id");
            b.ToTable("world_meta", t => t.HasCheckConstraint("CK_world_meta_singleton", "id = 1"));
        });
    }
}
