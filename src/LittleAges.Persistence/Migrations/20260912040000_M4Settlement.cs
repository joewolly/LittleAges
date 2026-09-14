using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912040000_M4Settlement")]
public partial class M4Settlement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite requires table rebuilds to change existing CHECK expressions.
        migrationBuilder.Sql("""
            CREATE TABLE world_meta_m4 (
                id INTEGER NOT NULL CONSTRAINT PK_world_meta PRIMARY KEY,
                world_seed TEXT NOT NULL, world_minute INTEGER NOT NULL,
                world_schema_version TEXT NOT NULL, simulation_rules_version TEXT NOT NULL,
                application_version TEXT NOT NULL, world_configuration_json TEXT NOT NULL,
                generation_version INTEGER NOT NULL, generation_attempt INTEGER NOT NULL,
                starting_x INTEGER NOT NULL, starting_y INTEGER NOT NULL,
                world_fingerprint TEXT NOT NULL, citizen_generation_version INTEGER NOT NULL,
                survival_version INTEGER NOT NULL, settlement_version INTEGER NOT NULL,
                next_entity_id INTEGER NOT NULL, next_historical_event_id INTEGER NOT NULL,
                next_scheduled_event_sequence INTEGER NOT NULL, created_utc TEXT NOT NULL,
                last_checkpoint_utc TEXT NOT NULL,
                CONSTRAINT CK_world_meta_singleton CHECK (id = 1),
                CONSTRAINT CK_world_meta_survival_version CHECK (survival_version IN (0, 1)),
                CONSTRAINT CK_world_meta_settlement_version CHECK (settlement_version IN (0, 1))
            );
            INSERT INTO world_meta_m4 SELECT id, world_seed, world_minute, world_schema_version,
                simulation_rules_version, application_version, world_configuration_json, generation_version,
                generation_attempt, starting_x, starting_y, world_fingerprint, citizen_generation_version,
                survival_version, 0, next_entity_id, next_historical_event_id, next_scheduled_event_sequence,
                created_utc, last_checkpoint_utc FROM world_meta;
            DROP TABLE world_meta;
            ALTER TABLE world_meta_m4 RENAME TO world_meta;

            CREATE TABLE citizens_m4 (
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
                carried_resource_quantity INTEGER NOT NULL, target_structure_id INTEGER NULL,
                lifetime_foraging_minutes INTEGER NOT NULL, lifetime_woodcutting_minutes INTEGER NOT NULL,
                lifetime_stoneworking_minutes INTEGER NOT NULL, lifetime_construction_minutes INTEGER NOT NULL,
                lifetime_hauling_minutes INTEGER NOT NULL,
                CONSTRAINT CK_citizens_id CHECK (id > 0),
                CONSTRAINT CK_citizens_founder CHECK (founder_ordinal BETWEEN 0 AND 19),
                CONSTRAINT CK_citizens_health CHECK (health BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_needs CHECK (hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_action CHECK (current_action IN (0,1,2,3,4,5,6,7,8,9,10,11)),
                CONSTRAINT CK_citizens_m4_state CHECK (health_updated_minute >= 0 AND action_phase IN (0,1,2,3,4,5,6) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND (target_structure_id IS NULL OR target_structure_id > 0) AND carried_resource_quantity >= 0 AND lifetime_foraging_minutes >= 0 AND lifetime_woodcutting_minutes >= 0 AND lifetime_stoneworking_minutes >= 0 AND lifetime_construction_minutes >= 0 AND lifetime_hauling_minutes >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3))))
            );
            INSERT INTO citizens_m4 SELECT id, founder_ordinal, given_name, family_name, birth_minute,
                death_minute, death_cause, parent_a_id, parent_b_id, partner_id, household_id, home_structure_id,
                location_x, location_y, health, hunger, rest, shelter, social, industriousness, sociability,
                curiosity, cooperativeness, risk_tolerance, resilience, foraging, woodcutting, stoneworking,
                construction, hauling, domestic, current_action, action_sequence, action_started_minute,
                action_completes_minute, action_target_x, action_target_y, needs_updated_minute,
                lifetime_movement_steps, lifetime_movement_cost, health_updated_minute, action_phase,
                target_resource_node_id, carried_resource_type, carried_resource_quantity, NULL, 0, 0, 0, 0, 0 FROM citizens;
            DROP TABLE citizens;
            ALTER TABLE citizens_m4 RENAME TO citizens;
            CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);

            CREATE TABLE settlement_state_m4 (
                id INTEGER NOT NULL CONSTRAINT PK_settlement_state PRIMARY KEY,
                food_stored INTEGER NOT NULL, wood_stored INTEGER NOT NULL, stone_stored INTEGER NOT NULL,
                base_storage_capacity INTEGER NOT NULL, demand_updated_minute INTEGER NOT NULL,
                exposure_consequences_start_minute INTEGER NOT NULL,
                CONSTRAINT CK_settlement_state_singleton CHECK (id = 1),
                CONSTRAINT CK_settlement_state_quantities CHECK (food_stored >= 0 AND wood_stored >= 0 AND stone_stored >= 0 AND base_storage_capacity >= 0 AND demand_updated_minute >= 0 AND exposure_consequences_start_minute >= 0)
            );
            INSERT INTO settlement_state_m4 SELECT id, food_stored, wood_stored, stone_stored, 0, 0, 0 FROM settlement_state;
            DROP TABLE settlement_state;
            ALTER TABLE settlement_state_m4 RENAME TO settlement_state;

            CREATE TABLE structures (
                id INTEGER NOT NULL CONSTRAINT PK_structures PRIMARY KEY,
                type INTEGER NOT NULL, status INTEGER NOT NULL, condition INTEGER NOT NULL, location_x INTEGER NOT NULL, location_y INTEGER NOT NULL,
                construction_started_minute INTEGER NOT NULL, completed_minute INTEGER NULL,
                required_wood INTEGER NOT NULL, delivered_wood INTEGER NOT NULL,
                required_stone INTEGER NOT NULL, delivered_stone INTEGER NOT NULL,
                required_work INTEGER NOT NULL, completed_work INTEGER NOT NULL,
                CONSTRAINT CK_structures_id CHECK (id > 0),
                CONSTRAINT CK_structures_type CHECK (type IN (1,2,3)),
                CONSTRAINT CK_structures_status CHECK (status IN (1,2)),
                CONSTRAINT CK_structures_coordinates CHECK (location_x >= 0 AND location_y >= 0),
                CONSTRAINT CK_structures_values CHECK (construction_started_minute >= 0 AND ((type = 1 AND required_wood = 40 AND required_stone = 10 AND required_work = 600) OR (type = 2 AND required_wood = 60 AND required_stone = 30 AND required_work = 900) OR (type = 3 AND required_wood = 80 AND required_stone = 50 AND required_work = 1200)) AND delivered_wood BETWEEN 0 AND required_wood AND delivered_stone BETWEEN 0 AND required_stone AND completed_work BETWEEN 0 AND required_work AND ((status = 1 AND completed_minute IS NULL AND condition = 0) OR (status = 2 AND completed_minute IS NOT NULL AND delivered_wood = required_wood AND delivered_stone = required_stone AND completed_work = required_work AND condition = 10000)))
            );
            CREATE UNIQUE INDEX IX_structures_location_x_location_y ON structures(location_x, location_y);
            CREATE TABLE structure_contributions (
                structure_id INTEGER NOT NULL, citizen_id INTEGER NOT NULL,
                construction_work INTEGER NOT NULL, wood_delivered INTEGER NOT NULL, stone_delivered INTEGER NOT NULL,
                CONSTRAINT PK_structure_contributions PRIMARY KEY (structure_id, citizen_id),
                CONSTRAINT FK_structure_contributions_structures_structure_id FOREIGN KEY (structure_id) REFERENCES structures(id) ON DELETE CASCADE,
                CONSTRAINT FK_structure_contributions_citizens_citizen_id FOREIGN KEY (citizen_id) REFERENCES citizens(id) ON DELETE CASCADE,
                CONSTRAINT CK_structure_contributions_values CHECK (construction_work >= 0 AND wood_delivered >= 0 AND stone_delivered >= 0)
            );
            CREATE INDEX IX_structure_contributions_citizen_id ON structure_contributions(citizen_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE structure_contributions;
            DROP TABLE structures;
            CREATE TABLE world_meta_m3 (
                id INTEGER NOT NULL CONSTRAINT PK_world_meta PRIMARY KEY, world_seed TEXT NOT NULL, world_minute INTEGER NOT NULL,
                world_schema_version TEXT NOT NULL, simulation_rules_version TEXT NOT NULL, application_version TEXT NOT NULL,
                world_configuration_json TEXT NOT NULL, generation_version INTEGER NOT NULL, generation_attempt INTEGER NOT NULL,
                starting_x INTEGER NOT NULL, starting_y INTEGER NOT NULL, world_fingerprint TEXT NOT NULL,
                citizen_generation_version INTEGER NOT NULL, survival_version INTEGER NOT NULL, next_entity_id INTEGER NOT NULL,
                next_historical_event_id INTEGER NOT NULL, next_scheduled_event_sequence INTEGER NOT NULL, created_utc TEXT NOT NULL,
                last_checkpoint_utc TEXT NOT NULL, CONSTRAINT CK_world_meta_singleton CHECK (id = 1),
                CONSTRAINT CK_world_meta_survival_version CHECK (survival_version IN (0, 1))
            );
            INSERT INTO world_meta_m3 SELECT id, world_seed, world_minute, world_schema_version, simulation_rules_version,
                application_version, world_configuration_json, generation_version, generation_attempt, starting_x, starting_y,
                world_fingerprint, citizen_generation_version, survival_version, next_entity_id, next_historical_event_id,
                next_scheduled_event_sequence, created_utc, last_checkpoint_utc FROM world_meta;
            DROP TABLE world_meta; ALTER TABLE world_meta_m3 RENAME TO world_meta;
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
                target_resource_node_id INTEGER NULL, carried_resource_type INTEGER NULL, carried_resource_quantity INTEGER NOT NULL,
                CONSTRAINT CK_citizens_id CHECK (id > 0), CONSTRAINT CK_citizens_founder CHECK (founder_ordinal BETWEEN 0 AND 19),
                CONSTRAINT CK_citizens_health CHECK (health BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_needs CHECK (hunger BETWEEN 0 AND 10000 AND rest BETWEEN 0 AND 10000 AND shelter BETWEEN 0 AND 10000 AND social BETWEEN 0 AND 10000),
                CONSTRAINT CK_citizens_action CHECK (current_action IN (0,1,2,3,4,5,6,7,8,9)),
                CONSTRAINT CK_citizens_m3_state CHECK (health_updated_minute >= 0 AND action_phase IN (0,1,2,3) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND carried_resource_quantity >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3))))
            );
            INSERT INTO citizens_m3 SELECT id, founder_ordinal, given_name, family_name, birth_minute, death_minute, death_cause,
                parent_a_id, parent_b_id, partner_id, household_id, home_structure_id, location_x, location_y, health, hunger,
                rest, shelter, social, industriousness, sociability, curiosity, cooperativeness, risk_tolerance, resilience,
                foraging, woodcutting, stoneworking, construction, hauling, domestic, current_action, action_sequence,
                action_started_minute, action_completes_minute, action_target_x, action_target_y, needs_updated_minute,
                lifetime_movement_steps, lifetime_movement_cost, health_updated_minute, action_phase, target_resource_node_id,
                carried_resource_type, carried_resource_quantity FROM citizens;
            DROP TABLE citizens; ALTER TABLE citizens_m3 RENAME TO citizens;
            CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);
            CREATE TABLE settlement_state_m3 (id INTEGER NOT NULL CONSTRAINT PK_settlement_state PRIMARY KEY, food_stored INTEGER NOT NULL, wood_stored INTEGER NOT NULL, stone_stored INTEGER NOT NULL, CONSTRAINT CK_settlement_state_singleton CHECK (id = 1), CONSTRAINT CK_settlement_state_quantities CHECK (food_stored >= 0 AND wood_stored >= 0 AND stone_stored >= 0));
            INSERT INTO settlement_state_m3 SELECT id, food_stored, wood_stored, stone_stored FROM settlement_state;
            DROP TABLE settlement_state; ALTER TABLE settlement_state_m3 RENAME TO settlement_state;
            """);
    }
}
