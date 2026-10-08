using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20261007000000_WorldRulesUpgrades")]
public sealed class WorldRulesUpgradesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE world_rules_upgrades (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            source_rules TEXT NOT NULL, target_rules TEXT NOT NULL,
            activation_minute INTEGER NOT NULL CHECK (activation_minute >= 0),
            converters_json TEXT NOT NULL,
            source_fingerprint TEXT NOT NULL, target_fingerprint TEXT NOT NULL,
            backup_path TEXT NOT NULL, backup_sha256 TEXT NOT NULL,
            upgraded_utc TEXT NOT NULL
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE world_rules_upgrades;");
}
