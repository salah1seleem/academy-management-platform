using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3SessionMediaPublication
{
    private static void AddSessionPublicationGuards(MigrationBuilder m)=>m.Sql("""
      CREATE OR REPLACE FUNCTION academy_group_media_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE actor uuid; previous integer; source_player uuid; source_group uuid; target_group uuid; event_on date;
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_TABLE_NAME='PrivateAssets' THEN
          IF TG_OP='INSERT' THEN
            IF NEW."TrainingSessionId" IS NOT NULL AND (NEW."Purpose"<>'Media' OR NEW."PlayerId" IS NOT NULL OR NEW."MediaPolicyVersion"<>1 OR NOT EXISTS(SELECT 1 FROM "TrainingSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."TrainingSessionId" AND s."TrainingGroupId"=NEW."TrainingGroupId" AND s."SessionDate"=NEW."MediaEventOn" AND s."IsActive" AND s."Status"<>'Cancelled')) OR NEW."MediaPolicyVersion" NOT IN (0,1) OR NEW."MediaPolicyVersion"=1 AND (NEW."Purpose"<>'Media' OR NEW."MediaEventOn" IS NULL OR NEW."MediaEventOn">(CURRENT_TIMESTAMP AT TIME ZONE (SELECT "TimeZone" FROM "Academies" WHERE "Id"=NEW."AcademyId"))::date OR (NEW."PlayerId" IS NULL)=(NEW."TrainingGroupId" IS NULL)) THEN RAISE EXCEPTION 'Media needs a dated private publication' USING ERRCODE='23514'; END IF;
            RETURN NEW;
          END IF;
          IF OLD."MediaPolicyVersion"=1 AND (TG_OP='DELETE' OR (to_jsonb(NEW)-'IsActive'-'UpdatedAtUtc') IS DISTINCT FROM (to_jsonb(OLD)-'IsActive'-'UpdatedAtUtc')) THEN RAISE EXCEPTION 'Preserve media publication source' USING ERRCODE='23514'; END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
        END IF;
        IF TG_TABLE_NAME='PrivateAssetAudiences' THEN
          IF TG_OP<>'INSERT' THEN
            IF OLD."SportEnrollmentId" IS NOT NULL THEN RAISE EXCEPTION 'Preserve event audience' USING ERRCODE='23514'; END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF;RETURN NEW;
          END IF;
          IF NEW."SportEnrollmentId" IS NOT NULL THEN
            SELECT e."PlayerId",CASE WHEN a."TrainingSessionId" IS NULL THEN academy_assigned_group_at(e."AcademyId",e."Id",a."MediaEventOn") WHEN academy_session_assigned(e."AcademyId",e."Id",a."TrainingSessionId") AND EXISTS(SELECT 1 FROM "TrainingSessions" s WHERE s."AcademyId"=a."AcademyId" AND s."Id"=a."TrainingSessionId" AND s."TrainingGroupId"=a."TrainingGroupId" AND s."SessionDate"=a."MediaEventOn" AND s."IsActive" AND s."Status"<>'Cancelled') THEN a."TrainingGroupId" ELSE NULL END,a."TrainingGroupId",a."MediaEventOn" INTO source_player,source_group,target_group,event_on FROM "SportEnrollments" e JOIN "PrivateAssets" a ON a."Id"=NEW."AssetId" AND a."AcademyId"=e."AcademyId" WHERE e."Id"=NEW."SportEnrollmentId" AND e."AcademyId"=NEW."AcademyId";
            IF source_player IS DISTINCT FROM NEW."PlayerId" OR source_group IS DISTINCT FROM target_group OR event_on IS NULL THEN RAISE EXCEPTION 'Audience must match dated enrollment source' USING ERRCODE='23514'; END IF;
          END IF;RETURN NEW;
        END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve consent and publication evidence' USING ERRCODE='23514'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('group-media-privacy:'||NEW."AcademyId"::text,0));
        IF TG_TABLE_NAME='GroupMediaConsents' THEN
          SELECT g."UserId" INTO actor FROM "GuardianPlayerLinks" l JOIN "GuardianProfiles" g ON g."Id"=l."GuardianId" WHERE l."Id"=NEW."GuardianPlayerLinkId" AND l."AcademyId"=NEW."AcademyId" AND l."PlayerId"=NEW."PlayerId" AND l."IsActive" AND g."IsActive";
          SELECT coalesce(max("Revision"),0) INTO previous FROM "GroupMediaConsents" WHERE "GuardianPlayerLinkId"=NEW."GuardianPlayerLinkId" AND "AcademyId"=NEW."AcademyId";
          IF actor IS DISTINCT FROM NEW."ActorUserId" OR NEW."Revision"<>previous+1 THEN RAISE EXCEPTION 'Consent needs linked guardian and next revision' USING ERRCODE='23514'; END IF;
        ELSE
          IF NOT EXISTS(SELECT 1 FROM "GroupMediaConsents" c JOIN "PrivateAssetAudiences" s ON s."AssetId"=NEW."AssetId" AND s."AcademyId"=c."AcademyId" AND s."PlayerId"=c."PlayerId" AND s."IsSubject" WHERE c."Id"=NEW."ConsentId" AND c."AcademyId"=NEW."AcademyId" AND c."PlayerId"=NEW."PlayerId" AND c."Allowed" AND NEW."CreatedAtUtc">=c."CreatedAtUtc" AND NOT EXISTS(SELECT 1 FROM "GroupMediaConsents" n WHERE n."GuardianPlayerLinkId"=c."GuardianPlayerLinkId" AND n."Revision">c."Revision")) THEN RAISE EXCEPTION 'Publication requires current saved consent' USING ERRCODE='23514'; END IF;
        END IF;RETURN NEW;
      END $$;

      CREATE FUNCTION academy_media_withdrawal_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve administrative media withdrawal' USING ERRCODE='23514'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('group-media-privacy:'||NEW."AcademyId"::text,0));
        IF NOT EXISTS(SELECT 1 FROM "PrivateAssets" a WHERE a."AcademyId"=NEW."AcademyId" AND a."Id"=NEW."AssetId" AND a."Purpose"='Media')
          OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" JOIN "Academies" a ON a."Id"=m."AcademyId"
            WHERE m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."ActorUserId" AND m."IsActive" AND u."IsActive" AND a."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin')
            AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive")))
        THEN RAISE EXCEPTION 'Withdrawal needs a media source and an administrative actor' USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER media_withdrawal_guard BEFORE INSERT OR UPDATE OR DELETE ON "PrivateMediaWithdrawals" FOR EACH ROW EXECUTE FUNCTION academy_media_withdrawal_guard();
      """);
    private static void RemoveSessionPublicationGuards(MigrationBuilder m)=>m.Sql("""
      DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PrivateMediaWithdrawals") OR EXISTS(SELECT 1 FROM "PrivateAssets" WHERE "TrainingSessionId" IS NOT NULL) THEN RAISE EXCEPTION 'Preserve session publication and withdrawal history' USING ERRCODE='23514'; END IF; END $$;
      DROP FUNCTION academy_media_withdrawal_guard() CASCADE;
      CREATE OR REPLACE FUNCTION academy_group_media_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE actor uuid; previous integer; source_player uuid; source_group uuid; target_group uuid; event_on date;
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_TABLE_NAME='PrivateAssets' THEN
          IF TG_OP='INSERT' THEN
            IF NEW."MediaPolicyVersion" NOT IN (0,1) OR NEW."MediaPolicyVersion"=1 AND (NEW."Purpose"<>'Media' OR NEW."MediaEventOn" IS NULL OR NEW."MediaEventOn">(CURRENT_TIMESTAMP AT TIME ZONE (SELECT "TimeZone" FROM "Academies" WHERE "Id"=NEW."AcademyId"))::date OR (NEW."PlayerId" IS NULL)=(NEW."TrainingGroupId" IS NULL)) THEN RAISE EXCEPTION 'Media needs a dated private publication' USING ERRCODE='23514'; END IF;
            RETURN NEW;
          END IF;
          IF OLD."MediaPolicyVersion"=1 AND (TG_OP='DELETE' OR (to_jsonb(NEW)-'IsActive'-'UpdatedAtUtc') IS DISTINCT FROM (to_jsonb(OLD)-'IsActive'-'UpdatedAtUtc')) THEN RAISE EXCEPTION 'Preserve media publication source' USING ERRCODE='23514'; END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
        END IF;
        IF TG_TABLE_NAME='PrivateAssetAudiences' THEN
          IF TG_OP<>'INSERT' THEN
            IF OLD."SportEnrollmentId" IS NOT NULL THEN RAISE EXCEPTION 'Preserve event audience' USING ERRCODE='23514'; END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF;RETURN NEW;
          END IF;
          IF NEW."SportEnrollmentId" IS NOT NULL THEN
            SELECT e."PlayerId",academy_assigned_group_at(e."AcademyId",e."Id",a."MediaEventOn"),a."TrainingGroupId",a."MediaEventOn" INTO source_player,source_group,target_group,event_on FROM "SportEnrollments" e JOIN "PrivateAssets" a ON a."Id"=NEW."AssetId" AND a."AcademyId"=e."AcademyId" WHERE e."Id"=NEW."SportEnrollmentId" AND e."AcademyId"=NEW."AcademyId";
            IF source_player IS DISTINCT FROM NEW."PlayerId" OR source_group IS DISTINCT FROM target_group OR event_on IS NULL THEN RAISE EXCEPTION 'Audience must match dated enrollment source' USING ERRCODE='23514'; END IF;
          END IF;RETURN NEW;
        END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve consent and publication evidence' USING ERRCODE='23514'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('group-media-privacy:'||NEW."AcademyId"::text,0));
        IF TG_TABLE_NAME='GroupMediaConsents' THEN
          SELECT g."UserId" INTO actor FROM "GuardianPlayerLinks" l JOIN "GuardianProfiles" g ON g."Id"=l."GuardianId" WHERE l."Id"=NEW."GuardianPlayerLinkId" AND l."AcademyId"=NEW."AcademyId" AND l."PlayerId"=NEW."PlayerId" AND l."IsActive" AND g."IsActive";
          SELECT coalesce(max("Revision"),0) INTO previous FROM "GroupMediaConsents" WHERE "GuardianPlayerLinkId"=NEW."GuardianPlayerLinkId" AND "AcademyId"=NEW."AcademyId";
          IF actor IS DISTINCT FROM NEW."ActorUserId" OR NEW."Revision"<>previous+1 THEN RAISE EXCEPTION 'Consent needs linked guardian and next revision' USING ERRCODE='23514'; END IF;
        ELSE
          IF NOT EXISTS(SELECT 1 FROM "GroupMediaConsents" c JOIN "PrivateAssetAudiences" s ON s."AssetId"=NEW."AssetId" AND s."AcademyId"=c."AcademyId" AND s."PlayerId"=c."PlayerId" AND s."IsSubject" WHERE c."Id"=NEW."ConsentId" AND c."AcademyId"=NEW."AcademyId" AND c."PlayerId"=NEW."PlayerId" AND c."Allowed" AND NEW."CreatedAtUtc">=c."CreatedAtUtc" AND NOT EXISTS(SELECT 1 FROM "GroupMediaConsents" n WHERE n."GuardianPlayerLinkId"=c."GuardianPlayerLinkId" AND n."Revision">c."Revision")) THEN RAISE EXCEPTION 'Publication requires current saved consent' USING ERRCODE='23514'; END IF;
        END IF;RETURN NEW;
      END $$;

      """);
}
