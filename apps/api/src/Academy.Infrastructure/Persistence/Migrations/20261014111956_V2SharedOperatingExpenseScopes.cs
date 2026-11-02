using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2SharedOperatingExpenseScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OperatingExpense_Terms",
                table: "OperatingExpenses");

            migrationBuilder.AddColumn<Guid>(
                name: "SportId",
                table: "OperatingExpenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_AcademyId_SportId",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OperatingExpense_Terms",
                table: "OperatingExpenses",
                sql: "\"Amount\">0 AND \"PeriodFrom\"<=\"PeriodTo\" AND \"Status\" BETWEEN 1 AND 4 AND ((\"Scope\"=1 AND \"TrainingGroupId\" IS NOT NULL AND \"ResourceId\" IS NULL AND \"SportId\" IS NULL) OR (\"Scope\"=2 AND \"ResourceId\" IS NOT NULL AND \"TrainingGroupId\" IS NULL AND \"SportId\" IS NULL) OR (\"Scope\"=3 AND \"TrainingGroupId\" IS NULL AND \"ResourceId\" IS NULL AND \"SportId\" IS NULL) OR (\"Scope\"=4 AND \"SportId\" IS NOT NULL AND \"TrainingGroupId\" IS NULL AND \"ResourceId\" IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_OperatingExpenses_Sports_AcademyId_SportId",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "SportId" },
                principalTable: "Sports",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OperatingExpenses_Sports_AcademyId_SportId",
                table: "OperatingExpenses");

            migrationBuilder.DropIndex(
                name: "IX_OperatingExpenses_AcademyId_SportId",
                table: "OperatingExpenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OperatingExpense_Terms",
                table: "OperatingExpenses");

            migrationBuilder.DropColumn(
                name: "SportId",
                table: "OperatingExpenses");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OperatingExpense_Terms",
                table: "OperatingExpenses",
                sql: "\"Amount\">0 AND \"PeriodFrom\"<=\"PeriodTo\" AND \"Status\" BETWEEN 1 AND 4 AND ((\"Scope\"=1 AND \"TrainingGroupId\" IS NOT NULL AND \"ResourceId\" IS NULL) OR (\"Scope\"=2 AND \"ResourceId\" IS NOT NULL AND \"TrainingGroupId\" IS NULL))");
        }
    }
}
