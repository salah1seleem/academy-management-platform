using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3OvernightStaffCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CheckOutDate",
                table: "StaffDailyAttendances",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetAttendanceId",
                table: "StaffAttendanceExceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TargetAttendanceVersion",
                table: "StaffAttendanceExceptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_TargetAttendanceId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "TargetAttendanceId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffException_CheckoutTarget",
                table: "StaffAttendanceExceptions",
                sql: "(\"TargetAttendanceId\" IS NULL AND \"TargetAttendanceVersion\" IS NULL) OR (\"Operation\"='CheckOut' AND \"TargetAttendanceId\" IS NOT NULL AND \"TargetAttendanceVersion\" IS NOT NULL AND \"TargetAttendanceVersion\" > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAttendanceExceptions_StaffDailyAttendances_AcademyId_T~",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "TargetAttendanceId" },
                principalTable: "StaffDailyAttendances",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
              UPDATE "StaffDailyAttendances" SET "CheckOutDate"="WorkDate"
                WHERE "Status"='Present' AND "CheckOut" IS NOT NULL;
              ALTER TABLE "StaffDailyAttendances" ADD CONSTRAINT "CK_StaffDaily_CheckoutDate"
                CHECK ("CheckOutDate" IS NULL OR ("Status"='Present' AND "CheckIn" IS NOT NULL
                  AND "CheckOut" IS NOT NULL AND "CheckOutDate">="WorkDate"
                  AND ("CheckOutDate">"WorkDate" OR "CheckOut">="CheckIn")));
              CREATE FUNCTION academy_staff_checkout_target_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='INSERT' AND NEW."Operation"='CheckOut' AND NEW."TargetAttendanceId" IS NULL THEN
                  RAISE EXCEPTION 'Checkout claim requires a bound open attendance source' USING ERRCODE='23514'; END IF;
                IF NEW."TargetAttendanceId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "StaffDailyAttendances" a
                  WHERE a."Id"=NEW."TargetAttendanceId" AND a."AcademyId"=NEW."AcademyId"
                    AND a."MembershipId"=NEW."MembershipId" AND a."WorkDate"=NEW."WorkDate") THEN
                  RAISE EXCEPTION 'Invalid checkout source identity' USING ERRCODE='23514'; END IF;
                IF TG_OP='INSERT' AND NEW."TargetAttendanceId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "StaffDailyAttendances" a
                  WHERE a."Id"=NEW."TargetAttendanceId" AND a."Status"='Present' AND a."CheckIn" IS NOT NULL
                    AND a."CheckOut" IS NULL AND a.xmin::text::bigint=NEW."TargetAttendanceVersion") THEN
                  RAISE EXCEPTION 'Checkout source must be open at the claimed version' USING ERRCODE='23514'; END IF;
                IF NEW."Status"='Approved' AND NEW."TargetAttendanceId" IS NOT NULL AND (NEW."AttendanceId" IS DISTINCT FROM NEW."TargetAttendanceId"
                  OR NOT EXISTS (SELECT 1 FROM "StaffDailyAttendances" a JOIN "Academies" academy ON academy."Id"=a."AcademyId"
                    WHERE a."Id"=NEW."TargetAttendanceId" AND a."CheckOutDate"=(NEW."ClaimedAtUtc" AT TIME ZONE academy."TimeZone")::date
                      AND a."CheckOut"=(NEW."ClaimedAtUtc" AT TIME ZONE academy."TimeZone")::time)) THEN
                  RAISE EXCEPTION 'Approved checkout must match the bound source and claimed local date' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER staff_checkout_target_guard BEFORE INSERT OR UPDATE ON "StaffAttendanceExceptions"
                FOR EACH ROW EXECUTE FUNCTION academy_staff_checkout_target_guard();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              DO $$ BEGIN IF EXISTS(SELECT 1 FROM "StaffAttendanceExceptions" WHERE "TargetAttendanceId" IS NOT NULL)
                OR EXISTS(SELECT 1 FROM "StaffDailyAttendances" WHERE "CheckOutDate">"WorkDate") THEN
                RAISE EXCEPTION 'Cannot remove bound or overnight attendance evidence' USING ERRCODE='23514'; END IF; END $$;
              DROP TRIGGER staff_checkout_target_guard ON "StaffAttendanceExceptions";
              DROP FUNCTION academy_staff_checkout_target_guard();
              ALTER TABLE "StaffDailyAttendances" DROP CONSTRAINT "CK_StaffDaily_CheckoutDate";
              """);
            migrationBuilder.DropForeignKey(
                name: "FK_StaffAttendanceExceptions_StaffDailyAttendances_AcademyId_T~",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_TargetAttendanceId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffException_CheckoutTarget",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "CheckOutDate",
                table: "StaffDailyAttendances");

            migrationBuilder.DropColumn(
                name: "TargetAttendanceId",
                table: "StaffAttendanceExceptions");

            migrationBuilder.DropColumn(
                name: "TargetAttendanceVersion",
                table: "StaffAttendanceExceptions");
        }
    }
}
