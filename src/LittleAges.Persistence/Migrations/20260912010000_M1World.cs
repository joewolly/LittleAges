using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleAges.Persistence.Migrations;

[DbContext(typeof(LittleAgesDbContext))]
[Migration("20260912010000_M1World")]
public partial class M1World : Migration
{
    private static readonly string[] TileCoordinateIndexColumns = ["x", "y"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "generation_attempt", table: "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "generation_version", table: "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "starting_x", table: "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "starting_y", table: "world_meta", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "world_fingerprint", table: "world_meta", type: "TEXT", nullable: false, defaultValue: "");

        migrationBuilder.CreateTable(
            name: "world_tiles",
            columns: table => new
            {
                tile_index = table.Column<long>(type: "INTEGER", nullable: false),
                x = table.Column<int>(type: "INTEGER", nullable: false),
                y = table.Column<int>(type: "INTEGER", nullable: false),
                terrain = table.Column<int>(type: "INTEGER", nullable: false),
                elevation = table.Column<int>(type: "INTEGER", nullable: false),
                fertility = table.Column<int>(type: "INTEGER", nullable: false),
                water_access = table.Column<int>(type: "INTEGER", nullable: false),
                walkable = table.Column<bool>(type: "INTEGER", nullable: false),
                movement_cost = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_tiles", x => x.tile_index);
                table.CheckConstraint("CK_world_tiles_index", "tile_index >= 0");
                table.CheckConstraint("CK_world_tiles_coordinates", "x >= 0 AND y >= 0");
                table.CheckConstraint("CK_world_tiles_values", "elevation BETWEEN 0 AND 10000 AND fertility BETWEEN 0 AND 10000 AND water_access BETWEEN 0 AND 10000");
                table.CheckConstraint("CK_world_tiles_walkability", "((walkable = 1 AND movement_cost > 0) OR (walkable = 0 AND movement_cost = 0))");
                table.CheckConstraint("CK_world_tiles_terrain", "terrain IN (1, 2, 3, 4, 5)");
            });

        migrationBuilder.CreateTable(
            name: "resource_nodes",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false),
                tile_index = table.Column<long>(type: "INTEGER", nullable: false),
                x = table.Column<int>(type: "INTEGER", nullable: false),
                y = table.Column<int>(type: "INTEGER", nullable: false),
                resource = table.Column<int>(type: "INTEGER", nullable: false),
                initial_quantity = table.Column<int>(type: "INTEGER", nullable: false),
                maximum_quantity = table.Column<int>(type: "INTEGER", nullable: false),
                regeneration_potential = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_resource_nodes", x => x.id);
                table.ForeignKey("FK_resource_nodes_world_tiles_tile_index", x => x.tile_index, "world_tiles", "tile_index", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_resource_nodes_identity", "id > 0 AND tile_index >= 0 AND x >= 0 AND y >= 0");
                table.CheckConstraint("CK_resource_nodes_type", "resource IN (1, 2, 3)");
                table.CheckConstraint("CK_resource_nodes_quantities", "initial_quantity > 0 AND maximum_quantity >= initial_quantity AND regeneration_potential BETWEEN 0 AND 10000");
            });

        migrationBuilder.CreateIndex(name: "IX_resource_nodes_tile_index", table: "resource_nodes", column: "tile_index");
        migrationBuilder.CreateIndex(name: "IX_world_tiles_x_y", table: "world_tiles", columns: TileCoordinateIndexColumns, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "resource_nodes");
        migrationBuilder.DropTable(name: "world_tiles");
        migrationBuilder.DropColumn(name: "generation_attempt", table: "world_meta");
        migrationBuilder.DropColumn(name: "generation_version", table: "world_meta");
        migrationBuilder.DropColumn(name: "starting_x", table: "world_meta");
        migrationBuilder.DropColumn(name: "starting_y", table: "world_meta");
        migrationBuilder.DropColumn(name: "world_fingerprint", table: "world_meta");
    }
}
