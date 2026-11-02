using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2SessionEvaluationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TrainingSessionId",
                table: "PlayerEvaluations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_TrainingSessionId_TrainingGroup~",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerEvaluations_TrainingSessions_AcademyId_TrainingSessio~",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" },
                principalTable: "TrainingSessions",
                principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_evaluation_context_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='UPDATE' AND (OLD."AcademyId",OLD."SportEnrollmentId",OLD."SportId",OLD."TrainingGroupId",OLD."EvaluatedByUserId",OLD."EvaluationDate",OLD."TrainingSessionId") IS DISTINCT FROM (NEW."AcademyId",NEW."SportEnrollmentId",NEW."SportId",NEW."TrainingGroupId",NEW."EvaluatedByUserId",NEW."EvaluationDate",NEW."TrainingSessionId") THEN RAISE EXCEPTION 'Evaluation source context is immutable' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' AND NEW."TrainingSessionId" IS NOT NULL AND (NOT EXISTS (SELECT 1 FROM "TrainingSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."TrainingSessionId" AND s."TrainingGroupId"=NEW."TrainingGroupId" AND s."SessionDate"=NEW."EvaluationDate") OR NOT academy_session_assigned(NEW."AcademyId",NEW."SportEnrollmentId",NEW."TrainingSessionId")) THEN RAISE EXCEPTION 'Evaluation occurrence membership mismatch' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER evaluation_context_guard BEFORE INSERT OR UPDATE ON "PlayerEvaluations" FOR EACH ROW EXECUTE FUNCTION academy_evaluation_context_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER evaluation_context_guard ON \"PlayerEvaluations\"; DROP FUNCTION academy_evaluation_context_guard();");
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerEvaluations_TrainingSessions_AcademyId_TrainingSessio~",
                table: "PlayerEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_PlayerEvaluations_AcademyId_TrainingSessionId_TrainingGroup~",
                table: "PlayerEvaluations");

            migrationBuilder.DropColumn(
                name: "TrainingSessionId",
                table: "PlayerEvaluations");
        }
    }
}
