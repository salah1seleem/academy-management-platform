using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlayerEncouragementBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerBadgeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Criterion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_PlayerBadgeRules", x => x.Id);
                    table.UniqueConstraint("AK_PlayerBadgeRules_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerBadgeRules_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_PlayerBadgeRule_Encouragement", "\"IsActive\" AND \"Points\" BETWEEN 0 AND 1000 AND length(trim(\"Title\"))>0 AND length(trim(\"Criterion\"))>0");
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRules_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRules_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRules_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerBadgeGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_PlayerBadgeGrants", x => x.Id);
                    table.UniqueConstraint("AK_PlayerBadgeGrants_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerBadgeGrants_AcademyId_Id_SportEnrollmentId_SportId", x => new { x.AcademyId, x.Id, x.SportEnrollmentId, x.SportId });
                    table.CheckConstraint("CK_PlayerBadgeGrant_Active", "\"IsActive\"");
                    table.ForeignKey(
                        name: "FK_PlayerBadgeGrants_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeGrants_PlayerBadgeRules_AcademyId_RuleId_SportId",
                        columns: x => new { x.AcademyId, x.RuleId, x.SportId },
                        principalTable: "PlayerBadgeRules",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeGrants_SportEnrollments_AcademyId_SportEnrollmen~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeGrants_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerBadgeRuleRetirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PlayerBadgeRuleRetirements", x => x.Id);
                    table.UniqueConstraint("AK_PlayerBadgeRuleRetirements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerBadgeRetirement_Active", "\"IsActive\"");
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRuleRetirements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRuleRetirements_PlayerBadgeRules_AcademyId_RuleI~",
                        columns: x => new { x.AcademyId, x.RuleId, x.SportId },
                        principalTable: "PlayerBadgeRules",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRuleRetirements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerBadgeRevocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_PlayerBadgeRevocations", x => x.Id);
                    table.UniqueConstraint("AK_PlayerBadgeRevocations_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerBadgeRevocation_Active", "\"IsActive\"");
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRevocations_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRevocations_PlayerBadgeGrants_AcademyId_GrantId_~",
                        columns: x => new { x.AcademyId, x.GrantId, x.SportEnrollmentId, x.SportId },
                        principalTable: "PlayerBadgeGrants",
                        principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerBadgeRevocations_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeGrants_AcademyId_ActorUserId_RequestId",
                table: "PlayerBadgeGrants",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeGrants_AcademyId_RuleId_SportId",
                table: "PlayerBadgeGrants",
                columns: new[] { "AcademyId", "RuleId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeGrants_AcademyId_SportEnrollmentId_RuleId",
                table: "PlayerBadgeGrants",
                columns: new[] { "AcademyId", "SportEnrollmentId", "RuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeGrants_AcademyId_SportEnrollmentId_SportId",
                table: "PlayerBadgeGrants",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeGrants_ActorUserId",
                table: "PlayerBadgeGrants",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRevocations_AcademyId_ActorUserId_RequestId",
                table: "PlayerBadgeRevocations",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRevocations_AcademyId_GrantId",
                table: "PlayerBadgeRevocations",
                columns: new[] { "AcademyId", "GrantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRevocations_AcademyId_GrantId_SportEnrollmentId_~",
                table: "PlayerBadgeRevocations",
                columns: new[] { "AcademyId", "GrantId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRevocations_ActorUserId",
                table: "PlayerBadgeRevocations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRuleRetirements_AcademyId_ActorUserId_RequestId",
                table: "PlayerBadgeRuleRetirements",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRuleRetirements_AcademyId_RuleId",
                table: "PlayerBadgeRuleRetirements",
                columns: new[] { "AcademyId", "RuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRuleRetirements_AcademyId_RuleId_SportId",
                table: "PlayerBadgeRuleRetirements",
                columns: new[] { "AcademyId", "RuleId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRuleRetirements_ActorUserId",
                table: "PlayerBadgeRuleRetirements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRules_AcademyId_ActorUserId_RequestId",
                table: "PlayerBadgeRules",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRules_AcademyId_SportId",
                table: "PlayerBadgeRules",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBadgeRules_ActorUserId",
                table: "PlayerBadgeRules",
                column: "ActorUserId");
            AddBadgeGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PlayerBadgeRules") THEN RAISE EXCEPTION 'Preserve badge source and history before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_badge_guard() CASCADE;""");
            migrationBuilder.DropTable(
                name: "PlayerBadgeRevocations");

            migrationBuilder.DropTable(
                name: "PlayerBadgeRuleRetirements");

            migrationBuilder.DropTable(
                name: "PlayerBadgeGrants");

            migrationBuilder.DropTable(
                name: "PlayerBadgeRules");
        }
    }
}
