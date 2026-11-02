using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcademyBrandingAndMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcademyExperiences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PrimaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SecondaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    LogoContentType = table.Column<string>(type: "text", nullable: true),
                    CoachAttendanceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyExperiences", x => x.Id);
                    table.UniqueConstraint("AK_AcademyExperiences_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyExperiences_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcademyTournaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyTournaments", x => x.Id);
                    table.UniqueConstraint("AK_AcademyTournaments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyTournaments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcademyTournaments_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BrandingAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    PrimaryColor = table.Column<string>(type: "text", nullable: false),
                    SecondaryColor = table.Column<string>(type: "text", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandingAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandingAudits_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandingAudits_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeams", x => x.Id);
                    table.UniqueConstraint("AK_MatchTeams_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MatchTeams_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeams_AcademyMemberships_AcademyId_CoachMembershipId",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeams_AgeCategories_AcademyId_AgeCategoryId",
                        columns: x => new { x.AcademyId, x.AgeCategoryId },
                        principalTable: "AgeCategories",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeams_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeams_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformBrandOperators",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBrandOperators", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_PlatformBrandOperators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TournamentPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentName = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProofAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceiptNumber = table.Column<string>(type: "text", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentPayments", x => x.Id);
                    table.UniqueConstraint("AK_TournamentPayments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_TournamentPayments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentPayments_AcademyTournaments_AcademyId_TournamentId",
                        columns: x => new { x.AcademyId, x.TournamentId },
                        principalTable: "AcademyTournaments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentPayments_PrivateAssets_AcademyId_ProofAssetId",
                        columns: x => new { x.AcademyId, x.ProofAssetId },
                        principalTable: "PrivateAssets",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentPayments_SportEnrollments_AcademyId_EnrollmentId",
                        columns: x => new { x.AcademyId, x.EnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcademyMatchs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Opponent = table.Column<string>(type: "text", nullable: false),
                    MatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Venue = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    OurScore = table.Column<int>(type: "integer", nullable: true),
                    OpponentScore = table.Column<int>(type: "integer", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedFingerprint = table.Column<string>(type: "text", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyMatchs", x => x.Id);
                    table.UniqueConstraint("AK_AcademyMatchs_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyMatchs_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcademyMatchs_AcademyTournaments_AcademyId_TournamentId",
                        columns: x => new { x.AcademyId, x.TournamentId },
                        principalTable: "AcademyTournaments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcademyMatchs_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchTeamPlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeamPlayers", x => x.Id);
                    table.UniqueConstraint("AK_MatchTeamPlayers_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MatchTeamPlayers_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeamPlayers_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchTeamPlayers_SportEnrollments_AcademyId_EnrollmentId",
                        columns: x => new { x.AcademyId, x.EnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsBench = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipants", x => x.Id);
                    table.UniqueConstraint("AK_MatchParticipants_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MatchParticipants_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchParticipants_AcademyMatchs_AcademyId_MatchId",
                        columns: x => new { x.AcademyId, x.MatchId },
                        principalTable: "AcademyMatchs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchParticipants_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyExperiences_AcademyId",
                table: "AcademyExperiences",
                column: "AcademyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMatchs_AcademyId_TeamId",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMatchs_AcademyId_TournamentId",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "TournamentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyTournaments_AcademyId_SportId",
                table: "AcademyTournaments",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_BrandingAudits_AcademyId",
                table: "BrandingAudits",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandingAudits_ActorUserId",
                table: "BrandingAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_AcademyId_MatchId_PlayerId",
                table: "MatchParticipants",
                columns: new[] { "AcademyId", "MatchId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_AcademyId_PlayerId",
                table: "MatchParticipants",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamPlayers_AcademyId_EnrollmentId",
                table: "MatchTeamPlayers",
                columns: new[] { "AcademyId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamPlayers_AcademyId_TeamId_EnrollmentId",
                table: "MatchTeamPlayers",
                columns: new[] { "AcademyId", "TeamId", "EnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeams_AcademyId_AgeCategoryId",
                table: "MatchTeams",
                columns: new[] { "AcademyId", "AgeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeams_AcademyId_BranchId",
                table: "MatchTeams",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeams_AcademyId_CoachMembershipId",
                table: "MatchTeams",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeams_AcademyId_SportId",
                table: "MatchTeams",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPayments_AcademyId_EnrollmentId_TournamentId",
                table: "TournamentPayments",
                columns: new[] { "AcademyId", "EnrollmentId", "TournamentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPayments_AcademyId_ProofAssetId",
                table: "TournamentPayments",
                columns: new[] { "AcademyId", "ProofAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPayments_AcademyId_RequestedByUserId_IdempotencyK~",
                table: "TournamentPayments",
                columns: new[] { "AcademyId", "RequestedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPayments_AcademyId_TournamentId",
                table: "TournamentPayments",
                columns: new[] { "AcademyId", "TournamentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcademyExperiences");

            migrationBuilder.DropTable(
                name: "BrandingAudits");

            migrationBuilder.DropTable(
                name: "MatchParticipants");

            migrationBuilder.DropTable(
                name: "MatchTeamPlayers");

            migrationBuilder.DropTable(
                name: "PlatformBrandOperators");

            migrationBuilder.DropTable(
                name: "TournamentPayments");

            migrationBuilder.DropTable(
                name: "AcademyMatchs");

            migrationBuilder.DropTable(
                name: "AcademyTournaments");

            migrationBuilder.DropTable(
                name: "MatchTeams");
        }
    }
}
