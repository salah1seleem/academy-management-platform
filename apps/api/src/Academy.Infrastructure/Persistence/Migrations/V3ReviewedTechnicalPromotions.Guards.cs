using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3ReviewedTechnicalPromotions
{
    private static void AddPromotionGuard(MigrationBuilder m)=>m.Sql("""
      CREATE FUNCTION academy_technical_promotion_source() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE d "PlayerTechnicalReviewDecisions"%ROWTYPE; r "PlayerTechnicalReviewRequests"%ROWTYPE;
        tr "EnrollmentGroupTransfers"%ROWTYPE; g "TrainingGroups"%ROWTYPE; p "PlayerSkillPlans"%ROWTYPE; head int;
      BEGIN
        IF NEW."TechnicalDecisionId" IS NULL THEN RETURN NEW; END IF;
        SELECT * INTO d FROM "PlayerTechnicalReviewDecisions" WHERE "Id"=NEW."TechnicalDecisionId";
        SELECT * INTO r FROM "PlayerTechnicalReviewRequests" WHERE "Id"=d."TechnicalRequestId";
        SELECT * INTO tr FROM "EnrollmentGroupTransfers" WHERE "Id"=NEW."GroupTransferId";
        SELECT * INTO g FROM "TrainingGroups" WHERE "Id"=tr."TargetGroupId";
        SELECT * INTO p FROM "PlayerSkillPlans" WHERE "Id"=r."PlanId";
        SELECT max("Sequence") INTO head FROM "PlayerSkillReviews" WHERE "PlanId"=r."PlanId";
        IF d."Outcome"<>'ReadyForPlacement' OR d."RecommendedLevelId" IS DISTINCT FROM NEW."ToLevelId" OR
          r."TrainingLevelId" IS DISTINCT FROM NEW."FromLevelId" OR p."IsActive" IS NOT TRUE OR
          head IS DISTINCT FROM r."SourceReviewSequence" OR g."IsActive" IS NOT TRUE OR
          g."TrainingLevelId" IS DISTINCT FROM NEW."ToLevelId" OR tr."ActorUserId"<>NEW."ActorUserId" OR
          tr."CreatedAtUtc"<>NEW."CreatedAtUtc" OR tr."RequestHash"<>NEW."RequestHash" OR
          tr."EffectiveDate"<>(SELECT (NEW."CreatedAtUtc" AT TIME ZONE "TimeZone")::date FROM "Academies" WHERE "Id"=NEW."AcademyId") THEN
          RAISE EXCEPTION 'Promotion must match the current decision, evidence and dated transfer' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END $$;
      CREATE CONSTRAINT TRIGGER technical_promotion_source AFTER INSERT ON "PlayerLevelReviews" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_technical_promotion_source();
      """);
}
