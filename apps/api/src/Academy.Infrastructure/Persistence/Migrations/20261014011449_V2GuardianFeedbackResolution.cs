using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2GuardianFeedbackResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerResponse",
                table: "GuardianFeedbacks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RespondedAtUtc",
                table: "GuardianFeedbacks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RespondedByUserId",
                table: "GuardianFeedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_RespondedByUserId",
                table: "GuardianFeedbacks",
                column: "RespondedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianFeedbacks_Users_RespondedByUserId",
                table: "GuardianFeedbacks",
                column: "RespondedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuardianFeedbacks_Users_RespondedByUserId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_GuardianFeedbacks_RespondedByUserId",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "OwnerResponse",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "RespondedAtUtc",
                table: "GuardianFeedbacks");

            migrationBuilder.DropColumn(
                name: "RespondedByUserId",
                table: "GuardianFeedbacks");
        }
    }
}
