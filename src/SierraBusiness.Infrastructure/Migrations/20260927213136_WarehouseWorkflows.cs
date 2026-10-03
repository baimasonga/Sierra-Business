using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Movements",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "MAIN");

            migrationBuilder.CreateTable(
                name: "StockLocations",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLocations", x => x.Code);
                });

            migrationBuilder.InsertData(
                table: "StockLocations",
                columns: new[] { "Code", "Name" },
                values: new object[] { "MAIN", "Main warehouse" });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_Location",
                table: "Movements",
                column: "Location");

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_StockLocations_Location",
                table: "Movements",
                column: "Location",
                principalTable: "StockLocations",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_StockLocations_Location",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "StockLocations");

            migrationBuilder.DropIndex(
                name: "IX_Movements_Location",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Movements");
        }
    }
}
