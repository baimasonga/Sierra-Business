using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StatutoryPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NassitExempt",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NassitNumber",
                table: "Employees",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "NonResident",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tin",
                table: "Employees",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PayrollPaymentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPaymentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPaymentLines_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPaymentLines_PayrollItems_PayrollItemId",
                        column: x => x.PayrollItemId,
                        principalTable: "PayrollItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryRemittances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryRemittances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryRemittances_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 2310, false, true, 1, "PAYE payable" },
                    { 2320, false, true, 1, "NASSIT payable" },
                    { 5210, false, true, 4, "Employer NASSIT contributions" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentLines_DocumentId",
                table: "PayrollPaymentLines",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentLines_PayrollItemId",
                table: "PayrollPaymentLines",
                column: "PayrollItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_Branch_EffectiveFrom",
                table: "PayrollRules",
                columns: new[] { "Branch", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_Branch_Version",
                table: "PayrollRules",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryRemittances_Branch_Month_Kind",
                table: "StatutoryRemittances",
                columns: new[] { "Branch", "Month", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryRemittances_DocumentId",
                table: "StatutoryRemittances",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollPaymentLines");

            migrationBuilder.DropTable(
                name: "PayrollRules");

            migrationBuilder.DropTable(
                name: "StatutoryRemittances");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 2310);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 2320);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 5210);

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "NassitExempt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "NassitNumber",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "NonResident",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Tin",
                table: "Employees");
        }
    }
}
