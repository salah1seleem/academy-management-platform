using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3PrivateMediaLifecycle
{
    private static void AddLifecycleGuards(MigrationBuilder m)=>m.Sql("""
      CREATE FUNCTION academy_media_lifecycle_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE a "PrivateAssets"%ROWTYPE;
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve media erasure and purge evidence' USING ERRCODE='23514'; END IF;
        SELECT * INTO a FROM "PrivateAssets" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."AssetId" AND "Purpose"='Media';
        IF a."Id" IS NULL THEN RAISE EXCEPTION 'Only private media can be erased' USING ERRCODE='23514'; END IF;
        IF TG_TABLE_NAME='PrivateMediaErasures' THEN
          IF NEW."RequestId"='00000000-0000-0000-0000-000000000000' OR length(NEW."RequestHash")<>64 THEN RAISE EXCEPTION 'Erasure requires saved intent' USING ERRCODE='23514'; END IF;
          IF NEW."Reason"='Retention' THEN
            IF NOT a."DeleteOnExpiry" OR a."RetainUntilUtc" IS NULL OR a."RetainUntilUtc">CURRENT_TIMESTAMP THEN RAISE EXCEPTION 'Retention must be explicitly selected and due' USING ERRCODE='23514'; END IF;
          ELSIF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin') AND m."IsActive" AND u."IsActive" AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive"))) THEN RAISE EXCEPTION 'Erasure requires an administrative actor' USING ERRCODE='23514'; END IF;
        ELSIF NEW."ReleasedBytes"<>a."Length" OR NOT EXISTS(SELECT 1 FROM "PrivateMediaErasures" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."ErasureId" AND e."AssetId"=NEW."AssetId" AND e."CreatedAtUtc"<=NEW."CreatedAtUtc") THEN RAISE EXCEPTION 'Purge must match the saved erasure and source length' USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER media_erasure_guard BEFORE INSERT OR UPDATE OR DELETE ON "PrivateMediaErasures" FOR EACH ROW EXECUTE FUNCTION academy_media_lifecycle_guard();
      CREATE TRIGGER media_purge_guard BEFORE INSERT OR UPDATE OR DELETE ON "PrivateMediaPurges" FOR EACH ROW EXECUTE FUNCTION academy_media_lifecycle_guard();
      """);
    private static void RemoveLifecycleGuards(MigrationBuilder m)=>m.Sql("""
      DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PrivateMediaErasures") OR EXISTS(SELECT 1 FROM "PrivateMediaPurges") OR EXISTS(SELECT 1 FROM "PrivateAssets" WHERE "RetainUntilUtc" IS NOT NULL) THEN RAISE EXCEPTION 'Preserve media lifecycle decisions and retention snapshots' USING ERRCODE='23514'; END IF; END $$;
      DROP FUNCTION academy_media_lifecycle_guard() CASCADE;
      """);
}
