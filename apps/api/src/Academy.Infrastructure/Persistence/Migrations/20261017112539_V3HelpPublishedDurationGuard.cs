using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3HelpPublishedDurationGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HelpVideo_Lifecycle",
                table: "HelpVideoRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HelpVideo_Lifecycle",
                table: "HelpVideoRevisions",
                sql: " \"Revision\" > 0 AND \"EditVersion\" > 0 AND \"State\" IN ('Draft','Published','Archived') AND (\"State\" <> 'Published' OR (\"VideoFile\" IS NOT NULL AND \"ThumbnailFile\" IS NOT NULL AND \"DurationSeconds\" IS NOT NULL AND \"DurationSeconds\" > 0 AND \"PublishedAtUtc\" IS NOT NULL)) ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HelpVideo_Lifecycle",
                table: "HelpVideoRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HelpVideo_Lifecycle",
                table: "HelpVideoRevisions",
                sql: "\"Revision\" > 0 AND \"EditVersion\" > 0 AND \"State\" IN ('Draft','Published','Archived') AND (\"State\" <> 'Published' OR (\"VideoFile\" IS NOT NULL AND \"ThumbnailFile\" IS NOT NULL AND \"DurationSeconds\" > 0 AND \"PublishedAtUtc\" IS NOT NULL))");
        }
    }
}
