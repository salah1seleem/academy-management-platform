using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlatformHelpVideoLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HelpVideoRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    EditVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false),
                    AlternativeText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    VideoFile = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ThumbnailFile = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpVideoRevisions", x => x.Id);
                    table.CheckConstraint("CK_HelpVideo_Lifecycle", "\"Revision\" > 0 AND \"EditVersion\" > 0 AND \"State\" IN ('Draft','Published','Archived') AND (\"State\" <> 'Published' OR (\"VideoFile\" IS NOT NULL AND \"ThumbnailFile\" IS NOT NULL AND \"DurationSeconds\" > 0 AND \"PublishedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_HelpVideoRevisions_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HelpVideoRevisions_CreatedBy",
                table: "HelpVideoRevisions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_HelpVideoRevisions_LessonId",
                table: "HelpVideoRevisions",
                column: "LessonId",
                unique: true,
                filter: "\"State\" = 'Published'");

            migrationBuilder.CreateIndex(
                name: "IX_HelpVideoRevisions_LessonId_Revision",
                table: "HelpVideoRevisions",
                columns: new[] { "LessonId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HelpVideoRevisions");
        }
    }
}
