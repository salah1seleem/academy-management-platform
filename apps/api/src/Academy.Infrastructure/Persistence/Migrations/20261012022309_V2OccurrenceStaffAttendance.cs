using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2OccurrenceStaffAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StaffAttendances_StaffGroupAssignments_AcademyId_AcademyMem~",
                table: "StaffAttendances");

            migrationBuilder.DropIndex(
                name: "IX_StaffAttendances_AcademyId_AcademyMembershipId_TrainingGrou~",
                table: "StaffAttendances");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_AcademyId_AcademyMembershipId",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "AcademyMembershipId" });
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_session_staff_assigned(academy uuid, member uuid, session uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
                  SELECT EXISTS (SELECT 1 FROM "TrainingSessions" s
                    JOIN "TrainingGroups" g ON g."AcademyId"=s."AcademyId" AND g."Id"=s."TrainingGroupId" AND g."IsActive"
                    JOIN "AcademyMemberships" m ON m."AcademyId"=s."AcademyId" AND m."Id"=member AND m."IsActive"
                    JOIN "Users" u ON u."Id"=m."UserId" AND u."IsActive"
                    WHERE s."AcademyId"=academy AND s."Id"=session AND s."Status"<>'Cancelled' AND (
                      (EXISTS (SELECT 1 FROM "StaffGroupAssignments" a WHERE a."AcademyId"=academy AND a."TrainingGroupId"=g."Id" AND a."AcademyMembershipId"=member AND a."IsActive")
                       AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" o WHERE o."AcademyId"=academy AND o."TrainingSessionId"=session AND o."OriginalCoachMembershipId"=member AND o."ActualCoachMembershipId"<>member AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" n WHERE n."AcademyId"=academy AND n."TrainingSessionId"=session AND n."OriginalCoachMembershipId"=member AND n."Sequence">o."Sequence")))
                      OR EXISTS (SELECT 1 FROM "SessionCoachOverrides" o WHERE o."AcademyId"=academy AND o."TrainingSessionId"=session AND o."ActualCoachMembershipId"=member AND m."Role"='Coach' AND NOT EXISTS (SELECT 1 FROM "SessionCoachOverrides" n WHERE n."AcademyId"=academy AND n."TrainingSessionId"=session AND n."OriginalCoachMembershipId"=o."OriginalCoachMembershipId" AND n."Sequence">o."Sequence"))));
                $$;
                CREATE FUNCTION academy_staff_occurrence_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' AND NOT academy_session_staff_assigned(NEW."AcademyId",NEW."AcademyMembershipId",NEW."TrainingSessionId") THEN RAISE EXCEPTION 'Staff member is not assigned to occurrence' USING ERRCODE='23514'; END IF;
                  IF TG_OP='UPDATE' AND (OLD."AcademyId",OLD."TrainingSessionId",OLD."TrainingGroupId",OLD."AcademyMembershipId",OLD."CreatedAtUtc") IS DISTINCT FROM (NEW."AcademyId",NEW."TrainingSessionId",NEW."TrainingGroupId",NEW."AcademyMembershipId",NEW."CreatedAtUtc") THEN RAISE EXCEPTION 'Staff attendance subject is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER staff_occurrence_guard BEFORE INSERT OR UPDATE ON "StaffAttendances" FOR EACH ROW EXECUTE FUNCTION academy_staff_occurrence_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM "StaffAttendances" s WHERE NOT EXISTS (SELECT 1 FROM "StaffGroupAssignments" a WHERE a."AcademyId"=s."AcademyId" AND a."AcademyMembershipId"=s."AcademyMembershipId" AND a."TrainingGroupId"=s."TrainingGroupId")) THEN RAISE EXCEPTION 'Rollback cannot discard substitute attendance; restore a backup to a separate database instead'; END IF; END $$;
                DROP TRIGGER staff_occurrence_guard ON "StaffAttendances"; DROP FUNCTION academy_staff_occurrence_guard();
                """);
            migrationBuilder.DropIndex(
                name: "IX_StaffAttendances_AcademyId_AcademyMembershipId",
                table: "StaffAttendances");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_AcademyId_AcademyMembershipId_TrainingGrou~",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" });

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAttendances_StaffGroupAssignments_AcademyId_AcademyMem~",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" },
                principalTable: "StaffGroupAssignments",
                principalColumns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
