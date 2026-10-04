using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ComplianceAndDocumentTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Connector = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalSubmissions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    From = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_Branch_Version",
                table: "CompanyProfiles",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_Branch_Version",
                table: "DocumentTemplates",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalSubmissions_Branch_Outcome",
                table: "FiscalSubmissions",
                columns: new[] { "Branch", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalSubmissions_DocumentId",
                table: "FiscalSubmissions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssignments_ProductId_From",
                table: "TaxAssignments",
                columns: new[] { "ProductId", "From" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfiles");

            migrationBuilder.DropTable(
                name: "DocumentTemplates");

            migrationBuilder.DropTable(
                name: "FiscalSubmissions");

            migrationBuilder.DropTable(
                name: "TaxAssignments");
        }
    }
}
