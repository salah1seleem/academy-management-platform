using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AudienceCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudienceAnnouncements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IncludeInvitation = table.Column<bool>(type: "boolean", nullable: false),
                    PlayerCount = table.Column<int>(type: "integer", nullable: false),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudienceAnnouncements", x => x.Id);
                    table.UniqueConstraint("AK_AudienceAnnouncements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_AudienceAnnouncement_Target", "(\"TrainingGroupId\" IS NULL) <> (\"TeamId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AudienceAnnouncements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceAnnouncements_AcademyMatchs_AcademyId_MatchId",
                        columns: x => new { x.AcademyId, x.MatchId },
                        principalTable: "AcademyMatchs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceAnnouncements_AcademyMemberships_AcademyId_ActorMem~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceAnnouncements_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceAnnouncements_TrainingGroups_AcademyId_TrainingGrou~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AudienceCommunications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    InviteUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudienceCommunications", x => x.Id);
                    table.UniqueConstraint("AK_AudienceCommunications_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_AudienceCommunication_Target", "(\"TrainingGroupId\" IS NULL) <> (\"TeamId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AudienceCommunications_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceCommunications_AcademyMemberships_AcademyId_Updated~",
                        columns: x => new { x.AcademyId, x.UpdatedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceCommunications_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceCommunications_TrainingGroups_AcademyId_TrainingGro~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AudienceLinkAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommunicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Linked = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudienceLinkAudits", x => x.Id);
                    table.UniqueConstraint("AK_AudienceLinkAudits_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AudienceLinkAudits_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceLinkAudits_AcademyMemberships_AcademyId_ActorMember~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AudienceLinkAudits_AudienceCommunications_AcademyId_Communi~",
                        columns: x => new { x.AcademyId, x.CommunicationId },
                        principalTable: "AudienceCommunications",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceAnnouncements_AcademyId_ActorMembershipId_RequestId",
                table: "AudienceAnnouncements",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AudienceAnnouncements_AcademyId_MatchId",
                table: "AudienceAnnouncements",
                columns: new[] { "AcademyId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceAnnouncements_AcademyId_TeamId",
                table: "AudienceAnnouncements",
                columns: new[] { "AcademyId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceAnnouncements_AcademyId_TrainingGroupId",
                table: "AudienceAnnouncements",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceCommunications_AcademyId_TeamId",
                table: "AudienceCommunications",
                columns: new[] { "AcademyId", "TeamId" },
                unique: true,
                filter: "\"TeamId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AudienceCommunications_AcademyId_TrainingGroupId",
                table: "AudienceCommunications",
                columns: new[] { "AcademyId", "TrainingGroupId" },
                unique: true,
                filter: "\"TrainingGroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AudienceCommunications_AcademyId_UpdatedByMembershipId",
                table: "AudienceCommunications",
                columns: new[] { "AcademyId", "UpdatedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceLinkAudits_AcademyId_ActorMembershipId",
                table: "AudienceLinkAudits",
                columns: new[] { "AcademyId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceLinkAudits_AcademyId_CommunicationId",
                table: "AudienceLinkAudits",
                columns: new[] { "AcademyId", "CommunicationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudienceAnnouncements");

            migrationBuilder.DropTable(
                name: "AudienceLinkAudits");

            migrationBuilder.DropTable(
                name: "AudienceCommunications");
        }
    }
}
