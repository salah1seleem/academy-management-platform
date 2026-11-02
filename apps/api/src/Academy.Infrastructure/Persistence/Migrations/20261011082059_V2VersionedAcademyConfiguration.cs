using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2VersionedAcademyConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcademyConfigurationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyConfigurationVersions", x => x.Id);
                    table.UniqueConstraint("AK_AcademyConfigurationVersions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyConfigurationVersions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcademyConfigurationVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcademyFeatureEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Feature = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyFeatureEntitlements", x => x.Id);
                    table.UniqueConstraint("AK_AcademyFeatureEntitlements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyFeatureEntitlements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    From = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    To = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationTransitions", x => x.Id);
                    table.UniqueConstraint("AK_ConfigurationTransitions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConfigurationTransitions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfigurationTransitions_AcademyConfigurationVersions_Acade~",
                        columns: x => new { x.AcademyId, x.ConfigurationId },
                        principalTable: "AcademyConfigurationVersions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfigurationTransitions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyConfigurationVersions_AcademyId",
                table: "AcademyConfigurationVersions",
                column: "AcademyId",
                unique: true,
                filter: "\"State\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyConfigurationVersions_AcademyId_IsActive",
                table: "AcademyConfigurationVersions",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyConfigurationVersions_AcademyId_Revision",
                table: "AcademyConfigurationVersions",
                columns: new[] { "AcademyId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyConfigurationVersions_CreatedByUserId",
                table: "AcademyConfigurationVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyFeatureEntitlements_AcademyId_Feature",
                table: "AcademyFeatureEntitlements",
                columns: new[] { "AcademyId", "Feature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyFeatureEntitlements_AcademyId_IsActive",
                table: "AcademyFeatureEntitlements",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationTransitions_AcademyId_ConfigurationId",
                table: "ConfigurationTransitions",
                columns: new[] { "AcademyId", "ConfigurationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationTransitions_AcademyId_IsActive",
                table: "ConfigurationTransitions",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationTransitions_ActorUserId",
                table: "ConfigurationTransitions",
                column: "ActorUserId");

            // Preserve only functionality already present for existing academies.
            // Newly provisioned academies must explicitly configure their license;
            // missing settings do not grant features. No children/finance rewritten.
            migrationBuilder.Sql("""
                WITH features AS (
                    SELECT jsonb_object_agg(f, f = ANY(ARRAY['training','nutrition','medical','media','catalog','news','matches','payroll'])) AS value
                    FROM unnest(ARRAY['training','nutrition','medical','media','catalog','news','matches','payroll','facilities','expenses','external_partners','learning_hub','calendar','crm','simulator','community_ads','voice_notes','ai_assistant']) f
                ), settings AS (
                    SELECT a."Id", jsonb_build_object('Currency', a."DefaultCurrency", 'TimeZone', a."TimeZone", 'Features', features.value,
                        'CashEnabled', true, 'ConsecutiveAbsenceAlert', 3, 'TenantMarketingOptIn', false) AS payload
                    FROM "Academies" a CROSS JOIN features
                )
                INSERT INTO "AcademyConfigurationVersions" ("Id", "AcademyId", "Revision", "State", "Payload", "ContentHash", "EffectiveAtUtc", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT md5("Id"::text || ':v2-config')::uuid, "Id", 1, 'Active', payload, encode(sha256(convert_to(payload::text, 'UTF8')), 'hex'), now(), true, now(), now() FROM settings;

                INSERT INTO "AcademyFeatureEntitlements" ("Id", "AcademyId", "Feature", "Enabled", "PlanCode", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT md5(a."Id"::text || ':v2-feature:' || f)::uuid, a."Id", f, f = ANY(ARRAY['training','nutrition','medical','media','catalog','news','matches','payroll']), 'LegacyPreserved', true, now(), now()
                FROM "Academies" a CROSS JOIN unnest(ARRAY['training','nutrition','medical','media','catalog','news','matches','payroll','facilities','expenses','external_partners','learning_hub','calendar','crm','simulator','community_ads','voice_notes','ai_assistant']) f;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcademyFeatureEntitlements");

            migrationBuilder.DropTable(
                name: "ConfigurationTransitions");

            migrationBuilder.DropTable(
                name: "AcademyConfigurationVersions");
        }
    }
}
