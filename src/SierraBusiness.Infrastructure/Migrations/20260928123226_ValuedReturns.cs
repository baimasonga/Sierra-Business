using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ValuedReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReturnDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Batch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Net = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Tax = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnAllocations_Documents_OriginalDocumentId",
                        column: x => x.OriginalDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAllocations_Documents_ReturnDocumentId",
                        column: x => x.ReturnDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAllocations_OriginalDocumentId",
                table: "ReturnAllocations",
                column: "OriginalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAllocations_ReturnDocumentId",
                table: "ReturnAllocations",
                column: "ReturnDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnAllocations");
        }
    }
}
