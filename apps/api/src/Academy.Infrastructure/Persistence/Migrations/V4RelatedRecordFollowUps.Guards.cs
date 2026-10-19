namespace Academy.Infrastructure.Persistence.Migrations;

internal static class V4RelatedRecordFollowUpGuards
{
    internal const string LegacyHistory = """
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
        """;
    internal const string Source = """
        CREATE FUNCTION academy_followup_record_source_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE valid_source boolean := false;
        BEGIN
          IF NEW."SourceKind"='Group' THEN RETURN NEW; END IF;
          CASE NEW."SourceKind"
            WHEN 'Player' THEN SELECT EXISTS(SELECT 1 FROM "Players" p JOIN "SportEnrollments" e ON e."AcademyId"=p."AcademyId" AND e."PlayerId"=p."Id" WHERE p."AcademyId"=NEW."AcademyId" AND p."Id"=NEW."SourceId" AND e."BranchId"=NEW."SourceBranchId" AND e."SportId"=NEW."SourceSportId") INTO valid_source;
            WHEN 'Employee' THEN SELECT EXISTS(SELECT 1 FROM "Employees" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."SourceId" AND e."BranchId"=NEW."SourceBranchId" AND NEW."SourceSportId" IS NULL) INTO valid_source;
            WHEN 'Guardian' THEN SELECT EXISTS(SELECT 1 FROM "GuardianProfiles" g WHERE g."AcademyId"=NEW."AcademyId" AND g."Id"=NEW."SourceId" AND NEW."SourceBranchId" IS NULL AND NEW."SourceSportId" IS NULL) INTO valid_source;
            WHEN 'Request' THEN SELECT EXISTS(SELECT 1 FROM "GuardianFeedbacks" r JOIN "TrainingGroups" g ON g."AcademyId"=r."AcademyId" AND g."Id"=r."TrainingGroupId" WHERE r."AcademyId"=NEW."AcademyId" AND r."Id"=NEW."SourceId" AND r."Kind"<>'Rating' AND r."BranchId"=NEW."SourceBranchId" AND g."SportId"=NEW."SourceSportId") INTO valid_source;
            WHEN 'Receivable' THEN SELECT EXISTS(SELECT 1 FROM "ReceivableDocuments" r JOIN "SportEnrollments" e ON e."AcademyId"=r."AcademyId" AND e."Id"=r."SportEnrollmentId" WHERE r."AcademyId"=NEW."AcademyId" AND r."Id"=NEW."SourceId" AND r."BranchId"=NEW."SourceBranchId" AND e."SportId"=NEW."SourceSportId") INTO valid_source;
            ELSE valid_source := false;
          END CASE;
          IF NOT valid_source THEN RAISE EXCEPTION 'Follow-up record must belong to the same academy and captured scope' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER owner_followup_record_source BEFORE INSERT ON "OwnerFollowUps" FOR EACH ROW EXECUTE FUNCTION academy_followup_record_source_guard();
        """;

    internal const string History = """
        CREATE OR REPLACE FUNCTION academy_owner_followup_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
          IF TG_OP='DELETE' OR TG_TABLE_NAME='OwnerFollowUpChanges' AND TG_OP='UPDATE' THEN RAISE EXCEPTION 'Preserve follow-up evidence' USING ERRCODE='23514'; END IF;
          IF TG_TABLE_NAME='OwnerFollowUps' AND TG_OP='UPDATE' AND
            (to_jsonb(OLD)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) IS DISTINCT FROM
            (to_jsonb(NEW)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) THEN
            RAISE EXCEPTION 'Follow-up source is immutable' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=CASE WHEN TG_TABLE_NAME='OwnerFollowUps' THEN (to_jsonb(NEW)->>'CreatedByMembershipId')::uuid ELSE (to_jsonb(NEW)->>'ActorMembershipId')::uuid END AND m."Role"='AcademyOwner') THEN
            IF TG_TABLE_NAME<>'OwnerFollowUpChanges' THEN RAISE EXCEPTION 'Only an owner creates a follow-up' USING ERRCODE='23514'; END IF;
            IF NOT EXISTS(
              SELECT 1 FROM "AcademyMemberships" m
              JOIN "Users" u ON u."Id"=m."UserId" AND u."IsActive"
              JOIN "Academies" a ON a."Id"=m."AcademyId" AND a."IsActive"
              JOIN "OwnerFollowUps" f ON f."AcademyId"=m."AcademyId" AND f."AssignedToMembershipId"=m."Id"
              LEFT JOIN "TrainingGroups" g ON g."AcademyId"=f."AcademyId" AND g."Id"=f."TrainingGroupId"
              WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyAdmin' AND m."IsActive" AND m."UseActionGrants"
                AND f."Id"=NEW."FollowUpId" AND f."State" IN ('InProgress','Snoozed','Closed','Rejected')
                AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."AcademyId"=m."AcademyId" AND p."Id"=m."PrimaryMembershipId" AND p."UserId"=m."UserId" AND p."IsActive" AND p."PrimaryMembershipId" IS NULL))
                AND EXISTS(SELECT 1 FROM "StaffActionGrants" s WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."Module"='followups' AND s."IsActive" AND s."Read" AND s."Edit")
                AND NOT EXISTS(SELECT 1 FROM "StaffGrants" s WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."IsActive" AND NOT s."Allowed" AND s."Permission"='workflow.followups.update')
                AND (f."SourceKind"='Group' OR EXISTS(SELECT 1 FROM "StaffActionGrants" s WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."Module"=CASE f."SourceKind" WHEN 'Employee' THEN 'payroll' WHEN 'Request' THEN 'finance' WHEN 'Receivable' THEN 'finance' ELSE 'players' END AND s."IsActive" AND s."Read"))
                AND (m."AllBranches" OR EXISTS(SELECT 1 FROM "MembershipBranchAccess" s JOIN "Branches" b ON b."AcademyId"=s."AcademyId" AND b."Id"=s."BranchId" AND b."IsActive" WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."BranchId"=COALESCE(g."BranchId",f."SourceBranchId") AND s."IsActive"))
                AND (m."AllSports" OR EXISTS(SELECT 1 FROM "MembershipSportAccess" s JOIN "Sports" sp ON sp."AcademyId"=s."AcademyId" AND sp."Id"=s."SportId" AND sp."IsActive" WHERE s."AcademyId"=m."AcademyId" AND s."MembershipId"=m."Id" AND s."SportId"=COALESCE(g."SportId",f."SourceSportId") AND s."IsActive"))
                AND NEW."AfterJson"=jsonb_build_object('State',f."State",'AssignedToMembershipId',f."AssignedToMembershipId",'DueOn',f."DueOn",'Note',f."Note",'ResolutionReason',f."ResolutionReason")
            ) THEN RAISE EXCEPTION 'Delegate must own the scoped follow-up and its current snapshot' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        """;
}
