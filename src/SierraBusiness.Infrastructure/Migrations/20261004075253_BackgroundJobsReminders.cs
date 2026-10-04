using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundJobsReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Job = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Started = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Finished = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobStates",
                columns: table => new
                {
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Job = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LastRun = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSuccess = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    NextDue = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    DeadLettered = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastMessage = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Checkpoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStates", x => new { x.Branch, x.Job });
                });

            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    Due = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Page = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReminderDismissals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReminderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReminderDismissals_Reminders_ReminderId",
                        column: x => x.ReminderId,
                        principalTable: "Reminders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_Branch_Job",
                table: "JobRuns",
                columns: new[] { "Branch", "Job" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDismissals_ReminderId_Actor",
                table: "ReminderDismissals",
                columns: new[] { "ReminderId", "Actor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_Branch_Key",
                table: "Reminders",
                columns: new[] { "Branch", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropTable(
                name: "JobStates");

            migrationBuilder.DropTable(
                name: "ReminderDismissals");

            migrationBuilder.DropTable(
                name: "Reminders");
        }
    }
}
