using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2GuardianCoachFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuardianFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PeriodMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianFeedbacks", x => x.Id);
                    table.UniqueConstraint("AK_GuardianFeedbacks_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_GuardianFeedback_KindScore", "(\"Kind\" = 'Rating' AND \"CoachMembershipId\" IS NOT NULL AND \"Score\" BETWEEN 1 AND 5) OR (\"Kind\" IN ('Complaint','Suggestion') AND \"Score\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_AcademyMemberships_AcademyId_CoachMembers~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_TrainingGroups_AcademyId_TrainingGroupId",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_Users_GuardianUserId",
                        column: x => x.GuardianUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianFeedbacks_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_BranchId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_CoachMembershipId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_GuardianUserId_IdempotencyKey",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "GuardianUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_GuardianUserId_PlayerId_CoachMe~",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "GuardianUserId", "PlayerId", "CoachMembershipId", "PeriodMonth" },
                unique: true,
                filter: "\"Kind\" = 'Rating'");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_PlayerId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_AcademyId_TrainingGroupId",
                table: "GuardianFeedbacks",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_GuardianUserId",
                table: "GuardianFeedbacks",
                column: "GuardianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFeedbacks_ReviewedByUserId",
                table: "GuardianFeedbacks",
                column: "ReviewedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuardianFeedbacks");
        }
    }
}
