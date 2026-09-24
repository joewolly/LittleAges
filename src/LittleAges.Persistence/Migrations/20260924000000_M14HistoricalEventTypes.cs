using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260924000000_M14HistoricalEventTypes")]
public sealed class M14HistoricalEventTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(RebuildHistoricalEventsSql(maxEventType: 22));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(RebuildHistoricalEventsSql(maxEventType: 15));

    private static string RebuildHistoricalEventsSql(int maxEventType) => $$"""
        CREATE TABLE "historical_events_m14" (
            "id" INTEGER NOT NULL,
            "world_minute" INTEGER NOT NULL,
            "event_type" INTEGER NOT NULL,
            "importance" INTEGER NOT NULL,
            "origin" INTEGER NOT NULL,
            "location_x" INTEGER NULL,
            "location_y" INTEGER NULL,
            "payload_json" TEXT NOT NULL,
            "schema_version" INTEGER NOT NULL,
            CONSTRAINT "PK_historical_events" PRIMARY KEY ("id"),
            CONSTRAINT "CK_historical_events_id" CHECK ("id" > 0),
            CONSTRAINT "CK_historical_events_values" CHECK ("world_minute" >= 0 AND "event_type" BETWEEN 1 AND {{maxEventType}} AND "importance" BETWEEN 0 AND 5 AND "origin" IN (1,2) AND "schema_version" = 1 AND (("location_x" IS NULL AND "location_y" IS NULL) OR ("location_x" >= 0 AND "location_y" >= 0)))
        );
        INSERT INTO "historical_events_m14" ("id", "world_minute", "event_type", "importance", "origin", "location_x", "location_y", "payload_json", "schema_version")
        SELECT "id", "world_minute", "event_type", "importance", "origin", "location_x", "location_y", "payload_json", "schema_version"
        FROM "historical_events";

        CREATE TABLE "historical_event_citizens_m14" (
            "event_id" INTEGER NOT NULL,
            "citizen_id" INTEGER NOT NULL,
            "role" TEXT NOT NULL,
            CONSTRAINT "PK_historical_event_citizens" PRIMARY KEY ("event_id", "citizen_id", "role"),
            CONSTRAINT "CK_historical_event_citizens_values" CHECK ("event_id" > 0 AND "citizen_id" > 0 AND "role" IN ('subject','parent','partner','founder','participant','member','contributor')),
            CONSTRAINT "FK_historical_event_citizens_historical_events_event_id" FOREIGN KEY ("event_id") REFERENCES "historical_events_m14" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_historical_event_citizens_citizens_citizen_id" FOREIGN KEY ("citizen_id") REFERENCES "citizens" ("id") ON DELETE RESTRICT
        );
        INSERT INTO "historical_event_citizens_m14" ("event_id", "citizen_id", "role")
        SELECT "event_id", "citizen_id", "role" FROM "historical_event_citizens";

        CREATE TABLE "historical_event_structures_m14" (
            "event_id" INTEGER NOT NULL,
            "structure_id" INTEGER NOT NULL,
            "role" TEXT NOT NULL,
            CONSTRAINT "PK_historical_event_structures" PRIMARY KEY ("event_id", "structure_id", "role"),
            CONSTRAINT "CK_historical_event_structures_values" CHECK ("event_id" > 0 AND "structure_id" > 0 AND "role" = 'subject'),
            CONSTRAINT "FK_historical_event_structures_historical_events_event_id" FOREIGN KEY ("event_id") REFERENCES "historical_events_m14" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_historical_event_structures_structures_structure_id" FOREIGN KEY ("structure_id") REFERENCES "structures" ("id") ON DELETE RESTRICT
        );
        INSERT INTO "historical_event_structures_m14" ("event_id", "structure_id", "role")
        SELECT "event_id", "structure_id", "role" FROM "historical_event_structures";

        CREATE TABLE "memories_m14" (
            "citizen_id" INTEGER NOT NULL,
            "event_id" INTEGER NOT NULL,
            "memory_type" INTEGER NOT NULL,
            "importance" INTEGER NOT NULL,
            "emotional_valence" INTEGER NOT NULL,
            "created_minute" INTEGER NOT NULL,
            CONSTRAINT "PK_memories" PRIMARY KEY ("citizen_id", "event_id", "memory_type"),
            CONSTRAINT "CK_memories_values" CHECK ("citizen_id" > 0 AND "event_id" > 0 AND "memory_type" BETWEEN 1 AND 6 AND "importance" BETWEEN 0 AND 5 AND "emotional_valence" BETWEEN -10000 AND 10000 AND "created_minute" >= 0),
            CONSTRAINT "FK_memories_historical_events_event_id" FOREIGN KEY ("event_id") REFERENCES "historical_events_m14" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_memories_citizens_citizen_id" FOREIGN KEY ("citizen_id") REFERENCES "citizens" ("id") ON DELETE RESTRICT
        );
        INSERT INTO "memories_m14" ("citizen_id", "event_id", "memory_type", "importance", "emotional_valence", "created_minute")
        SELECT "citizen_id", "event_id", "memory_type", "importance", "emotional_valence", "created_minute" FROM "memories";

        DROP TABLE "memories";
        DROP TABLE "historical_event_structures";
        DROP TABLE "historical_event_citizens";
        DROP TABLE "historical_events";

        ALTER TABLE "historical_events_m14" RENAME TO "historical_events";
        ALTER TABLE "historical_event_citizens_m14" RENAME TO "historical_event_citizens";
        ALTER TABLE "historical_event_structures_m14" RENAME TO "historical_event_structures";
        ALTER TABLE "memories_m14" RENAME TO "memories";

        CREATE INDEX "IX_historical_events_world_minute_id" ON "historical_events" ("world_minute", "id");
        CREATE INDEX "IX_historical_events_event_type" ON "historical_events" ("event_type");
        CREATE INDEX "IX_historical_events_importance" ON "historical_events" ("importance");
        CREATE INDEX "IX_historical_event_citizens_citizen_id" ON "historical_event_citizens" ("citizen_id");
        CREATE INDEX "IX_historical_event_structures_structure_id" ON "historical_event_structures" ("structure_id");
        CREATE INDEX "IX_memories_citizen_id" ON "memories" ("citizen_id");
        CREATE INDEX "IX_memories_event_id" ON "memories" ("event_id");
        """;
}
