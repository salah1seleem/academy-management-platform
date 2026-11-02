using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3TrainingContentArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrainingContentArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrainingLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingContentArchives", x => x.Id);
                    table.UniqueConstraint("AK_TrainingContentArchives_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_TrainingContentArchive_Source", "\"IsActive\" AND ((\"ExerciseId\" IS NOT NULL AND \"TemplateId\" IS NULL) OR (\"ExerciseId\" IS NULL AND \"TemplateId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_TrainingContentArchives_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingContentArchives_SessionLessonTemplates_AcademyId_Te~",
                        columns: x => new { x.AcademyId, x.TemplateId, x.TrainingLevelId, x.SportId },
                        principalTable: "SessionLessonTemplates",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingContentArchives_TrainingExercises_AcademyId_Exercis~",
                        columns: x => new { x.AcademyId, x.ExerciseId, x.TrainingLevelId, x.SportId },
                        principalTable: "TrainingExercises",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingContentArchives_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContentArchives_AcademyId_ActorUserId_RequestId",
                table: "TrainingContentArchives",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContentArchives_AcademyId_ExerciseId",
                table: "TrainingContentArchives",
                columns: new[] { "AcademyId", "ExerciseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContentArchives_AcademyId_ExerciseId_TrainingLevelI~",
                table: "TrainingContentArchives",
                columns: new[] { "AcademyId", "ExerciseId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContentArchives_AcademyId_TemplateId",
                table: "TrainingContentArchives",
                columns: new[] { "AcademyId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContentArchives_AcademyId_TemplateId_TrainingLevelI~",
                table: "TrainingContentArchives",
                columns: new[] { "AcademyId", "TemplateId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContentArchives_ActorUserId",
                table: "TrainingContentArchives",
                column: "ActorUserId");
            AddArchiveGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "TrainingContentArchives") THEN RAISE EXCEPTION 'Preserve archive history before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_lesson_archive_use_guard() CASCADE; DROP FUNCTION IF EXISTS academy_lesson_archive_guard() CASCADE;""");
            migrationBuilder.DropTable(
                name: "TrainingContentArchives");
        }
    }
}
