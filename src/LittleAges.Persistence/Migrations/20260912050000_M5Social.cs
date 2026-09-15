using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912050000_M5Social")]
public partial class M5Social : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite rebuilds are deliberate: FounderOrdinal becomes nullable and the
        // pre-existing contribution FK must continue to reference a keyed citizens table.
        migrationBuilder.Sql("""
            CREATE TABLE world_meta_m5 (
                id INTEGER NOT NULL CONSTRAINT PK_world_meta PRIMARY KEY,
                world_seed TEXT NOT NULL, world_minute INTEGER NOT NULL,
                world_schema_version TEXT NOT NULL, simulation_rules_version TEXT NOT NULL,
                application_version TEXT NOT NULL, world_configuration_json TEXT NOT NULL,
                generation_version INTEGER NOT NULL, generation_attempt INTEGER NOT NULL,
                starting_x INTEGER NOT NULL, starting_y INTEGER NOT NULL,
                world_fingerprint TEXT NOT NULL, citizen_generation_version INTEGER NOT NULL,
                survival_version INTEGER NOT NULL, settlement_version INTEGER NOT NULL,
                social_version INTEGER NOT NULL, next_entity_id INTEGER NOT NULL,
                next_historical_event_id INTEGER NOT NULL, next_scheduled_event_sequence INTEGER NOT NULL,
                created_utc TEXT NOT NULL, last_checkpoint_utc TEXT NOT NULL,
                CONSTRAINT CK_world_meta_singleton CHECK (id = 1),
                CONSTRAINT CK_world_meta_survival_version CHECK (survival_version IN (0, 1)),
                CONSTRAINT CK_world_meta_settlement_version CHECK (settlement_version IN (0, 1)),
                CONSTRAINT CK_world_meta_social_version CHECK (social_version IN (0, 1))
            );
            INSERT INTO world_meta_m5 SELECT id, world_seed, world_minute, world_schema_version,
                simulation_rules_version, application_version, world_configuration_json, generation_version,
                generation_attempt, starting_x, starting_y, world_fingerprint, citizen_generation_version,
                survival_version, settlement_version, 0, next_entity_id, next_historical_event_id,
                next_scheduled_event_sequence, created_utc, last_checkpoint_utc FROM world_meta;
            DROP TABLE world_meta;
            ALTER TABLE world_meta_m5 RENAME TO world_meta;

            CREATE TABLE structure_contributions_m5 (
                structure_id INTEGER NOT NULL, citizen_id INTEGER NOT NULL,
                construction_work INTEGER NOT NULL, wood_delivered INTEGER NOT NULL, stone_delivered INTEGER NOT NULL,
                CONSTRAINT PK_structure_contributions_m5 PRIMARY KEY (structure_id, citizen_id),
                CONSTRAINT CK_structure_contributions_m5_values CHECK (construction_work >= 0 AND wood_delivered >= 0 AND stone_delivered >= 0)
            );
            INSERT INTO structure_contributions_m5 SELECT structure_id, citizen_id, construction_work, wood_delivered, stone_delivered FROM structure_contributions;
            DROP TABLE structure_contributions;

            CREATE TABLE citizens_m5 (
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
                CONSTRAINT CK_citizens_action CHECK (current_action IN (0,1,2,3,4,5,6,7,8,9,10,11,12)),
                CONSTRAINT CK_citizens_m4_state CHECK (health_updated_minute >= 0 AND action_phase IN (0,1,2,3,4,5,6) AND (target_citizen_id IS NULL OR target_citizen_id > 0) AND (target_resource_node_id IS NULL OR target_resource_node_id > 0) AND (target_structure_id IS NULL OR target_structure_id > 0) AND carried_resource_quantity >= 0 AND lifetime_foraging_minutes >= 0 AND lifetime_woodcutting_minutes >= 0 AND lifetime_stoneworking_minutes >= 0 AND lifetime_construction_minutes >= 0 AND lifetime_hauling_minutes >= 0 AND ((carried_resource_quantity = 0 AND carried_resource_type IS NULL) OR (carried_resource_quantity > 0 AND carried_resource_type IN (1,2,3))))
            );
            INSERT INTO citizens_m5 SELECT id, founder_ordinal, NULL, given_name, family_name, birth_minute,
                death_minute, death_cause, parent_a_id, parent_b_id, partner_id, household_id, home_structure_id,
                location_x, location_y, health, hunger, rest, shelter, social, industriousness, sociability,
                curiosity, cooperativeness, risk_tolerance, resilience, foraging, woodcutting, stoneworking,
                construction, hauling, domestic, current_action, action_sequence, action_started_minute,
                action_completes_minute, action_target_x, action_target_y, needs_updated_minute,
                lifetime_movement_steps, lifetime_movement_cost, health_updated_minute, action_phase,
                target_resource_node_id, carried_resource_type, carried_resource_quantity, target_structure_id,
                lifetime_foraging_minutes, lifetime_woodcutting_minutes, lifetime_stoneworking_minutes,
                lifetime_construction_minutes, lifetime_hauling_minutes FROM citizens;
            DROP TABLE citizens;
            ALTER TABLE citizens_m5 RENAME TO citizens;
            CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);

            CREATE TABLE structure_contributions (
                structure_id INTEGER NOT NULL, citizen_id INTEGER NOT NULL,
                construction_work INTEGER NOT NULL, wood_delivered INTEGER NOT NULL, stone_delivered INTEGER NOT NULL,
                CONSTRAINT PK_structure_contributions PRIMARY KEY (structure_id, citizen_id),
                CONSTRAINT FK_structure_contributions_structures_structure_id FOREIGN KEY (structure_id) REFERENCES structures(id) ON DELETE CASCADE,
                CONSTRAINT FK_structure_contributions_citizens_citizen_id FOREIGN KEY (citizen_id) REFERENCES citizens(id) ON DELETE CASCADE,
                CONSTRAINT CK_structure_contributions_values CHECK (construction_work >= 0 AND wood_delivered >= 0 AND stone_delivered >= 0)
            );
            INSERT INTO structure_contributions SELECT structure_id, citizen_id, construction_work, wood_delivered, stone_delivered FROM structure_contributions_m5;
            DROP TABLE structure_contributions_m5;
            CREATE INDEX IX_structure_contributions_citizen_id ON structure_contributions(citizen_id);

            CREATE TABLE relationships (
                citizen_a_id INTEGER NOT NULL, citizen_b_id INTEGER NOT NULL,
                familiarity INTEGER NOT NULL, affinity INTEGER NOT NULL, trust INTEGER NOT NULL, conflict INTEGER NOT NULL,
                last_interaction_minute INTEGER NOT NULL, interaction_count INTEGER NOT NULL,
                CONSTRAINT PK_relationships PRIMARY KEY (citizen_a_id, citizen_b_id),
                CONSTRAINT CK_relationships_values CHECK (citizen_a_id > 0 AND citizen_b_id > citizen_a_id AND familiarity BETWEEN 0 AND 10000 AND affinity BETWEEN -10000 AND 10000 AND trust BETWEEN 0 AND 10000 AND conflict BETWEEN 0 AND 10000 AND last_interaction_minute >= 0 AND interaction_count >= 1)
            );
            CREATE TABLE households (
                id INTEGER NOT NULL CONSTRAINT PK_households PRIMARY KEY,
                created_minute INTEGER NOT NULL, dissolved_minute INTEGER NULL, dwelling_structure_id INTEGER NULL,
                CONSTRAINT CK_households_minutes CHECK (id > 0 AND created_minute >= 0 AND (dissolved_minute IS NULL OR dissolved_minute >= created_minute) AND (dwelling_structure_id IS NULL OR dwelling_structure_id > 0))
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // M5 is forward-only for live worlds; this is retained only for EF migration bookkeeping.
        migrationBuilder.DropTable(name: "relationships");
        migrationBuilder.DropTable(name: "households");
    }
}
