using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ReopenedFeedbackReplySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_feedback_change_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                                DECLARE f "GuardianFeedbacks"%ROWTYPE;
                                BEGIN
                                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve private case evidence' USING ERRCODE='23514'; END IF;
                                  IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive"))) THEN RAISE EXCEPTION 'Private case actor must be an active owner' USING ERRCODE='23514'; END IF;
                                  SELECT * INTO f FROM "GuardianFeedbacks" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."FeedbackId";
                                  IF f."Id" IS NULL OR f."Status"<>NEW."ToStatus" OR f."DueOn" IS DISTINCT FROM NEW."AfterDueOn" THEN RAISE EXCEPTION 'Case evidence must match the saved source' USING ERRCODE='23514'; END IF;
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

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "FeedbackChanges" WHERE "Action"='Transition' AND "ToStatus"='Reopened' AND "PublicResponse" IS NOT NULL) THEN RAISE EXCEPTION 'Preserve reopened reply snapshots before rollback' USING ERRCODE='23514'; END IF; END $$;""");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_feedback_change_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                                DECLARE f "GuardianFeedbacks"%ROWTYPE;
                                BEGIN
                                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve private case evidence' USING ERRCODE='23514'; END IF;
                                  IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive"))) THEN RAISE EXCEPTION 'Private case actor must be an active owner' USING ERRCODE='23514'; END IF;
                                  SELECT * INTO f FROM "GuardianFeedbacks" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."FeedbackId";
                                  IF f."Id" IS NULL OR f."Status"<>NEW."ToStatus" OR f."DueOn" IS DISTINCT FROM NEW."AfterDueOn" THEN RAISE EXCEPTION 'Case evidence must match the saved source' USING ERRCODE='23514'; END IF;
                                  IF NEW."Action" NOT IN ('Review','Due','Resolve','Transition') OR
                                    (NEW."Action"='Resolve' AND (f."Kind"='Rating' OR NEW."ToStatus"<>'Resolved' OR NEW."PublicResponse" IS DISTINCT FROM f."OwnerResponse")) OR
                                    (NEW."Action"<>'Resolve' AND NEW."PublicResponse" IS NOT NULL) OR
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

        }
    }
}
