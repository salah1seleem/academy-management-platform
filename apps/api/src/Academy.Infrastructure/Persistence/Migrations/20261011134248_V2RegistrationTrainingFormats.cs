using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2RegistrationTrainingFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_AcademyId_PlayerId_SportId_BranchId",
                table: "SportEnrollments");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FormatReviewedAtUtc",
                table: "TrainingGroups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormatReviewedByUserId",
                table: "TrainingGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationFormat",
                table: "TrainingGroups",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationFormat",
                table: "SportEnrollments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingGroups_FormatReviewedByUserId",
                table: "TrainingGroups",
                column: "FormatReviewedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Group_RegistrationFormat",
                table: "TrainingGroups",
                sql: "\"RegistrationFormat\" IN ('Group','Private','SemiPrivate')");

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_AcademyId_PlayerId_SportId_BranchId_Regist~",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "PlayerId", "SportId", "BranchId", "RegistrationFormat" },
                unique: true,
                filter: "\"TrainingGroupId\" IS NULL AND \"IsActive\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Enrollment_RegistrationFormat",
                table: "SportEnrollments",
                sql: "\"RegistrationFormat\" IN ('Group','Private','SemiPrivate')");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingGroups_Users_FormatReviewedByUserId",
                table: "TrainingGroups",
                column: "FormatReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainingGroups_Users_FormatReviewedByUserId",
                table: "TrainingGroups");

            migrationBuilder.DropIndex(
                name: "IX_TrainingGroups_FormatReviewedByUserId",
                table: "TrainingGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Group_RegistrationFormat",
                table: "TrainingGroups");

            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_AcademyId_PlayerId_SportId_BranchId_Regist~",
                table: "SportEnrollments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Enrollment_RegistrationFormat",
                table: "SportEnrollments");

            migrationBuilder.DropColumn(
                name: "FormatReviewedAtUtc",
                table: "TrainingGroups");

            migrationBuilder.DropColumn(
                name: "FormatReviewedByUserId",
                table: "TrainingGroups");

            migrationBuilder.DropColumn(
                name: "RegistrationFormat",
                table: "TrainingGroups");

            migrationBuilder.DropColumn(
                name: "RegistrationFormat",
                table: "SportEnrollments");

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_AcademyId_PlayerId_SportId_BranchId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "PlayerId", "SportId", "BranchId" },
                unique: true,
                filter: "\"TrainingGroupId\" IS NULL AND \"IsActive\"");
        }
    }
}
