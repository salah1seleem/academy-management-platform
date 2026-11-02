using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PrivateAchievementCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_PlayerLevelReviews_AcademyId_Id_SportEnrollmentId_SportId",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateTable(
                name: "PlayerAchievementCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLevelReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SportName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LevelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MilestoneAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MilestoneOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAchievementCertificates", x => x.Id);
                    table.UniqueConstraint("AK_PlayerAchievementCertificates_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerAchievementCertificates_AcademyId_Id_SportEnrollmentI~", x => new { x.AcademyId, x.Id, x.SportEnrollmentId, x.SportId });
                    table.CheckConstraint("CK_PlayerCertificate_Source", "\"IsActive\" AND length(trim(\"PlayerName\"))>0 AND length(trim(\"LevelName\"))>0 AND \"MilestoneAtUtc\"<=\"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_PlayerAchievementCertificates_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerAchievementCertificates_PlayerLevelReviews_AcademyId_~",
                        columns: x => new { x.AcademyId, x.SourceLevelReviewId, x.SportEnrollmentId, x.SportId },
                        principalTable: "PlayerLevelReviews",
                        principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerAchievementCertificates_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerCertificateWithdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCertificateWithdrawals", x => x.Id);
                    table.UniqueConstraint("AK_PlayerCertificateWithdrawals_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerCertificateWithdrawal_Active", "\"IsActive\"");
                    table.ForeignKey(
                        name: "FK_PlayerCertificateWithdrawals_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerCertificateWithdrawals_PlayerAchievementCertificates_~",
                        columns: x => new { x.AcademyId, x.CertificateId, x.SportEnrollmentId, x.SportId },
                        principalTable: "PlayerAchievementCertificates",
                        principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerCertificateWithdrawals_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievementCertificates_AcademyId_ActorUserId_Request~",
                table: "PlayerAchievementCertificates",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievementCertificates_AcademyId_SourceLevelReviewId",
                table: "PlayerAchievementCertificates",
                columns: new[] { "AcademyId", "SourceLevelReviewId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievementCertificates_AcademyId_SourceLevelReviewId~",
                table: "PlayerAchievementCertificates",
                columns: new[] { "AcademyId", "SourceLevelReviewId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievementCertificates_ActorUserId",
                table: "PlayerAchievementCertificates",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCertificateWithdrawals_AcademyId_ActorUserId_RequestId",
                table: "PlayerCertificateWithdrawals",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCertificateWithdrawals_AcademyId_CertificateId",
                table: "PlayerCertificateWithdrawals",
                columns: new[] { "AcademyId", "CertificateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCertificateWithdrawals_AcademyId_CertificateId_SportE~",
                table: "PlayerCertificateWithdrawals",
                columns: new[] { "AcademyId", "CertificateId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCertificateWithdrawals_ActorUserId",
                table: "PlayerCertificateWithdrawals",
                column: "ActorUserId");
            AddCertificateGuard(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PlayerAchievementCertificates") THEN RAISE EXCEPTION 'Preserve certificates before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_certificate_guard() CASCADE;""");
            migrationBuilder.DropTable(
                name: "PlayerCertificateWithdrawals");

            migrationBuilder.DropTable(
                name: "PlayerAchievementCertificates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PlayerLevelReviews_AcademyId_Id_SportEnrollmentId_SportId",
                table: "PlayerLevelReviews");
        }
    }
}
