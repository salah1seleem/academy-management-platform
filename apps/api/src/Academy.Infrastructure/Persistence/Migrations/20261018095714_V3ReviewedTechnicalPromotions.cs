using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ReviewedTechnicalPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupTransferId",
                table: "PlayerLevelReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TechnicalDecisionId",
                table: "PlayerLevelReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PlayerTechnicalReviewDecisions_AcademyId_Id_SportEnrollment~",
                table: "PlayerTechnicalReviewDecisions",
                columns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EnrollmentGroupTransfers_AcademyId_Id_SportEnrollmentId_Spo~",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_GroupTransferId_SportEnrollmen~",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "GroupTransferId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_TechnicalDecisionId",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "TechnicalDecisionId" },
                unique: true,
                filter: "\"TechnicalDecisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_TechnicalDecisionId_SportEnrol~",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "TechnicalDecisionId", "SportEnrollmentId", "SportId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LevelReview_Promotion",
                table: "PlayerLevelReviews",
                sql: "(\"TechnicalDecisionId\" IS NULL AND \"GroupTransferId\" IS NULL) OR (\"TechnicalDecisionId\" IS NOT NULL AND \"GroupTransferId\" IS NOT NULL AND \"Action\"='Changed')");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerLevelReviews_EnrollmentGroupTransfers_AcademyId_Group~",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "GroupTransferId", "SportEnrollmentId", "SportId" },
                principalTable: "EnrollmentGroupTransfers",
                principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerLevelReviews_PlayerTechnicalReviewDecisions_AcademyId~",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "TechnicalDecisionId", "SportEnrollmentId", "SportId" },
                principalTable: "PlayerTechnicalReviewDecisions",
                principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" },
                onDelete: ReferentialAction.Restrict);
            AddPromotionGuard(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PlayerLevelReviews" WHERE "TechnicalDecisionId" IS NOT NULL) THEN RAISE EXCEPTION 'Preserve applied technical decisions before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_technical_promotion_source() CASCADE;""");
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerLevelReviews_EnrollmentGroupTransfers_AcademyId_Group~",
                table: "PlayerLevelReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerLevelReviews_PlayerTechnicalReviewDecisions_AcademyId~",
                table: "PlayerLevelReviews");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PlayerTechnicalReviewDecisions_AcademyId_Id_SportEnrollment~",
                table: "PlayerTechnicalReviewDecisions");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLevelReviews_AcademyId_GroupTransferId_SportEnrollmen~",
                table: "PlayerLevelReviews");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLevelReviews_AcademyId_TechnicalDecisionId",
                table: "PlayerLevelReviews");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLevelReviews_AcademyId_TechnicalDecisionId_SportEnrol~",
                table: "PlayerLevelReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LevelReview_Promotion",
                table: "PlayerLevelReviews");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EnrollmentGroupTransfers_AcademyId_Id_SportEnrollmentId_Spo~",
                table: "EnrollmentGroupTransfers");

            migrationBuilder.DropColumn(
                name: "GroupTransferId",
                table: "PlayerLevelReviews");

            migrationBuilder.DropColumn(
                name: "TechnicalDecisionId",
                table: "PlayerLevelReviews");
        }
    }
}
