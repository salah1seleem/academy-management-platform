using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3PlayerEncouragementBadges
{
    private static void AddBadgeGuards(MigrationBuilder m)=>m.Sql("""
      CREATE FUNCTION academy_badge_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE source_at timestamptz; actor_name text; born date; today date;
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve badge source and grant history' USING ERRCODE='23514'; END IF;
        SELECT "DisplayName" INTO actor_name FROM "Users" WHERE "Id"=NEW."ActorUserId";
        IF NEW."ActorName" IS DISTINCT FROM actor_name THEN RAISE EXCEPTION 'Badge actor snapshot must match source' USING ERRCODE='23514'; END IF;
        IF TG_TABLE_NAME='PlayerBadgeRules' THEN RETURN NEW; END IF;
        IF TG_TABLE_NAME IN ('PlayerBadgeRuleRetirements','PlayerBadgeGrants') THEN
          PERFORM pg_advisory_xact_lock(hashtextextended('badge-rule:'||NEW."AcademyId"::text||':'||NEW."RuleId"::text,0));
          SELECT "CreatedAtUtc" INTO source_at FROM "PlayerBadgeRules" WHERE "Id"=NEW."RuleId" AND "AcademyId"=NEW."AcademyId";
          IF TG_TABLE_NAME='PlayerBadgeGrants' THEN
            SELECT p."DateOfBirth",(CURRENT_TIMESTAMP AT TIME ZONE a."TimeZone")::date INTO born,today FROM "SportEnrollments" e JOIN "Players" p ON p."Id"=e."PlayerId" JOIN "Academies" a ON a."Id"=e."AcademyId" WHERE e."Id"=NEW."SportEnrollmentId" AND e."AcademyId"=NEW."AcademyId";
            IF born IS NULL OR NEW."ObservedOn"<born OR NEW."ObservedOn">today OR EXISTS(SELECT 1 FROM "PlayerBadgeRuleRetirements" WHERE "RuleId"=NEW."RuleId" AND "AcademyId"=NEW."AcademyId") THEN RAISE EXCEPTION 'Badge needs a current rule and a nonfuture observation' USING ERRCODE='23514'; END IF;
          END IF;
        ELSE
          SELECT "CreatedAtUtc" INTO source_at FROM "PlayerBadgeGrants" WHERE "Id"=NEW."GrantId" AND "AcademyId"=NEW."AcademyId";
        END IF;
        IF source_at IS NULL OR NEW."CreatedAtUtc"<source_at THEN RAISE EXCEPTION 'Badge action requires a saved source' USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER player_badge_rule_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerBadgeRules" FOR EACH ROW EXECUTE FUNCTION academy_badge_guard();
      CREATE TRIGGER player_badge_retirement_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerBadgeRuleRetirements" FOR EACH ROW EXECUTE FUNCTION academy_badge_guard();
      CREATE TRIGGER player_badge_grant_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerBadgeGrants" FOR EACH ROW EXECUTE FUNCTION academy_badge_guard();
      CREATE TRIGGER player_badge_revocation_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerBadgeRevocations" FOR EACH ROW EXECUTE FUNCTION academy_badge_guard();
      """);
}
