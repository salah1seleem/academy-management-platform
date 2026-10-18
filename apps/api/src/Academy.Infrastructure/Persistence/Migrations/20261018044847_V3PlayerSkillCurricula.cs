using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlayerSkillCurricula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SkillCurricula",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    EditRevision = table.Column<int>(type: "integer", nullable: false),
                    CopiedFromId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LevelNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetiredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillCurricula", x => x.Id);
                    table.UniqueConstraint("AK_SkillCurricula_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_SkillCurricula_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.UniqueConstraint("AK_SkillCurricula_AcademyId_Id_TrainingLevelId_SportId", x => new { x.AcademyId, x.Id, x.TrainingLevelId, x.SportId });
                    table.CheckConstraint("CK_SkillCurriculum_EditRevision", "\"EditRevision\">0");
                    table.CheckConstraint("CK_SkillCurriculum_State", "\"IsActive\" AND \"RevisionNumber\">0 AND length(trim(\"Title\"))>0 AND \"State\" IN ('Draft','Published','Retired') AND (\"State\"<>'Published' OR \"PublishedByUserId\" IS NOT NULL AND \"PublishedAtUtc\" IS NOT NULL AND \"PublishRequestId\" IS NOT NULL) AND (\"State\"<>'Retired' OR \"RetiredByUserId\" IS NOT NULL AND \"RetiredAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SkillCurricula_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillCurricula_SkillCurricula_AcademyId_CopiedFromId_Traini~",
                        columns: x => new { x.AcademyId, x.CopiedFromId, x.TrainingLevelId, x.SportId },
                        principalTable: "SkillCurricula",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" });
                    table.ForeignKey(
                        name: "FK_SkillCurricula_TrainingLevels_AcademyId_TrainingLevelId_Spo~",
                        columns: x => new { x.AcademyId, x.TrainingLevelId, x.SportId },
                        principalTable: "TrainingLevels",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillCurricula_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillCurricula_Users_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillCurricula_Users_RetiredByUserId",
                        column: x => x.RetiredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSkillPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_PlayerSkillPlans", x => x.Id);
                    table.UniqueConstraint("AK_PlayerSkillPlans_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerSkillPlans_AcademyId_Id_CurriculumId_SportId", x => new { x.AcademyId, x.Id, x.CurriculumId, x.SportId });
                    table.CheckConstraint("CK_PlayerSkillPlan_State", "(\"IsActive\" AND \"EndedAtUtc\" IS NULL) OR (NOT \"IsActive\" AND \"EndedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PlayerSkillPlans_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSkillPlans_SkillCurricula_AcademyId_CurriculumId_Spor~",
                        columns: x => new { x.AcademyId, x.CurriculumId, x.SportId },
                        principalTable: "SkillCurricula",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSkillPlans_SportEnrollments_AcademyId_SportEnrollment~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSkillPlans_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkillRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillRequirements", x => x.Id);
                    table.UniqueConstraint("AK_SkillRequirements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_SkillRequirements_AcademyId_Id_CurriculumId_SportId", x => new { x.AcademyId, x.Id, x.CurriculumId, x.SportId });
                    table.CheckConstraint("CK_SkillRequirement_Protocol", "\"IsActive\" AND length(trim(\"Name\"))>0 AND length(trim(\"Protocol\"))>0 AND \"Unit\" IN ('Checklist','Count','Seconds','Metres') AND \"DisplayOrder\" BETWEEN 0 AND 19");
                    table.ForeignKey(
                        name: "FK_SkillRequirements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillRequirements_SkillCurricula_AcademyId_CurriculumId_Spo~",
                        columns: x => new { x.AcademyId, x.CurriculumId, x.SportId },
                        principalTable: "SkillCurricula",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSkillReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MeasuredValue = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
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
                    table.PrimaryKey("PK_PlayerSkillReviews", x => x.Id);
                    table.UniqueConstraint("AK_PlayerSkillReviews_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerSkillReview_Value", "\"IsActive\" AND \"Sequence\">0 AND \"State\" IN ('NotAssessed','Learning','Mastered') AND (\"MeasuredValue\" IS NULL OR \"MeasuredValue\" BETWEEN 0 AND 1000000) AND (\"State\"<>'NotAssessed' OR \"MeasuredValue\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PlayerSkillReviews_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSkillReviews_PlayerSkillPlans_AcademyId_PlanId_Curric~",
                        columns: x => new { x.AcademyId, x.PlanId, x.CurriculumId, x.SportId },
                        principalTable: "PlayerSkillPlans",
                        principalColumns: new[] { "AcademyId", "Id", "CurriculumId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSkillReviews_SkillRequirements_AcademyId_RequirementI~",
                        columns: x => new { x.AcademyId, x.RequirementId, x.CurriculumId, x.SportId },
                        principalTable: "SkillRequirements",
                        principalColumns: new[] { "AcademyId", "Id", "CurriculumId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSkillReviews_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillPlans_AcademyId_AssignedByUserId_RequestId",
                table: "PlayerSkillPlans",
                columns: new[] { "AcademyId", "AssignedByUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillPlans_AcademyId_CurriculumId_SportId",
                table: "PlayerSkillPlans",
                columns: new[] { "AcademyId", "CurriculumId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillPlans_AcademyId_SportEnrollmentId",
                table: "PlayerSkillPlans",
                columns: new[] { "AcademyId", "SportEnrollmentId" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillPlans_AcademyId_SportEnrollmentId_SportId",
                table: "PlayerSkillPlans",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillPlans_AssignedByUserId",
                table: "PlayerSkillPlans",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillReviews_AcademyId_ActorUserId_RequestId",
                table: "PlayerSkillReviews",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillReviews_AcademyId_PlanId_CurriculumId_SportId",
                table: "PlayerSkillReviews",
                columns: new[] { "AcademyId", "PlanId", "CurriculumId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillReviews_AcademyId_PlanId_RequirementId_ObservedO~",
                table: "PlayerSkillReviews",
                columns: new[] { "AcademyId", "PlanId", "RequirementId", "ObservedOn", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillReviews_AcademyId_PlanId_Sequence",
                table: "PlayerSkillReviews",
                columns: new[] { "AcademyId", "PlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillReviews_AcademyId_RequirementId_CurriculumId_Spo~",
                table: "PlayerSkillReviews",
                columns: new[] { "AcademyId", "RequirementId", "CurriculumId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSkillReviews_ActorUserId",
                table: "PlayerSkillReviews",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_AcademyId_CopiedFromId_TrainingLevelId_Sport~",
                table: "SkillCurricula",
                columns: new[] { "AcademyId", "CopiedFromId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_AcademyId_CreatedByUserId_CreationRequestId",
                table: "SkillCurricula",
                columns: new[] { "AcademyId", "CreatedByUserId", "CreationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_AcademyId_TrainingLevelId_RevisionNumber",
                table: "SkillCurricula",
                columns: new[] { "AcademyId", "TrainingLevelId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_AcademyId_TrainingLevelId_SportId",
                table: "SkillCurricula",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_CreatedByUserId",
                table: "SkillCurricula",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_PublishedByUserId",
                table: "SkillCurricula",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillCurricula_RetiredByUserId",
                table: "SkillCurricula",
                column: "RetiredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillRequirements_AcademyId_CurriculumId_DisplayOrder",
                table: "SkillRequirements",
                columns: new[] { "AcademyId", "CurriculumId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillRequirements_AcademyId_CurriculumId_SportId",
                table: "SkillRequirements",
                columns: new[] { "AcademyId", "CurriculumId", "SportId" });
            AddJourneyGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "SkillCurricula") OR EXISTS(SELECT 1 FROM "PlayerSkillPlans") THEN RAISE EXCEPTION 'Preserve skill journeys before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_skill_guard() CASCADE; DROP FUNCTION IF EXISTS academy_skill_publication_required() CASCADE;""");
            migrationBuilder.DropTable(
                name: "PlayerSkillReviews");

            migrationBuilder.DropTable(
                name: "PlayerSkillPlans");

            migrationBuilder.DropTable(
                name: "SkillRequirements");

            migrationBuilder.DropTable(
                name: "SkillCurricula");
        }
    }
}
