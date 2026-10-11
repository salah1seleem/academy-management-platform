using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2UnassignedSportEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TrainingGroupId",
                table: "SportEnrollments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_AcademyId_PlayerId_SportId_BranchId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "PlayerId", "SportId", "BranchId" },
                unique: true,
                filter: "\"TrainingGroupId\" IS NULL AND \"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_AcademyId_PlayerId_SportId_BranchId",
                table: "SportEnrollments");

            migrationBuilder.AlterColumn<Guid>(
                name: "TrainingGroupId",
                table: "SportEnrollments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
