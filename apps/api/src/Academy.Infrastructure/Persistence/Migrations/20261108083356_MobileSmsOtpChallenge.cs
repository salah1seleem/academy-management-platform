using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MobileSmsOtpChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProviderUserId",
                table: "MobileOtpChallenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderVerificationSid",
                table: "MobileOtpChallenges",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderUserId",
                table: "MobileOtpChallenges");

            migrationBuilder.DropColumn(
                name: "ProviderVerificationSid",
                table: "MobileOtpChallenges");
        }
    }
}
