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
        migrationBuilder.DropCheckConstraint(name: "CK_citizens_action", table: "citizens");
        migrationBuilder.AddCheckConstraint(name: "CK_citizens_action", table: "citizens", sql: "current_action IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13)");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "living_state_json", table: "world_meta");
}
