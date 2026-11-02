using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3GroupMediaPrivacy
{
    private static void AddPrivacyGuards(MigrationBuilder m)=>m.Sql("""
      CREATE FUNCTION academy_group_media_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
      CREATE TRIGGER group_media_consent_guard BEFORE INSERT OR UPDATE OR DELETE ON "GroupMediaConsents" FOR EACH ROW EXECUTE FUNCTION academy_group_media_guard();
      CREATE TRIGGER group_media_basis_guard BEFORE INSERT OR UPDATE OR DELETE ON "PrivateMediaConsentBasiss" FOR EACH ROW EXECUTE FUNCTION academy_group_media_guard();
      CREATE TRIGGER group_media_audience_guard BEFORE INSERT OR UPDATE OR DELETE ON "PrivateAssetAudiences" FOR EACH ROW EXECUTE FUNCTION academy_group_media_guard();
      CREATE TRIGGER group_media_asset_guard BEFORE INSERT OR UPDATE OR DELETE ON "PrivateAssets" FOR EACH ROW EXECUTE FUNCTION academy_group_media_guard();
      """);
}
