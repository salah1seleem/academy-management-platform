using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2SessionCoachOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionCoachOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalCoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActualCoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PreviousDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QualificationEvidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PayBasis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionCoachOverrides", x => x.Id);
                    table.UniqueConstraint("AK_SessionCoachOverrides_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CoachOverride_Review", "\"Sequence\">0 AND length(btrim(\"Reason\"))>0 AND length(btrim(\"QualificationEvidence\"))>0 AND \"StartTime\"<\"EndTime\" AND \"PayBasis\"='ContractReviewPending'");
                    table.ForeignKey(
                        name: "FK_SessionCoachOverrides_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoachOverrides_AcademyMemberships_AcademyId_ActualCo~",
                        columns: x => new { x.AcademyId, x.ActualCoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoachOverrides_AcademyMemberships_AcademyId_Original~",
                        columns: x => new { x.AcademyId, x.OriginalCoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoachOverrides_SessionCoachOverrides_AcademyId_Previ~",
                        columns: x => new { x.AcademyId, x.PreviousDecisionId },
                        principalTable: "SessionCoachOverrides",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoachOverrides_TrainingSessions_AcademyId_TrainingSe~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoachOverrides_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_AcademyId_ActorUserId_RequestKey",
                table: "SessionCoachOverrides",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_AcademyId_ActualCoachMembershipId",
                table: "SessionCoachOverrides",
                columns: new[] { "AcademyId", "ActualCoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_AcademyId_OriginalCoachMembershipId",
                table: "SessionCoachOverrides",
                columns: new[] { "AcademyId", "OriginalCoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_AcademyId_PreviousDecisionId",
                table: "SessionCoachOverrides",
                columns: new[] { "AcademyId", "PreviousDecisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_AcademyId_TrainingSessionId_OriginalC~",
                table: "SessionCoachOverrides",
                columns: new[] { "AcademyId", "TrainingSessionId", "OriginalCoachMembershipId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_AcademyId_TrainingSessionId_TrainingG~",
                table: "SessionCoachOverrides",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoachOverrides_ActorUserId",
                table: "SessionCoachOverrides",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_session_staff_assigned(academy uuid, member uuid, session uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
                  SELECT EXISTS (SELECT 1 FROM "TrainingSessions" s
                    JOIN "TrainingGroups" g ON g."AcademyId"=s."AcademyId" AND g."Id"=s."TrainingGroupId" AND g."IsActive"
                    JOIN "AcademyMemberships" m ON m."AcademyId"=s."AcademyId" AND m."Id"=member AND m."IsActive"
                    JOIN "Users" u ON u."Id"=m."UserId" AND u."IsActive"
                    WHERE s."AcademyId"=academy AND s."Id"=session AND s."Status"<>'Cancelled' AND (
                      (EXISTS (SELECT 1 FROM "StaffGroupAssignments" a WHERE a."AcademyId"=academy AND a."TrainingGroupId"=g."Id" AND a."AcademyMembershipId"=member AND a."IsActive")
                       AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" o WHERE o."AcademyId"=academy AND o."TrainingSessionId"=session AND o."OriginalCoachMembershipId"=member AND o."ActualCoachMembershipId"<>member AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" n WHERE n."AcademyId"=academy AND n."TrainingSessionId"=session AND n."OriginalCoachMembershipId"=member AND n."Sequence">o."Sequence")))
                      OR EXISTS (SELECT 1 FROM "SessionCoachOverrides" o WHERE o."AcademyId"=academy AND o."TrainingSessionId"=session AND o."ActualCoachMembershipId"=member AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" n WHERE n."AcademyId"=academy AND n."TrainingSessionId"=session AND n."OriginalCoachMembershipId"=o."OriginalCoachMembershipId" AND n."Sequence">o."Sequence"))));
                $$;
                CREATE FUNCTION academy_coach_override_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE previous "SessionCoachOverrides";
                BEGIN
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Coach override is immutable' USING ERRCODE='23514'; END IF;
                  SELECT * INTO previous FROM "SessionCoachOverrides" WHERE "AcademyId"=NEW."AcademyId" AND "TrainingSessionId"=NEW."TrainingSessionId" AND "OriginalCoachMembershipId"=NEW."OriginalCoachMembershipId" ORDER BY "Sequence" DESC LIMIT 1;
                  IF NEW."Sequence"<>COALESCE(previous."Sequence",0)+1 OR NEW."PreviousDecisionId" IS DISTINCT FROM previous."Id" THEN RAISE EXCEPTION 'Coach override chain mismatch' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS (SELECT 1 FROM "TrainingSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."TrainingSessionId" AND s."TrainingGroupId"=NEW."TrainingGroupId" AND s."BranchId"=NEW."BranchId" AND s."SportId"=NEW."SportId" AND s."SessionDate"=NEW."SessionDate" AND s."StartTime"=NEW."StartTime" AND s."EndTime"=NEW."EndTime") THEN RAISE EXCEPTION 'Coach override occurrence mismatch' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS (SELECT 1 FROM "StaffGroupAssignments" a JOIN "AcademyMemberships" m ON m."AcademyId"=a."AcademyId" AND m."Id"=a."AcademyMembershipId" AND m."Role"='Coach' AND m."IsActive" WHERE a."AcademyId"=NEW."AcademyId" AND a."TrainingGroupId"=NEW."TrainingGroupId" AND a."AcademyMembershipId"=NEW."OriginalCoachMembershipId" AND a."IsActive") OR NOT EXISTS (SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActualCoachMembershipId" AND m."Role"='Coach' AND m."IsActive") THEN RAISE EXCEPTION 'Coach identity mismatch' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER coach_override_guard BEFORE INSERT OR UPDATE OR DELETE ON "SessionCoachOverrides" FOR EACH ROW EXECUTE FUNCTION academy_coach_override_guard();
                CREATE FUNCTION academy_override_session_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."SessionDate",OLD."StartTime",OLD."EndTime") IS DISTINCT FROM (NEW."SessionDate",NEW."StartTime",NEW."EndTime") AND EXISTS (SELECT 1 FROM "SessionCoachOverrides" o WHERE o."AcademyId"=OLD."AcademyId" AND o."TrainingSessionId"=OLD."Id" AND o."ActualCoachMembershipId"<>o."OriginalCoachMembershipId" AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" n WHERE n."AcademyId"=o."AcademyId" AND n."TrainingSessionId"=o."TrainingSessionId" AND n."OriginalCoachMembershipId"=o."OriginalCoachMembershipId" AND n."Sequence">o."Sequence")) THEN RAISE EXCEPTION 'Restore or cancel the occurrence override before rescheduling' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER override_session_guard BEFORE UPDATE ON "TrainingSessions" FOR EACH ROW EXECUTE FUNCTION academy_override_session_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER override_session_guard ON \"TrainingSessions\"; DROP FUNCTION academy_override_session_guard(); DROP TRIGGER coach_override_guard ON \"SessionCoachOverrides\"; DROP FUNCTION academy_coach_override_guard(); DROP FUNCTION academy_session_staff_assigned(uuid,uuid,uuid);");
            migrationBuilder.DropTable(
                name: "SessionCoachOverrides");
        }
    }
}
