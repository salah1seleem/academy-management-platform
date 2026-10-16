using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3StaffAttendanceChangeEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffAttendanceChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BeforeVersion = table.Column<long>(type: "bigint", nullable: false),
                    AfterVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AfterSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendanceChanges", x => x.Id);
                    table.UniqueConstraint("AK_StaffAttendanceChanges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_StaffAttendanceChange_Evidence", "\"IsActive\" AND \"BeforeVersion\" >= 0 AND \"AfterVersion\" > 0 AND length(\"RequestKey\") BETWEEN 8 AND 100 AND length(\"RequestHash\")=64 AND jsonb_typeof(\"AfterSnapshot\")='object' AND jsonb_typeof(\"BeforeSnapshot\") IN ('null','object')");
                    table.ForeignKey(
                        name: "FK_StaffAttendanceChanges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceChanges_AcademyMemberships_AcademyId_ActorMe~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceChanges_AcademyMemberships_AcademyId_Members~",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceChanges_StaffDailyAttendances_AcademyId_Atte~",
                        columns: x => new { x.AcademyId, x.AttendanceId },
                        principalTable: "StaffDailyAttendances",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceChanges_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceChanges_AcademyId_ActorMembershipId",
                table: "StaffAttendanceChanges",
                columns: new[] { "AcademyId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceChanges_AcademyId_ActorUserId_RequestKey",
                table: "StaffAttendanceChanges",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceChanges_AcademyId_AttendanceId",
                table: "StaffAttendanceChanges",
                columns: new[] { "AcademyId", "AttendanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceChanges_AcademyId_MembershipId_WorkDate_Crea~",
                table: "StaffAttendanceChanges",
                columns: new[] { "AcademyId", "MembershipId", "WorkDate", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceChanges_ActorUserId",
                table: "StaffAttendanceChanges",
                column: "ActorUserId");
            migrationBuilder.Sql("""
              CREATE FUNCTION academy_staff_attendance_change_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Manual attendance evidence is immutable' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId"
                  WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."UserId"=NEW."ActorUserId"
                    AND m."IsActive" AND u."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                  THEN RAISE EXCEPTION 'Invalid attendance editor' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS(SELECT 1 FROM "StaffDailyAttendances" a WHERE a."AcademyId"=NEW."AcademyId" AND a."Id"=NEW."AttendanceId"
                  AND a."MembershipId"=NEW."MembershipId" AND a."WorkDate"=NEW."WorkDate" AND a.xmin::text::bigint=NEW."AfterVersion"
                  AND a."RecordedByUserId"=NEW."ActorUserId" AND a."UpdatedAtUtc"=NEW."CreatedAtUtc"
                  AND NEW."AfterSnapshot"->>'id'=a."Id"::text AND NEW."AfterSnapshot"->>'status'=a."Status"
                  AND (NEW."AfterSnapshot"->>'checkIn')::time IS NOT DISTINCT FROM a."CheckIn"
                  AND (NEW."AfterSnapshot"->>'checkOut')::time IS NOT DISTINCT FROM a."CheckOut"
                  AND (NEW."AfterSnapshot"->>'checkOutDate')::date IS NOT DISTINCT FROM a."CheckOutDate"
                  AND (NEW."AfterSnapshot"->>'approvedDeduction')::numeric=a."ApprovedDeduction"
                  AND (NEW."AfterSnapshot"->>'version')::bigint=NEW."AfterVersion"
                  AND (NEW."AfterSnapshot"->>'workDate')::date=NEW."WorkDate"
                  AND ((NEW."BeforeVersion"=0 AND NEW."BeforeSnapshot"='null'::jsonb)
                    OR (NEW."BeforeVersion">0 AND (NEW."BeforeSnapshot"->>'id')::uuid=NEW."AttendanceId"
                      AND (NEW."BeforeSnapshot"->>'version')::bigint=NEW."BeforeVersion")))
                  THEN RAISE EXCEPTION 'Attendance edit evidence must match applied source' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER staff_attendance_change_guard BEFORE INSERT OR UPDATE OR DELETE ON "StaffAttendanceChanges"
                FOR EACH ROW EXECUTE FUNCTION academy_staff_attendance_change_guard();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"StaffAttendanceChanges\") THEN RAISE EXCEPTION 'Cannot remove recorded manual attendance evidence' USING ERRCODE='23514'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "StaffAttendanceChanges");
            migrationBuilder.Sql("DROP FUNCTION academy_staff_attendance_change_guard();");
        }
    }
}
