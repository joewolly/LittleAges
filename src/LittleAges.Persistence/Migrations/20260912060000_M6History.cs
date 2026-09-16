using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912060000_M6History")]
public partial class M6History : Migration
{
    private static readonly string[] HistoricalEventOrderColumns = ["world_minute", "id"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "history_version", table: "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "historical_events",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false),
                world_minute = table.Column<long>(type: "INTEGER", nullable: false),
                event_type = table.Column<int>(type: "INTEGER", nullable: false),
                importance = table.Column<int>(type: "INTEGER", nullable: false),
                origin = table.Column<int>(type: "INTEGER", nullable: false),
                location_x = table.Column<int>(type: "INTEGER", nullable: true),
                location_y = table.Column<int>(type: "INTEGER", nullable: true),
                payload_json = table.Column<string>(type: "TEXT", nullable: false),
                schema_version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_historical_events", x => x.id);
                table.CheckConstraint("CK_historical_events_id", "id > 0");
                table.CheckConstraint("CK_historical_events_values", "world_minute >= 0 AND event_type BETWEEN 1 AND 15 AND importance BETWEEN 0 AND 5 AND origin IN (1,2) AND schema_version = 1 AND ((location_x IS NULL AND location_y IS NULL) OR (location_x >= 0 AND location_y >= 0))");
            });

        migrationBuilder.CreateTable(
            name: "history_state",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false),
                history_start_minute = table.Column<long>(type: "INTEGER", nullable: false),
                history_start_event_id = table.Column<long>(type: "INTEGER", nullable: false),
                period_start_minute = table.Column<long>(type: "INTEGER", nullable: false),
                births_since_sample = table.Column<long>(type: "INTEGER", nullable: false),
                deaths_since_sample = table.Column<long>(type: "INTEGER", nullable: false),
                food_produced_since_sample = table.Column<long>(type: "INTEGER", nullable: false),
                food_consumed_since_sample = table.Column<long>(type: "INTEGER", nullable: false),
                active_food_shortage = table.Column<bool>(type: "INTEGER", nullable: false),
                population_milestone_watermark = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_history_state", x => x.id);
                table.CheckConstraint("CK_history_state_singleton", "id = 1");
            });

        migrationBuilder.CreateTable(
            name: "statistics_samples",
            columns: table => new
            {
                world_minute = table.Column<long>(type: "INTEGER", nullable: false),
                period_start_minute = table.Column<long>(type: "INTEGER", nullable: false),
                population = table.Column<int>(type: "INTEGER", nullable: false),
                births_period = table.Column<long>(type: "INTEGER", nullable: false),
                deaths_period = table.Column<long>(type: "INTEGER", nullable: false),
                food_stored = table.Column<int>(type: "INTEGER", nullable: false),
                food_produced_period = table.Column<long>(type: "INTEGER", nullable: false),
                food_consumed_period = table.Column<long>(type: "INTEGER", nullable: false),
                wood_stored = table.Column<int>(type: "INTEGER", nullable: false),
                stone_stored = table.Column<int>(type: "INTEGER", nullable: false),
                shelter_capacity = table.Column<int>(type: "INTEGER", nullable: false),
                average_health = table.Column<int>(type: "INTEGER", nullable: false),
                average_hunger = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statistics_samples", x => x.world_minute);
                table.CheckConstraint("CK_statistics_samples_values", "world_minute >= 0 AND period_start_minute >= 0 AND period_start_minute <= world_minute AND population >= 0 AND births_period >= 0 AND deaths_period >= 0 AND food_stored >= 0 AND food_produced_period >= 0 AND food_consumed_period >= 0 AND wood_stored >= 0 AND stone_stored >= 0 AND shelter_capacity >= 0 AND average_health BETWEEN 0 AND 10000 AND average_hunger BETWEEN 0 AND 10000");
            });

        migrationBuilder.CreateTable(
            name: "historical_event_citizens",
            columns: table => new
            {
                event_id = table.Column<long>(type: "INTEGER", nullable: false),
                citizen_id = table.Column<long>(type: "INTEGER", nullable: false),
                role = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_historical_event_citizens", x => new { x.event_id, x.citizen_id, x.role });
                table.CheckConstraint("CK_historical_event_citizens_values", "event_id > 0 AND citizen_id > 0 AND role IN ('subject','parent','partner','founder','participant','member','contributor')");
                table.ForeignKey("FK_historical_event_citizens_historical_events_event_id", x => x.event_id, "historical_events", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_historical_event_citizens_citizens_citizen_id", x => x.citizen_id, "citizens", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "historical_event_structures",
            columns: table => new
            {
                event_id = table.Column<long>(type: "INTEGER", nullable: false),
                structure_id = table.Column<long>(type: "INTEGER", nullable: false),
                role = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_historical_event_structures", x => new { x.event_id, x.structure_id, x.role });
                table.CheckConstraint("CK_historical_event_structures_values", "event_id > 0 AND structure_id > 0 AND role = 'subject'");
                table.ForeignKey("FK_historical_event_structures_historical_events_event_id", x => x.event_id, "historical_events", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_historical_event_structures_structures_structure_id", x => x.structure_id, "structures", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "memories",
            columns: table => new
            {
                citizen_id = table.Column<long>(type: "INTEGER", nullable: false),
                event_id = table.Column<long>(type: "INTEGER", nullable: false),
                memory_type = table.Column<int>(type: "INTEGER", nullable: false),
                importance = table.Column<int>(type: "INTEGER", nullable: false),
                emotional_valence = table.Column<int>(type: "INTEGER", nullable: false),
                created_minute = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_memories", x => new { x.citizen_id, x.event_id, x.memory_type });
                table.CheckConstraint("CK_memories_values", "citizen_id > 0 AND event_id > 0 AND memory_type BETWEEN 1 AND 6 AND importance BETWEEN 0 AND 5 AND emotional_valence BETWEEN -10000 AND 10000 AND created_minute >= 0");
                table.ForeignKey("FK_memories_historical_events_event_id", x => x.event_id, "historical_events", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_memories_citizens_citizen_id", x => x.citizen_id, "citizens", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_historical_events_world_minute_id", table: "historical_events", columns: HistoricalEventOrderColumns);
        migrationBuilder.CreateIndex(name: "IX_historical_events_event_type", table: "historical_events", column: "event_type");
        migrationBuilder.CreateIndex(name: "IX_historical_events_importance", table: "historical_events", column: "importance");
        migrationBuilder.CreateIndex(name: "IX_historical_event_citizens_citizen_id", table: "historical_event_citizens", column: "citizen_id");
        migrationBuilder.CreateIndex(name: "IX_historical_event_structures_structure_id", table: "historical_event_structures", column: "structure_id");
        migrationBuilder.CreateIndex(name: "IX_memories_citizen_id", table: "memories", column: "citizen_id");
        migrationBuilder.CreateIndex(name: "IX_memories_event_id", table: "memories", column: "event_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Little Ages does not support downgrading a live M6 history database.");
    }
}
