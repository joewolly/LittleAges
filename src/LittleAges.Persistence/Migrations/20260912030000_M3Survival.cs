using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912030000_M3Survival")]
public partial class M3Survival : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("survival_version", "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>("health_updated_minute", "citizens", type: "INTEGER", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<int>("action_phase", "citizens", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>("target_resource_node_id", "citizens", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("carried_resource_type", "citizens", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("carried_resource_quantity", "citizens", type: "INTEGER", nullable: false, defaultValue: 0);

        // SQLite cannot alter CHECK expressions in-place. Rebuild the two legacy
        // tables so M3 enum/version constraints apply to databases upgraded from M2.
        migrationBuilder.Sql("""
            CREATE TABLE world_meta_m3 (
                id INTEGER NOT NULL CONSTRAINT PK_world_meta PRIMARY KEY,
                world_seed TEXT NOT NULL, world_minute INTEGER NOT NULL,
                world_schema_version TEXT NOT NULL, simulation_rules_version TEXT NOT NULL,
                application_version TEXT NOT NULL, world_configuration_json TEXT NOT NULL,
                generation_version INTEGER NOT NULL, generation_attempt INTEGER NOT NULL,
                starting_x INTEGER NOT NULL, starting_y INTEGER NOT NULL,
                world_fingerprint TEXT NOT NULL, citizen_generation_version INTEGER NOT NULL,
                survival_version INTEGER NOT NULL, next_entity_id INTEGER NOT NULL,
                next_historical_event_id INTEGER NOT NULL, next_scheduled_event_sequence INTEGER NOT NULL,
                created_utc TEXT NOT NULL, last_checkpoint_utc TEXT NOT NULL,
                CONSTRAINT CK_world_meta_singleton CHECK (id = 1),
                CONSTRAINT CK_world_meta_survival_version CHECK (survival_version IN (0, 1))
            );
            INSERT INTO world_meta_m3 SELECT id, world_seed, world_minute, world_schema_version,
                simulation_rules_version, application_version, world_configuration_json,
                generation_version, generation_attempt, starting_x, starting_y, world_fingerprint,
                citizen_generation_version, survival_version, next_entity_id, next_historical_event_id,
                next_scheduled_event_sequence, created_utc, last_checkpoint_utc FROM world_meta;
            DROP TABLE world_meta;
            ALTER TABLE world_meta_m3 RENAME TO world_meta;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE citizens_m3 (
                id INTEGER NOT NULL CONSTRAINT PK_citizens PRIMARY KEY,
                founder_ordinal INTEGER NOT NULL, given_name TEXT NOT NULL, family_name TEXT NOT NULL,
                birth_minute INTEGER NOT NULL, death_minute INTEGER NULL, death_cause TEXT NULL,
                parent_a_id INTEGER NULL, parent_b_id INTEGER NULL, partner_id INTEGER NULL,
                household_id INTEGER NULL, home_structure_id INTEGER NULL,
                location_x INTEGER NOT NULL, location_y INTEGER NOT NULL, health INTEGER NOT NULL,
                hunger INTEGER NOT NULL, rest INTEGER NOT NULL, shelter INTEGER NOT NULL, social INTEGER NOT NULL,
                industriousness INTEGER NOT NULL, sociability INTEGER NOT NULL, curiosity INTEGER NOT NULL,
                cooperativeness INTEGER NOT NULL, risk_tolerance INTEGER NOT NULL, resilience INTEGER NOT NULL,
                foraging INTEGER NOT NULL, woodcutting INTEGER NOT NULL, stoneworking INTEGER NOT NULL,
                construction INTEGER NOT NULL, hauling INTEGER NOT NULL, domestic INTEGER NOT NULL,
                current_action INTEGER NOT NULL, action_sequence INTEGER NOT NULL,
                action_started_minute INTEGER NULL, action_completes_minute INTEGER NULL,
                action_target_x INTEGER NULL, action_target_y INTEGER NULL, needs_updated_minute INTEGER NOT NULL,
                lifetime_movement_steps INTEGER NOT NULL, lifetime_movement_cost INTEGER NOT NULL,
                health_updated_minute INTEGER NOT NULL, action_phase INTEGER NOT NULL,
                target_resource_node_id INTEGER NULL, carried_resource_type INTEGER NULL,
                carried_resource_quantity INTEGER NOT NULL,
                CONSTRAINT CK_citizens_id CHECK (id > 0),
                CONSTRAINT CK_citizens_founder CHECK (founder_ordinal BETWEEN 0 AND 19),
                CONSTRAINT CK_citizens_health CHECK (health BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_needs CHECK (hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_action CHECK (current_action IN (0,1,2,3,4,5,6,7,8,9)),
                CONSTRAINT CK_citizens_m3_state CHECK (health_updated_minute >= 0 AND action_phase IN (0,1,2,3) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND carried_resource_quantity >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3))))
            );
            INSERT INTO citizens_m3 SELECT id, founder_ordinal, given_name, family_name, birth_minute,
                death_minute, death_cause, parent_a_id, parent_b_id, partner_id, household_id, home_structure_id,
                location_x, location_y, health, hunger, rest, shelter, social, industriousness, sociability,
                curiosity, cooperativeness, risk_tolerance, resilience, foraging, woodcutting, stoneworking,
                construction, hauling, domestic, current_action, action_sequence, action_started_minute,
                action_completes_minute, action_target_x, action_target_y, needs_updated_minute,
                lifetime_movement_steps, lifetime_movement_cost, health_updated_minute, action_phase,
                target_resource_node_id, carried_resource_type, carried_resource_quantity FROM citizens;
            DROP TABLE citizens;
            ALTER TABLE citizens_m3 RENAME TO citizens;
            CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);
            """);

        migrationBuilder.CreateTable(
            name: "resource_state",
            columns: table => new
            {
                resource_node_id = table.Column<long>(type: "INTEGER", nullable: false),
                current_quantity = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_resource_state", x => x.resource_node_id);
                table.ForeignKey("FK_resource_state_resource_nodes_resource_node_id", x => x.resource_node_id, "resource_nodes", "id", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_resource_state_quantity", "current_quantity >= 0");
            });

        migrationBuilder.CreateTable(
            name: "settlement_state",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false),
                food_stored = table.Column<int>(type: "INTEGER", nullable: false),
                wood_stored = table.Column<int>(type: "INTEGER", nullable: false),
                stone_stored = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_settlement_state", x => x.id);
                table.CheckConstraint("CK_settlement_state_singleton", "id = 1");
                table.CheckConstraint("CK_settlement_state_quantities", "food_stored >= 0 AND wood_stored >= 0 AND stone_stored >= 0");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // SQLite does not implement DROP COLUMN for migration operations. Rebuild
        // the two tables to return to the exact M2 shape and checks.
        migrationBuilder.Sql("""
            DROP TABLE settlement_state;
            DROP TABLE resource_state;
            CREATE TABLE world_meta_m2 (
                id INTEGER NOT NULL CONSTRAINT PK_world_meta PRIMARY KEY,
                world_seed TEXT NOT NULL, world_minute INTEGER NOT NULL,
                world_schema_version TEXT NOT NULL, simulation_rules_version TEXT NOT NULL,
                application_version TEXT NOT NULL, world_configuration_json TEXT NOT NULL,
                generation_version INTEGER NOT NULL, generation_attempt INTEGER NOT NULL,
                starting_x INTEGER NOT NULL, starting_y INTEGER NOT NULL,
                world_fingerprint TEXT NOT NULL, citizen_generation_version INTEGER NOT NULL,
                next_entity_id INTEGER NOT NULL, next_historical_event_id INTEGER NOT NULL,
                next_scheduled_event_sequence INTEGER NOT NULL,
                created_utc TEXT NOT NULL, last_checkpoint_utc TEXT NOT NULL,
                CONSTRAINT CK_world_meta_singleton CHECK (id = 1)
            );
            INSERT INTO world_meta_m2 (id, world_seed, world_minute, world_schema_version,
                simulation_rules_version, application_version, world_configuration_json,
                generation_version, generation_attempt, starting_x, starting_y, world_fingerprint,
                citizen_generation_version, next_entity_id, next_historical_event_id,
                next_scheduled_event_sequence, created_utc, last_checkpoint_utc)
                SELECT id, world_seed, world_minute, world_schema_version, simulation_rules_version,
                application_version, world_configuration_json, generation_version, generation_attempt,
                starting_x, starting_y, world_fingerprint, citizen_generation_version, next_entity_id,
                next_historical_event_id, next_scheduled_event_sequence, created_utc, last_checkpoint_utc
                FROM world_meta;
            DROP TABLE world_meta;
            ALTER TABLE world_meta_m2 RENAME TO world_meta;
            CREATE TABLE citizens_m2 (
                id INTEGER NOT NULL CONSTRAINT PK_citizens PRIMARY KEY,
                founder_ordinal INTEGER NOT NULL, given_name TEXT NOT NULL, family_name TEXT NOT NULL,
                birth_minute INTEGER NOT NULL, death_minute INTEGER NULL, death_cause TEXT NULL,
                parent_a_id INTEGER NULL, parent_b_id INTEGER NULL, partner_id INTEGER NULL,
                household_id INTEGER NULL, home_structure_id INTEGER NULL,
                location_x INTEGER NOT NULL, location_y INTEGER NOT NULL, health INTEGER NOT NULL,
                hunger INTEGER NOT NULL, rest INTEGER NOT NULL, shelter INTEGER NOT NULL, social INTEGER NOT NULL,
                industriousness INTEGER NOT NULL, sociability INTEGER NOT NULL, curiosity INTEGER NOT NULL,
                cooperativeness INTEGER NOT NULL, risk_tolerance INTEGER NOT NULL, resilience INTEGER NOT NULL,
                foraging INTEGER NOT NULL, woodcutting INTEGER NOT NULL, stoneworking INTEGER NOT NULL,
                construction INTEGER NOT NULL, hauling INTEGER NOT NULL, domestic INTEGER NOT NULL,
                current_action INTEGER NOT NULL, action_sequence INTEGER NOT NULL,
                action_started_minute INTEGER NULL, action_completes_minute INTEGER NULL,
                action_target_x INTEGER NULL, action_target_y INTEGER NULL, needs_updated_minute INTEGER NOT NULL,
                lifetime_movement_steps INTEGER NOT NULL, lifetime_movement_cost INTEGER NOT NULL,
                CONSTRAINT CK_citizens_id CHECK (id > 0),
                CONSTRAINT CK_citizens_founder CHECK (founder_ordinal BETWEEN 0 AND 19),
                CONSTRAINT CK_citizens_health CHECK (health BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_needs CHECK (hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_action CHECK (current_action IN (0,1,2,3,4))
            );
            INSERT INTO citizens_m2 SELECT id, founder_ordinal, given_name, family_name, birth_minute,
                death_minute, death_cause, parent_a_id, parent_b_id, partner_id, household_id, home_structure_id,
                location_x, location_y, health, hunger, rest, shelter, social, industriousness, sociability,
                curiosity, cooperativeness, risk_tolerance, resilience, foraging, woodcutting, stoneworking,
                construction, hauling, domestic, current_action, action_sequence, action_started_minute,
                action_completes_minute, action_target_x, action_target_y, needs_updated_minute,
                lifetime_movement_steps, lifetime_movement_cost FROM citizens;
            DROP TABLE citizens;
            ALTER TABLE citizens_m2 RENAME TO citizens;
            CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);
            """);
    }
}
