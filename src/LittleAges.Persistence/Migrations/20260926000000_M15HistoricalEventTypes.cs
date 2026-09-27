using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260926000000_M15HistoricalEventTypes")]
public sealed class M15HistoricalEventTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(M14HistoricalEventTypes.RebuildHistoricalEventsSql(maxEventType: 28));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(M14HistoricalEventTypes.RebuildHistoricalEventsSql(maxEventType: 22));
}
