using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260929000000_M16FestivalHistory")]
public sealed class M16FestivalHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(M14HistoricalEventTypes.RebuildHistoricalEventsSql(maxEventType: 31)
            .Replace("memory_type\" BETWEEN 1 AND 6", "memory_type\" BETWEEN 1 AND 7", StringComparison.Ordinal));

    // Copying into the earlier constraints deliberately rejects a downgrade when
    // festival history exists, instead of discarding events or memories.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(M14HistoricalEventTypes.RebuildHistoricalEventsSql(maxEventType: 28));
}
