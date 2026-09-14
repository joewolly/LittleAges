using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using LittleAges.Persistence;

#nullable disable
namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912020000_M2Citizens")]
public partial class M2Citizens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("citizen_generation_version", "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.CreateTable("citizens", t => new
        {
            id = t.Column<long>(type: "INTEGER", nullable: false), founder_ordinal = t.Column<int>(type: "INTEGER", nullable: false), given_name = t.Column<string>(type: "TEXT", nullable: false), family_name = t.Column<string>(type: "TEXT", nullable: false), birth_minute = t.Column<long>(type: "INTEGER", nullable: false), death_minute = t.Column<long>(type: "INTEGER", nullable: true), death_cause = t.Column<string>(type: "TEXT", nullable: true), parent_a_id = t.Column<long>(type: "INTEGER", nullable: true), parent_b_id = t.Column<long>(type: "INTEGER", nullable: true), partner_id = t.Column<long>(type: "INTEGER", nullable: true), household_id = t.Column<long>(type: "INTEGER", nullable: true), home_structure_id = t.Column<long>(type: "INTEGER", nullable: true), location_x = t.Column<int>(type: "INTEGER", nullable: false), location_y = t.Column<int>(type: "INTEGER", nullable: false), health = t.Column<int>(type: "INTEGER", nullable: false), hunger = t.Column<int>(type: "INTEGER", nullable: false), rest = t.Column<int>(type: "INTEGER", nullable: false), shelter = t.Column<int>(type: "INTEGER", nullable: false), social = t.Column<int>(type: "INTEGER", nullable: false), industriousness = t.Column<int>(type: "INTEGER", nullable: false), sociability = t.Column<int>(type: "INTEGER", nullable: false), curiosity = t.Column<int>(type: "INTEGER", nullable: false), cooperativeness = t.Column<int>(type: "INTEGER", nullable: false), risk_tolerance = t.Column<int>(type: "INTEGER", nullable: false), resilience = t.Column<int>(type: "INTEGER", nullable: false), foraging = t.Column<int>(type: "INTEGER", nullable: false), woodcutting = t.Column<int>(type: "INTEGER", nullable: false), stoneworking = t.Column<int>(type: "INTEGER", nullable: false), construction = t.Column<int>(type: "INTEGER", nullable: false), hauling = t.Column<int>(type: "INTEGER", nullable: false), domestic = t.Column<int>(type: "INTEGER", nullable: false), current_action = t.Column<int>(type: "INTEGER", nullable: false), action_sequence = t.Column<long>(type: "INTEGER", nullable: false), action_started_minute = t.Column<long>(type: "INTEGER", nullable: true), action_completes_minute = t.Column<long>(type: "INTEGER", nullable: true), action_target_x = t.Column<int>(type: "INTEGER", nullable: true), action_target_y = t.Column<int>(type: "INTEGER", nullable: true), needs_updated_minute = t.Column<long>(type: "INTEGER", nullable: false), lifetime_movement_steps = t.Column<long>(type: "INTEGER", nullable: false), lifetime_movement_cost = t.Column<long>(type: "INTEGER", nullable: false)
        }, constraints: t => { t.PrimaryKey("PK_citizens", x => x.id); t.CheckConstraint("CK_citizens_id", "id > 0"); t.CheckConstraint("CK_citizens_founder", "founder_ordinal BETWEEN 0 AND 19"); t.CheckConstraint("CK_citizens_health", "health BETWEEN 0 AND 10000"); t.CheckConstraint("CK_citizens_needs", "hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000"); t.CheckConstraint("CK_citizens_action", "current_action IN (0,1,2,3,4)"); });
        migrationBuilder.CreateIndex("IX_citizens_founder_ordinal", "citizens", "founder_ordinal", unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("citizens"); migrationBuilder.DropColumn("citizen_generation_version", "world_meta"); }
}
