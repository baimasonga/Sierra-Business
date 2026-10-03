using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupplierInvoiceMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptMovementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    ExpectedSource = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    GoodsCleared = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    PriceVariance = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    InventoryAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CostOfSalesAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Documents_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Documents_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Movements_ReceiptMovementId",
                        column: x => x.ReceiptMovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderCurrencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCurrencies_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[] { 2050, false, true, 1, "Goods received not invoiced" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceMatches_InvoiceId_ReceiptMovementId",
                table: "InvoiceMatches",
                columns: new[] { "InvoiceId", "ReceiptMovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceMatches_ReceiptId",
                table: "InvoiceMatches",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceMatches_ReceiptMovementId",
                table: "InvoiceMatches",
                column: "ReceiptMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCurrencies_OrderId",
                table: "OrderCurrencies",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceMatches");

            migrationBuilder.DropTable(
                name: "OrderCurrencies");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 2050);
        }
    }
}
