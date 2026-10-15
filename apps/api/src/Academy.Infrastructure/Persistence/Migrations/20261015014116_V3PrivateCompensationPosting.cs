using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PrivateCompensationPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_PrivateServiceDeliveries_AcademyId_Id_EmployeeId_BranchId_S~",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "SportId" });

            migrationBuilder.CreateTable(
                name: "PrivateCompensationEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Component = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PostedBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Delta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayrollAdjustmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CutoffAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    FactsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateCompensationEntries", x => x.Id);
                    table.UniqueConstraint("AK_PrivateCompensationEntries_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PrivateEntry_Amounts", "\"Component\"='PrivateVariable' AND \"Revision\">0 AND \"PostedBefore\">=0 AND (\"TargetAmount\" IS NULL OR \"TargetAmount\">=0) AND ((\"Kind\"='Review' AND \"Delta\"=0 AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Accrual' AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Reversal' AND \"Delta\"<0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_PrivateCompensationEntries_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateCompensationEntries_AcademyMemberships_AcademyId_Act~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateCompensationEntries_PayrollAdjustments_AcademyId_Pay~",
                        columns: x => new { x.AcademyId, x.PayrollAdjustmentId },
                        principalTable: "PayrollAdjustments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateCompensationEntries_PrivateCompensationEntries_Acade~",
                        columns: x => new { x.AcademyId, x.ReviewEntryId },
                        principalTable: "PrivateCompensationEntries",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateCompensationEntries_PrivateServiceDeliveries_Academy~",
                        columns: x => new { x.AcademyId, x.DeliveryId, x.EmployeeId, x.BranchId, x.SportId },
                        principalTable: "PrivateServiceDeliveries",
                        principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_ActorMembershipId_Requ~",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_DeliveryId_Component_R~",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "DeliveryId", "Component", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_DeliveryId_EmployeeId_~",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "DeliveryId", "EmployeeId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_PayrollAdjustmentId",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "PayrollAdjustmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_ReviewEntryId",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "ReviewEntryId" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER private_entry_immutable BEFORE UPDATE OR DELETE ON "PrivateCompensationEntries" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE OR REPLACE FUNCTION academy_private_entry_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE posted numeric; revision integer; today date;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT COALESCE(SUM("Delta"),0),COALESCE(MAX("Revision"),0) INTO posted,revision FROM "PrivateCompensationEntries"
                    WHERE "AcademyId"=NEW."AcademyId" AND "DeliveryId"=NEW."DeliveryId" AND "Component"=NEW."Component";
                  SELECT timezone("TimeZone",clock_timestamp())::date INTO today FROM "Academies" WHERE "Id"=NEW."AcademyId" AND "IsActive";
                  IF NEW."Revision"<>revision+1 OR NEW."PostedBefore"<>posted OR NEW."DueDate" IS DISTINCT FROM today
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" actor JOIN "Users" u ON u."Id"=actor."UserId"
                      WHERE actor."Id"=NEW."ActorMembershipId" AND actor."AcademyId"=NEW."AcademyId" AND actor."UserId"=NEW."ActorUserId"
                        AND actor."Role"='AcademyOwner' AND actor."IsActive" AND u."IsActive")
                    OR NOT EXISTS(SELECT 1 FROM "PrivateServiceDeliveries" d JOIN "CoachCompensationAgreements" a ON a."Id"=d."AgreementId" AND a."AcademyId"=d."AcademyId"
                      WHERE d."Id"=NEW."DeliveryId" AND d."AcademyId"=NEW."AcademyId" AND a."Currency"=NEW."Currency")
                    OR (NEW."SourceSnapshot"->>'delivery')::uuid IS DISTINCT FROM NEW."DeliveryId"
                    OR (NEW."SourceSnapshot"->>'AcademyId')::uuid IS DISTINCT FROM NEW."AcademyId"
                    OR (NEW."SourceSnapshot"->>'Earned')::numeric IS DISTINCT FROM NEW."TargetAmount"
                    THEN RAISE EXCEPTION 'Invalid private payroll source, actor, date or revision chain' USING ERRCODE='23514'; END IF;
                  IF NEW."Kind"='Review' THEN
                    IF NOT (COALESCE((NEW."SourceSnapshot"->>'RequiresReview')::boolean,false) OR COALESCE(NEW."TargetAmount"<posted,false))
                      THEN RAISE EXCEPTION 'Private review must describe a real source exception' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF EXISTS(SELECT 1 FROM "PayrollSettlements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=date_trunc('month',NEW."DueDate")::date)
                      OR COALESCE((NEW."SourceSnapshot"->>'RequiresReview')::boolean,false)
                      OR NOT EXISTS(SELECT 1 FROM "PayrollAdjustments" p WHERE p."Id"=NEW."PayrollAdjustmentId" AND p."AcademyId"=NEW."AcademyId"
                        AND p."EmployeeId"=NEW."EmployeeId" AND p."Currency"=NEW."Currency" AND p."Amount"=abs(NEW."Delta") AND p."DueDate"=NEW."DueDate"
                        AND p."PerformedByUserId"=NEW."ActorUserId" AND p."AdditionRequestId" IS NULL
                        AND p."Kind"=CASE WHEN NEW."Delta">0 THEN 'Addition' ELSE 'Deduction' END)
                      THEN RAISE EXCEPTION 'Private source cannot rewrite paid payroll or mismatch its adjustment' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF NEW."Kind"='Reversal' AND (
                    NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" r WHERE r."Id"=NEW."ReviewEntryId" AND r."AcademyId"=NEW."AcademyId"
                      AND r."DeliveryId"=NEW."DeliveryId" AND r."Kind"='Review' AND r."FactsHash"=NEW."FactsHash"
                      AND r."PostedBefore"=NEW."PostedBefore" AND r."TargetAmount"=NEW."TargetAmount")
                    OR EXISTS(SELECT 1 FROM "PrivateCompensationEntries" e WHERE e."AcademyId"=NEW."AcademyId" AND e."DeliveryId"=NEW."DeliveryId"
                      AND e."PayrollAdjustmentId" IS NOT NULL AND date_trunc('month',e."DueDate")<>date_trunc('month',NEW."DueDate")))
                    THEN RAISE EXCEPTION 'Reversal requires matching review and same unpaid payroll month' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER private_entry_source BEFORE INSERT ON "PrivateCompensationEntries" FOR EACH ROW EXECUTE FUNCTION academy_private_entry_source();
                CREATE FUNCTION academy_private_adjustment_preserved() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "PrivateCompensationEntries" WHERE "AcademyId"=OLD."AcademyId" AND "PayrollAdjustmentId"=OLD."Id") THEN
                    IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                    RAISE EXCEPTION 'Private payroll source adjustment is append-only' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER private_adjustment_preserved BEFORE UPDATE OR DELETE ON "PayrollAdjustments" FOR EACH ROW EXECUTE FUNCTION academy_private_adjustment_preserved();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PrivateCompensationEntries") THEN RAISE EXCEPTION 'Preserved payroll source entries exist; downgrade refused'; END IF; END $$;
                DROP FUNCTION academy_private_entry_source() CASCADE;
                DROP FUNCTION academy_private_adjustment_preserved() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "PrivateCompensationEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PrivateServiceDeliveries_AcademyId_Id_EmployeeId_BranchId_S~",
                table: "PrivateServiceDeliveries");
        }
    }
}
