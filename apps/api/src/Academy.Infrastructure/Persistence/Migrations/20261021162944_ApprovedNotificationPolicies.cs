using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedNotificationPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimingDays",
                table: "NoticeTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "AcademyNotices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NoticeTemplate_TimingDays",
                table: "NoticeTemplates",
                sql: "\"TimingDays\" IS NULL OR (\"TimingDays\" >= 1 AND \"TimingDays\" <= 30)");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNotices_AcademyId_EventKey_CreatedAtUtc",
                table: "AcademyNotices",
                columns: new[] { "AcademyId", "EventKey", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"AcademyNotices\" WHERE \"EventKey\" IS NOT NULL) OR EXISTS (SELECT 1 FROM \"NoticeTemplates\" WHERE \"TimingDays\" IS NOT NULL OR NOT \"IsActive\") THEN RAISE EXCEPTION 'Saved notification policy and delivery evidence require a reviewed forward migration'; END IF; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_NoticeTemplate_TimingDays",
                table: "NoticeTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AcademyNotices_AcademyId_EventKey_CreatedAtUtc",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "TimingDays",
                table: "NoticeTemplates");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "AcademyNotices");
        }
    }
}
