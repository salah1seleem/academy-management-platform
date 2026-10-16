using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3BranchStaffAttendancePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StaffCheckInAdminRequired",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StaffCheckInChallengeSeconds",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<bool>(
                name: "StaffCheckInCoachRequired",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StaffCheckInEnabled",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "StaffCheckInMaxAccuracyMeters",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "StaffCheckInMaxAgeSeconds",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "StaffCheckInOwnerRequired",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StaffCheckInRadiusMeters",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<Guid>(
                name: "StaffCheckInReviewerMembershipId",
                table: "Branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterPolicy",
                table: "BranchCheckInSiteAudits",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforePolicy",
                table: "BranchCheckInSiteAudits",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_AcademyId_StaffCheckInReviewerMembershipId",
                table: "Branches",
                columns: new[] { "AcademyId", "StaffCheckInReviewerMembershipId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Branch_StaffCheckInPolicy",
                table: "Branches",
                sql: "\"StaffCheckInRadiusMeters\" BETWEEN 10 AND 500 AND \"StaffCheckInMaxAccuracyMeters\" BETWEEN 1 AND 100 AND \"StaffCheckInMaxAccuracyMeters\" <= \"StaffCheckInRadiusMeters\" AND \"StaffCheckInMaxAgeSeconds\" BETWEEN 5 AND 60 AND \"StaffCheckInChallengeSeconds\" BETWEEN 30 AND 120");

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_AcademyMemberships_AcademyId_StaffCheckInReviewerM~",
                table: "Branches",
                columns: new[] { "AcademyId", "StaffCheckInReviewerMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                DO $patch$
                DECLARE original text; revised text;
                BEGIN
                  original=pg_get_functiondef('academy_signed_attendance_evidence_guard()'::regprocedure);
                  revised=replace(original,'b."IsActive" AND b."CheckInLatitude"','b."IsActive" AND b."StaffCheckInEnabled" AND b."CheckInLatitude"');
                  IF revised=original THEN RAISE EXCEPTION 'Unexpected signed evidence guard baseline'; END IF;
                  EXECUTE revised;
                END $patch$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Policy history and signed attendance require a reviewed forward migration; automatic downgrade is blocked'; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_AcademyMemberships_AcademyId_StaffCheckInReviewerM~",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_AcademyId_StaffCheckInReviewerMembershipId",
                table: "Branches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Branch_StaffCheckInPolicy",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInAdminRequired",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInChallengeSeconds",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInCoachRequired",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInEnabled",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInMaxAccuracyMeters",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInMaxAgeSeconds",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInOwnerRequired",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInRadiusMeters",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "StaffCheckInReviewerMembershipId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "AfterPolicy",
                table: "BranchCheckInSiteAudits");

            migrationBuilder.DropColumn(
                name: "BeforePolicy",
                table: "BranchCheckInSiteAudits");
        }
    }
}
