using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchResultsAndScopedCoach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CoachCanManageMatches",
                table: "MatchTeams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeduplicationKey",
                table: "AcademyNotices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectPlayerId",
                table: "AcademyNotices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByMembershipId",
                table: "AcademyMatchs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreationHash",
                table: "AcademyMatchs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreationRequestId",
                table: "AcademyMatchs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResultFinalized",
                table: "AcademyMatchs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResultFingerprint",
                table: "AcademyMatchs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResultPublishedAtUtc",
                table: "AcademyMatchs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultRevision",
                table: "AcademyMatchs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchGoalSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchGoalSlots", x => x.Id);
                    table.UniqueConstraint("AK_MatchGoalSlots_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_MatchGoalSlot_Value", "\"Ordinal\" BETWEEN 1 AND 100 AND ((\"Kind\"='Player' AND \"PlayerId\" IS NOT NULL) OR (\"Kind\" IN ('Unknown','OwnGoal') AND \"PlayerId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_MatchGoalSlots_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchGoalSlots_AcademyMatchs_AcademyId_MatchId",
                        columns: x => new { x.AcademyId, x.MatchId },
                        principalTable: "AcademyMatchs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchGoalSlots_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchResultChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    After = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchResultChanges", x => x.Id);
                    table.UniqueConstraint("AK_MatchResultChanges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MatchResultChanges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchResultChanges_AcademyMatchs_AcademyId_MatchId",
                        columns: x => new { x.AcademyId, x.MatchId },
                        principalTable: "AcademyMatchs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchResultChanges_AcademyMemberships_AcademyId_ActorMember~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchTeamPermissionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Before = table.Column<bool>(type: "boolean", nullable: false),
                    After = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeamPermissionAudits", x => x.Id);
                    table.UniqueConstraint("AK_MatchTeamPermissionAudits_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MatchTeamPermissionAudits_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeamPermissionAudits_AcademyMemberships_AcademyId_Acto~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeamPermissionAudits_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNotices_AcademyId_SubjectPlayerId",
                table: "AcademyNotices",
                columns: new[] { "AcademyId", "SubjectPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNotices_AcademyId_UserId_DeduplicationKey",
                table: "AcademyNotices",
                columns: new[] { "AcademyId", "UserId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMatchs_AcademyId_CreatedByMembershipId_CreationReque~",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "CreatedByMembershipId", "CreationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchGoalSlots_AcademyId_MatchId_Ordinal",
                table: "MatchGoalSlots",
                columns: new[] { "AcademyId", "MatchId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchGoalSlots_AcademyId_PlayerId",
                table: "MatchGoalSlots",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchResultChanges_AcademyId_ActorMembershipId",
                table: "MatchResultChanges",
                columns: new[] { "AcademyId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchResultChanges_AcademyId_MatchId_Revision",
                table: "MatchResultChanges",
                columns: new[] { "AcademyId", "MatchId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamPermissionAudits_AcademyId_ActorMembershipId",
                table: "MatchTeamPermissionAudits",
                columns: new[] { "AcademyId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamPermissionAudits_AcademyId_TeamId",
                table: "MatchTeamPermissionAudits",
                columns: new[] { "AcademyId", "TeamId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyMatchs_AcademyMemberships_AcademyId_CreatedByMembers~",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "CreatedByMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyNotices_Players_AcademyId_SubjectPlayerId",
                table: "AcademyNotices",
                columns: new[] { "AcademyId", "SubjectPlayerId" },
                principalTable: "Players",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            AddResultGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveResultGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_AcademyMatchs_AcademyMemberships_AcademyId_CreatedByMembers~",
                table: "AcademyMatchs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcademyNotices_Players_AcademyId_SubjectPlayerId",
                table: "AcademyNotices");

            migrationBuilder.DropTable(
                name: "MatchGoalSlots");

            migrationBuilder.DropTable(
                name: "MatchResultChanges");

            migrationBuilder.DropTable(
                name: "MatchTeamPermissionAudits");

            migrationBuilder.DropIndex(
                name: "IX_AcademyNotices_AcademyId_SubjectPlayerId",
                table: "AcademyNotices");

            migrationBuilder.DropIndex(
                name: "IX_AcademyNotices_AcademyId_UserId_DeduplicationKey",
                table: "AcademyNotices");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMatchs_AcademyId_CreatedByMembershipId_CreationReque~",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CoachCanManageMatches",
                table: "MatchTeams");

            migrationBuilder.DropColumn(
                name: "DeduplicationKey",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "SubjectPlayerId",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "CreatedByMembershipId",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CreationHash",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CreationRequestId",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "ResultFinalized",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "ResultFingerprint",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "ResultPublishedAtUtc",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "ResultRevision",
                table: "AcademyMatchs");
        }
    }
}
