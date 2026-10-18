using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3SessionLessonPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_TrainingSessions_AcademyId_Id_SportId",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "Id", "SportId" });

            migrationBuilder.CreateTable(
                name: "SessionLessonTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    CopiedFromId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeclaredMinutes = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionLessonTemplates", x => x.Id);
                    table.UniqueConstraint("AK_SessionLessonTemplates_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_SessionLessonTemplates_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.UniqueConstraint("AK_SessionLessonTemplates_AcademyId_Id_TrainingLevelId_SportId", x => new { x.AcademyId, x.Id, x.TrainingLevelId, x.SportId });
                    table.CheckConstraint("CK_SessionLessonTemplate_Review", "\"IsActive\" AND \"DeclaredMinutes\" BETWEEN 1 AND 180 AND \"State\" IN ('Draft','Reviewed') AND (\"State\"<>'Reviewed' OR \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SessionLessonTemplates_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonTemplates_SessionLessonTemplates_AcademyId_Cop~",
                        columns: x => new { x.AcademyId, x.CopiedFromId, x.TrainingLevelId, x.SportId },
                        principalTable: "SessionLessonTemplates",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonTemplates_TrainingLevels_AcademyId_TrainingLev~",
                        columns: x => new { x.AcademyId, x.TrainingLevelId, x.SportId },
                        principalTable: "TrainingLevels",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonTemplates_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonTemplates_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    CopiedFromId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Skill = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Equipment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    RightsKind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    RightsReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    State = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingExercises", x => x.Id);
                    table.UniqueConstraint("AK_TrainingExercises_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_TrainingExercises_AcademyId_Id_TrainingLevelId_SportId", x => new { x.AcademyId, x.Id, x.TrainingLevelId, x.SportId });
                    table.CheckConstraint("CK_TrainingExercise_Review", "\"IsActive\" AND \"Minutes\" BETWEEN 1 AND 180 AND \"RightsKind\" IN ('Original','Licensed') AND length(trim(\"RightsReference\"))>0 AND \"State\" IN ('Draft','Reviewed') AND (\"State\"<>'Reviewed' OR \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TrainingExercises_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingExercises_TrainingExercises_AcademyId_CopiedFromId_~",
                        columns: x => new { x.AcademyId, x.CopiedFromId, x.TrainingLevelId, x.SportId },
                        principalTable: "TrainingExercises",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingExercises_TrainingLevels_AcademyId_TrainingLevelId_~",
                        columns: x => new { x.AcademyId, x.TrainingLevelId, x.SportId },
                        principalTable: "TrainingLevels",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingExercises_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingExercises_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionLessonAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
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
                    table.PrimaryKey("PK_SessionLessonAssignments", x => x.Id);
                    table.UniqueConstraint("AK_SessionLessonAssignments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SessionLessonAssignment_Revision", "\"IsActive\" AND \"Revision\">0");
                    table.ForeignKey(
                        name: "FK_SessionLessonAssignments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonAssignments_SessionLessonTemplates_AcademyId_T~",
                        columns: x => new { x.AcademyId, x.TemplateId, x.SportId },
                        principalTable: "SessionLessonTemplates",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonAssignments_TrainingSessions_AcademyId_Trainin~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId, x.SportId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonAssignments_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionLessonSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionLessonSteps", x => x.Id);
                    table.UniqueConstraint("AK_SessionLessonSteps_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SessionLessonStep_Range", "\"IsActive\" AND \"Minutes\" BETWEEN 1 AND 180 AND \"Position\" BETWEEN 0 AND 19");
                    table.ForeignKey(
                        name: "FK_SessionLessonSteps_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonSteps_SessionLessonTemplates_AcademyId_Templat~",
                        columns: x => new { x.AcademyId, x.TemplateId, x.TrainingLevelId, x.SportId },
                        principalTable: "SessionLessonTemplates",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionLessonSteps_TrainingExercises_AcademyId_ExerciseId_T~",
                        columns: x => new { x.AcademyId, x.ExerciseId, x.TrainingLevelId, x.SportId },
                        principalTable: "TrainingExercises",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonAssignments_AcademyId_ActorUserId_RequestId",
                table: "SessionLessonAssignments",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonAssignments_AcademyId_TemplateId_SportId",
                table: "SessionLessonAssignments",
                columns: new[] { "AcademyId", "TemplateId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonAssignments_AcademyId_TrainingSessionId_Revisi~",
                table: "SessionLessonAssignments",
                columns: new[] { "AcademyId", "TrainingSessionId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonAssignments_AcademyId_TrainingSessionId_SportId",
                table: "SessionLessonAssignments",
                columns: new[] { "AcademyId", "TrainingSessionId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonAssignments_ActorUserId",
                table: "SessionLessonAssignments",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonSteps_AcademyId_ExerciseId_TrainingLevelId_Spo~",
                table: "SessionLessonSteps",
                columns: new[] { "AcademyId", "ExerciseId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonSteps_AcademyId_TemplateId_Position",
                table: "SessionLessonSteps",
                columns: new[] { "AcademyId", "TemplateId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonSteps_AcademyId_TemplateId_TrainingLevelId_Spo~",
                table: "SessionLessonSteps",
                columns: new[] { "AcademyId", "TemplateId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonTemplates_AcademyId_CopiedFromId_TrainingLevel~",
                table: "SessionLessonTemplates",
                columns: new[] { "AcademyId", "CopiedFromId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonTemplates_AcademyId_CreatedByUserId_RequestId",
                table: "SessionLessonTemplates",
                columns: new[] { "AcademyId", "CreatedByUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonTemplates_AcademyId_TrainingLevelId_SportId",
                table: "SessionLessonTemplates",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonTemplates_CreatedByUserId",
                table: "SessionLessonTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionLessonTemplates_ReviewedByUserId",
                table: "SessionLessonTemplates",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingExercises_AcademyId_CopiedFromId_TrainingLevelId_Sp~",
                table: "TrainingExercises",
                columns: new[] { "AcademyId", "CopiedFromId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingExercises_AcademyId_CreatedByUserId_RequestId",
                table: "TrainingExercises",
                columns: new[] { "AcademyId", "CreatedByUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingExercises_AcademyId_TrainingLevelId_SportId",
                table: "TrainingExercises",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingExercises_CreatedByUserId",
                table: "TrainingExercises",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingExercises_ReviewedByUserId",
                table: "TrainingExercises",
                column: "ReviewedByUserId");
            AddLessonGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "TrainingExercises") OR EXISTS(SELECT 1 FROM "SessionLessonTemplates") OR EXISTS(SELECT 1 FROM "SessionLessonAssignments") THEN RAISE EXCEPTION 'Refuse loss of lesson history'; END IF; END $$;
                DROP FUNCTION academy_lesson_guard() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "SessionLessonAssignments");

            migrationBuilder.DropTable(
                name: "SessionLessonSteps");

            migrationBuilder.DropTable(
                name: "SessionLessonTemplates");

            migrationBuilder.DropTable(
                name: "TrainingExercises");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TrainingSessions_AcademyId_Id_SportId",
                table: "TrainingSessions");
        }
    }
}
