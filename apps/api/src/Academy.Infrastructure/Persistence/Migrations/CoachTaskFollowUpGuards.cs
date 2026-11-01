namespace Academy.Infrastructure.Persistence.Migrations;

internal static class CoachTaskFollowUpGuards
{
    internal const string Authority = """
        CREATE FUNCTION academy_coach_task_authority(academy uuid, member uuid, grp uuid, manager boolean)
        RETURNS boolean LANGUAGE sql STABLE AS $$
          SELECT EXISTS (
            SELECT 1 FROM "AcademyMemberships" m
            JOIN "Users" u ON u."Id"=m."UserId" AND u."IsActive"
            JOIN "Academies" a ON a."Id"=m."AcademyId" AND a."IsActive"
            JOIN "TrainingGroups" g ON g."AcademyId"=m."AcademyId" AND g."Id"=grp AND (manager OR g."IsActive")
            JOIN "Branches" b ON b."AcademyId"=g."AcademyId" AND b."Id"=g."BranchId" AND (manager OR b."IsActive")
            JOIN "Sports" s ON s."AcademyId"=g."AcademyId" AND s."Id"=g."SportId" AND (manager OR s."IsActive")
            WHERE m."AcademyId"=academy AND m."Id"=member AND m."IsActive"
              AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."AcademyId"=m."AcademyId" AND p."Id"=m."PrimaryMembershipId" AND p."UserId"=m."UserId" AND p."IsActive" AND p."PrimaryMembershipId" IS NULL))
              AND (m."AllBranches" OR EXISTS(SELECT 1 FROM "MembershipBranchAccess" x WHERE x."AcademyId"=academy AND x."MembershipId"=member AND x."BranchId"=g."BranchId" AND x."IsActive"))
              AND (m."AllSports" OR EXISTS(SELECT 1 FROM "MembershipSportAccess" x WHERE x."AcademyId"=academy AND x."MembershipId"=member AND x."SportId"=g."SportId" AND x."IsActive"))
              AND (manager AND (m."Role"='AcademyOwner' OR m."Role"='AcademyAdmin' AND m."UseActionGrants"
                AND EXISTS(SELECT 1 FROM "StaffActionGrants" x WHERE x."AcademyId"=academy AND x."MembershipId"=member AND x."Module"='followups' AND x."IsActive" AND x."Read" AND x."Add" AND x."Edit")
                AND EXISTS(SELECT 1 FROM "StaffGrants" x WHERE x."AcademyId"=academy AND x."MembershipId"=member AND x."Permission"='workflow.coach-tasks.assign' AND x."IsActive" AND x."Allowed"))
                OR NOT manager AND m."Role"='Coach' AND EXISTS(SELECT 1 FROM "StaffGroupAssignments" x WHERE x."AcademyId"=academy AND x."AcademyMembershipId"=member AND x."TrainingGroupId"=grp AND x."IsActive"))
          );
        $$;
        """;

    private const string TaskGuards = """
          IF TG_TABLE_NAME='OwnerFollowUps' THEN
           IF NEW."Condition" IN ('CoachAttendanceTask','CoachEvaluationTask') THEN
            IF NEW."SourceKind"<>'Group' OR NEW."AssignedToMembershipId" IS NULL THEN RAISE EXCEPTION 'Coach task must retain its group and coach' USING ERRCODE='23514'; END IF;
            IF TG_OP='INSERT' AND (NOT academy_coach_task_authority(NEW."AcademyId",NEW."CreatedByMembershipId",NEW."TrainingGroupId",true)
              OR NOT academy_coach_task_authority(NEW."AcademyId",NEW."AssignedToMembershipId",NEW."TrainingGroupId",false)) THEN
              RAISE EXCEPTION 'Coach task requires an authorized manager and assigned coach' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' AND OLD."AssignedToMembershipId" IS DISTINCT FROM NEW."AssignedToMembershipId" THEN
              RAISE EXCEPTION 'Coach task assignee is immutable' USING ERRCODE='23514'; END IF;
            RETURN NEW;
           END IF;
          END IF;
          IF TG_TABLE_NAME='OwnerFollowUpChanges' THEN
           IF EXISTS(SELECT 1 FROM "OwnerFollowUps" f WHERE f."AcademyId"=NEW."AcademyId" AND f."Id"=NEW."FollowUpId" AND f."Condition" IN ('CoachAttendanceTask','CoachEvaluationTask')) THEN
            IF NOT EXISTS(SELECT 1 FROM "OwnerFollowUps" f WHERE f."AcademyId"=NEW."AcademyId" AND f."Id"=NEW."FollowUpId"
              AND (academy_coach_task_authority(f."AcademyId",NEW."ActorMembershipId",f."TrainingGroupId",true)
                OR f."AssignedToMembershipId"=NEW."ActorMembershipId" AND academy_coach_task_authority(f."AcademyId",NEW."ActorMembershipId",f."TrainingGroupId",false))
              AND NEW."AfterJson"=jsonb_build_object('State',f."State",'AssignedToMembershipId',f."AssignedToMembershipId",'DueOn',f."DueOn",'Note',f."Note",'ResolutionReason',f."ResolutionReason")) THEN
                RAISE EXCEPTION 'Coach task history requires current scope and matching evidence' USING ERRCODE='23514'; END IF;
            RETURN NEW;
           END IF;
          END IF;
        """;

    internal static string History => V4RelatedRecordFollowUpGuards.History.Replace(
        "IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM \"AcademyMemberships\"",
        TaskGuards + "\n IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM \"AcademyMemberships\"");
}
