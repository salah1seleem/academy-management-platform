using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class MatchResultsAndScopedCoach
{
    private static void AddResultGuards(MigrationBuilder b)=>b.Sql("""
        UPDATE "AcademyMatchs" SET "ResultFinalized"=true,"ResultPublishedAtUtc"="PublishedAtUtc"
        WHERE "PublishedAtUtc" IS NOT NULL AND "OurScore" IS NOT NULL AND "OpponentScore" IS NOT NULL AND "IsActive";
        INSERT INTO "MatchGoalSlots" ("Id","AcademyId","MatchId","Ordinal","Kind","IsActive","CreatedAtUtc","UpdatedAtUtc")
        SELECT gen_random_uuid(),m."AcademyId",m."Id",n,'Unknown',true,m."CreatedAtUtc",m."UpdatedAtUtc"
        FROM "AcademyMatchs" m JOIN "MatchTeams" t ON t."Id"=m."TeamId" JOIN "Sports" s ON s."Id"=t."SportId"
        CROSS JOIN LATERAL generate_series(1,m."OurScore") n WHERE s."Kind"='Football';
        ALTER TABLE "AcademyMatchs" ADD CONSTRAINT "CK_Match_Result" CHECK
        ("ResultRevision">=0 AND (NOT "ResultFinalized" OR ("PublishedAtUtc" IS NOT NULL AND "ResultPublishedAtUtc" IS NOT NULL AND "OurScore" IS NOT NULL AND "OpponentScore" IS NOT NULL)));
        CREATE FUNCTION academy_match_result_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE k uuid; m record; c integer; football boolean;
        BEGIN
          IF TG_TABLE_NAME='AcademyMatchs' THEN k=NEW."Id";
          ELSIF TG_OP='DELETE' THEN k=OLD."MatchId"; ELSE k=NEW."MatchId"; END IF;
          SELECT * INTO m FROM "AcademyMatchs" WHERE "Id"=k;
          IF NOT FOUND OR m."ResultRevision"=0 THEN RETURN NULL; END IF;
          SELECT s."Kind"='Football' INTO football FROM "MatchTeams" t JOIN "Sports" s ON s."Id"=t."SportId" WHERE t."Id"=m."TeamId";
          SELECT count(*) INTO c FROM "MatchGoalSlots" WHERE "MatchId"=k;
          IF c<>(CASE WHEN football THEN COALESCE(m."OurScore",0) ELSE 0 END)
             OR EXISTS(SELECT 1 FROM "MatchGoalSlots" g WHERE g."MatchId"=k AND (g."Ordinal">COALESCE(m."OurScore",0) OR NOT g."IsActive"))
             OR (m."PublishedAtUtc" IS NOT NULL AND EXISTS(SELECT 1 FROM "MatchGoalSlots" g WHERE g."MatchId"=k AND g."Kind"='Player' AND NOT EXISTS(SELECT 1 FROM "MatchParticipants" p WHERE p."MatchId"=k AND p."AcademyId"=m."AcademyId" AND p."PlayerId"=g."PlayerId" AND p."IsActive")))
          THEN RAISE EXCEPTION 'Match result roster or goal count is inconsistent' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER match_result_guard AFTER INSERT OR UPDATE ON "AcademyMatchs" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_match_result_guard();
        CREATE CONSTRAINT TRIGGER match_goal_guard AFTER INSERT OR UPDATE OR DELETE ON "MatchGoalSlots" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_match_result_guard();
        CREATE FUNCTION academy_match_audit_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Match audit is immutable' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER match_result_audit_immutable BEFORE UPDATE OR DELETE ON "MatchResultChanges" FOR EACH ROW EXECUTE FUNCTION academy_match_audit_immutable();
        CREATE TRIGGER match_team_permission_audit_immutable BEFORE UPDATE OR DELETE ON "MatchTeamPermissionAudits" FOR EACH ROW EXECUTE FUNCTION academy_match_audit_immutable();
        """);
    private static void RemoveResultGuards(MigrationBuilder b)=>b.Sql("""
        DROP TRIGGER IF EXISTS match_result_guard ON "AcademyMatchs";
        DROP TRIGGER IF EXISTS match_goal_guard ON "MatchGoalSlots";
        DROP FUNCTION IF EXISTS academy_match_result_guard();
        DROP TRIGGER IF EXISTS match_result_audit_immutable ON "MatchResultChanges";
        DROP TRIGGER IF EXISTS match_team_permission_audit_immutable ON "MatchTeamPermissionAudits";
        DROP FUNCTION IF EXISTS academy_match_audit_immutable();
        ALTER TABLE "AcademyMatchs" DROP CONSTRAINT IF EXISTS "CK_Match_Result";
        """);
}
