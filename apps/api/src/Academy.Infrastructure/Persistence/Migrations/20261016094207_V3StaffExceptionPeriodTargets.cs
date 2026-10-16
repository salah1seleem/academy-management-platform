using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3StaffExceptionPeriodTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffException_CheckoutTarget",
                table: "StaffAttendanceExceptions");

            migrationBuilder.AddColumn<Guid>(
                name: "AppliedWorkPeriodId",
                table: "StaffAttendanceExceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestTimeZoneId",
                table: "StaffAttendanceExceptions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPeriodSnapshot",
                table: "StaffAttendanceExceptions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetWorkPeriodId",
                table: "StaffAttendanceExceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TargetWorkPeriodVersion",
                table: "StaffAttendanceExceptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_AppliedWorkPeriodId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "AppliedWorkPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_TargetWorkPeriodId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "TargetWorkPeriodId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffException_CheckoutTarget",
                table: "StaffAttendanceExceptions",
                sql: "(\"TargetAttendanceId\" IS NULL AND \"TargetAttendanceVersion\" IS NULL) OR (\"TargetAttendanceId\" IS NOT NULL AND \"TargetAttendanceVersion\" IS NOT NULL AND \"TargetAttendanceVersion\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffException_PeriodTarget",
                table: "StaffAttendanceExceptions",
                sql: "((\"TargetWorkPeriodId\" IS NULL AND \"TargetWorkPeriodVersion\" IS NULL AND \"TargetPeriodSnapshot\" IS NULL)\n  OR (\"Operation\"='CheckOut' AND \"TargetAttendanceId\" IS NOT NULL AND \"TargetWorkPeriodId\" IS NOT NULL\n    AND \"TargetWorkPeriodVersion\">0 AND \"TargetPeriodSnapshot\" IS NOT NULL AND jsonb_typeof(\"TargetPeriodSnapshot\")='object'))\nAND (\"AppliedWorkPeriodId\" IS NULL OR (\"Status\"='Approved' AND \"AttendanceId\" IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAttendanceExceptions_StaffWorkPeriods_AcademyId_Applie~",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "AppliedWorkPeriodId" },
                principalTable: "StaffWorkPeriods",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
              CREATE OR REPLACE FUNCTION academy_staff_exception_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Attendance claims and decisions are immutable' USING ERRCODE='23514'; END IF;
                IF TG_OP='UPDATE' AND (OLD."Status" <> 'Pending' OR NEW."Status"='Pending'
                  OR (to_jsonb(OLD)-ARRAY['Status','ReviewedByUserId','ReviewedByMembershipId','ReviewedAtUtc','ReviewNote','ReviewKey','ReviewHash','AttendanceId','AppliedWorkPeriodId','BeforeSnapshot','AfterSnapshot','UpdatedAtUtc'])
                    IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['Status','ReviewedByUserId','ReviewedByMembershipId','ReviewedAtUtc','ReviewNote','ReviewKey','ReviewHash','AttendanceId','AppliedWorkPeriodId','BeforeSnapshot','AfterSnapshot','UpdatedAtUtc']))
                  THEN RAISE EXCEPTION 'Attendance claim source or final decision cannot change' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" m ON m."Id"=e."MembershipId" AND m."AcademyId"=e."AcademyId"
                  WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."MembershipId" AND m."UserId"=NEW."RequestedByUserId")
                  THEN RAISE EXCEPTION 'Invalid employee claim identity' USING ERRCODE='23514'; END IF;
                IF TG_OP='INSERT' AND NEW."Status" <> 'Pending' THEN RAISE EXCEPTION 'Claim must begin pending' USING ERRCODE='23514'; END IF;
                IF NEW."Status" <> 'Pending' AND NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId"
                  WHERE m."Id"=NEW."ReviewedByMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."ReviewedByUserId"
                    AND m."IsActive" AND u."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                  THEN RAISE EXCEPTION 'Invalid independent reviewer' USING ERRCODE='23514'; END IF;
                IF NEW."Status"='Approved' AND NOT EXISTS(SELECT 1 FROM "StaffDailyAttendances" a
                  WHERE a."Id"=NEW."AttendanceId" AND a."AcademyId"=NEW."AcademyId" AND a."MembershipId"=NEW."MembershipId"
                    AND a."WorkDate"=NEW."WorkDate" AND a."Status"='Present' AND a."RecordedByUserId"=NEW."ReviewedByUserId" AND a."UpdatedAtUtc"=NEW."ReviewedAtUtc")
                  THEN RAISE EXCEPTION 'Claim decision must match applied attendance' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;

              CREATE OR REPLACE FUNCTION academy_staff_checkout_target_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='INSERT' AND NEW."Operation"='CheckOut' AND NEW."TargetAttendanceId" IS NULL THEN
                  RAISE EXCEPTION 'Checkout requires a bound source' USING ERRCODE='23514'; END IF;
                IF NEW."TargetAttendanceId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "StaffDailyAttendances" a WHERE a."Id"=NEW."TargetAttendanceId" AND a."AcademyId"=NEW."AcademyId" AND a."MembershipId"=NEW."MembershipId" AND a."WorkDate"=NEW."WorkDate")
                  THEN RAISE EXCEPTION 'Invalid target identity' USING ERRCODE='23514'; END IF;
                IF TG_OP='INSERT' AND NEW."TargetAttendanceId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "StaffDailyAttendances" a WHERE a."Id"=NEW."TargetAttendanceId" AND a.xmin::text::bigint=NEW."TargetAttendanceVersion" AND a."Status"='Present' AND a."CheckIn" IS NOT NULL
                  AND ((NEW."Operation"='CheckOut' AND a."CheckOut" IS NULL) OR (NEW."Operation"='CheckIn' AND a."CheckOut" IS NOT NULL)))
                  THEN RAISE EXCEPTION 'Target must match its current source version and state' USING ERRCODE='23514'; END IF;
                IF NEW."TargetWorkPeriodId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."Id"=NEW."TargetWorkPeriodId" AND p."AcademyId"=NEW."AcademyId" AND p."AttendanceId"=NEW."TargetAttendanceId" AND p."MembershipId"=NEW."MembershipId" AND p."StartDate"=NEW."WorkDate")
                  THEN RAISE EXCEPTION 'Invalid target period identity' USING ERRCODE='23514'; END IF;
                IF TG_OP='INSERT' AND NEW."TargetWorkPeriodId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."Id"=NEW."TargetWorkPeriodId" AND p.xmin::text::bigint=NEW."TargetWorkPeriodVersion"
                  AND p."EndTime" IS NULL AND p."StartUtc"<NEW."ClaimedAtUtc" AND p."TimeZoneId"=NEW."RequestTimeZoneId"
                  AND (NEW."TargetPeriodSnapshot"->>'id')::uuid=p."Id" AND (NEW."TargetPeriodSnapshot"->>'version')::bigint=NEW."TargetWorkPeriodVersion"
                  AND (NEW."TargetPeriodSnapshot"->>'startUtc')::timestamptz=p."StartUtc" AND (NEW."TargetPeriodSnapshot"->>'startDate')::date=p."StartDate"
                  AND (NEW."TargetPeriodSnapshot"->>'startTime')::time=p."StartTime" AND NEW."TargetPeriodSnapshot"->>'timeZoneId'=p."TimeZoneId")
                  THEN RAISE EXCEPTION 'Claim must preserve the exact open period snapshot' USING ERRCODE='23514'; END IF;
                IF NEW."Status"='Approved' AND NEW."TargetAttendanceId" IS NOT NULL AND NEW."AttendanceId" IS DISTINCT FROM NEW."TargetAttendanceId"
                  THEN RAISE EXCEPTION 'Decision cannot switch its bound day' USING ERRCODE='23514'; END IF;
                IF NEW."Status"='Approved' AND (NEW."TargetWorkPeriodId" IS NOT NULL OR NEW."Operation"='CheckIn' AND NEW."TargetAttendanceId" IS NOT NULL)
                  AND ((NEW."BeforeSnapshot"->>'version')::bigint IS DISTINCT FROM NEW."TargetAttendanceVersion"
                    OR (NEW."TargetWorkPeriodId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(NEW."BeforeSnapshot"->'periods') p
                      WHERE (p->>'id')::uuid=NEW."TargetWorkPeriodId" AND (p->>'version')::bigint=NEW."TargetWorkPeriodVersion")))
                  THEN RAISE EXCEPTION 'Decision must preserve the claimed source version' USING ERRCODE='23514'; END IF;
                IF NEW."Status"='Approved' AND (NEW."TargetWorkPeriodId" IS NOT NULL OR NEW."Operation"='CheckIn' AND NEW."TargetAttendanceId" IS NOT NULL)
                  AND (NEW."AppliedWorkPeriodId" IS NULL OR NOT EXISTS(SELECT 1 FROM "StaffWorkPeriods" p WHERE p."Id"=NEW."AppliedWorkPeriodId" AND p."AttendanceId"=NEW."AttendanceId" AND p."AcademyId"=NEW."AcademyId" AND p."MembershipId"=NEW."MembershipId"
                    AND p."RecordedByUserId"=NEW."ReviewedByUserId" AND p."UpdatedAtUtc"=NEW."ReviewedAtUtc" AND p."TrustSource"='AdminDeclared'
                    AND ((NEW."Operation"='CheckOut' AND p."Id"=NEW."TargetWorkPeriodId" AND p."EndUtc"=NEW."ClaimedAtUtc")
                      OR (NEW."Operation"='CheckIn' AND p."StartUtc"=NEW."ClaimedAtUtc" AND p."EndUtc" IS NULL))))
                  THEN RAISE EXCEPTION 'Decision must apply the exact period and claimed time' USING ERRCODE='23514'; END IF;
                IF NEW."Status"='Approved' AND NEW."Operation"='CheckOut' AND NEW."TargetAttendanceId" IS NOT NULL AND NEW."TargetWorkPeriodId" IS NULL AND NOT EXISTS(SELECT 1 FROM "StaffDailyAttendances" a JOIN "Academies" academy ON academy."Id"=a."AcademyId"
                  WHERE a."Id"=NEW."TargetAttendanceId" AND a."CheckOutDate"=(NEW."ClaimedAtUtc" AT TIME ZONE academy."TimeZone")::date AND a."CheckOut"=(NEW."ClaimedAtUtc" AT TIME ZONE academy."TimeZone")::time)
                  THEN RAISE EXCEPTION 'Legacy checkout must match its bound daily source' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              """);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAttendanceExceptions_StaffWorkPeriods_AcademyId_Target~",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "TargetWorkPeriodId" },
                principalTable: "StaffWorkPeriods",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removing these columns/functions would discard immutable target evidence.
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Period claim evidence migration is forward-only' USING ERRCODE='23514'; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_StaffAttendanceExceptions_StaffWorkPeriods_AcademyId_Applie~",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffAttendanceExceptions_StaffWorkPeriods_AcademyId_Target~",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_AppliedWorkPeriodId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_TargetWorkPeriodId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffException_CheckoutTarget",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffException_PeriodTarget",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "AppliedWorkPeriodId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "RequestTimeZoneId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "TargetPeriodSnapshot",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "TargetWorkPeriodId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "TargetWorkPeriodVersion",
                table: "StaffAttendanceExceptions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffException_CheckoutTarget",
                table: "StaffAttendanceExceptions",
                sql: "(\"TargetAttendanceId\" IS NULL AND \"TargetAttendanceVersion\" IS NULL) OR (\"Operation\"='CheckOut' AND \"TargetAttendanceId\" IS NOT NULL AND \"TargetAttendanceVersion\" IS NOT NULL AND \"TargetAttendanceVersion\" > 0)");
        }
    }
}
