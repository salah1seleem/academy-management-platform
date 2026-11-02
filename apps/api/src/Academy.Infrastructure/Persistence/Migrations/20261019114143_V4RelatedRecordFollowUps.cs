using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4RelatedRecordFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps");

            migrationBuilder.AlterColumn<Guid>(
                name: "TrainingGroupId",
                table: "OwnerFollowUps",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "ManualPriority",
                table: "OwnerFollowUps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceBranchId",
                table: "OwnerFollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceBranchSnapshot",
                table: "OwnerFollowUps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "OwnerFollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "OwnerFollowUps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.AddColumn<string>(
                name: "SourceNameSnapshot",
                table: "OwnerFollowUps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSportId",
                table: "OwnerFollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSportSnapshot",
                table: "OwnerFollowUps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUps_AcademyId_SourceBranchId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "SourceBranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUps_AcademyId_SourceKind_SourceId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "SourceKind", "SourceId" },
                unique: true,
                filter: "\"SourceKind\" <> 'Group' AND \"State\" NOT IN ('Closed','Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUps_AcademyId_SourceSportId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "SourceSportId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_Source",
                table: "OwnerFollowUps",
                sql: "(\"SourceKind\"='Group' AND \"TrainingGroupId\" IS NOT NULL AND \"SourceId\" IS NULL AND \"ManualPriority\" IS NULL) OR (\"SourceKind\" IN ('Player','Employee','Guardian','Request','Receivable') AND \"TrainingGroupId\" IS NULL AND \"SourceId\" IS NOT NULL AND \"Condition\"='RecordFollowUp' AND length(trim(\"SourceNameSnapshot\"))>0 AND \"SourceNameSnapshot\" IS NOT NULL AND \"ManualPriority\" IS NOT NULL AND \"ManualPriority\" IN (1,2))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps",
                sql: "\"IsActive\" AND \"Condition\" IN ('RecordFollowUp','GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences','GroupOverdueFeedback') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_OwnerFollowUps_Branches_AcademyId_SourceBranchId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "SourceBranchId" },
                principalTable: "Branches",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OwnerFollowUps_Sports_AcademyId_SourceSportId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "SourceSportId" },
                principalTable: "Sports",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(V4RelatedRecordFollowUpGuards.Source);
            migrationBuilder.Sql(V4RelatedRecordFollowUpGuards.History);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"OwnerFollowUps\" WHERE \"SourceKind\"<>'Group') THEN RAISE EXCEPTION 'Preserve related-record follow-ups before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION academy_followup_record_source_guard() CASCADE;");
            migrationBuilder.Sql(V4RelatedRecordFollowUpGuards.LegacyHistory);
            migrationBuilder.DropForeignKey(
                name: "FK_OwnerFollowUps_Branches_AcademyId_SourceBranchId",
                table: "OwnerFollowUps");

            migrationBuilder.DropForeignKey(
                name: "FK_OwnerFollowUps_Sports_AcademyId_SourceSportId",
                table: "OwnerFollowUps");

            migrationBuilder.DropIndex(
                name: "IX_OwnerFollowUps_AcademyId_SourceBranchId",
                table: "OwnerFollowUps");

            migrationBuilder.DropIndex(
                name: "IX_OwnerFollowUps_AcademyId_SourceKind_SourceId",
                table: "OwnerFollowUps");

            migrationBuilder.DropIndex(
                name: "IX_OwnerFollowUps_AcademyId_SourceSportId",
                table: "OwnerFollowUps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_Source",
                table: "OwnerFollowUps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "ManualPriority",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceBranchId",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceBranchSnapshot",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceNameSnapshot",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceSportId",
                table: "OwnerFollowUps");

            migrationBuilder.DropColumn(
                name: "SourceSportSnapshot",
                table: "OwnerFollowUps");

            migrationBuilder.AlterColumn<Guid>(
                name: "TrainingGroupId",
                table: "OwnerFollowUps",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps",
                sql: "\"IsActive\" AND \"Condition\" IN ('GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences','GroupOverdueFeedback') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");
        }
    }
}
