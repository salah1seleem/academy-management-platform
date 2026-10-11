using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CompensationSourceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_compensation_source_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM "SubscriptionPeriods" p WHERE p."AcademyId"=NEW."AcademyId" AND p."Id"=NEW."SourcePeriodId" AND p."SportEnrollmentId"=NEW."SportEnrollmentId")
                    OR NEW."Cause" NOT IN ('CancelledSession','CoachAbsent','ApprovedAbsence')
                  THEN RAISE EXCEPTION 'Compensation source must belong to the same enrollment' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER compensation_source_guard BEFORE INSERT ON "CompensationGrants" FOR EACH ROW EXECUTE FUNCTION academy_compensation_source_guard();
                CREATE FUNCTION academy_compensation_initial_booking_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."State"<>'Reserved' OR NEW."CancelledAtUtc" IS NOT NULL OR NEW."CancelledByUserId" IS NOT NULL OR NOT NEW."IsActive"
                  THEN RAISE EXCEPTION 'A compensation booking starts reserved without cancellation metadata' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER compensation_initial_booking_guard BEFORE INSERT ON "CompensationBookings" FOR EACH ROW EXECUTE FUNCTION academy_compensation_initial_booking_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER compensation_initial_booking_guard ON "CompensationBookings";
                DROP FUNCTION academy_compensation_initial_booking_guard();
                DROP TRIGGER compensation_source_guard ON "CompensationGrants";
                DROP FUNCTION academy_compensation_source_guard();
                """);
        }
    }
}
