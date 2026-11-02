using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2PlanPublicationTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllBranches",
                table: "SubscriptionPlans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationDaysSnapshot",
                table: "SubscriptionPeriods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanNameSnapshot",
                table: "SubscriptionPeriods",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanRevisionSnapshot",
                table: "SubscriptionPeriods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanTypeSnapshot",
                table: "SubscriptionPeriods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionCountSnapshot",
                table: "SubscriptionPeriods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationDaysSnapshot",
                table: "RenewalRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanNameSnapshot",
                table: "RenewalRequests",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanRevisionSnapshot",
                table: "RenewalRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanTypeSnapshot",
                table: "RenewalRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionCountSnapshot",
                table: "RenewalRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationDaysSnapshot",
                table: "NewEnrollmentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanNameSnapshot",
                table: "NewEnrollmentRequests",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanRevisionSnapshot",
                table: "NewEnrollmentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanTypeSnapshot",
                table: "NewEnrollmentRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionCountSnapshot",
                table: "NewEnrollmentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanBranchPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanBranchPublications", x => x.Id);
                    table.UniqueConstraint("AK_PlanBranchPublications_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlanBranchPublications_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanBranchPublications_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanBranchPublications_SubscriptionPlans_AcademyId_Subscrip~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanPriceVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PlanType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    SessionCount = table.Column<int>(type: "integer", nullable: true),
                    AllBranches = table.Column<bool>(type: "boolean", nullable: false),
                    BranchIdsJson = table.Column<string>(type: "text", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPriceVersions", x => x.Id);
                    table.UniqueConstraint("AK_PlanPriceVersions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlanPriceVersions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanPriceVersions_SubscriptionPlans_AcademyId_SubscriptionP~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanPriceVersions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanBranchPublications_AcademyId_BranchId",
                table: "PlanBranchPublications",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanBranchPublications_AcademyId_SubscriptionPlanId_BranchId",
                table: "PlanBranchPublications",
                columns: new[] { "AcademyId", "SubscriptionPlanId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanPriceVersions_AcademyId_SubscriptionPlanId_Revision",
                table: "PlanPriceVersions",
                columns: new[] { "AcademyId", "SubscriptionPlanId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanPriceVersions_ActorUserId",
                table: "PlanPriceVersions",
                column: "ActorUserId");
            // This records only configuration observed at migration time, not reconstructed sales history.
            migrationBuilder.Sql("""
                INSERT INTO "PlanPriceVersions" ("Id", "AcademyId", "SubscriptionPlanId", "Revision", "ArabicName", "PlanType", "Price", "Currency", "DurationDays", "SessionCount", "AllBranches", "BranchIdsJson", "Published", "EffectiveAtUtc", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), "AcademyId", "Id", "Revision", "ArabicName", "PlanType", "Price", "Currency", "DurationDays", "SessionCount", "AllBranches", '[]', "IsActive", CURRENT_TIMESTAMP, true, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "SubscriptionPlans";
                CREATE TRIGGER "TR_PlanPriceVersions_Immutable" BEFORE UPDATE OR DELETE ON "PlanPriceVersions" FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_plan_terms_immutable() RETURNS trigger AS $$
                BEGIN
                  IF OLD."PlanTypeSnapshot" IS NOT NULL AND
                    ROW(OLD."PlanNameSnapshot", OLD."PlanTypeSnapshot", OLD."DurationDaysSnapshot", OLD."SessionCountSnapshot", OLD."PlanRevisionSnapshot") IS DISTINCT FROM
                    ROW(NEW."PlanNameSnapshot", NEW."PlanTypeSnapshot", NEW."DurationDaysSnapshot", NEW."SessionCountSnapshot", NEW."PlanRevisionSnapshot") THEN
                    RAISE EXCEPTION 'Posted subscription terms cannot be changed' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER "TR_RenewalTerms_Immutable" BEFORE UPDATE ON "RenewalRequests" FOR EACH ROW EXECUTE FUNCTION academy_plan_terms_immutable();
                CREATE TRIGGER "TR_PeriodTerms_Immutable" BEFORE UPDATE ON "SubscriptionPeriods" FOR EACH ROW EXECUTE FUNCTION academy_plan_terms_immutable();
                CREATE TRIGGER "TR_EnrollmentTerms_Immutable" BEFORE UPDATE ON "NewEnrollmentRequests" FOR EACH ROW EXECUTE FUNCTION academy_plan_terms_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_RenewalTerms_Immutable" ON "RenewalRequests";
                DROP TRIGGER "TR_PeriodTerms_Immutable" ON "SubscriptionPeriods";
                DROP TRIGGER "TR_EnrollmentTerms_Immutable" ON "NewEnrollmentRequests";
                DROP FUNCTION academy_plan_terms_immutable();
                """);
            migrationBuilder.DropTable(
                name: "PlanBranchPublications");

            migrationBuilder.DropTable(
                name: "PlanPriceVersions");

            migrationBuilder.DropColumn(
                name: "AllBranches",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "DurationDaysSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "PlanNameSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "PlanRevisionSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "PlanTypeSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "SessionCountSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "DurationDaysSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "PlanNameSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "PlanRevisionSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "PlanTypeSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "SessionCountSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DurationDaysSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "PlanNameSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "PlanRevisionSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "PlanTypeSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "SessionCountSnapshot",
                table: "NewEnrollmentRequests");
        }
    }
}
