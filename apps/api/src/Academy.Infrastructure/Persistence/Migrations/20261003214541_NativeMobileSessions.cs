using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeMobileSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId",
                table: "AcademyMemberships");

            migrationBuilder.CreateTable(
                name: "MobileOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumberNormalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobileSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccessTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccessExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobileSessions_AcademyMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "AcademyMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobileSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileOtpChallenges_PhoneNumberNormalized_CreatedAtUtc",
                table: "MobileOtpChallenges",
                columns: new[] { "PhoneNumberNormalized", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MobileSessions_AccessTokenHash",
                table: "MobileSessions",
                column: "AccessTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileSessions_MembershipId",
                table: "MobileSessions",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileSessions_RefreshTokenHash",
                table: "MobileSessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileSessions_UserId_RevokedAtUtc",
                table: "MobileSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobileOtpChallenges");

            migrationBuilder.DropTable(
                name: "MobileSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId" },
                unique: true);
        }
    }
}
