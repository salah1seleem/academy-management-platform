using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3PrivateAchievementCertificates
{
    private static void AddCertificateGuard(MigrationBuilder m)=>m.Sql("""
      CREATE FUNCTION academy_certificate_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE r "PlayerLevelReviews"%ROWTYPE; c "PlayerAchievementCertificates"%ROWTYPE; player_name text; sport_name text; academy_name text; zone text; actor_name text;
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve issued certificates and withdrawals' USING ERRCODE='23514'; END IF;
        SELECT "DisplayName" INTO actor_name FROM "Users" WHERE "Id"=NEW."ActorUserId";
        IF NEW."ActorName" IS DISTINCT FROM actor_name THEN RAISE EXCEPTION 'Preserve issuing actor snapshot' USING ERRCODE='23514'; END IF;
        IF TG_TABLE_NAME='PlayerAchievementCertificates' THEN
          SELECT * INTO r FROM "PlayerLevelReviews" WHERE "Id"=NEW."SourceLevelReviewId" AND "AcademyId"=NEW."AcademyId";
          SELECT p."ArabicName",s."ArabicName",a."ArabicName",a."TimeZone" INTO player_name,sport_name,academy_name,zone FROM "SportEnrollments" e JOIN "Players" p ON p."Id"=e."PlayerId" JOIN "Sports" s ON s."Id"=e."SportId" JOIN "Academies" a ON a."Id"=e."AcademyId" WHERE e."Id"=NEW."SportEnrollmentId" AND e."AcademyId"=NEW."AcademyId";
          IF r."TechnicalDecisionId" IS NULL OR r."GroupTransferId" IS NULL OR r."Action"<>'Changed' OR r."ToLevelId" IS NULL OR
            NEW."LevelName" IS DISTINCT FROM r."ToLevelName" OR NEW."MilestoneAtUtc" IS DISTINCT FROM r."CreatedAtUtc" OR
            NEW."PlayerName" IS DISTINCT FROM player_name OR NEW."SportName" IS DISTINCT FROM sport_name OR NEW."AcademyName" IS DISTINCT FROM academy_name OR
            NEW."MilestoneOn" IS DISTINCT FROM (r."CreatedAtUtc" AT TIME ZONE zone)::date OR NEW."IssuedOn" IS DISTINCT FROM (NEW."CreatedAtUtc" AT TIME ZONE zone)::date THEN
            RAISE EXCEPTION 'Certificate requires an applied technical promotion and matching snapshots' USING ERRCODE='23514'; END IF;
        ELSE
          SELECT * INTO c FROM "PlayerAchievementCertificates" WHERE "Id"=NEW."CertificateId" AND "AcademyId"=NEW."AcademyId";
          IF c."Id" IS NULL OR NEW."CreatedAtUtc"<c."CreatedAtUtc" THEN RAISE EXCEPTION 'Withdrawal requires an issued certificate' USING ERRCODE='23514'; END IF;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER player_certificate_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerAchievementCertificates" FOR EACH ROW EXECUTE FUNCTION academy_certificate_guard();
      CREATE TRIGGER player_certificate_withdrawal_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerCertificateWithdrawals" FOR EACH ROW EXECUTE FUNCTION academy_certificate_guard();
      """);
}
