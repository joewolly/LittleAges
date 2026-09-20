using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260920010000_LivingSettlement")]
public sealed partial class LivingSettlement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "living_state_json", table: "world_meta", type: "TEXT", nullable: true);
        // M10 already permits the persisted LivingWork ID (13). Do not rebuild
        // citizens using the historical Living-only target model: it would narrow
        // the constraints of an existing M11 world.
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "living_state_json", table: "world_meta");
}
