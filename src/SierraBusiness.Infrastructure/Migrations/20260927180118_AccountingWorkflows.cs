using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountingWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Upgrade existing fully privileged owners only; read-only users retain read-only access.
            migrationBuilder.Sql("""
                INSERT INTO AspNetUserClaims (UserId, ClaimType, ClaimValue)
                SELECT u.Id, 'permission', 'accounting.write' FROM AspNetUsers u
                WHERE (SELECT COUNT(DISTINCT c.ClaimValue) FROM AspNetUserClaims c WHERE c.UserId=u.Id AND c.ClaimType='permission'
                  AND c.ClaimValue IN ('business.read','catalogue.write','sales.create','inventory.receive','documents.reverse','accounting.read','audit.read'))=7
                AND NOT EXISTS (SELECT 1 FROM AspNetUserClaims c WHERE c.UserId=u.Id AND c.ClaimType='permission' AND c.ClaimValue='accounting.write');
                """);
            migrationBuilder.AddColumn<string>(
                name: "CostCentre",
                table: "Journal",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClearedDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StatementReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankMatches_Journal_JournalLineId",
                        column: x => x.JournalLineId,
                        principalTable: "Journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillAllocations_Documents_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAllocations_Documents_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Group = table.Column<int>(type: "INTEGER", nullable: false),
                    CashOrBank = table.Column<bool>(type: "INTEGER", nullable: false),
                    Control = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "INTEGER", nullable: false),
                    From = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    To = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CostCentre = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerBudgets_LedgerAccounts_AccountCode",
                        column: x => x.AccountCode,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 1000, true, false, 0, "Cash" },
                    { 1010, true, false, 0, "Bank" },
                    { 1020, false, false, 0, "Cheques in hand" },
                    { 1100, false, true, 0, "Trade receivables" },
                    { 1200, false, true, 0, "Inventory" },
                    { 2000, false, true, 1, "Trade payables" },
                    { 2100, false, false, 1, "Tax payable" },
                    { 2200, false, false, 1, "Payroll payable" },
                    { 3000, false, false, 2, "Owner capital" },
                    { 3100, false, false, 2, "Retained earnings" },
                    { 4000, false, true, 3, "Sales" },
                    { 4100, false, false, 3, "Other income" },
                    { 5000, false, true, 4, "Cost of sales" },
                    { 5100, false, false, 4, "Operating expenses" },
                    { 5200, false, false, 4, "Wages and salaries" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankMatches_JournalLineId",
                table: "BankMatches",
                column: "JournalLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_InvoiceId",
                table: "BillAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_VoucherId_InvoiceId",
                table: "BillAllocations",
                columns: new[] { "VoucherId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerBudgets_AccountCode",
                table: "LedgerBudgets",
                column: "AccountCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankMatches");

            migrationBuilder.DropTable(
                name: "BillAllocations");

            migrationBuilder.DropTable(
                name: "LedgerBudgets");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "CostCentre",
                table: "Journal");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Documents");
        }
    }
}
