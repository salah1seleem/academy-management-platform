using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3PlayerSkillCurricula
{
    private static void AddJourneyGuards(MigrationBuilder m) => m.Sql("""
        CREATE FUNCTION academy_skill_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE c "SkillCurricula"%ROWTYPE; p "PlayerSkillPlans"%ROWTYPE; e "SportEnrollments"%ROWTYPE;
          u text; day date;
        BEGIN
          IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
          IF TG_TABLE_NAME='SkillRequirements' THEN
            IF TG_OP='DELETE' THEN SELECT * INTO c FROM "SkillCurricula" WHERE "Id"=OLD."CurriculumId" FOR SHARE;
            ELSE SELECT * INTO c FROM "SkillCurricula" WHERE "Id"=NEW."CurriculumId" FOR SHARE; END IF;
            IF c."State" IS DISTINCT FROM 'Draft' OR TG_OP='UPDATE' AND
              (to_jsonb(OLD)-ARRAY['Name','Protocol','Unit','DisplayOrder','UpdatedAtUtc']) IS DISTINCT FROM
              (to_jsonb(NEW)-ARRAY['Name','Protocol','Unit','DisplayOrder','UpdatedAtUtc']) THEN
              RAISE EXCEPTION 'Published requirements are immutable' USING ERRCODE='23514'; END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
          END IF;
          IF TG_OP='DELETE' OR TG_TABLE_NAME='PlayerSkillReviews' AND TG_OP='UPDATE' THEN
            RAISE EXCEPTION 'Preserve skill learning history' USING ERRCODE='23514'; END IF;
          IF TG_TABLE_NAME='SkillCurricula' THEN
            IF TG_OP='INSERT' AND (NEW."State"<>'Draft' OR NEW."PublishedAtUtc" IS NOT NULL OR NEW."EditRevision"<>1) OR
              TG_OP='UPDATE' AND (NEW."EditRevision"<>OLD."EditRevision"+1 OR
                (to_jsonb(OLD)-ARRAY['Title','LevelNameSnapshot','State','PublishedByUserId','PublishedAtUtc','PublishRequestId','RetiredByUserId','RetiredAtUtc','EditRevision','UpdatedAtUtc']) IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['Title','LevelNameSnapshot','State','PublishedByUserId','PublishedAtUtc','PublishRequestId','RetiredByUserId','RetiredAtUtc','EditRevision','UpdatedAtUtc']) OR
                OLD."State"<>'Draft' AND (OLD."State"<>'Published' OR NEW."State"<>'Retired' OR
                  (to_jsonb(OLD)-ARRAY['State','RetiredByUserId','RetiredAtUtc','EditRevision','UpdatedAtUtc']) IS DISTINCT FROM
                  (to_jsonb(NEW)-ARRAY['State','RetiredByUserId','RetiredAtUtc','EditRevision','UpdatedAtUtc']))) THEN
              RAISE EXCEPTION 'Curriculum publication is immutable' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='PlayerSkillPlans' THEN
            IF TG_OP='UPDATE' THEN
              IF NOT OLD."IsActive" OR NEW."IsActive" OR NEW."EndedAtUtc" IS NULL OR
                (to_jsonb(OLD)-ARRAY['IsActive','EndedAtUtc','UpdatedAtUtc']) IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['IsActive','EndedAtUtc','UpdatedAtUtc']) THEN
                RAISE EXCEPTION 'Replace plans explicitly without resetting history' USING ERRCODE='23514'; END IF;
              RETURN NEW;
            END IF;
            SELECT * INTO e FROM "SportEnrollments" WHERE "Id"=NEW."SportEnrollmentId" FOR SHARE;
            SELECT * INTO c FROM "SkillCurricula" WHERE "Id"=NEW."CurriculumId" FOR SHARE;
            SELECT (CURRENT_TIMESTAMP AT TIME ZONE "TimeZone")::date INTO day FROM "Academies" WHERE "Id"=NEW."AcademyId";
            IF NOT NEW."IsActive" OR NEW."AssignedOn"<>day OR c."State" IS DISTINCT FROM 'Published' OR
              e."TrainingLevelId" IS DISTINCT FROM c."TrainingLevelId" OR e."LevelReviewedAtUtc" IS NULL OR e."LevelReviewedByUserId" IS NULL OR
              NOT e."IsActive" OR NOT EXISTS(SELECT 1 FROM "TrainingLevels" l WHERE l."Id"=c."TrainingLevelId" AND l."IsActive") THEN
              RAISE EXCEPTION 'Plan requires a reviewed current level and published requirements' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='PlayerSkillReviews' THEN
            SELECT * INTO p FROM "PlayerSkillPlans" WHERE "Id"=NEW."PlanId" FOR SHARE;
            SELECT * INTO e FROM "SportEnrollments" WHERE "Id"=p."SportEnrollmentId" FOR SHARE;
            SELECT * INTO c FROM "SkillCurricula" WHERE "Id"=NEW."CurriculumId" FOR SHARE;
            SELECT "Unit" INTO u FROM "SkillRequirements" WHERE "Id"=NEW."RequirementId";
            SELECT (CURRENT_TIMESTAMP AT TIME ZONE "TimeZone")::date INTO day FROM "Academies" WHERE "Id"=NEW."AcademyId";
            IF NOT p."IsActive" OR NOT e."IsActive" OR c."State" IS DISTINCT FROM 'Published' OR
              e."TrainingLevelId" IS DISTINCT FROM c."TrainingLevelId" OR e."LevelReviewedAtUtc" IS NULL OR
              NEW."ObservedOn"<p."AssignedOn" OR NEW."ObservedOn">day OR
              NOT EXISTS(SELECT 1 FROM "TrainingLevels" l WHERE l."Id"=c."TrainingLevelId" AND l."IsActive") OR
              u='Checklist' AND NEW."MeasuredValue" IS NOT NULL OR
              u<>'Checklist' AND NEW."State"<>'NotAssessed' AND NEW."MeasuredValue" IS NULL OR
              u='Count' AND NEW."MeasuredValue"<>trunc(NEW."MeasuredValue") THEN
              RAISE EXCEPTION 'Review must match current plan, date and protocol unit' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER skill_curriculum_guard BEFORE INSERT OR UPDATE OR DELETE ON "SkillCurricula" FOR EACH ROW EXECUTE FUNCTION academy_skill_guard();
        CREATE TRIGGER skill_requirement_guard BEFORE INSERT OR UPDATE OR DELETE ON "SkillRequirements" FOR EACH ROW EXECUTE FUNCTION academy_skill_guard();
        CREATE TRIGGER player_skill_plan_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerSkillPlans" FOR EACH ROW EXECUTE FUNCTION academy_skill_guard();
        CREATE TRIGGER player_skill_review_guard BEFORE INSERT OR UPDATE OR DELETE ON "PlayerSkillReviews" FOR EACH ROW EXECUTE FUNCTION academy_skill_guard();
        CREATE FUNCTION academy_skill_publication_required() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE n int;
        BEGIN
          IF NEW."State"='Published' THEN
            SELECT count(*) INTO n FROM "SkillRequirements" WHERE "CurriculumId"=NEW."Id" AND "AcademyId"=NEW."AcademyId";
            IF n<1 OR n>20 THEN RAISE EXCEPTION 'Publish between one and twenty requirements' USING ERRCODE='23514'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER skill_publication_required AFTER INSERT OR UPDATE ON "SkillCurricula"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_skill_publication_required();
        """);
}
