using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankingCashControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var permission in new[] { "tills.use", "tills.manage" })
                migrationBuilder.Sql($"INSERT INTO AspNetUserClaims (UserId, ClaimType, ClaimValue) SELECT u.Id, 'permission', '{permission}' FROM AspNetUsers u WHERE NOT EXISTS (SELECT 1 FROM AspNetUserClaims c WHERE c.UserId = u.Id AND c.ClaimType = 'permission' AND c.ClaimValue = '{permission}') AND (SELECT COUNT(DISTINCT c.ClaimValue) FROM AspNetUserClaims c WHERE c.UserId = u.Id AND c.ClaimType = 'permission' AND c.ClaimValue IN ('business.read','catalogue.write','sales.create','inventory.receive','documents.reverse','accounting.read','audit.read')) = 7;");
            migrationBuilder.AddColumn<Guid>(
                name: "StatementRowId",
                table: "BankMatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Opening = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Closing = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatements_LedgerAccounts_AccountCode",
                        column: x => x.AccountCode,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedCheques",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Party = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Bank = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChequeDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedCheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivedCheques_Documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReturnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCredits_Documents_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TillShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cashier = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Opened = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OpeningCount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    OpeningBook = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillShifts_LedgerAccounts_AccountCode",
                        column: x => x.AccountCode,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Line = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementRows_BankStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "BankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementVoids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementVoids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementVoids_BankStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "BankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChequeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChequeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousState = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    BankAccount = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChequeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChequeEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChequeEvents_ReceivedCheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "ReceivedCheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditUses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditUses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditUses_SupplierCredits_CreditId",
                        column: x => x.CreditId,
                        principalTable: "SupplierCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TillCloses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Closed = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Expected = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Counted = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillCloses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillCloses_TillShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "TillShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TillEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillEntries_Journal_JournalLineId",
                        column: x => x.JournalLineId,
                        principalTable: "Journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TillEntries_TillShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "TillShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1020,
                column: "Control",
                value: true);

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 1030, false, true, 0, "Cheques deposited awaiting clearance" },
                    { 1300, false, true, 0, "Supplier refunds and credits" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankMatches_StatementRowId",
                table: "BankMatches",
                column: "StatementRowId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_StatementId_Line",
                table: "BankStatementRows",
                columns: new[] { "StatementId", "Line" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_AccountCode",
                table: "BankStatements",
                column: "AccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_Branch_AccountCode_ContentHash",
                table: "BankStatements",
                columns: new[] { "Branch", "AccountCode", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_Branch_AccountCode_Reference",
                table: "BankStatements",
                columns: new[] { "Branch", "AccountCode", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementVoids_StatementId",
                table: "BankStatementVoids",
                column: "StatementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChequeEvents_ChequeId_Version",
                table: "ChequeEvents",
                columns: new[] { "ChequeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChequeEvents_DocumentId",
                table: "ChequeEvents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedCheques_Branch_Party_Reference_Bank",
                table: "ReceivedCheques",
                columns: new[] { "Branch", "Party", "Reference", "Bank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedCheques_SourceDocumentId",
                table: "ReceivedCheques",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCredits_ReturnId",
                table: "SupplierCredits",
                column: "ReturnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditUses_CreditId",
                table: "SupplierCreditUses",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditUses_DocumentId",
                table: "SupplierCreditUses",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TillCloses_ShiftId",
                table: "TillCloses",
                column: "ShiftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillEntries_JournalLineId",
                table: "TillEntries",
                column: "JournalLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillEntries_ShiftId",
                table: "TillEntries",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TillShifts_AccountCode",
                table: "TillShifts",
                column: "AccountCode");

            migrationBuilder.AddForeignKey(
                name: "FK_BankMatches_BankStatementRows_StatementRowId",
                table: "BankMatches",
                column: "StatementRowId",
                principalTable: "BankStatementRows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankMatches_BankStatementRows_StatementRowId",
                table: "BankMatches");

            migrationBuilder.DropTable(
                name: "BankStatementRows");

            migrationBuilder.DropTable(
                name: "BankStatementVoids");

            migrationBuilder.DropTable(
                name: "ChequeEvents");

            migrationBuilder.DropTable(
                name: "SupplierCreditUses");

            migrationBuilder.DropTable(
                name: "TillCloses");

            migrationBuilder.DropTable(
                name: "TillEntries");

            migrationBuilder.DropTable(
                name: "BankStatements");

            migrationBuilder.DropTable(
                name: "ReceivedCheques");

            migrationBuilder.DropTable(
                name: "SupplierCredits");

            migrationBuilder.DropTable(
                name: "TillShifts");

            migrationBuilder.DropIndex(
                name: "IX_BankMatches_StatementRowId",
                table: "BankMatches");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1030);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1300);

            migrationBuilder.DropColumn(
                name: "StatementRowId",
                table: "BankMatches");

            migrationBuilder.UpdateData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1020,
                column: "Control",
                value: false);
        }
    }
}

