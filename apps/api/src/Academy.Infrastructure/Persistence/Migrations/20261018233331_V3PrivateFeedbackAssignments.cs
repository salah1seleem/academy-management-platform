using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PrivateFeedbackAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvestigationBrief",
                table: "GuardianFeedbacks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvestigationId",
                table: "GuardianFeedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvestigatorMembershipId",
                table: "GuardianFeedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvestigationId",
                table: "FeedbackChanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvestigatorMembershipId",
                table: "FeedbackChanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_InvestigationId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "InvestigationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_InvestigatorMembershipId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "InvestigatorMembershipId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Feedback_Investigation",
                table: "GuardianFeedbacks",
                sql: "(\"InvestigationId\" IS NULL AND \"InvestigatorMembershipId\" IS NULL AND \"InvestigationBrief\" IS NULL) OR (\"Kind\" <> 'Rating' AND \"InvestigationId\" IS NOT NULL AND \"InvestigatorMembershipId\" IS NOT NULL AND \"InvestigationBrief\" IS NOT NULL AND length(btrim(\"InvestigationBrief\")) BETWEEN 5 AND 500)");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackChanges_AcademyId_InvestigatorMembershipId",
                table: "FeedbackChanges",
                columns: new[] { "AcademyId", "InvestigatorMembershipId" });

            migrationBuilder.AddForeignKey(
                name: "FK_FeedbackChanges_AcademyMemberships_AcademyId_InvestigatorMe~",
                table: "FeedbackChanges",
                columns: new[] { "AcademyId", "InvestigatorMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_feedback_change_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE f "GuardianFeedbacks"%ROWTYPE;
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve private case evidence' USING ERRCODE='23514'; END IF;
                  SELECT * INTO f FROM "GuardianFeedbacks" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."FeedbackId";
                  IF f."Id" IS NULL OR f."Status"<>NEW."ToStatus" OR f."DueOn" IS DISTINCT FROM NEW."AfterDueOn" THEN RAISE EXCEPTION 'Case evidence must match the saved source' USING ERRCODE='23514'; END IF;
                  IF NEW."Action" IN ('Assign','Findings') THEN
                    IF NOT f."IsActive" OR f."Kind"='Rating' OR f."Status" IN ('Resolved','Closed') OR NEW."FromStatus"<>NEW."ToStatus" OR NEW."BeforeDueOn" IS DISTINCT FROM NEW."AfterDueOn" OR NEW."PublicResponse" IS NOT NULL
                      OR NEW."RequestId" IS NULL OR NEW."RequestId"='00000000-0000-0000-0000-000000000000' OR NEW."RequestHash" IS NULL OR length(NEW."RequestHash")<>64
                      OR NEW."InvestigationId" IS DISTINCT FROM f."InvestigationId" OR NEW."InvestigatorMembershipId" IS DISTINCT FROM f."InvestigatorMembershipId"
                      THEN RAISE EXCEPTION 'Invalid private assignment evidence' USING ERRCODE='23514'; END IF;
                    IF NEW."Action"='Assign' THEN
                      IF NEW."InternalNote" IS DISTINCT FROM f."InvestigationBrief" OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p JOIN "Users" pu ON pu."Id"=p."UserId" WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive" AND pu."IsActive")))
                        THEN RAISE EXCEPTION 'Assignment requires an active owner and reviewed brief' USING ERRCODE='23514'; END IF;
                    ELSE
                      IF NEW."InvestigationId" IS NULL OR NEW."ActorMembershipId" IS DISTINCT FROM f."InvestigatorMembershipId" OR NEW."InternalNote" IS NULL OR length(btrim(NEW."InternalNote"))<5 OR NOT EXISTS(
                        SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" JOIN "TrainingGroups" g ON g."Id"=f."TrainingGroupId" AND g."AcademyId"=f."AcademyId"
                        JOIN "Branches" b ON b."Id"=g."BranchId" JOIN "Sports" s ON s."Id"=g."SportId"
                        WHERE m."AcademyId"=f."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyAdmin' AND m."IsActive" AND u."IsActive" AND m."UseActionGrants" AND m."UserId"<>f."GuardianUserId"
                          AND g."IsActive" AND b."IsActive" AND s."IsActive" AND g."BranchId"=f."BranchId"
                          AND NOT EXISTS(SELECT 1 FROM "AcademyMemberships" subject WHERE subject."Id"=f."CoachMembershipId" AND subject."UserId"=m."UserId")
                          AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p JOIN "Users" pu ON pu."Id"=p."UserId" WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive" AND pu."IsActive"))
                          AND (m."AllBranches" OR EXISTS(SELECT 1 FROM "MembershipBranchAccesses" a WHERE a."MembershipId"=m."Id" AND a."BranchId"=g."BranchId" AND a."IsActive"))
                          AND (m."AllSports" OR EXISTS(SELECT 1 FROM "MembershipSportAccesses" a WHERE a."MembershipId"=m."Id" AND a."SportId"=g."SportId" AND a."IsActive"))
                          AND EXISTS(SELECT 1 FROM "StaffActionGrants" a WHERE a."MembershipId"=m."Id" AND a."AcademyId"=m."AcademyId" AND a."Module"='followups' AND a."IsActive" AND a."Read" AND a."Edit"))
                        THEN RAISE EXCEPTION 'Findings require a live scoped independent delegate' USING ERRCODE='23514'; END IF;
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF NEW."InvestigationId" IS NOT NULL OR NEW."InvestigatorMembershipId" IS NOT NULL THEN RAISE EXCEPTION 'No implicit private assignment snapshot' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive"))) THEN RAISE EXCEPTION 'Private case actor must be an active owner' USING ERRCODE='23514'; END IF;
                  IF NEW."Action" NOT IN ('Review','Due','Resolve','Transition') OR
                    (NEW."Action"='Resolve' AND (f."Kind"='Rating' OR NEW."ToStatus"<>'Resolved' OR NEW."PublicResponse" IS DISTINCT FROM f."OwnerResponse")) OR
                    (NEW."Action"<>'Resolve' AND NEW."PublicResponse" IS NOT NULL AND NOT (NEW."Action"='Transition' AND NEW."ToStatus"='Reopened')) OR
                    (NEW."Action"='Transition' AND NEW."ToStatus"='Reopened' AND NEW."PublicResponse" IS DISTINCT FROM f."OwnerResponse") OR
                    (NEW."Action"='Review' AND (NEW."FromStatus"<>'Open' OR NEW."ToStatus"<>'Reviewed')) OR
                    (NEW."Action"='Due' AND (NEW."FromStatus"<>NEW."ToStatus" OR NEW."BeforeDueOn" IS NOT NULL OR NEW."AfterDueOn" IS NULL)) OR
                    (NEW."Action"='Transition' AND (f."Kind"='Rating' OR NEW."RequestId" IS NULL OR NEW."RequestId"='00000000-0000-0000-0000-000000000000' OR length(NEW."RequestHash")<>64 OR NOT (
                     (NEW."FromStatus" IN ('Open','Reviewed','Reopened','AwaitingInfo') AND NEW."ToStatus"='InProgress') OR
                     (NEW."FromStatus"='InProgress' AND NEW."ToStatus"='AwaitingInfo') OR
                     (NEW."FromStatus"='Resolved' AND NEW."ToStatus"='Closed') OR
                     (NEW."FromStatus" IN ('Resolved','Closed') AND NEW."ToStatus"='Reopened')))) THEN RAISE EXCEPTION 'Invalid case change evidence' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianFeedbacks_AcademyMemberships_AcademyId_Investigator~",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "InvestigatorMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting a private assignment can expose old delegated links or destroy its
            // evidence. A reviewed forward migration is required, not an automatic downgrade.
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Private assignment evidence requires a reviewed forward migration' USING ERRCODE='23514'; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_FeedbackChanges_AcademyMemberships_AcademyId_InvestigatorMe~",
                table: "FeedbackChanges");

            migrationBuilder.DropForeignKey(
                name: "FK_GuardianFeedbacks_AcademyMemberships_AcademyId_Investigator~",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_GuardianFeedbacks_AcademyId_InvestigationId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_GuardianFeedbacks_AcademyId_InvestigatorMembershipId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Feedback_Investigation",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_FeedbackChanges_AcademyId_InvestigatorMembershipId",
                table: "FeedbackChanges");

            migrationBuilder.DropColumn(
                name: "InvestigationBrief",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "InvestigationId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "InvestigatorMembershipId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "InvestigationId",
                table: "FeedbackChanges");

            migrationBuilder.DropColumn(
                name: "InvestigatorMembershipId",
                table: "FeedbackChanges");
        }
    }
}
