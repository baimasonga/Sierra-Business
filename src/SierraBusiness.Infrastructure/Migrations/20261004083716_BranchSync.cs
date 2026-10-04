using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BranchSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncChanges",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Commit = table.Column<Guid>(type: "TEXT", nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Operation = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Seq);
                });

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IncomingJson = table.Column<string>(type: "TEXT", nullable: false),
                    Reviewed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncPeers",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Pushed = table.Column<long>(type: "INTEGER", nullable: false),
                    Pulled = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAttempt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSuccess = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncPeers", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Commit",
                table: "SyncChanges",
                column: "Commit");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Origin_Seq",
                table: "SyncChanges",
                columns: new[] { "Origin", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Scope_Seq",
                table: "SyncChanges",
                columns: new[] { "Scope", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_Time",
                table: "SyncConflicts",
                column: "Time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncChanges");

            migrationBuilder.DropTable(
                name: "SyncConflicts");

            migrationBuilder.DropTable(
                name: "SyncPeers");
        }
    }
}
