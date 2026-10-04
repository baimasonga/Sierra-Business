using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UpdateStatuses",
                columns: table => new
                {
                    Branch = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Checked = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Latest = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NotesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateStatuses", x => x.Branch);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpdateStatuses");
        }
    }
}
