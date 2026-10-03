using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankMatchCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankMatches_JournalLineId",
                table: "BankMatches");

            migrationBuilder.CreateTable(
                name: "BankMatchCancellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankMatchCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankMatchCancellations_BankMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "BankMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankMatches_JournalLineId",
                table: "BankMatches",
                column: "JournalLineId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMatchCancellations_MatchId",
                table: "BankMatchCancellations",
                column: "MatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankMatchCancellations");

            migrationBuilder.DropIndex(
                name: "IX_BankMatches_JournalLineId",
                table: "BankMatches");

            migrationBuilder.CreateIndex(
                name: "IX_BankMatches_JournalLineId",
                table: "BankMatches",
                column: "JournalLineId",
                unique: true);
        }
    }
}
