using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlayerTechnicalReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerTechnicalReviewRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    LevelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurriculumTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    SourceReviewSequence = table.Column<int>(type: "integer", nullable: false),
                    EvidenceReviewIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    PreviousRequestId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_PlayerTechnicalReviewRequests", x => x.Id);
                    table.UniqueConstraint("AK_PlayerTechnicalReviewRequests_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerTechnicalReviewRequests_AcademyId_Id_PlanId", x => new { x.AcademyId, x.Id, x.PlanId });
                    table.UniqueConstraint("AK_PlayerTechnicalReviewRequests_AcademyId_Id_SportEnrollmentI~", x => new { x.AcademyId, x.Id, x.SportEnrollmentId, x.SportId });
                    table.CheckConstraint("CK_PlayerTechnicalReviewRequest_Source", "\"IsActive\" AND \"Sequence\">0 AND \"SourceReviewSequence\">0 AND cardinality(\"EvidenceReviewIds\") BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewRequests_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewRequests_PlayerSkillPlans_AcademyId_Pl~",
                        columns: x => new { x.AcademyId, x.PlanId, x.CurriculumId, x.SportId },
                        principalTable: "PlayerSkillPlans",
                        principalColumns: new[] { "AcademyId", "Id", "CurriculumId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewRequests_PlayerTechnicalReviewRequests~",
                        columns: x => new { x.AcademyId, x.PreviousRequestId, x.PlanId },
                        principalTable: "PlayerTechnicalReviewRequests",
                        principalColumns: new[] { "AcademyId", "Id", "PlanId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewRequests_SkillCurricula_AcademyId_Curr~",
                        columns: x => new { x.AcademyId, x.CurriculumId, x.TrainingLevelId, x.SportId },
                        principalTable: "SkillCurricula",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingLevelId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewRequests_SportEnrollments_AcademyId_Sp~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewRequests_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTechnicalReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RecommendedLevelId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecommendedLevelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FamilyNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PlayerTechnicalReviewDecisions", x => x.Id);
                    table.UniqueConstraint("AK_PlayerTechnicalReviewDecisions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerTechnicalReviewDecision_Outcome", "\"IsActive\" AND \"Outcome\" IN ('ReadyForPlacement','NeedsPractice','ContextChanged') AND ((\"Outcome\"='ReadyForPlacement' AND \"RecommendedLevelId\" IS NOT NULL AND \"RecommendedLevelName\" IS NOT NULL) OR (\"Outcome\"<>'ReadyForPlacement' AND \"RecommendedLevelId\" IS NULL AND \"RecommendedLevelName\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewDecisions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewDecisions_PlayerTechnicalReviewRequest~",
                        columns: x => new { x.AcademyId, x.TechnicalRequestId, x.SportEnrollmentId, x.SportId },
                        principalTable: "PlayerTechnicalReviewRequests",
                        principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewDecisions_TrainingLevels_AcademyId_Rec~",
                        columns: x => new { x.AcademyId, x.RecommendedLevelId, x.SportId },
                        principalTable: "TrainingLevels",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTechnicalReviewDecisions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewDecisions_AcademyId_ActorUserId_Reques~",
                table: "PlayerTechnicalReviewDecisions",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewDecisions_AcademyId_RecommendedLevelId~",
                table: "PlayerTechnicalReviewDecisions",
                columns: new[] { "AcademyId", "RecommendedLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewDecisions_AcademyId_TechnicalRequestId",
                table: "PlayerTechnicalReviewDecisions",
                columns: new[] { "AcademyId", "TechnicalRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewDecisions_AcademyId_TechnicalRequestId~",
                table: "PlayerTechnicalReviewDecisions",
                columns: new[] { "AcademyId", "TechnicalRequestId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewDecisions_ActorUserId",
                table: "PlayerTechnicalReviewDecisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_AcademyId_ActorUserId_Request~",
                table: "PlayerTechnicalReviewRequests",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_AcademyId_CurriculumId_Traini~",
                table: "PlayerTechnicalReviewRequests",
                columns: new[] { "AcademyId", "CurriculumId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_AcademyId_PlanId_CurriculumId~",
                table: "PlayerTechnicalReviewRequests",
                columns: new[] { "AcademyId", "PlanId", "CurriculumId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_AcademyId_PlanId_Sequence",
                table: "PlayerTechnicalReviewRequests",
                columns: new[] { "AcademyId", "PlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_AcademyId_PreviousRequestId_P~",
                table: "PlayerTechnicalReviewRequests",
                columns: new[] { "AcademyId", "PreviousRequestId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_AcademyId_SportEnrollmentId_S~",
                table: "PlayerTechnicalReviewRequests",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTechnicalReviewRequests_ActorUserId",
                table: "PlayerTechnicalReviewRequests",
                column: "ActorUserId");
            AddTechnicalGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PlayerTechnicalReviewRequests") OR EXISTS(SELECT 1 FROM "PlayerTechnicalReviewDecisions") THEN RAISE EXCEPTION 'Refuse loss of technical review history'; END IF; END $$;
                DROP FUNCTION academy_technical_review_guard() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "PlayerTechnicalReviewDecisions");

            migrationBuilder.DropTable(
                name: "PlayerTechnicalReviewRequests");
        }
    }
}
