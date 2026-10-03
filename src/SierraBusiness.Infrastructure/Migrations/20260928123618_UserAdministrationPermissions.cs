using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserAdministrationPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("INSERT INTO AspNetUserClaims (UserId, ClaimType, ClaimValue) SELECT u.Id, 'permission', 'users.manage' FROM AspNetUsers u WHERE (SELECT COUNT(DISTINCT c.ClaimValue) FROM AspNetUserClaims c WHERE c.UserId=u.Id AND c.ClaimType='permission' AND c.ClaimValue IN ('business.read','catalogue.write','sales.create','inventory.receive','documents.reverse','accounting.read','audit.read'))=7 AND NOT EXISTS (SELECT 1 FROM AspNetUserClaims c WHERE c.UserId=u.Id AND c.ClaimType='permission' AND c.ClaimValue='users.manage');");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

