using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3StaffAttendanceExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffAttendanceExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttendanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    AfterSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendanceExceptions", x => x.Id);
                    table.UniqueConstraint("AK_StaffAttendanceExceptions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_StaffException_Decision", "\"IsActive\" AND \"Operation\" IN ('CheckIn','CheckOut') AND \"Status\" IN ('Pending','Approved','Rejected')\nAND length(trim(\"Reason\")) BETWEEN 5 AND 500 AND \"ClaimedAtUtc\" <= \"CreatedAtUtc\"\nAND ((\"Status\"='Pending' AND \"ReviewedByUserId\" IS NULL AND \"ReviewedByMembershipId\" IS NULL\n  AND \"ReviewedAtUtc\" IS NULL AND \"ReviewKey\" IS NULL AND \"ReviewHash\" IS NULL AND \"ReviewNote\" IS NULL\n  AND \"AttendanceId\" IS NULL AND \"BeforeSnapshot\" IS NULL AND \"AfterSnapshot\" IS NULL)\nOR (\"Status\" <> 'Pending' AND \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedByUserId\" <> \"RequestedByUserId\"\n  AND \"ReviewedByMembershipId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedAtUtc\" >= \"CreatedAtUtc\"\n  AND \"ReviewKey\" IS NOT NULL AND \"ReviewHash\" IS NOT NULL\n  AND ((\"Status\"='Approved' AND \"AttendanceId\" IS NOT NULL AND \"BeforeSnapshot\" IS NOT NULL AND \"AfterSnapshot\" IS NOT NULL)\n    OR (\"Status\"='Rejected' AND \"ReviewNote\" IS NOT NULL AND length(trim(\"ReviewNote\")) BETWEEN 5 AND 500\n      AND \"AttendanceId\" IS NULL AND \"BeforeSnapshot\" IS NULL AND \"AfterSnapshot\" IS NULL))))");
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_AcademyMemberships_AcademyId_Memb~",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_AcademyMemberships_AcademyId_Revi~",
                        columns: x => new { x.AcademyId, x.ReviewedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_Employees_AcademyId_EmployeeId",
                        columns: x => new { x.AcademyId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_StaffDailyAttendances_AcademyId_A~",
                        columns: x => new { x.AcademyId, x.AttendanceId },
                        principalTable: "StaffDailyAttendances",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceExceptions_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_AttendanceId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "AttendanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_BranchId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_EmployeeId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_MembershipId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "MembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_RequestedByUserId_Reque~",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "RequestedByUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_ReviewedByMembershipId",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "ReviewedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_ReviewedByUserId_Review~",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "ReviewedByUserId", "ReviewKey" },
                unique: true,
                filter: "\"ReviewKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_AcademyId_Status_BranchId_WorkDate",
                table: "StaffAttendanceExceptions",
                columns: new[] { "AcademyId", "Status", "BranchId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_RequestedByUserId",
                table: "StaffAttendanceExceptions",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceExceptions_ReviewedByUserId",
                table: "StaffAttendanceExceptions",
                column: "ReviewedByUserId");
            migrationBuilder.Sql("""
              CREATE OR REPLACE FUNCTION academy_staff_exception_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Attendance claims and decisions are immutable' USING ERRCODE='23514'; END IF;
                IF TG_OP='UPDATE' AND (OLD."Status" <> 'Pending' OR NEW."Status"='Pending'
                  OR (to_jsonb(OLD)-ARRAY['Status','ReviewedByUserId','ReviewedByMembershipId','ReviewedAtUtc','ReviewNote','ReviewKey','ReviewHash','AttendanceId','BeforeSnapshot','AfterSnapshot','UpdatedAtUtc'])
                    IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['Status','ReviewedByUserId','ReviewedByMembershipId','ReviewedAtUtc','ReviewNote','ReviewKey','ReviewHash','AttendanceId','BeforeSnapshot','AfterSnapshot','UpdatedAtUtc']))
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
                    AND a."WorkDate"=NEW."WorkDate" AND a."Status"='Present' AND a."RecordedByUserId"=NEW."ReviewedByUserId"
                    AND a."UpdatedAtUtc"=NEW."ReviewedAtUtc")
                  THEN RAISE EXCEPTION 'Claim decision must match applied attendance' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER staff_exception_guard BEFORE INSERT OR UPDATE OR DELETE ON "StaffAttendanceExceptions"
                FOR EACH ROW EXECUTE FUNCTION academy_staff_exception_guard();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              DO $$ BEGIN IF EXISTS(SELECT 1 FROM "StaffAttendanceExceptions") THEN
                RAISE EXCEPTION 'Cannot remove recorded attendance claim evidence' USING ERRCODE='23514'; END IF; END $$;
              """);
            migrationBuilder.DropTable(
                name: "StaffAttendanceExceptions");
            migrationBuilder.Sql("DROP FUNCTION academy_staff_exception_guard();");
        }
    }
}
