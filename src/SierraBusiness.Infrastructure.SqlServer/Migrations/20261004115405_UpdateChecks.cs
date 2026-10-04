using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.SqlServer.Migrations
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
                    Branch = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Checked = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Latest = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NotesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Problem = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
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
