using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3FeedbackHumanTriage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive nullable fields preserve legacy intake as unknown, not invented routine/safe data.
            migrationBuilder.AddColumn<DateOnly>(
                name: "IncidentOn",
                table: "GuardianFeedbacks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedSubject",
                table: "GuardianFeedbacks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedUrgency",
                table: "GuardianFeedbacks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "GuardianFeedbacks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TriagedAtUtc",
                table: "GuardianFeedbacks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TriagedByMembershipId",
                table: "GuardianFeedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Urgency",
                table: "GuardianFeedbacks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterSubject",
                table: "FeedbackChanges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterUrgency",
                table: "FeedbackChanges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeSubject",
                table: "FeedbackChanges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeUrgency",
                table: "FeedbackChanges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_TriagedByMembershipId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "TriagedByMembershipId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Feedback_Triage",
                table: "GuardianFeedbacks",
                sql: "(\"Subject\" IS NULL) = (\"Urgency\" IS NULL) AND (\"ReportedSubject\" IS NULL) = (\"ReportedUrgency\" IS NULL) AND (\"Subject\" IS NULL OR \"Subject\" IN ('Coach','Schedule','Administration','Facility','Water','Payment','Other')) AND (\"ReportedSubject\" IS NULL OR \"ReportedSubject\" IN ('Coach','Schedule','Administration','Facility','Water','Payment','Other')) AND (\"Urgency\" IS NULL OR \"Urgency\" IN ('Routine','Urgent','Safety')) AND (\"ReportedUrgency\" IS NULL OR \"ReportedUrgency\" IN ('Routine','Urgent','Safety')) AND (\"TriagedAtUtc\" IS NULL) = (\"TriagedByMembershipId\" IS NULL) AND (\"TriagedAtUtc\" IS NULL OR \"Subject\" IS NOT NULL) AND (\"Kind\" <> 'Rating' OR (\"Subject\" IS NULL AND \"ReportedSubject\" IS NULL AND \"IncidentOn\" IS NULL AND \"TriagedAtUtc\" IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianFeedbacks_AcademyMemberships_AcademyId_TriagedByMem~",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "TriagedByMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                DO $$ DECLARE definition text; marker text := 'IF NEW."Action" IN (''Assign'',''Findings'') THEN'; extra text; BEGIN
                  SELECT pg_get_functiondef('academy_feedback_change_guard()'::regprocedure) INTO definition;
                  IF position(marker in definition)=0 THEN RAISE EXCEPTION 'Expected private case guard before triage migration'; END IF;
                  extra := $triage$
                    IF NEW."Action"='Triage' THEN
                      IF NOT f."IsActive" OR f."Kind"='Rating' OR f."Status" IN ('Resolved','Closed') OR NEW."FromStatus"<>NEW."ToStatus" OR NEW."BeforeDueOn" IS DISTINCT FROM NEW."AfterDueOn"
                        OR NEW."PublicResponse" IS NOT NULL OR NEW."InvestigationId" IS NOT NULL OR NEW."InvestigatorMembershipId" IS NOT NULL
                        OR NEW."RequestId" IS NULL OR NEW."RequestId"='00000000-0000-0000-0000-000000000000' OR NEW."RequestHash" IS NULL OR length(NEW."RequestHash")<>64
                        OR NEW."AfterSubject" IS NULL OR NEW."AfterUrgency" IS NULL OR NEW."AfterSubject" IS DISTINCT FROM f."Subject" OR NEW."AfterUrgency" IS DISTINCT FROM f."Urgency"
                        OR f."TriagedByMembershipId" IS DISTINCT FROM NEW."ActorMembershipId" OR f."TriagedAtUtc" IS DISTINCT FROM NEW."CreatedAtUtc"
                        OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=f."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive"
                          AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p JOIN "Users" pu ON pu."Id"=p."UserId" WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive" AND pu."IsActive")))
                        THEN RAISE EXCEPTION 'Invalid human triage evidence' USING ERRCODE='23514'; END IF;
                      RETURN NEW;
                    END IF;
                    IF NEW."BeforeSubject" IS NOT NULL OR NEW."AfterSubject" IS NOT NULL OR NEW."BeforeUrgency" IS NOT NULL OR NEW."AfterUrgency" IS NOT NULL THEN RAISE EXCEPTION 'Triage evidence has its own explicit action' USING ERRCODE='23514'; END IF;
                  $triage$;
                  EXECUTE replace(definition, marker, extra || marker);
                END $$;
                CREATE FUNCTION academy_feedback_intake_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW."ReportedSubject" IS DISTINCT FROM OLD."ReportedSubject" OR NEW."ReportedUrgency" IS DISTINCT FROM OLD."ReportedUrgency" OR NEW."IncidentOn" IS DISTINCT FROM OLD."IncidentOn" THEN RAISE EXCEPTION 'Preserve the original family classification and incident date' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER feedback_intake_immutable BEFORE UPDATE ON "GuardianFeedbacks" FOR EACH ROW EXECUTE FUNCTION academy_feedback_intake_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Preserve human triage evidence with a reviewed forward migration' USING ERRCODE='23514'; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_GuardianFeedbacks_AcademyMemberships_AcademyId_TriagedByMem~",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_GuardianFeedbacks_AcademyId_TriagedByMembershipId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Feedback_Triage",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "IncidentOn",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "ReportedSubject",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "ReportedUrgency",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "TriagedAtUtc",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "TriagedByMembershipId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "Urgency",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "AfterSubject",
                table: "FeedbackChanges");

            migrationBuilder.DropColumn(
                name: "AfterUrgency",
                table: "FeedbackChanges");

            migrationBuilder.DropColumn(
                name: "BeforeSubject",
                table: "FeedbackChanges");

            migrationBuilder.DropColumn(
                name: "BeforeUrgency",
                table: "FeedbackChanges");
        }
    }
}
