using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2GuardianFeedbackDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueOn",
                table: "GuardianFeedbacks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueSetAtUtc",
                table: "GuardianFeedbacks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DueSetByUserId",
                table: "GuardianFeedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_DueSetByUserId",
                table: "GuardianFeedbacks",
                column: "DueSetByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianFeedbacks_Users_DueSetByUserId",
                table: "GuardianFeedbacks",
                column: "DueSetByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuardianFeedbacks_Users_DueSetByUserId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_GuardianFeedbacks_DueSetByUserId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "DueOn",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "DueSetAtUtc",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "DueSetByUserId",
                table: "GuardianFeedbacks");
        }
    }
}
