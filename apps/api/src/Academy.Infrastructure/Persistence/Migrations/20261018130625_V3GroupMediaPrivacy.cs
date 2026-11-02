using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3GroupMediaPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "MediaEventOn",
                table: "PrivateAssets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaPolicyVersion",
                table: "PrivateAssets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NoRecognizableChildren",
                table: "PrivateAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSubject",
                table: "PrivateAssetAudiences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SportEnrollmentId",
                table: "PrivateAssetAudiences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PrivateAssetAudiences_AcademyId_AssetId_PlayerId",
                table: "PrivateAssetAudiences",
                columns: new[] { "AcademyId", "AssetId", "PlayerId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GuardianPlayerLinks_AcademyId_Id_PlayerId",
                table: "GuardianPlayerLinks",
                columns: new[] { "AcademyId", "Id", "PlayerId" });

            migrationBuilder.CreateTable(
                name: "GroupMediaConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianPlayerLinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_GroupMediaConsents", x => x.Id);
                    table.UniqueConstraint("AK_GroupMediaConsents_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_GroupMediaConsents_AcademyId_Id_PlayerId", x => new { x.AcademyId, x.Id, x.PlayerId });
                    table.CheckConstraint("CK_GroupMediaConsent_Revision", "\"IsActive\" AND \"Revision\">0");
                    table.ForeignKey(
                        name: "FK_GroupMediaConsents_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMediaConsents_GuardianPlayerLinks_AcademyId_GuardianPl~",
                        columns: x => new { x.AcademyId, x.GuardianPlayerLinkId, x.PlayerId },
                        principalTable: "GuardianPlayerLinks",
                        principalColumns: new[] { "AcademyId", "Id", "PlayerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMediaConsents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateMediaConsentBasiss",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaConsentBasiss", x => x.Id);
                    table.UniqueConstraint("AK_PrivateMediaConsentBasiss_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PrivateMediaConsentBasis_Active", "\"IsActive\"");
                    table.ForeignKey(
                        name: "FK_PrivateMediaConsentBasiss_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaConsentBasiss_GroupMediaConsents_AcademyId_Cons~",
                        columns: x => new { x.AcademyId, x.ConsentId, x.PlayerId },
                        principalTable: "GroupMediaConsents",
                        principalColumns: new[] { "AcademyId", "Id", "PlayerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaConsentBasiss_PrivateAssetAudiences_AcademyId_A~",
                        columns: x => new { x.AcademyId, x.AssetId, x.PlayerId },
                        principalTable: "PrivateAssetAudiences",
                        principalColumns: new[] { "AcademyId", "AssetId", "PlayerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateAssetAudiences_AcademyId_SportEnrollmentId",
                table: "PrivateAssetAudiences",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMediaConsents_AcademyId_ActorUserId_RequestId",
                table: "GroupMediaConsents",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMediaConsents_AcademyId_GuardianPlayerLinkId_PlayerId",
                table: "GroupMediaConsents",
                columns: new[] { "AcademyId", "GuardianPlayerLinkId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMediaConsents_AcademyId_GuardianPlayerLinkId_Revision",
                table: "GroupMediaConsents",
                columns: new[] { "AcademyId", "GuardianPlayerLinkId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMediaConsents_ActorUserId",
                table: "GroupMediaConsents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaConsentBasiss_AcademyId_AssetId_ConsentId",
                table: "PrivateMediaConsentBasiss",
                columns: new[] { "AcademyId", "AssetId", "ConsentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaConsentBasiss_AcademyId_AssetId_PlayerId",
                table: "PrivateMediaConsentBasiss",
                columns: new[] { "AcademyId", "AssetId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaConsentBasiss_AcademyId_ConsentId_PlayerId",
                table: "PrivateMediaConsentBasiss",
                columns: new[] { "AcademyId", "ConsentId", "PlayerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PrivateAssetAudiences_SportEnrollments_AcademyId_SportEnrol~",
                table: "PrivateAssetAudiences",
                columns: new[] { "AcademyId", "SportEnrollmentId" },
                principalTable: "SportEnrollments",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            AddPrivacyGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "GroupMediaConsents") OR EXISTS(SELECT 1 FROM "PrivateAssets" WHERE "MediaPolicyVersion"=1) THEN RAISE EXCEPTION 'Preserve group media consent and audience history' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_group_media_guard() CASCADE;""");
            migrationBuilder.DropForeignKey(
                name: "FK_PrivateAssetAudiences_SportEnrollments_AcademyId_SportEnrol~",
                table: "PrivateAssetAudiences");

            migrationBuilder.DropTable(
                name: "PrivateMediaConsentBasiss");

            migrationBuilder.DropTable(
                name: "GroupMediaConsents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PrivateAssetAudiences_AcademyId_AssetId_PlayerId",
                table: "PrivateAssetAudiences");

            migrationBuilder.DropIndex(
                name: "IX_PrivateAssetAudiences_AcademyId_SportEnrollmentId",
                table: "PrivateAssetAudiences");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GuardianPlayerLinks_AcademyId_Id_PlayerId",
                table: "GuardianPlayerLinks");

            migrationBuilder.DropColumn(
                name: "MediaEventOn",
                table: "PrivateAssets");

            migrationBuilder.DropColumn(
                name: "MediaPolicyVersion",
                table: "PrivateAssets");

            migrationBuilder.DropColumn(
                name: "NoRecognizableChildren",
                table: "PrivateAssets");

            migrationBuilder.DropColumn(
                name: "IsSubject",
                table: "PrivateAssetAudiences");

            migrationBuilder.DropColumn(
                name: "SportEnrollmentId",
                table: "PrivateAssetAudiences");
        }
    }
}
