using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3SportScopedSwimmingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Sports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.CreateTable(
                name: "SportSettingsAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_SportSettingsAudits", x => x.Id);
                    table.UniqueConstraint("AK_SportSettingsAudits_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_SportSettingsAudits_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SportSettingsAudits_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SportSettingsAudits_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SportSettingsAudits_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SwimmingBranchPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PrivateTrainingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OpenSwimEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DayPassEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwimmingBranchPolicies", x => x.Id);
                    table.UniqueConstraint("AK_SwimmingBranchPolicies_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_SwimmingBranchPolicies_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SwimmingBranchPolicies_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SwimmingBranchPolicies_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SportSettingsAudits_AcademyId_BranchId",
                table: "SportSettingsAudits",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SportSettingsAudits_AcademyId_SportId",
                table: "SportSettingsAudits",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SportSettingsAudits_ActorUserId",
                table: "SportSettingsAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SwimmingBranchPolicies_AcademyId_BranchId",
                table: "SwimmingBranchPolicies",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SwimmingBranchPolicies_AcademyId_SportId_BranchId",
                table: "SwimmingBranchPolicies",
                columns: new[] { "AcademyId", "SportId", "BranchId" },
                unique: true);

            // Preserve only already configured pool operations. Names never classify a sport.
            // A sport shared with a non-pool resource is ambiguous and requires owner review.
            migrationBuilder.Sql("""
                UPDATE "Sports" s SET "Kind"='Swimming'
                WHERE EXISTS (SELECT 1 FROM "FacilitySpaces" p WHERE p."AcademyId"=s."AcademyId" AND p."SportId"=s."Id" AND p."Kind"='Pool')
                  AND NOT EXISTS (SELECT 1 FROM "FacilitySpaces" p WHERE p."AcademyId"=s."AcademyId" AND p."SportId"=s."Id" AND p."Kind"<>'Pool');
                INSERT INTO "SwimmingBranchPolicies" ("Id","AcademyId","SportId","BranchId","Enabled","PrivateTrainingEnabled","OpenSwimEnabled","DayPassEnabled","IsActive","CreatedAtUtc","UpdatedAtUtc")
                SELECT gen_random_uuid(), p."AcademyId", p."SportId", p."BranchId", bool_or(p."IsActive"), true,
                    coalesce(bool_or(r."IsActive" AND 'OpenSwim'=ANY(r."ServiceKinds")),false),
                    coalesce(bool_or(r."IsActive" AND 'DayPass'=ANY(r."ServiceKinds")),false), true, now(), now()
                FROM "FacilitySpaces" p JOIN "Sports" s ON s."AcademyId"=p."AcademyId" AND s."Id"=p."SportId" AND s."Kind"='Swimming'
                LEFT JOIN "FacilityResources" r ON r."AcademyId"=p."AcademyId" AND r."FacilitySpaceId"=p."Id"
                WHERE p."Kind"='Pool' GROUP BY p."AcademyId",p."SportId",p."BranchId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SportSettingsAudits");

            migrationBuilder.DropTable(
                name: "SwimmingBranchPolicies");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Sports");
        }
    }
}
