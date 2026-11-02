using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3FeedbackIncidentSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IncidentSessionEvidence",
                table: "GuardianFeedbacks",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IncidentTrainingSessionId",
                table: "GuardianFeedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_IncidentTrainingSessionId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "IncidentTrainingSessionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Feedback_IncidentSession",
                table: "GuardianFeedbacks",
                sql: "(\"IncidentTrainingSessionId\" IS NULL) = (\"IncidentSessionEvidence\" IS NULL) AND (\"IncidentTrainingSessionId\" IS NULL OR (\"IncidentOn\" IS NOT NULL AND \"Kind\" <> 'Rating'))");

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianFeedbacks_TrainingSessions_AcademyId_IncidentTraini~",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "IncidentTrainingSessionId" },
                principalTable: "TrainingSessions",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_feedback_intake_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW."ReportedSubject" IS DISTINCT FROM OLD."ReportedSubject" OR NEW."ReportedUrgency" IS DISTINCT FROM OLD."ReportedUrgency" OR NEW."IncidentOn" IS DISTINCT FROM OLD."IncidentOn"
                    OR NEW."IncidentTrainingSessionId" IS DISTINCT FROM OLD."IncidentTrainingSessionId" OR NEW."IncidentSessionEvidence" IS DISTINCT FROM OLD."IncidentSessionEvidence"
                    THEN RAISE EXCEPTION 'Preserve original family intake and occurrence evidence' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE FUNCTION academy_feedback_incident_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW."IncidentTrainingSessionId" IS NOT NULL AND (NOT EXISTS(SELECT 1 FROM "TrainingSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."IncidentTrainingSessionId" AND s."TrainingGroupId"=NEW."TrainingGroupId" AND s."BranchId"=NEW."BranchId" AND s."SessionDate"=NEW."IncidentOn")
                    OR (NEW."IncidentSessionEvidence"::jsonb->>'sessionId') IS DISTINCT FROM NEW."IncidentTrainingSessionId"::text
                    OR (NEW."IncidentSessionEvidence"::jsonb->>'date') IS DISTINCT FROM NEW."IncidentOn"::text
                    OR (NEW."IncidentSessionEvidence"::jsonb->>'selectedCoachId') IS DISTINCT FROM NEW."CoachMembershipId"::text)
                    THEN RAISE EXCEPTION 'Incident context must match its academy occurrence and intake' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER feedback_incident_source BEFORE INSERT ON "GuardianFeedbacks" FOR EACH ROW EXECUTE FUNCTION academy_feedback_incident_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Preserve family occurrence evidence with a reviewed forward migration' USING ERRCODE='23514'; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_GuardianFeedbacks_TrainingSessions_AcademyId_IncidentTraini~",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_GuardianFeedbacks_AcademyId_IncidentTrainingSessionId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Feedback_IncidentSession",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "IncidentSessionEvidence",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "IncidentTrainingSessionId",
                table: "GuardianFeedbacks");
        }
    }
}
