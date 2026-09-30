using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260930000000_M16Storehouses")]
public sealed class M16Storehouses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Admit the M16 storehouse structure type. SQLite cannot alter a CHECK
        // constraint, so the table is rebuilt with every row and index preserved.
        migrationBuilder.Sql("PRAGMA foreign_keys=OFF;", suppressTransaction: true);
        migrationBuilder.Sql("""
            CREATE TABLE structures_m16 (
                id INTEGER NOT NULL CONSTRAINT PK_structures PRIMARY KEY,
                type INTEGER NOT NULL, status INTEGER NOT NULL, condition INTEGER NOT NULL, location_x INTEGER NOT NULL, location_y INTEGER NOT NULL,
                construction_started_minute INTEGER NOT NULL, completed_minute INTEGER NULL,
                required_wood INTEGER NOT NULL, delivered_wood INTEGER NOT NULL,
                required_stone INTEGER NOT NULL, delivered_stone INTEGER NOT NULL,
                required_work INTEGER NOT NULL, completed_work INTEGER NOT NULL,
                CONSTRAINT CK_structures_id CHECK (id > 0),
                CONSTRAINT CK_structures_type CHECK (type IN (1,2,3,4,5,6,7)),
                CONSTRAINT CK_structures_status CHECK (status IN (1,2)),
                CONSTRAINT CK_structures_coordinates CHECK (location_x >= 0 AND location_y >= 0),
                CONSTRAINT CK_structures_values CHECK (construction_started_minute >= 0 AND ((type = 1 AND required_wood = 40 AND required_stone = 10 AND required_work = 600) OR (type = 2 AND required_wood = 60 AND required_stone = 30 AND required_work = 900) OR (type = 3 AND required_wood = 80 AND required_stone = 50 AND required_work = 1200) OR (type = 4 AND required_wood = 60 AND required_stone = 10 AND required_work = 900) OR (type = 5 AND required_wood = 100 AND required_stone = 60 AND required_work = 1500) OR (type = 6 AND required_wood = 80 AND required_stone = 30 AND required_work = 1000) OR (type = 7 AND required_wood = 120 AND required_stone = 60 AND required_work = 1800)) AND delivered_wood BETWEEN 0 AND required_wood AND delivered_stone BETWEEN 0 AND required_stone AND completed_work BETWEEN 0 AND required_work AND ((status = 1 AND completed_minute IS NULL AND condition = 0) OR (status = 2 AND completed_minute IS NOT NULL AND delivered_wood = required_wood AND delivered_stone = required_stone AND completed_work = required_work AND condition = 10000)))
            );
            INSERT INTO structures_m16 (id, type, status, condition, location_x, location_y, construction_started_minute, completed_minute, required_wood, delivered_wood, required_stone, delivered_stone, required_work, completed_work)
                SELECT id, type, status, condition, location_x, location_y, construction_started_minute, completed_minute, required_wood, delivered_wood, required_stone, delivered_stone, required_work, completed_work FROM structures;
            DROP TABLE structures;
            ALTER TABLE structures_m16 RENAME TO structures;
            CREATE UNIQUE INDEX IX_structures_location_x_location_y ON structures(location_x, location_y);
            """);
        migrationBuilder.Sql("PRAGMA foreign_keys=ON;", suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restores the M15 constraint. A database that already holds a storehouse
        // fails the copy's CHECK, so the downgrade is refused rather than losing the row.
        migrationBuilder.Sql("PRAGMA foreign_keys=OFF;", suppressTransaction: true);
        migrationBuilder.Sql("""
            CREATE TABLE structures_m15 (
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
            INSERT INTO structures_m15 (id, type, status, condition, location_x, location_y, construction_started_minute, completed_minute, required_wood, delivered_wood, required_stone, delivered_stone, required_work, completed_work)
                SELECT id, type, status, condition, location_x, location_y, construction_started_minute, completed_minute, required_wood, delivered_wood, required_stone, delivered_stone, required_work, completed_work FROM structures;
            DROP TABLE structures;
            ALTER TABLE structures_m15 RENAME TO structures;
            CREATE UNIQUE INDEX IX_structures_location_x_location_y ON structures(location_x, location_y);
            """);
        migrationBuilder.Sql("PRAGMA foreign_keys=ON;", suppressTransaction: true);
    }
}
