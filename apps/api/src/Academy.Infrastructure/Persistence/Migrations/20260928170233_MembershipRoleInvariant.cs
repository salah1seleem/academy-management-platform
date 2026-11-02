using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipRoleInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId",
                table: "AcademyMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "Role" },
                unique: true);
        }
    }
}
