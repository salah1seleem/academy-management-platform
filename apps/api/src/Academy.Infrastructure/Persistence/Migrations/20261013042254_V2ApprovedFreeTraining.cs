using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ApprovedFreeTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionPeriod_Origin",
                table: "SubscriptionPeriods");

            migrationBuilder.AddColumn<Guid>(
                name: "FreeTrainingApprovalId",
                table: "SubscriptionPeriods",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FreeTrainingApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    WaivedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InitialSessions = table.Column<int>(type: "integer", nullable: true),
                    PlanTermsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreeTrainingApprovals", x => x.Id);
                    table.UniqueConstraint("AK_FreeTrainingApprovals_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_FreeTrainingApproval_Source", "\"WaivedAmount\" >= 0 AND length(btrim(\"Reason\")) BETWEEN 1 AND 500 AND length(\"IdempotencyKey\") BETWEEN 8 AND 80 AND length(\"RequestHash\") = 64 AND length(\"QuoteHash\") = 64 AND (\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\") AND (\"InitialSessions\" IS NULL OR \"InitialSessions\" > 0)");
                    table.ForeignKey(
                        name: "FK_FreeTrainingApprovals_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FreeTrainingApprovals_AcademyMemberships_AcademyId_ActorMem~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FreeTrainingApprovals_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FreeTrainingApprovals_SportEnrollments_AcademyId_SportEnrol~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FreeTrainingApprovals_SubscriptionPlans_AcademyId_Subscript~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FreeTrainingApprovals_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_FreeTrainingApprovalId",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "FreeTrainingApprovalId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionPeriod_Origin",
                table: "SubscriptionPeriods",
                sql: "(\"Origin\" = 'Collected' AND \"CollectionId\" IS NOT NULL AND \"ImportJobId\" IS NULL AND \"FreeTrainingApprovalId\" IS NULL AND \"PriceSnapshot\" IS NOT NULL AND \"PriceSnapshot\" >= 0) OR (\"Origin\" = 'ImportedHistorical' AND \"CollectionId\" IS NULL AND \"ImportJobId\" IS NOT NULL AND \"FreeTrainingApprovalId\" IS NULL AND (\"PriceSnapshot\" IS NULL OR \"PriceSnapshot\" >= 0)) OR (\"Origin\" = 'ApprovedWaiver' AND \"CollectionId\" IS NULL AND \"ImportJobId\" IS NULL AND \"FreeTrainingApprovalId\" IS NOT NULL AND \"PriceSnapshot\" IS NOT NULL AND \"PriceSnapshot\" = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_AcademyId_ActorMembershipId",
                table: "FreeTrainingApprovals",
                columns: new[] { "AcademyId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_AcademyId_ActorUserId_IdempotencyKey",
                table: "FreeTrainingApprovals",
                columns: new[] { "AcademyId", "ActorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_AcademyId_BranchId",
                table: "FreeTrainingApprovals",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_AcademyId_IsActive",
                table: "FreeTrainingApprovals",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_AcademyId_SportEnrollmentId_CreatedAt~",
                table: "FreeTrainingApprovals",
                columns: new[] { "AcademyId", "SportEnrollmentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_AcademyId_SubscriptionPlanId",
                table: "FreeTrainingApprovals",
                columns: new[] { "AcademyId", "SubscriptionPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeTrainingApprovals_ActorUserId",
                table: "FreeTrainingApprovals",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_FreeTrainingApprovals_AcademyId_FreeTra~",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "FreeTrainingApprovalId" },
                principalTable: "FreeTrainingApprovals",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_free_training_demo_reset(a uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
                  SELECT a='30000000-0000-0000-0000-000000000003'::uuid
                    AND current_setting('academy.reset_demo_tenant',true)=a::text
                    AND EXISTS(SELECT 1 FROM "Academies" WHERE "Id"=a AND "Slug"='football-demo' AND "EnglishName"='Football Demo Dataset v1')
                $$;
                CREATE OR REPLACE FUNCTION academy_free_training_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Waiver approvals are immutable' USING ERRCODE='23514'; END IF;
                  IF NOT NEW."IsActive" OR NOT EXISTS (
                    SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId"
                    WHERE m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."ActorUserId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive")
                    OR NOT EXISTS (SELECT 1 FROM "SportEnrollments" e JOIN "SubscriptionPlans" p ON p."Id"=NEW."SubscriptionPlanId" AND p."AcademyId"=e."AcademyId"
                    WHERE e."Id"=NEW."SportEnrollmentId" AND e."AcademyId"=NEW."AcademyId" AND e."BranchId"=NEW."BranchId" AND e."SportId"=p."SportId" AND p."IsActive" AND e."IsActive"
                    AND p."ServiceKind"='Training' AND p."Price"=NEW."WaivedAmount" AND p."Currency"=NEW."Currency")
                  THEN RAISE EXCEPTION 'Invalid owner waiver source' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                DROP TRIGGER IF EXISTS "TR_FreeTrainingApprovals_Source" ON "FreeTrainingApprovals";
                CREATE TRIGGER "TR_FreeTrainingApprovals_Source" BEFORE INSERT OR UPDATE OR DELETE ON "FreeTrainingApprovals" FOR EACH ROW EXECUTE FUNCTION academy_free_training_source();
                CREATE OR REPLACE FUNCTION academy_free_training_period() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE a "FreeTrainingApprovals"%ROWTYPE;
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP IN ('UPDATE','DELETE') AND OLD."Origin"='ApprovedWaiver' THEN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Waived service cannot be deleted' USING ERRCODE='23514'; END IF;
                    IF ROW(NEW."AcademyId",NEW."Origin",NEW."FreeTrainingApprovalId",NEW."SportEnrollmentId",NEW."SportId",NEW."SubscriptionPlanId",NEW."CollectionId",NEW."ImportJobId",NEW."PriceSnapshot",NEW."CurrencySnapshot",NEW."CreatedByUserId",NEW."InitialSessions",NEW."PlanNameSnapshot",NEW."PlanTypeSnapshot",NEW."DurationDaysSnapshot",NEW."DurationMonthsSnapshot",NEW."SessionCountSnapshot",NEW."PlanRevisionSnapshot",NEW."ServiceKindSnapshot",NEW."TrainingFormatSnapshot") IS DISTINCT FROM
                       ROW(OLD."AcademyId",OLD."Origin",OLD."FreeTrainingApprovalId",OLD."SportEnrollmentId",OLD."SportId",OLD."SubscriptionPlanId",OLD."CollectionId",OLD."ImportJobId",OLD."PriceSnapshot",OLD."CurrencySnapshot",OLD."CreatedByUserId",OLD."InitialSessions",OLD."PlanNameSnapshot",OLD."PlanTypeSnapshot",OLD."DurationDaysSnapshot",OLD."DurationMonthsSnapshot",OLD."SessionCountSnapshot",OLD."PlanRevisionSnapshot",OLD."ServiceKindSnapshot",OLD."TrainingFormatSnapshot")
                    THEN RAISE EXCEPTION 'Waived service source and terms are immutable' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF TG_OP='UPDATE' AND OLD."Origin" <> 'ApprovedWaiver' AND NEW."Origin"='ApprovedWaiver' THEN RAISE EXCEPTION 'Existing service cannot become a waiver' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' AND NEW."Origin"='ApprovedWaiver' THEN
                    SELECT * INTO a FROM "FreeTrainingApprovals" WHERE "Id"=NEW."FreeTrainingApprovalId" AND "AcademyId"=NEW."AcademyId";
                    IF NOT FOUND OR ROW(NEW."SportEnrollmentId",NEW."SubscriptionPlanId",NEW."CurrencySnapshot",NEW."CreatedByUserId",NEW."StartDate",NEW."EndDate",NEW."InitialSessions") IS DISTINCT FROM ROW(a."SportEnrollmentId",a."SubscriptionPlanId",a."Currency",a."ActorUserId",a."StartDate",a."EndDate",a."InitialSessions")
                      OR NOT EXISTS (SELECT 1 FROM "SportEnrollments" e WHERE e."Id"=NEW."SportEnrollmentId" AND e."AcademyId"=NEW."AcademyId" AND e."SportId"=NEW."SportId" AND e."BranchId"=a."BranchId")
                      OR NEW."PlanNameSnapshot" IS DISTINCT FROM a."PlanTermsJson"->>'Name'
                      OR NEW."PlanTypeSnapshot" IS DISTINCT FROM a."PlanTermsJson"->>'Type'
                      OR NEW."DurationDaysSnapshot" IS DISTINCT FROM (a."PlanTermsJson"->>'Days')::int
                      OR NEW."DurationMonthsSnapshot" IS DISTINCT FROM (a."PlanTermsJson"->>'Months')::int
                      OR NEW."SessionCountSnapshot" IS DISTINCT FROM (a."PlanTermsJson"->>'Sessions')::int
                      OR NEW."PlanRevisionSnapshot" IS DISTINCT FROM (a."PlanTermsJson"->>'Revision')::int
                      OR NEW."ServiceKindSnapshot" IS DISTINCT FROM a."PlanTermsJson"->>'ServiceKind'
                      OR NEW."TrainingFormatSnapshot" IS DISTINCT FROM a."PlanTermsJson"->>'Format'
                    THEN RAISE EXCEPTION 'Waived period must match its approval' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                DROP TRIGGER IF EXISTS "TR_SubscriptionPeriods_Waiver" ON "SubscriptionPeriods";
                CREATE TRIGGER "TR_SubscriptionPeriods_Waiver" BEFORE INSERT OR UPDATE OR DELETE ON "SubscriptionPeriods" FOR EACH ROW EXECUTE FUNCTION academy_free_training_period();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM "FreeTrainingApprovals") THEN RAISE EXCEPTION 'Waiver history exists; do not downgrade'; END IF; END $$;
                DROP TRIGGER "TR_SubscriptionPeriods_Waiver" ON "SubscriptionPeriods";
                DROP FUNCTION academy_free_training_period();
                DROP TRIGGER "TR_FreeTrainingApprovals_Source" ON "FreeTrainingApprovals";
                DROP FUNCTION academy_free_training_source();
                DROP FUNCTION academy_free_training_demo_reset(uuid);
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPeriods_FreeTrainingApprovals_AcademyId_FreeTra~",
                table: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "FreeTrainingApprovals");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPeriods_AcademyId_FreeTrainingApprovalId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionPeriod_Origin",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "FreeTrainingApprovalId",
                table: "SubscriptionPeriods");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionPeriod_Origin",
                table: "SubscriptionPeriods",
                sql: "(\"Origin\" = 'Collected' AND \"CollectionId\" IS NOT NULL AND \"ImportJobId\" IS NULL AND \"PriceSnapshot\" IS NOT NULL AND \"PriceSnapshot\" >= 0) OR (\"Origin\" = 'ImportedHistorical' AND \"CollectionId\" IS NULL AND \"ImportJobId\" IS NOT NULL AND (\"PriceSnapshot\" IS NULL OR \"PriceSnapshot\" >= 0))");
        }
    }
}
