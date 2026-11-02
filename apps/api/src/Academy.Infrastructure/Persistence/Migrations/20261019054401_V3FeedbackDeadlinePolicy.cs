using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3FeedbackDeadlinePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FollowUpPolicyEvidence",
                table: "GuardianFeedbacks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_feedback_deadline_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE p jsonb; BEGIN
                  IF TG_OP='UPDATE' AND NEW."FollowUpPolicyEvidence" IS DISTINCT FROM OLD."FollowUpPolicyEvidence" THEN
                    RAISE EXCEPTION 'Preserve intake follow-up policy evidence' USING ERRCODE='23514';
                  END IF;
                  IF NEW."FollowUpPolicyEvidence" IS NOT NULL THEN
                    p := NEW."FollowUpPolicyEvidence"::jsonb;
                    IF NEW."Kind"='Rating' OR (p->>'days')::int NOT BETWEEN 1 AND 30
                      OR (p->>'dueOn')::date IS DISTINCT FROM NEW."DueOn"
                      OR (p->>'receivedOn')::date + (p->>'days')::int IS DISTINCT FROM NEW."DueOn"
                      OR NOT EXISTS(SELECT 1 FROM "AcademyConfigurationVersions" c WHERE c."AcademyId"=NEW."AcademyId" AND c."Id"=(p->>'configurationId')::uuid AND c."Revision"=(p->>'revision')::int AND (c."Payload"::jsonb->>'FeedbackFollowUpDays')::int=(p->>'days')::int)
                      THEN RAISE EXCEPTION 'Deadline evidence must match its academy policy and captured date' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER feedback_deadline_evidence BEFORE INSERT OR UPDATE ON "GuardianFeedbacks" FOR EACH ROW EXECUTE FUNCTION academy_feedback_deadline_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Preserve feedback policy evidence with a reviewed forward migration' USING ERRCODE='23514'; END $$;");
        }
    }
}
