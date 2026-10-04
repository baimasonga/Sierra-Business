using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PricingBankingStatementsPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccount",
                table: "Employees",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Employees",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BankFormats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Bank = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FormatJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLoans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Principal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Installment = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLoans_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssuedCheques",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BankAccount = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedCheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedCheques_Documents_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CustomerCategory = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MinQuantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    From = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    To = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TillAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Cashier = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    YearEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Profit = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndClosures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssuedChequeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChequeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedChequeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedChequeEvents_IssuedCheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "IssuedCheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[] { 1310, false, true, 0, "Employee loans and advances" });

            migrationBuilder.CreateIndex(
                name: "IX_BankFormats_Bank",
                table: "BankFormats",
                column: "Bank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_DocumentId",
                table: "EmployeeLoans",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_EmployeeId",
                table: "EmployeeLoans",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedChequeEvents_ChequeId",
                table: "IssuedChequeEvents",
                column: "ChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCheques_Branch_BankAccount_Number",
                table: "IssuedCheques",
                columns: new[] { "Branch", "BankAccount", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCheques_VoucherId",
                table: "IssuedCheques",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceRules_Code",
                table: "PriceRules",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillAssignments_Branch_AccountCode_Cashier",
                table: "TillAssignments",
                columns: new[] { "Branch", "AccountCode", "Cashier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndClosures_Branch_YearEnd",
                table: "YearEndClosures",
                columns: new[] { "Branch", "YearEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankFormats");

            migrationBuilder.DropTable(
                name: "EmployeeLoans");

            migrationBuilder.DropTable(
                name: "IssuedChequeEvents");

            migrationBuilder.DropTable(
                name: "PriceRules");

            migrationBuilder.DropTable(
                name: "TillAssignments");

            migrationBuilder.DropTable(
                name: "YearEndClosures");

            migrationBuilder.DropTable(
                name: "IssuedCheques");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1310);

            migrationBuilder.DropColumn(
                name: "BankAccount",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Employees");
        }
    }
}
