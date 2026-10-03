using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ForeignCurrencyLandedCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForeignBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForeignBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForeignBills_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForeignSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CarryingAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForeignSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForeignSettlements_Documents_BillId",
                        column: x => x.BillId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForeignSettlements_Documents_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LandedCostAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptMovementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Basis = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    InventoryAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CostOfSalesAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_Movements_ReceiptMovementId",
                        column: x => x.ReceiptMovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 4200, false, true, 3, "Exchange gains" },
                    { 5300, false, true, 4, "Exchange losses" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_Branch_Currency_EffectiveFrom",
                table: "ExchangeRates",
                columns: new[] { "Branch", "Currency", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForeignBills_DocumentId",
                table: "ForeignBills",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForeignSettlements_BillId",
                table: "ForeignSettlements",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignSettlements_VoucherId",
                table: "ForeignSettlements",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_DocumentId",
                table: "LandedCostAllocations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_ReceiptMovementId",
                table: "LandedCostAllocations",
                column: "ReceiptMovementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "ForeignBills");

            migrationBuilder.DropTable(
                name: "ForeignSettlements");

            migrationBuilder.DropTable(
                name: "LandedCostAllocations");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 4200);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 5300);
        }
    }
}
