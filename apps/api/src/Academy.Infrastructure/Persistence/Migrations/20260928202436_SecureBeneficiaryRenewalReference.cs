using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureBeneficiaryRenewalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BeneficiaryRenewalReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CodeHint = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GeneratedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeneficiaryRenewalReferences", x => x.Id);
                    table.UniqueConstraint("AK_BeneficiaryRenewalReferences_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_BeneficiaryRenewalReferences_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BeneficiaryRenewalReferences_SportEnrollments_AcademyId_Spo~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BeneficiaryRenewalReferences_AcademyId_CodeHash",
                table: "BeneficiaryRenewalReferences",
                columns: new[] { "AcademyId", "CodeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BeneficiaryRenewalReferences_AcademyId_IsActive",
                table: "BeneficiaryRenewalReferences",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BeneficiaryRenewalReferences_AcademyId_SportEnrollmentId_Re~",
                table: "BeneficiaryRenewalReferences",
                columns: new[] { "AcademyId", "SportEnrollmentId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BeneficiaryRenewalReferences");
        }
    }
}
