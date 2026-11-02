using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4RoleScopedInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudienceRole",
                table: "AcademyNotices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetId",
                table: "AcademyNotices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetKind",
                table: "AcademyNotices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"AcademyNotices\" WHERE \"AudienceRole\" IS NOT NULL OR \"TargetId\" IS NOT NULL OR \"TargetKind\" IS NOT NULL) THEN RAISE EXCEPTION 'Role-scoped inbox history requires a reviewed forward migration'; END IF; END $$;");
            migrationBuilder.DropColumn(
                name: "AudienceRole",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "TargetKind",
                table: "AcademyNotices");
        }
    }
}
