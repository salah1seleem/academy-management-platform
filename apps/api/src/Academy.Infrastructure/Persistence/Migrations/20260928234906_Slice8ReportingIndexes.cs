using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice8ReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Players_AcademyId_DateOfBirth",
                table: "Players",
                columns: new[] { "AcademyId", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_AcademyId_Status_CreatedAtUtc",
                table: "PaymentRequests",
                columns: new[] { "AcademyId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_AcademyId_ConfirmedAtUtc",
                table: "Collections",
                columns: new[] { "AcademyId", "ConfirmedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_AcademyId_DateOfBirth",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_AcademyId_Status_CreatedAtUtc",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_Collections_AcademyId_ConfirmedAtUtc",
                table: "Collections");
        }
    }
}
