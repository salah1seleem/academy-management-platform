using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ScopedDiscountPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DiscountRuleIdSnapshot",
                table: "RenewalRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscountRuleRevisionSnapshot",
                table: "RenewalRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousDiscountRuleId",
                table: "RenewalDiscountAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousDiscountRuleRevision",
                table: "RenewalDiscountAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                table: "Receipts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DiscountRuleIdSnapshot",
                table: "Receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscountRuleRevisionSnapshot",
                table: "Receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "NewEnrollmentRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                table: "NewEnrollmentRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DiscountRuleIdSnapshot",
                table: "NewEnrollmentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscountRuleRevisionSnapshot",
                table: "NewEnrollmentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "NewEnrollmentRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "NewEnrollmentRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "DiscountRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "DiscountRules",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EGP");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndsOn",
                table: "DiscountRules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedAmount",
                table: "DiscountRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumAmount",
                table: "DiscountRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "DiscountRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "DiscountRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SportId",
                table: "DiscountRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartsOn",
                table: "DiscountRules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId",
                table: "DiscountRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "DiscountRules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Percentage");

            migrationBuilder.CreateTable(
                name: "DiscountRuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRuleVersions", x => x.Id);
                    table.UniqueConstraint("AK_DiscountRuleVersions_AcademyId_DiscountRuleId_Revision", x => new { x.AcademyId, x.DiscountRuleId, x.Revision });
                    table.UniqueConstraint("AK_DiscountRuleVersions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_DiscountRuleVersions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscountRuleVersions_DiscountRules_AcademyId_DiscountRuleId",
                        columns: x => new { x.AcademyId, x.DiscountRuleId },
                        principalTable: "DiscountRules",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscountRuleVersions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_AcademyId_DiscountRuleIdSnapshot_DiscountRu~",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_AcademyId_DiscountRuleIdSnapshot_DiscountRuleRevis~",
                table: "Receipts",
                columns: new[] { "AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_DiscountRuleIdSnapshot_Disc~",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_AcademyId_BranchId",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_AcademyId_SportId",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_AcademyId_SubscriptionPlanId",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "SubscriptionPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRuleVersions_ActorUserId",
                table: "DiscountRuleVersions",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountRules_Branches_AcademyId_BranchId",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "BranchId" },
                principalTable: "Branches",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountRules_Sports_AcademyId_SportId",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "SportId" },
                principalTable: "Sports",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountRules_SubscriptionPlans_AcademyId_SubscriptionPlanId",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "SubscriptionPlanId" },
                principalTable: "SubscriptionPlans",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NewEnrollmentRequests_DiscountRuleVersions_AcademyId_Discou~",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot" },
                principalTable: "DiscountRuleVersions",
                principalColumns: new[] { "AcademyId", "DiscountRuleId", "Revision" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Receipts_DiscountRuleVersions_AcademyId_DiscountRuleIdSnaps~",
                table: "Receipts",
                columns: new[] { "AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot" },
                principalTable: "DiscountRuleVersions",
                principalColumns: new[] { "AcademyId", "DiscountRuleId", "Revision" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_DiscountRuleVersions_AcademyId_DiscountRule~",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot" },
                principalTable: "DiscountRuleVersions",
                principalColumns: new[] { "AcademyId", "DiscountRuleId", "Revision" },
                onDelete: ReferentialAction.Restrict);
            // Observe current rules once. This is not a reconstructed history of pre-V2 changes.
            migrationBuilder.Sql("""
                UPDATE "DiscountRules" r SET "Currency" = a."DefaultCurrency" FROM "Academies" a WHERE a."Id" = r."AcademyId";
                INSERT INTO "DiscountRuleVersions" ("Id", "DiscountRuleId", "Revision", "Payload", "ActorUserId", "AcademyId", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), r."Id", r."Revision", jsonb_build_object(
                    'arabicName', r."ArabicName", 'type', r."Type", 'percentage', r."Percentage"::text,
                    'fixedAmount', r."FixedAmount"::text, 'maximumAmount', r."MaximumAmount"::text, 'currency', r."Currency",
                    'branchId', r."BranchId", 'sportId', r."SportId", 'subscriptionPlanId', r."SubscriptionPlanId",
                    'startsOn', r."StartsOn", 'endsOn', r."EndsOn", 'priority', r."Priority", 'isSiblingOffer', r."IsSiblingOffer",
                    'isActive', r."IsActive", 'reason', r."Reason", 'provenance', 'observed-on-v2-migration'),
                    NULL, r."AcademyId", true, now(), now() FROM "DiscountRules" r;
                CREATE TRIGGER "DiscountRuleVersions_immutable" BEFORE UPDATE OR DELETE ON "DiscountRuleVersions"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                ALTER TABLE "DiscountRules" ADD CONSTRAINT "DiscountRules_valid_pricing" CHECK (
                    "Revision" >= 0 AND "Priority" >= 0 AND "Priority" <= 10000 AND
                    ("StartsOn" IS NULL OR "EndsOn" IS NULL OR "StartsOn" <= "EndsOn") AND
                    ("MaximumAmount" IS NULL OR "MaximumAmount" > 0) AND
                    (("Type" = 'Percentage' AND "Percentage" BETWEEN 0 AND 100 AND "FixedAmount" IS NULL)
                        OR ("Type" = 'FixedAmount' AND "Percentage" = 0 AND "FixedAmount" > 0)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"DiscountRules\" DROP CONSTRAINT \"DiscountRules_valid_pricing\";");
            migrationBuilder.DropForeignKey(
                name: "FK_DiscountRules_Branches_AcademyId_BranchId",
                table: "DiscountRules");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountRules_Sports_AcademyId_SportId",
                table: "DiscountRules");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountRules_SubscriptionPlans_AcademyId_SubscriptionPlanId",
                table: "DiscountRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NewEnrollmentRequests_DiscountRuleVersions_AcademyId_Discou~",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_DiscountRuleVersions_AcademyId_DiscountRuleIdSnaps~",
                table: "Receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_DiscountRuleVersions_AcademyId_DiscountRule~",
                table: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "DiscountRuleVersions");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_AcademyId_DiscountRuleIdSnapshot_DiscountRu~",
                table: "RenewalRequests");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_AcademyId_DiscountRuleIdSnapshot_DiscountRuleRevis~",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_DiscountRuleIdSnapshot_Disc~",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_DiscountRules_AcademyId_BranchId",
                table: "DiscountRules");

            migrationBuilder.DropIndex(
                name: "IX_DiscountRules_AcademyId_SportId",
                table: "DiscountRules");

            migrationBuilder.DropIndex(
                name: "IX_DiscountRules_AcademyId_SubscriptionPlanId",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "DiscountRuleIdSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountRuleRevisionSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "PreviousDiscountRuleId",
                table: "RenewalDiscountAdjustments");

            migrationBuilder.DropColumn(
                name: "PreviousDiscountRuleRevision",
                table: "RenewalDiscountAdjustments");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "DiscountRuleIdSnapshot",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "DiscountRuleRevisionSnapshot",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "DiscountRuleIdSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "DiscountRuleRevisionSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "EndsOn",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "FixedAmount",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "MaximumAmount",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "SportId",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "StartsOn",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "DiscountRules");
        }
    }
}
