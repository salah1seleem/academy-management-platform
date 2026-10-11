using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2SeparatePlatformIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_IdentityUserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAdministrators",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FailedMfaAttempts = table.Column<int>(type: "integer", nullable: false),
                    MfaLockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMfaFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastMfaAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAdministrators", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_PlatformAdministrators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAcademyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Details = table.Column<string>(type: "jsonb", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformAudits_Academies_TargetAcademyId",
                        column: x => x.TargetAcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformAudits_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformMfaUses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformMfaUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformMfaUses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAudits_ActorUserId",
                table: "PlatformAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAudits_TargetAcademyId_AtUtc",
                table: "PlatformAudits",
                columns: new[] { "TargetAcademyId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformMfaUses_UserId_Fingerprint_AtUtc",
                table: "PlatformMfaUses",
                columns: new[] { "UserId", "Fingerprint", "AtUtc" });
            // Preserve only explicit historical operator grants, never infer from Owner.
            // These operators still require the separate password + TOTP login.
            migrationBuilder.Sql("""
                INSERT INTO "PlatformAdministrators" ("UserId", "IsActive", "FailedMfaAttempts")
                SELECT "UserId", "IsActive", 0 FROM "PlatformBrandOperators"
                ON CONFLICT ("UserId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityUserTokens");

            migrationBuilder.DropTable(
                name: "PlatformAdministrators");

            migrationBuilder.DropTable(
                name: "PlatformAudits");

            migrationBuilder.DropTable(
                name: "PlatformMfaUses");
        }
    }
}
