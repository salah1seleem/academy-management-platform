using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2SessionReplacementLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_moved_session_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF ((OLD."SessionDate",OLD."StartTime",OLD."EndTime",OLD."TrainingGroupId") IS DISTINCT FROM (NEW."SessionDate",NEW."StartTime",NEW."EndTime",NEW."TrainingGroupId")
                    OR (OLD."Status" IS DISTINCT FROM NEW."Status" AND NOT (OLD."Status"='Scheduled' AND NEW."Status"='Held')))
                    AND EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = OLD."AcademyId" AND (m."SourceSessionId" = OLD."Id" OR m."TargetSessionId" = OLD."Id"))
                  THEN RAISE EXCEPTION 'Reviewed session replacement is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_moved_session_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."SessionDate",OLD."StartTime",OLD."EndTime",OLD."Status",OLD."TrainingGroupId") IS DISTINCT FROM (NEW."SessionDate",NEW."StartTime",NEW."EndTime",NEW."Status",NEW."TrainingGroupId")
                    AND EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = OLD."AcademyId" AND (m."SourceSessionId" = OLD."Id" OR m."TargetSessionId" = OLD."Id"))
                  THEN RAISE EXCEPTION 'Reviewed session replacement is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);
        }
    }
}
