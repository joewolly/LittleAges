using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20261002000000_M17NewcomerHistory")]
public sealed class M17NewcomerHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(M14HistoricalEventTypes.RebuildHistoricalEventsSql(maxEventType: 37)
            .Replace("memory_type\" BETWEEN 1 AND 6", "memory_type\" BETWEEN 1 AND 7", StringComparison.Ordinal));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(M14HistoricalEventTypes.RebuildHistoricalEventsSql(maxEventType: 31)
            .Replace("memory_type\" BETWEEN 1 AND 6", "memory_type\" BETWEEN 1 AND 7", StringComparison.Ordinal));
}
