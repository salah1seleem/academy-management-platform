using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3DelegatedOperationalFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_owner_followup_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP='DELETE' OR TG_TABLE_NAME='OwnerFollowUpChanges' AND TG_OP='UPDATE' THEN RAISE EXCEPTION 'Preserve follow-up evidence' USING ERRCODE='23514'; END IF;
                  IF TG_TABLE_NAME='OwnerFollowUps' AND TG_OP='UPDATE' AND
                    (to_jsonb(OLD)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) IS DISTINCT FROM
                    (to_jsonb(NEW)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) THEN
                    RAISE EXCEPTION 'Follow-up source is immutable' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' AND NOT EXISTS(
                    SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId"
                      AND m."Id"=CASE WHEN TG_TABLE_NAME='OwnerFollowUps' THEN (to_jsonb(NEW)->>'CreatedByMembershipId')::uuid ELSE (to_jsonb(NEW)->>'ActorMembershipId')::uuid END
                      AND m."Role"='AcademyOwner') THEN
                    IF TG_TABLE_NAME<>'OwnerFollowUpChanges' THEN RAISE EXCEPTION 'Only an owner creates a follow-up' USING ERRCODE='23514'; END IF;
                    IF NOT EXISTS(
                      SELECT 1 FROM "AcademyMemberships" m
                      JOIN "Users" u ON u."Id"=m."UserId" AND u."IsActive"
                      JOIN "Academies" a ON a."Id"=m."AcademyId" AND a."IsActive"
                      JOIN "OwnerFollowUps" f ON f."AcademyId"=m."AcademyId" AND f."AssignedToMembershipId"=m."Id"
                      JOIN "TrainingGroups" g ON g."AcademyId"=f."AcademyId" AND g."Id"=f."TrainingGroupId"
                      WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyAdmin' AND m."IsActive" AND m."UseActionGrants"
                        AND f."Id"=NEW."FollowUpId" AND f."State" IN ('InProgress','Snoozed','Closed','Rejected')
                        AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."AcademyId"=m."AcademyId" AND p."Id"=m."PrimaryMembershipId" AND p."UserId"=m."UserId" AND p."IsActive" AND p."PrimaryMembershipId" IS NULL))
                        AND EXISTS(SELECT 1 FROM "StaffActionGrants" s WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."Module"='followups' AND s."IsActive" AND s."Read" AND s."Edit")
                        AND (m."AllBranches" OR EXISTS(SELECT 1 FROM "MembershipBranchAccess" s JOIN "Branches" b ON b."AcademyId"=s."AcademyId" AND b."Id"=s."BranchId" AND b."IsActive" WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."BranchId"=g."BranchId" AND s."IsActive"))
                        AND (m."AllSports" OR EXISTS(SELECT 1 FROM "MembershipSportAccess" s JOIN "Sports" sp ON sp."AcademyId"=s."AcademyId" AND sp."Id"=s."SportId" AND sp."IsActive" WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."SportId"=g."SportId" AND s."IsActive"))
                        AND NEW."AfterJson"=jsonb_build_object('State',f."State",'AssignedToMembershipId',f."AssignedToMembershipId",'DueOn',f."DueOn",'Note',f."Note",'ResolutionReason',f."ResolutionReason")
                    ) THEN RAISE EXCEPTION 'Delegate must own the assigned scoped follow-up and its current snapshot' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "OwnerFollowUpChanges" c JOIN "AcademyMemberships" m ON m."AcademyId"=c."AcademyId" AND m."Id"=c."ActorMembershipId" WHERE m."Role"<>'AcademyOwner') THEN RAISE EXCEPTION 'Preserve delegated follow-up history before rollback' USING ERRCODE='23514'; END IF; END $$;
                CREATE OR REPLACE FUNCTION academy_owner_followup_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP='DELETE' OR TG_TABLE_NAME='OwnerFollowUpChanges' AND TG_OP='UPDATE' THEN RAISE EXCEPTION 'Preserve follow-up evidence' USING ERRCODE='23514'; END IF;
                  IF TG_TABLE_NAME='OwnerFollowUps' AND TG_OP='UPDATE' AND (to_jsonb(OLD)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) THEN RAISE EXCEPTION 'Follow-up source is immutable' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=CASE WHEN TG_TABLE_NAME='OwnerFollowUps' THEN (to_jsonb(NEW)->>'CreatedByMembershipId')::uuid ELSE (to_jsonb(NEW)->>'ActorMembershipId')::uuid END AND m."Role"='AcademyOwner') THEN RAISE EXCEPTION 'Follow-up actor must be an academy owner' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);
        }
    }
}
