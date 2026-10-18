using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3PlayerTechnicalReviews
{
    private static void AddTechnicalGuards(MigrationBuilder m)=>m.Sql("""
        CREATE FUNCTION academy_technical_review_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE p "PlayerSkillPlans"%ROWTYPE; c "SkillCurricula"%ROWTYPE; e "SportEnrollments"%ROWTYPE;
          r "PlayerTechnicalReviewRequests"%ROWTYPE; previous "PlayerTechnicalReviewRequests"%ROWTYPE;
          evidence uuid[]; n int; head int; all_mastered boolean; current_context boolean; expected_name text;
        BEGIN
          IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
          IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve technical review evidence and decisions' USING ERRCODE='23514'; END IF;
          IF TG_TABLE_NAME='PlayerTechnicalReviewRequests' THEN
            SELECT * INTO p FROM "PlayerSkillPlans" WHERE "Id"=NEW."PlanId" FOR SHARE;
          ELSE
            SELECT * INTO r FROM "PlayerTechnicalReviewRequests" WHERE "Id"=NEW."TechnicalRequestId";
            SELECT * INTO p FROM "PlayerSkillPlans" WHERE "Id"=r."PlanId" FOR SHARE;
          END IF;
          SELECT * INTO c FROM "SkillCurricula" WHERE "Id"=p."CurriculumId" FOR SHARE;
          SELECT * INTO e FROM "SportEnrollments" WHERE "Id"=p."SportEnrollmentId" FOR SHARE;
          SELECT count(*) INTO n FROM "SkillRequirements" WHERE "CurriculumId"=c."Id";
          SELECT coalesce(max("Sequence"),0) INTO head FROM "PlayerSkillReviews" WHERE "PlanId"=p."Id";
          SELECT array_agg(x."Id" ORDER BY x."Id"),bool_and(x."State"='Mastered') INTO evidence,all_mastered FROM (
            SELECT DISTINCT ON (v."RequirementId") v."Id",v."State" FROM "PlayerSkillReviews" v
              WHERE v."PlanId"=p."Id" ORDER BY v."RequirementId",v."ObservedOn" DESC,v."Sequence" DESC) x;
          current_context:=p."IsActive" AND e."IsActive" AND c."State"='Published' AND e."TrainingLevelId"=c."TrainingLevelId" AND e."LevelReviewedAtUtc" IS NOT NULL AND e."LevelReviewedByUserId" IS NOT NULL
            AND EXISTS(SELECT 1 FROM "TrainingLevels" l WHERE l."Id"=c."TrainingLevelId" AND l."IsActive") AND n>0 AND cardinality(evidence)=n AND all_mastered;
          IF TG_TABLE_NAME='PlayerTechnicalReviewRequests' THEN
            SELECT * INTO previous FROM "PlayerTechnicalReviewRequests" WHERE "PlanId"=p."Id" ORDER BY "Sequence" DESC LIMIT 1;
            SELECT "ArabicName" INTO expected_name FROM "TrainingLevels" WHERE "Id"=c."TrainingLevelId";
            IF current_context IS NOT TRUE OR NEW."SportEnrollmentId"<>p."SportEnrollmentId" OR NEW."SourceReviewSequence"<>head OR
              NEW."LevelName" IS DISTINCT FROM expected_name OR NEW."CurriculumTitle" IS DISTINCT FROM c."Title" OR
              NEW."EvidenceReviewIds" IS NULL OR (SELECT array_agg(id ORDER BY id) FROM unnest(NEW."EvidenceReviewIds") id) IS DISTINCT FROM evidence OR
              NEW."Sequence"<>coalesce(previous."Sequence",0)+1 OR NEW."PreviousRequestId" IS DISTINCT FROM previous."Id" OR
              previous."Id" IS NOT NULL AND (head<=previous."SourceReviewSequence" OR NOT EXISTS(SELECT 1 FROM "PlayerTechnicalReviewDecisions" d WHERE d."TechnicalRequestId"=previous."Id")) THEN
              RAISE EXCEPTION 'Request requires complete current evidence and no pending prior request' USING ERRCODE='23514'; END IF;
          ELSE
            IF NEW."Outcome"='ReadyForPlacement' THEN
              SELECT "ArabicName" INTO expected_name FROM "TrainingLevels" l WHERE l."Id"=NEW."RecommendedLevelId" AND l."IsActive" AND l."SportId"=r."SportId" AND l."Rank">(SELECT "Rank" FROM "TrainingLevels" WHERE "Id"=r."TrainingLevelId");
              IF current_context IS NOT TRUE OR head<>r."SourceReviewSequence" OR expected_name IS NULL OR NEW."RecommendedLevelName" IS DISTINCT FROM expected_name THEN
                RAISE EXCEPTION 'Readiness requires unchanged evidence and a valid recommended level' USING ERRCODE='23514'; END IF;
            ELSIF NEW."Outcome"='ContextChanged' AND current_context IS TRUE AND head=r."SourceReviewSequence" THEN
              RAISE EXCEPTION 'Current evidence has not changed' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER player_technical_request_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerTechnicalReviewRequests" FOR EACH ROW EXECUTE FUNCTION academy_technical_review_guard();
        CREATE TRIGGER player_technical_decision_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerTechnicalReviewDecisions" FOR EACH ROW EXECUTE FUNCTION academy_technical_review_guard();
        """);
}
