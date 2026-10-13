using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ExpenseServiceAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OperatingExpenseAllocations_AcademyId_ExpenseId_TrainingGro~",
                table: "OperatingExpenseAllocations");

            migrationBuilder.AddColumn<string>(
                name: "ServiceKind",
                table: "OperatingExpenseAllocations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Training");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenseAllocations_AcademyId_ExpenseId_ServiceKind~",
                table: "OperatingExpenseAllocations",
                columns: new[] { "AcademyId", "ExpenseId", "ServiceKind", "TrainingGroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OperatingExpenseAllocations_AcademyId_ExpenseId_ServiceKind~",
                table: "OperatingExpenseAllocations");

            migrationBuilder.DropColumn(
                name: "ServiceKind",
                table: "OperatingExpenseAllocations");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenseAllocations_AcademyId_ExpenseId_TrainingGro~",
                table: "OperatingExpenseAllocations",
                columns: new[] { "AcademyId", "ExpenseId", "TrainingGroupId" },
                unique: true);
        }
    }
}
