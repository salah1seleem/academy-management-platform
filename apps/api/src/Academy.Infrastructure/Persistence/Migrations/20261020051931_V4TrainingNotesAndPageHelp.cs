using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4TrainingNotesAndPageHelp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrainingSafetyNote",
                table: "SportEnrollments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PageHelpVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Screen = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageHelpVideos", x => x.Id);
                    table.UniqueConstraint("AK_PageHelpVideos_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PageHelpVideos_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageHelpVideos_AcademyId_Role_Screen",
                table: "PageHelpVideos",
                columns: new[] { "AcademyId", "Role", "Screen" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"PageHelpVideos\") OR EXISTS (SELECT 1 FROM \"SportEnrollments\" WHERE \"TrainingSafetyNote\" IS NOT NULL) THEN RAISE EXCEPTION 'Saved page help and training notes require a reviewed forward migration'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "PageHelpVideos");

            migrationBuilder.DropColumn(
                name: "TrainingSafetyNote",
                table: "SportEnrollments");
        }
    }
}
