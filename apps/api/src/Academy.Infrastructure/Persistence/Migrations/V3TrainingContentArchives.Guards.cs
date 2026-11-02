using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
public partial class V3TrainingContentArchives
{
    private static void AddArchiveGuards(MigrationBuilder m)=>m.Sql("""
      CREATE FUNCTION academy_lesson_archive_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE source_at timestamptz; actor_name text;
      BEGIN
        IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
        IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve content archive history' USING ERRCODE='23514'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('lesson-content:'||NEW."AcademyId"::text,0));
        SELECT "DisplayName" INTO actor_name FROM "Users" WHERE "Id"=NEW."ActorUserId";
        IF NEW."ExerciseId" IS NOT NULL THEN SELECT "CreatedAtUtc" INTO source_at FROM "TrainingExercises" WHERE "Id"=NEW."ExerciseId" AND "AcademyId"=NEW."AcademyId";
        ELSE SELECT "CreatedAtUtc" INTO source_at FROM "SessionLessonTemplates" WHERE "Id"=NEW."TemplateId" AND "AcademyId"=NEW."AcademyId"; END IF;
        IF source_at IS NULL OR NEW."CreatedAtUtc"<source_at OR NEW."ActorName" IS DISTINCT FROM actor_name THEN RAISE EXCEPTION 'Archive requires matching source and actor' USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER training_content_archive_guard BEFORE INSERT OR UPDATE OR DELETE ON "TrainingContentArchives" FOR EACH ROW EXECUTE FUNCTION academy_lesson_archive_guard();
      CREATE FUNCTION academy_lesson_archive_use_guard() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN
        PERFORM pg_advisory_xact_lock(hashtextextended('lesson-content:'||NEW."AcademyId"::text,0));
        IF TG_TABLE_NAME='TrainingExercises' THEN
          IF EXISTS(SELECT 1 FROM "TrainingContentArchives" WHERE "ExerciseId"=NEW."Id") THEN RAISE EXCEPTION 'Archived exercise cannot be reviewed' USING ERRCODE='23514'; END IF;
        ELSIF TG_TABLE_NAME='SessionLessonTemplates' THEN
          IF EXISTS(SELECT 1 FROM "TrainingContentArchives" WHERE "TemplateId"=NEW."Id") OR EXISTS(SELECT 1 FROM "SessionLessonSteps" s JOIN "TrainingContentArchives" a ON a."ExerciseId"=s."ExerciseId" WHERE s."TemplateId"=NEW."Id") THEN RAISE EXCEPTION 'Archived lesson content cannot be reviewed' USING ERRCODE='23514'; END IF;
        ELSIF TG_TABLE_NAME='SessionLessonSteps' THEN
          IF EXISTS(SELECT 1 FROM "TrainingContentArchives" WHERE "ExerciseId"=NEW."ExerciseId" OR "TemplateId"=NEW."TemplateId") THEN RAISE EXCEPTION 'Archived lesson content cannot be copied into new steps' USING ERRCODE='23514'; END IF;
        ELSE
          IF EXISTS(SELECT 1 FROM "TrainingContentArchives" WHERE "TemplateId"=NEW."TemplateId") OR EXISTS(SELECT 1 FROM "SessionLessonSteps" s JOIN "TrainingContentArchives" a ON a."ExerciseId"=s."ExerciseId" WHERE s."TemplateId"=NEW."TemplateId") THEN RAISE EXCEPTION 'Archived lesson content cannot be assigned again' USING ERRCODE='23514'; END IF;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER training_exercise_archive_use BEFORE UPDATE ON "TrainingExercises" FOR EACH ROW EXECUTE FUNCTION academy_lesson_archive_use_guard();
      CREATE TRIGGER lesson_template_archive_use BEFORE UPDATE ON "SessionLessonTemplates" FOR EACH ROW EXECUTE FUNCTION academy_lesson_archive_use_guard();
      CREATE TRIGGER lesson_step_archive_use BEFORE INSERT ON "SessionLessonSteps" FOR EACH ROW EXECUTE FUNCTION academy_lesson_archive_use_guard();
      CREATE TRIGGER lesson_assignment_archive_use BEFORE INSERT ON "SessionLessonAssignments" FOR EACH ROW EXECUTE FUNCTION academy_lesson_archive_use_guard();
      """);
}
