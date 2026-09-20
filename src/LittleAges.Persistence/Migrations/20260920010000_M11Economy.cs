using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260920010000_M11Economy")]
public sealed class M11Economy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Rebuild the two enum-constrained tables while preserving every dependent
        // row. Only the schema changes; persisted gameplay versions are untouched.
        migrationBuilder.Sql("PRAGMA foreign_keys=OFF;", suppressTransaction: true);
        migrationBuilder.Sql("""
            CREATE TABLE citizens_m11 (
                id INTEGER NOT NULL CONSTRAINT PK_citizens PRIMARY KEY,
                founder_ordinal INTEGER NULL, target_citizen_id INTEGER NULL,
                given_name TEXT NOT NULL, family_name TEXT NOT NULL, birth_minute INTEGER NOT NULL,
                death_minute INTEGER NULL, death_cause TEXT NULL,
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
                carried_resource_quantity INTEGER NOT NULL, target_structure_id INTEGER NULL,
                lifetime_foraging_minutes INTEGER NOT NULL, lifetime_woodcutting_minutes INTEGER NOT NULL,
                lifetime_stoneworking_minutes INTEGER NOT NULL, lifetime_construction_minutes INTEGER NOT NULL,
                lifetime_hauling_minutes INTEGER NOT NULL,
                CONSTRAINT CK_citizens_id CHECK (id > 0),
                CONSTRAINT CK_citizens_founder CHECK (founder_ordinal IS NULL OR founder_ordinal BETWEEN 0 AND 19),
                CONSTRAINT CK_citizens_health CHECK (health BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_needs CHECK (hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_action CHECK (current_action IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15)),
                CONSTRAINT CK_citizens_m4_state CHECK (health_updated_minute >= 0 AND action_phase IN (0,1,2,3,4,5,6) AND (target_citizen_id IS NULL OR target_citizen_id > 0) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND (target_structure_id IS NULL OR target_structure_id > 0) AND carried_resource_quantity >= 0 AND lifetime_foraging_minutes >= 0 AND lifetime_woodcutting_minutes >= 0 AND lifetime_stoneworking_minutes >= 0 AND lifetime_construction_minutes >= 0 AND lifetime_hauling_minutes >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3))))
            );
            INSERT INTO citizens_m11 (id, founder_ordinal, target_citizen_id, given_name, family_name, birth_minute, death_minute, death_cause, parent_a_id, parent_b_id, partner_id, household_id, home_structure_id, location_x, location_y, health, hunger, rest, shelter, social, industriousness, sociability, curiosity, cooperativeness, risk_tolerance, resilience, foraging, woodcutting, stoneworking, construction, hauling, domestic, current_action, action_sequence, action_started_minute, action_completes_minute, action_target_x, action_target_y, needs_updated_minute, lifetime_movement_steps, lifetime_movement_cost, health_updated_minute, action_phase, target_resource_node_id, carried_resource_type, carried_resource_quantity, target_structure_id, lifetime_foraging_minutes, lifetime_woodcutting_minutes, lifetime_stoneworking_minutes, lifetime_construction_minutes, lifetime_hauling_minutes)
                SELECT id, founder_ordinal, target_citizen_id, given_name, family_name, birth_minute, death_minute, death_cause, parent_a_id, parent_b_id, partner_id, household_id, home_structure_id, location_x, location_y, health, hunger, rest, shelter, social, industriousness, sociability, curiosity, cooperativeness, risk_tolerance, resilience, foraging, woodcutting, stoneworking, construction, hauling, domestic, current_action, action_sequence, action_started_minute, action_completes_minute, action_target_x, action_target_y, needs_updated_minute, lifetime_movement_steps, lifetime_movement_cost, health_updated_minute, action_phase, target_resource_node_id, carried_resource_type, carried_resource_quantity, target_structure_id, lifetime_foraging_minutes, lifetime_woodcutting_minutes, lifetime_stoneworking_minutes, lifetime_construction_minutes, lifetime_hauling_minutes FROM citizens;
            DROP TABLE citizens;
            ALTER TABLE citizens_m11 RENAME TO citizens;
            CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);
            CREATE TABLE structures_m11 (
                id INTEGER NOT NULL CONSTRAINT PK_structures PRIMARY KEY,
                type INTEGER NOT NULL, status INTEGER NOT NULL, condition INTEGER NOT NULL, location_x INTEGER NOT NULL, location_y INTEGER NOT NULL,
                construction_started_minute INTEGER NOT NULL, completed_minute INTEGER NULL,
                required_wood INTEGER NOT NULL, delivered_wood INTEGER NOT NULL,
                required_stone INTEGER NOT NULL, delivered_stone INTEGER NOT NULL,
                required_work INTEGER NOT NULL, completed_work INTEGER NOT NULL,
                CONSTRAINT CK_structures_id CHECK (id > 0),
                CONSTRAINT CK_structures_type CHECK (type IN (1,2,3,4,5,6)),
                CONSTRAINT CK_structures_status CHECK (status IN (1,2)),
                CONSTRAINT CK_structures_coordinates CHECK (location_x >= 0 AND location_y >= 0),
                CONSTRAINT CK_structures_values CHECK (construction_started_minute >= 0 AND ((type = 1 AND required_wood = 40 AND required_stone = 10 AND required_work = 600) OR (type = 2 AND required_wood = 60 AND required_stone = 30 AND required_work = 900) OR (type = 3 AND required_wood = 80 AND required_stone = 50 AND required_work = 1200) OR (type = 4 AND required_wood = 60 AND required_stone = 10 AND required_work = 900) OR (type = 5 AND required_wood = 100 AND required_stone = 60 AND required_work = 1500) OR (type = 6 AND required_wood = 80 AND required_stone = 30 AND required_work = 1000)) AND delivered_wood BETWEEN 0 AND required_wood AND delivered_stone BETWEEN 0 AND required_stone AND completed_work BETWEEN 0 AND required_work AND ((status = 1 AND completed_minute IS NULL AND condition = 0) OR (status = 2 AND completed_minute IS NOT NULL AND delivered_wood = required_wood AND delivered_stone = required_stone AND completed_work = required_work AND condition = 10000)))
            );
            INSERT INTO structures_m11 (id, type, status, condition, location_x, location_y, construction_started_minute, completed_minute, required_wood, delivered_wood, required_stone, delivered_stone, required_work, completed_work)
                SELECT id, type, status, condition, location_x, location_y, construction_started_minute, completed_minute, required_wood, delivered_wood, required_stone, delivered_stone, required_work, completed_work FROM structures;
            DROP TABLE structures;
            ALTER TABLE structures_m11 RENAME TO structures;
            CREATE UNIQUE INDEX IX_structures_location_x_location_y ON structures(location_x, location_y);
            CREATE TABLE economy_state (
                id INTEGER NOT NULL CONSTRAINT PK_economy_state PRIMARY KEY,
                canonical_json TEXT NOT NULL,
                CONSTRAINT CK_economy_state_singleton CHECK (id = 1)
            );
            """);
        migrationBuilder.Sql("PRAGMA foreign_keys=ON;", suppressTransaction: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Economic databases cannot be downgraded.");
}
