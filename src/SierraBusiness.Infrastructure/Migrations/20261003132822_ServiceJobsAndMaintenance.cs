using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServiceJobsAndMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Customer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IntervalMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstDue = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ChecklistJson = table.Column<string>(type: "TEXT", nullable: false),
                    Engineer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlanEnds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlanEnds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePlanEnds_MaintenancePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "MaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Customer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Contact = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Equipment = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReportedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlanDue = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ChecklistJson = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceJobs_MaintenancePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "MaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceJobEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceJobEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceJobEvents_ServiceJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ServiceJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 1260, false, true, 0, "Service jobs in progress" },
                    { 4300, false, true, 3, "Service income" },
                    { 5400, false, true, 4, "Warranty and service costs" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlanEnds_PlanId",
                table: "MaintenancePlanEnds",
                column: "PlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_Number",
                table: "MaintenancePlans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobEvents_DocumentId",
                table: "ServiceJobEvents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobEvents_JobId_Sequence",
                table: "ServiceJobEvents",
                columns: new[] { "JobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_Number",
                table: "ServiceJobs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_PlanId_PlanDue",
                table: "ServiceJobs",
                columns: new[] { "PlanId", "PlanDue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_ProductId_Serial",
                table: "ServiceJobs",
                columns: new[] { "ProductId", "Serial" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenancePlanEnds");

            migrationBuilder.DropTable(
                name: "ServiceJobEvents");

            migrationBuilder.DropTable(
                name: "ServiceJobs");

            migrationBuilder.DropTable(
                name: "MaintenancePlans");

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 1260);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 4300);

            migrationBuilder.DeleteData(
                table: "LedgerAccounts",
                keyColumn: "Id",
                keyValue: 5400);
        }
    }
}
