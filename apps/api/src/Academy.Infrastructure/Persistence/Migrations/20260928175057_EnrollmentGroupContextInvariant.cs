using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrollmentGroupContextInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SportEnrollments_TrainingGroups_AcademyId_TrainingGroupId",
                table: "SportEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_AcademyId_TrainingGroupId",
                table: "SportEnrollments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TrainingGroups_AcademyId_Id_BranchId_SportId",
                table: "TrainingGroups",
                columns: new[] { "AcademyId", "Id", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_AcademyId_TrainingGroupId_BranchId_SportId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "TrainingGroupId", "BranchId", "SportId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SportEnrollments_TrainingGroups_AcademyId_TrainingGroupId_B~",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "TrainingGroupId", "BranchId", "SportId" },
                principalTable: "TrainingGroups",
                principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SportEnrollments_TrainingGroups_AcademyId_TrainingGroupId_B~",
                table: "SportEnrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TrainingGroups_AcademyId_Id_BranchId_SportId",
                table: "TrainingGroups");

            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_AcademyId_TrainingGroupId_BranchId_SportId",
                table: "SportEnrollments");

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_AcademyId_TrainingGroupId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SportEnrollments_TrainingGroups_AcademyId_TrainingGroupId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "TrainingGroupId" },
                principalTable: "TrainingGroups",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
