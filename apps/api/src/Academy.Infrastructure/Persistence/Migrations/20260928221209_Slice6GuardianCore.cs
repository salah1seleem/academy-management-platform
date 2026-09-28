using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice6GuardianCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewEnrollmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByGuardianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExistingPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewChildArabicName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    NewChildDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    NewChildGender = table.Column<int>(type: "integer", nullable: true),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AdminNotes = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    GuardianVisibleReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ApprovedPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedSportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewEnrollmentRequests", x => x.Id);
                    table.UniqueConstraint("AK_NewEnrollmentRequests_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_NewEnrollmentRequests_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_NewEnrollmentRequests_Review", "(\"Status\" IN ('Pending', 'UnderReview') AND \"ReviewedAtUtc\" IS NULL AND \"ReviewedByUserId\" IS NULL) OR (\"Status\" IN ('Approved', 'Rejected', 'Cancelled'))");
                    table.CheckConstraint("CK_NewEnrollmentRequests_Shape", "(\"RequestType\" = 'ExistingChildNewSport' AND \"ExistingPlayerId\" IS NOT NULL AND \"NewChildArabicName\" IS NULL AND \"NewChildDateOfBirth\" IS NULL) OR (\"RequestType\" = 'NewChild' AND \"ExistingPlayerId\" IS NULL AND \"NewChildArabicName\" IS NOT NULL AND \"NewChildDateOfBirth\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Branches_AcademyId_PreferredBranchId",
                        columns: x => new { x.AcademyId, x.PreferredBranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Players_AcademyId_ApprovedPlayerId",
                        columns: x => new { x.AcademyId, x.ApprovedPlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Players_AcademyId_ExistingPlayerId",
                        columns: x => new { x.AcademyId, x.ExistingPlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_SportEnrollments_AcademyId_CreatedSpo~",
                        columns: x => new { x.AcademyId, x.CreatedSportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Users_RequestedByGuardianUserId",
                        column: x => x.RequestedByGuardianUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewEnrollmentRequests_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_ApprovedPlayerId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "ApprovedPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_CreatedSportEnrollmentId_Sp~",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "CreatedSportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_ExistingPlayerId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "ExistingPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_IsActive",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_PreferredBranchId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "PreferredBranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_RequestedByGuardianUserId_I~",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "RequestedByGuardianUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_SportId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_Status_CreatedAtUtc",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_RequestedByGuardianUserId",
                table: "NewEnrollmentRequests",
                column: "RequestedByGuardianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_ReviewedByUserId",
                table: "NewEnrollmentRequests",
                column: "ReviewedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewEnrollmentRequests");
        }
    }
}
