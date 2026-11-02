using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3FeedbackCaseLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BeforeDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AfterDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    InternalNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PublicResponse = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackChanges", x => x.Id);
                    table.UniqueConstraint("AK_FeedbackChanges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_FeedbackChange_Active", "\"IsActive\" AND (\"RequestId\" IS NULL) = (\"RequestHash\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_FeedbackChanges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackChanges_AcademyMemberships_AcademyId_ActorMembershi~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackChanges_GuardianFeedbacks_AcademyId_FeedbackId",
                        columns: x => new { x.AcademyId, x.FeedbackId },
                        principalTable: "GuardianFeedbacks",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_GuardianFeedback_Status",
                table: "GuardianFeedbacks",
                sql: "\"Status\" IN ('Open','Reviewed','InProgress','AwaitingInfo','Resolved','Closed','Reopened') AND (\"Kind\" <> 'Rating' OR \"Status\" IN ('Open','Reviewed'))");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackChanges_AcademyId_ActorMembershipId_RequestId",
                table: "FeedbackChanges",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackChanges_AcademyId_FeedbackId",
                table: "FeedbackChanges",
                columns: new[] { "AcademyId", "FeedbackId" });
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_feedback_change_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE f "GuardianFeedbacks"%ROWTYPE;
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Preserve private case evidence' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND (m."PrimaryMembershipId" IS NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=m."PrimaryMembershipId" AND p."IsActive"))) THEN RAISE EXCEPTION 'Private case actor must be an active owner' USING ERRCODE='23514'; END IF;
                  SELECT * INTO f FROM "GuardianFeedbacks" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."FeedbackId";
                  IF f."Id" IS NULL OR f."Status"<>NEW."ToStatus" OR f."DueOn" IS DISTINCT FROM NEW."AfterDueOn" THEN RAISE EXCEPTION 'Case evidence must match the saved source' USING ERRCODE='23514'; END IF;
                  IF NEW."Action" NOT IN ('Review','Due','Resolve','Transition') OR
                    (NEW."Action"='Resolve' AND (f."Kind"='Rating' OR NEW."ToStatus"<>'Resolved' OR NEW."PublicResponse" IS DISTINCT FROM f."OwnerResponse")) OR
                    (NEW."Action"<>'Resolve' AND NEW."PublicResponse" IS NOT NULL) OR
                    (NEW."Action"='Review' AND (NEW."FromStatus"<>'Open' OR NEW."ToStatus"<>'Reviewed')) OR
                    (NEW."Action"='Due' AND (NEW."FromStatus"<>NEW."ToStatus" OR NEW."BeforeDueOn" IS NOT NULL OR NEW."AfterDueOn" IS NULL)) OR
                    (NEW."Action"='Transition' AND (f."Kind"='Rating' OR NEW."RequestId" IS NULL OR NEW."RequestId"='00000000-0000-0000-0000-000000000000' OR length(NEW."RequestHash")<>64 OR NOT (
                     (NEW."FromStatus" IN ('Open','Reviewed','Reopened','AwaitingInfo') AND NEW."ToStatus"='InProgress') OR
                     (NEW."FromStatus"='InProgress' AND NEW."ToStatus"='AwaitingInfo') OR
                     (NEW."FromStatus"='Resolved' AND NEW."ToStatus"='Closed') OR
                     (NEW."FromStatus" IN ('Resolved','Closed') AND NEW."ToStatus"='Reopened')))) THEN RAISE EXCEPTION 'Invalid case change evidence' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER feedback_change_guard BEFORE INSERT OR UPDATE OR DELETE ON "FeedbackChanges" FOR EACH ROW EXECUTE FUNCTION academy_feedback_change_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "FeedbackChanges") OR EXISTS(SELECT 1 FROM "GuardianFeedbacks" WHERE "Status" NOT IN ('Open','Reviewed','Resolved')) THEN RAISE EXCEPTION 'Preserve case evidence before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION academy_feedback_change_guard() CASCADE;""");
            migrationBuilder.DropTable(
                name: "FeedbackChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GuardianFeedback_Status",
                table: "GuardianFeedbacks");
        }
    }
}
