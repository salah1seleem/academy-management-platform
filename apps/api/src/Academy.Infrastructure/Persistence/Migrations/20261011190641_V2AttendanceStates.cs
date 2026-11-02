using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2AttendanceStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffAttendance_States",
                table: "StaffAttendances",
                sql: "\"Status\" IN ('NotRecorded','Present','Absent','Late','Excused')");

            migrationBuilder.Sql("""
                CREATE FUNCTION academy_compensation_attended_cancel_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."State"='Cancelled' AND EXISTS (
                    SELECT 1 FROM "PlayerAttendances" a WHERE a."AcademyId"=NEW."AcademyId"
                    AND a."SportEnrollmentId"=NEW."SportEnrollmentId" AND a."TrainingSessionId"=NEW."TrainingSessionId" AND a."Status"='Late')
                  THEN RAISE EXCEPTION 'Correct late attendance before cancelling compensation' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER compensation_attended_cancel_guard BEFORE UPDATE ON "CompensationBookings"
                  FOR EACH ROW EXECUTE FUNCTION academy_compensation_attended_cancel_guard();
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlayerAttendance_States",
                table: "PlayerAttendances",
                sql: "\"Status\" IN ('NotRecorded','Present','Absent','Late','Excused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "PlayerAttendances" WHERE "Status" IN ('Late','Excused'))
                    OR EXISTS (SELECT 1 FROM "StaffAttendances" WHERE "Status" IN ('Late','Excused'))
                  THEN RAISE EXCEPTION 'Review new attendance states before downgrading'; END IF;
                END $$;
                DROP TRIGGER compensation_attended_cancel_guard ON "CompensationBookings";
                DROP FUNCTION academy_compensation_attended_cancel_guard();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffAttendance_States",
                table: "StaffAttendances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlayerAttendance_States",
                table: "PlayerAttendances");
        }
    }
}
