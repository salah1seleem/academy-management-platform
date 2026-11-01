using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcademyLauncherIconMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BundledIconKey",
                table: "AcademyPlatformProfiles",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PremiumIconEnabled",
                table: "AcademyPlatformProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BundledIconKey",
                table: "AcademyPlatformProfiles");

            migrationBuilder.DropColumn(
                name: "PremiumIconEnabled",
                table: "AcademyPlatformProfiles");
        }
    }
}
