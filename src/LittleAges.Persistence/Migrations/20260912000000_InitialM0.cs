using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912000000_InitialM0")]
public partial class InitialM0 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "world_meta",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false),
                world_seed = table.Column<string>(type: "TEXT", nullable: false),
                world_minute = table.Column<long>(type: "INTEGER", nullable: false),
                world_schema_version = table.Column<string>(type: "TEXT", nullable: false),
                simulation_rules_version = table.Column<string>(type: "TEXT", nullable: false),
                application_version = table.Column<string>(type: "TEXT", nullable: false),
                world_configuration_json = table.Column<string>(type: "TEXT", nullable: false),
                next_entity_id = table.Column<long>(type: "INTEGER", nullable: false),
                next_historical_event_id = table.Column<long>(type: "INTEGER", nullable: false),
                next_scheduled_event_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                created_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                last_checkpoint_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_meta", x => x.id);
                table.CheckConstraint("CK_world_meta_singleton", "id = 1");
            });

        migrationBuilder.CreateTable(
            name: "scheduled_events",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false),
                due_world_minute = table.Column<long>(type: "INTEGER", nullable: false),
                priority = table.Column<int>(type: "INTEGER", nullable: false),
                entity_sort_key = table.Column<long>(type: "INTEGER", nullable: false),
                sequence = table.Column<long>(type: "INTEGER", nullable: false),
                event_name = table.Column<string>(type: "TEXT", nullable: false),
                event_payload_json = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scheduled_events", x => x.id);
                table.CheckConstraint("CK_scheduled_events_identity", "id = sequence");
            });

        migrationBuilder.CreateIndex(
            name: "IX_scheduled_events_sequence",
            table: "scheduled_events",
            column: "sequence",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "scheduled_events");
        migrationBuilder.DropTable(name: "world_meta");
    }
}
