using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PreservedStaffWorkPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffWorkPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TrustSource = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffWorkPeriods", x => x.Id);
                    table.UniqueConstraint("AK_StaffWorkPeriods_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_StaffWorkPeriod_Bounds", "\"IsActive\" AND ((\"EndDate\" IS NULL AND \"EndTime\" IS NULL) OR (\"EndDate\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND (\"EndDate\"+\"EndTime\") >= (\"StartDate\"+\"StartTime\")))\nAND ((\"TrustSource\"='LegacyLocal' AND \"StartUtc\" IS NULL AND \"EndUtc\" IS NULL AND \"TimeZoneId\" IS NULL)\n  OR (\"TrustSource\"='AdminDeclared' AND \"StartUtc\" IS NOT NULL AND \"TimeZoneId\" IS NOT NULL AND ((\"EndTime\" IS NULL AND \"EndUtc\" IS NULL) OR (\"EndTime\" IS NOT NULL AND \"EndUtc\" IS NOT NULL AND \"EndUtc\" >= \"StartUtc\"))))");
                    table.ForeignKey(
                        name: "FK_StaffWorkPeriods_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkPeriods_AcademyMemberships_AcademyId_MembershipId",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkPeriods_StaffDailyAttendances_AcademyId_Attendance~",
                        columns: x => new { x.AcademyId, x.AttendanceId },
                        principalTable: "StaffDailyAttendances",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkPeriods_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkPeriods_AcademyId_AttendanceId_StartTime",
                table: "StaffWorkPeriods",
                columns: new[] { "AcademyId", "AttendanceId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkPeriods_AcademyId_MembershipId",
                table: "StaffWorkPeriods",
                columns: new[] { "AcademyId", "MembershipId" },
                unique: true,
                filter: "\"EndTime\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkPeriods_RecordedByUserId",
                table: "StaffWorkPeriods",
                column: "RecordedByUserId");
            migrationBuilder.Sql("""
              CREATE FUNCTION academy_staff_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Preserved work periods cannot be deleted' USING ERRCODE='23514'; END IF;
                PERFORM pg_advisory_xact_lock(hashtextextended('staff-open:'||NEW."AcademyId"::text||':'||NEW."MembershipId"::text,0));
                IF TG_OP='UPDATE' AND (to_jsonb(OLD)-ARRAY['StartTime','EndDate','EndTime','StartUtc','EndUtc','TimeZoneId','TrustSource','RecordedByUserId','UpdatedAtUtc']) IS DISTINCT FROM
                  (to_jsonb(NEW)-ARRAY['StartTime','EndDate','EndTime','StartUtc','EndUtc','TimeZoneId','TrustSource','RecordedByUserId','UpdatedAtUtc'])
                  THEN RAISE EXCEPTION 'Work period identity is immutable' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS(SELECT 1 FROM "StaffDailyAttendances" a WHERE a."Id"=NEW."AttendanceId" AND a."AcademyId"=NEW."AcademyId" AND a."MembershipId"=NEW."MembershipId" AND a."WorkDate"=NEW."StartDate")
                  THEN RAISE EXCEPTION 'Period must belong to its daily source' USING ERRCODE='23514'; END IF;
                IF NEW."TrustSource"='AdminDeclared' AND (NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."RecordedByUserId" AND m."IsActive" AND u."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                  OR (NEW."StartUtc" AT TIME ZONE NEW."TimeZoneId") <> NEW."StartDate"+NEW."StartTime"
                  OR (NEW."EndTime" IS NOT NULL AND (NEW."EndUtc" AT TIME ZONE NEW."TimeZoneId") <> NEW."EndDate"+NEW."EndTime"))
                  THEN RAISE EXCEPTION 'Invalid administrative period time or actor' USING ERRCODE='23514'; END IF;
                IF EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."AcademyId"=NEW."AcademyId" AND p."MembershipId"=NEW."MembershipId" AND p."Id"<>NEW."Id"
                  AND CASE WHEN p."StartUtc" IS NOT NULL AND NEW."StartUtc" IS NOT NULL THEN NEW."StartUtc"<COALESCE(p."EndUtc",'infinity'::timestamptz) AND p."StartUtc"<COALESCE(NEW."EndUtc",'infinity'::timestamptz)
                    ELSE NEW."StartDate"+NEW."StartTime"<COALESCE(p."EndDate"+p."EndTime",'infinity'::timestamp) AND p."StartDate"+p."StartTime"<COALESCE(NEW."EndDate"+NEW."EndTime",'infinity'::timestamp) END)
                  OR EXISTS(SELECT 1 FROM "StaffDailyAttendances" a WHERE a."AcademyId"=NEW."AcademyId" AND a."MembershipId"=NEW."MembershipId" AND a."Id"<>NEW."AttendanceId" AND a."Status"='Present' AND a."CheckIn" IS NOT NULL
                    AND NOT EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=a."Id")
                    AND NEW."StartDate"+NEW."StartTime"<COALESCE(COALESCE(a."CheckOutDate",a."WorkDate")+a."CheckOut",'infinity'::timestamp)
                    AND a."WorkDate"+a."CheckIn"<COALESCE(NEW."EndDate"+NEW."EndTime",'infinity'::timestamp))
                  THEN RAISE EXCEPTION 'Work periods overlap' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER staff_period_guard BEFORE INSERT OR UPDATE OR DELETE ON "StaffWorkPeriods" FOR EACH ROW EXECUTE FUNCTION academy_staff_period_guard();

              CREATE FUNCTION academy_staff_period_source_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              DECLARE sid uuid; aid uuid; a record; evidence jsonb; parts jsonb;
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_TABLE_NAME='StaffWorkPeriods' THEN sid=NEW."AttendanceId"; ELSE sid=NEW."Id"; END IF; aid=NEW."AcademyId";
                IF NOT EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=sid AND p."AcademyId"=aid) THEN RETURN NEW; END IF;
                SELECT d.*,d.xmin::text::bigint AS source_version INTO a FROM "StaffDailyAttendances" d WHERE d."Id"=sid AND d."AcademyId"=aid;
                IF a."Status"<>'Present' OR a."CheckIn" IS DISTINCT FROM (SELECT min(p."StartTime") FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=sid)
                  OR a."CheckOut" IS DISTINCT FROM (SELECT p."EndTime" FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=sid ORDER BY p."EndDate" DESC NULLS FIRST,p."EndTime" DESC NULLS FIRST LIMIT 1)
                  OR a."CheckOutDate" IS DISTINCT FROM (SELECT p."EndDate" FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=sid ORDER BY p."EndDate" DESC NULLS FIRST,p."EndTime" DESC NULLS FIRST LIMIT 1)
                  THEN RAISE EXCEPTION 'Daily source must summarize all preserved periods without deleting them' USING ERRCODE='23514'; END IF;
                SELECT c."AfterSnapshot" INTO evidence FROM "StaffAttendanceChanges" c WHERE c."AttendanceId"=sid AND c."AcademyId"=aid AND c."AfterVersion"=a.source_version AND c."ActorUserId"=a."RecordedByUserId" AND c."CreatedAtUtc"=a."UpdatedAtUtc" ORDER BY c."CreatedAtUtc" DESC LIMIT 1;
                parts=CASE WHEN jsonb_typeof(evidence->'periods')='array' THEN evidence->'periods' ELSE '[]'::jsonb END;
                IF jsonb_array_length(parts)<>(SELECT count(*) FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=sid)
                  OR EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."AttendanceId"=sid AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(parts) v WHERE (v->>'id')::uuid=p."Id" AND (v->>'version')::bigint=p.xmin::text::bigint
                    AND (v->>'startDate')::date=p."StartDate" AND (v->>'startTime')::time=p."StartTime" AND (v->>'endDate')::date IS NOT DISTINCT FROM p."EndDate" AND (v->>'endTime')::time IS NOT DISTINCT FROM p."EndTime"
                    AND (v->>'startUtc')::timestamptz IS NOT DISTINCT FROM p."StartUtc" AND (v->>'endUtc')::timestamptz IS NOT DISTINCT FROM p."EndUtc" AND v->>'trustSource'=p."TrustSource" AND v->>'timeZoneId' IS NOT DISTINCT FROM p."TimeZoneId"))
                  THEN RAISE EXCEPTION 'Period changes require matching immutable before/after evidence' USING ERRCODE='23514'; END IF;
                IF TG_TABLE_NAME='StaffWorkPeriods' AND TG_OP='INSERT' THEN
                  IF NEW."TrustSource"='LegacyLocal' AND NOT EXISTS(SELECT 1 FROM "StaffAttendanceChanges" c WHERE c."AttendanceId"=sid AND c."AfterVersion"=a.source_version
                    AND (c."BeforeSnapshot"->>'workDate')::date=NEW."StartDate" AND (c."BeforeSnapshot"->>'checkIn')::time=NEW."StartTime"
                    AND (c."BeforeSnapshot"->>'checkOut')::time IS NOT DISTINCT FROM NEW."EndTime" AND COALESCE((c."BeforeSnapshot"->>'checkOutDate')::date,(c."BeforeSnapshot"->>'workDate')::date) IS NOT DISTINCT FROM NEW."EndDate")
                    THEN RAISE EXCEPTION 'Legacy interval must preserve the original daily bounds' USING ERRCODE='23514'; END IF;
                END IF;
                RETURN NEW;
              END $$;
              CREATE CONSTRAINT TRIGGER staff_period_source_guard AFTER INSERT OR UPDATE ON "StaffWorkPeriods" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_staff_period_source_guard();
              CREATE CONSTRAINT TRIGGER staff_daily_period_source_guard AFTER INSERT OR UPDATE ON "StaffDailyAttendances" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_staff_period_source_guard();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"StaffWorkPeriods\") THEN RAISE EXCEPTION 'Cannot remove preserved work periods' USING ERRCODE='23514'; END IF; END $$; DROP TRIGGER staff_daily_period_source_guard ON \"StaffDailyAttendances\";");
            migrationBuilder.DropTable(
                name: "StaffWorkPeriods");
            migrationBuilder.Sql("DROP FUNCTION academy_staff_period_guard(); DROP FUNCTION academy_staff_period_source_guard();");
        }
    }
}
