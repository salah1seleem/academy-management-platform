using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3SessionLessonPlans
{
    private static void AddLessonGuards(MigrationBuilder m)=>m.Sql("""
        CREATE FUNCTION academy_lesson_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE p "SessionLessonTemplates"%ROWTYPE; s "TrainingSessions"%ROWTYPE; level_id uuid; n int; total int;
        BEGIN
          IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Preserve lesson source history' USING ERRCODE='23514'; END IF;
          IF TG_TABLE_NAME IN ('TrainingExercises','SessionLessonTemplates') THEN
            IF TG_OP='INSERT' AND (NEW."State"<>'Draft' OR NEW."ReviewedByUserId" IS NOT NULL OR NEW."ReviewedAtUtc" IS NOT NULL) OR
              TG_OP='UPDATE' AND (OLD."State"<>'Draft' OR NEW."State"<>'Reviewed' OR
                (to_jsonb(OLD)-ARRAY['State','ReviewedByUserId','ReviewedAtUtc','UpdatedAtUtc','xmin']) IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['State','ReviewedByUserId','ReviewedAtUtc','UpdatedAtUtc','xmin'])) THEN
              RAISE EXCEPTION 'Submit a new immutable lesson version' USING ERRCODE='23514'; END IF;
            IF TG_TABLE_NAME='SessionLessonTemplates' AND NEW."State"='Reviewed' THEN
              SELECT count(*),sum("Minutes") INTO n,total FROM "SessionLessonSteps" WHERE "TemplateId"=NEW."Id";
              IF n<1 OR n>20 OR total IS DISTINCT FROM NEW."DeclaredMinutes" OR EXISTS(
                SELECT 1 FROM "SessionLessonSteps" r JOIN "TrainingExercises" e ON e."Id"=r."ExerciseId" WHERE r."TemplateId"=NEW."Id" AND e."State"<>'Reviewed') THEN
                RAISE EXCEPTION 'Review exercises and computed duration first' USING ERRCODE='23514'; END IF;
            END IF;
          ELSIF TG_TABLE_NAME='SessionLessonSteps' THEN
            SELECT * INTO p FROM "SessionLessonTemplates" WHERE "Id"=NEW."TemplateId" FOR SHARE;
            IF TG_OP<>'INSERT' OR p."State" IS DISTINCT FROM 'Draft' THEN RAISE EXCEPTION 'Lesson steps are immutable' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='SessionLessonAssignments' THEN
            IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Lesson assignment history is immutable' USING ERRCODE='23514'; END IF;
            SELECT * INTO s FROM "TrainingSessions" WHERE "Id"=NEW."TrainingSessionId" FOR SHARE;
            SELECT * INTO p FROM "SessionLessonTemplates" WHERE "Id"=NEW."TemplateId" FOR SHARE;
            SELECT "TrainingLevelId" INTO level_id FROM "TrainingGroups" WHERE "Id"=s."TrainingGroupId";
            IF p."State" IS DISTINCT FROM 'Reviewed' OR s."Status"<>'Scheduled' OR p."TrainingLevelId" IS DISTINCT FROM level_id OR
              NOT EXISTS(SELECT 1 FROM "TrainingLevels" l WHERE l."Id"=level_id AND l."IsActive") OR
              p."DeclaredMinutes" IS DISTINCT FROM (extract(epoch FROM (s."EndTime"-s."StartTime"))/60)::int THEN
              RAISE EXCEPTION 'Lesson must match actual scheduled session and reviewed group level' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER training_exercise_guard BEFORE INSERT OR UPDATE OR DELETE ON "TrainingExercises" FOR EACH ROW EXECUTE FUNCTION academy_lesson_guard();
        CREATE TRIGGER session_lesson_template_guard BEFORE INSERT OR UPDATE OR DELETE ON "SessionLessonTemplates" FOR EACH ROW EXECUTE FUNCTION academy_lesson_guard();
        CREATE TRIGGER session_lesson_step_guard BEFORE INSERT OR UPDATE OR DELETE ON "SessionLessonSteps" FOR EACH ROW EXECUTE FUNCTION academy_lesson_guard();
        CREATE TRIGGER session_lesson_assignment_guard BEFORE INSERT OR UPDATE OR DELETE ON "SessionLessonAssignments" FOR EACH ROW EXECUTE FUNCTION academy_lesson_guard();
        """);
}
