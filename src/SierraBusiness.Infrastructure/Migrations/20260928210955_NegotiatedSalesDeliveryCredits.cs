using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NegotiatedSalesDeliveryCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BillCredit",
                table: "ReturnAllocations",
                type: "TEXT",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CustomerCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReturnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCredits_Documents_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryInvoices_Documents_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryInvoices_Documents_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MinimumPrice = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricePolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricePolicies_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Batch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_StockLocations_Location",
                        column: x => x.Location,
                        principalTable: "StockLocations",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCreditUses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCreditUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCreditUses_CustomerCredits_CreditId",
                        column: x => x.CreditId,
                        principalTable: "CustomerCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditUses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReservationReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReservationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationReleases_StockReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 1250, false, true, 0, "Delivered goods awaiting invoice" },
                    { 2400, false, true, 1, "Customer credits" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCredits_ReturnId",
                table: "CustomerCredits",
                column: "ReturnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditUses_CreditId",
                table: "CustomerCreditUses",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditUses_DocumentId",
                table: "CustomerCreditUses",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInvoices_DeliveryId",
                table: "DeliveryInvoices",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInvoices_InvoiceId",
                table: "DeliveryInvoices",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricePolicies_ProductId_Version",
                table: "PricePolicies",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservationReleases_ReservationId",
                table: "ReservationReleases",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_Location",
                table: "StockReservations",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                table: "StockReservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ProductId",
                table: "StockReservations",
                column: "ProductId");
            migrationBuilder.Sql("UPDATE ReturnAllocations SET BillCredit = ROUND(CAST(Net AS REAL) + CAST(Tax AS REAL), 2) WHERE OriginalDocumentId IN (SELECT Id FROM Documents WHERE Kind = 0 OR (Kind = 1 AND Settlement = 'Credit'));");
            migrationBuilder.Sql("INSERT INTO AspNetUserClaims (UserId, ClaimType, ClaimValue) SELECT u.Id, 'permission', 'pricing.manage' FROM AspNetUsers u WHERE NOT EXISTS (SELECT 1 FROM AspNetUserClaims c WHERE c.UserId = u.Id AND c.ClaimType = 'permission' AND c.ClaimValue = 'pricing.manage') AND (SELECT COUNT(DISTINCT c.ClaimValue) FROM AspNetUserClaims c WHERE c.UserId = u.Id AND c.ClaimType = 'permission' AND c.ClaimValue IN ('business.read','catalogue.write','sales.create','inventory.receive','documents.reverse','accounting.read','audit.read')) = 7;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerCreditUses");

            migrationBuilder.DropTable(
                name: "DeliveryInvoices");

            migrationBuilder.DropTable(
                name: "PricePolicies");

            migrationBuilder.DropTable(
                name: "ReservationReleases");

            migrationBuilder.DropTable(
                name: "CustomerCredits");

            migrationBuilder.DropTable(
                name: "StockReservations");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1250);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 2400);

            migrationBuilder.DropColumn(
                name: "BillCredit",
                table: "ReturnAllocations");
        }
    }
}


