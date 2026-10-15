using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3DatedEmploymentContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ContractStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ContractEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    ApprovedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVersions", x => x.Id);
                    table.UniqueConstraint("AK_ContractVersions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ContractVersion_Terms", "\"Revision\">0 AND EXTRACT(DAY FROM \"EffectiveFrom\")=1 AND (\"ContractEnd\" IS NULL OR \"ContractEnd\">=\"ContractStart\") AND \"Kind\" IN ('Initial','LegacyBaseline','Change') AND ((\"Revision\"=1 AND \"PreviousVersionId\" IS NULL AND \"Kind\"<>'Change') OR (\"Revision\">1 AND \"PreviousVersionId\" IS NOT NULL AND \"Kind\"='Change'))");
                    table.ForeignKey(
                        name: "FK_ContractVersions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_AcademyMemberships_AcademyId_ApprovedByMem~",
                        columns: x => new { x.AcademyId, x.ApprovedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_ContractVersions_AcademyId_PreviousVersion~",
                        columns: x => new { x.AcademyId, x.PreviousVersionId },
                        principalTable: "ContractVersions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Employees_AcademyId_EmployeeId",
                        columns: x => new { x.AcademyId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_AcademyId_ApprovedByMembershipId_RequestKey",
                table: "ContractVersions",
                columns: new[] { "AcademyId", "ApprovedByMembershipId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_AcademyId_BranchId",
                table: "ContractVersions",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_AcademyId_EmployeeId_EffectiveFrom",
                table: "ContractVersions",
                columns: new[] { "AcademyId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_AcademyId_EmployeeId_Revision",
                table: "ContractVersions",
                columns: new[] { "AcademyId", "EmployeeId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_AcademyId_PreviousVersionId",
                table: "ContractVersions",
                columns: new[] { "AcademyId", "PreviousVersionId" });
            migrationBuilder.Sql("""

                CREATE FUNCTION academy_employee_contract(academy uuid, employee uuid, day date)
                  RETURNS TABLE("ContractStart" date,"ContractEnd" date) LANGUAGE sql STABLE AS $$
                  SELECT COALESCE(v."ContractStart",e."ContractStart"),CASE WHEN v."Id" IS NULL THEN e."ContractEnd" ELSE v."ContractEnd" END
                  FROM "Employees" e LEFT JOIN LATERAL (
                    SELECT c.* FROM "ContractVersions" c WHERE c."AcademyId"=e."AcademyId" AND c."EmployeeId"=e."Id" AND c."EffectiveFrom"<=date_trunc('month',day)::date
                    ORDER BY c."EffectiveFrom" DESC,c."Revision" DESC LIMIT 1
                  ) v ON true WHERE e."AcademyId"=academy AND e."Id"=employee;
                $$;
                CREATE TRIGGER contract_version_immutable BEFORE UPDATE OR DELETE ON "ContractVersions" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE FUNCTION academy_contract_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE last_version "ContractVersions"%ROWTYPE; month date;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT * INTO last_version FROM "ContractVersions" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" ORDER BY "Revision" DESC LIMIT 1;
                  SELECT date_trunc('month',timezone("TimeZone",clock_timestamp()))::date INTO month FROM "Academies" WHERE "Id"=NEW."AcademyId" AND "IsActive";
                  IF month IS NULL OR NEW."Revision"<>COALESCE(last_version."Revision",0)+1 OR NEW."PreviousVersionId" IS DISTINCT FROM last_version."Id"
                    OR NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" actor ON actor."Id"=NEW."ApprovedByMembershipId" AND actor."AcademyId"=e."AcademyId"
                      JOIN "Users" u ON u."Id"=actor."UserId" WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."BranchId"=NEW."BranchId"
                        AND actor."UserId"=NEW."ActorUserId" AND actor."IsActive" AND u."IsActive" AND actor."Role" IN ('AcademyOwner','AcademyAdmin')
                        AND (actor."Role"='AcademyOwner' OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" self WHERE self."Id"=e."MembershipId" AND self."UserId"=actor."UserId")))
                    THEN RAISE EXCEPTION 'Invalid employment contract source or revision chain' USING ERRCODE='23514'; END IF;
                  IF NEW."Kind"='Initial' THEN
                    IF NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."ContractStart"=NEW."ContractStart"
                      AND e."ContractEnd" IS NOT DISTINCT FROM NEW."ContractEnd" AND date_trunc('month',e."ContractStart")::date=NEW."EffectiveFrom"
                      AND current_setting('academy.contract_initial_employee',true)=e."Id"::text)
                      THEN RAISE EXCEPTION 'Initial employment terms require the new explicit employee transaction' USING ERRCODE='23514'; END IF;
                  ELSIF NEW."Kind"='LegacyBaseline' THEN
                    IF NEW."EffectiveFrom"<>month OR NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId"
                      AND e."ContractStart"=NEW."ContractStart" AND e."ContractEnd" IS NOT DISTINCT FROM NEW."ContractEnd")
                      THEN RAISE EXCEPTION 'Legacy employment baseline starts at review month only' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF NEW."EffectiveFrom"<month OR NEW."EffectiveFrom">(month+interval '12 months')::date OR NEW."EffectiveFrom"<last_version."EffectiveFrom"
                      OR NEW."ContractStart">=NEW."EffectiveFrom"+interval '1 month' OR NEW."ContractEnd"<NEW."EffectiveFrom"
                      OR EXISTS(SELECT 1 FROM "PayrollStatements" p WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."Month">=NEW."EffectiveFrom")
                      OR EXISTS(SELECT 1 FROM "PayrollSettlements" p WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."Month">=NEW."EffectiveFrom")
                      OR EXISTS(SELECT 1 FROM "PrivateCompensationEntries" p WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."Kind"='Correction'
                        AND p."DueDate">=NEW."EffectiveFrom" AND (NEW."ContractStart">=p."DueDate"+interval '1 month' OR NEW."ContractEnd"<p."DueDate"))
                      THEN RAISE EXCEPTION 'Contract change cannot rewrite approved payroll or orphan reviewed corrections' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER contract_source BEFORE INSERT ON "ContractVersions" FOR EACH ROW EXECUTE FUNCTION academy_contract_source();
                CREATE FUNCTION academy_contract_reference_preserved() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF (NEW."ContractStart" IS DISTINCT FROM OLD."ContractStart" OR NEW."ContractEnd" IS DISTINCT FROM OLD."ContractEnd")
                    AND EXISTS(SELECT 1 FROM "ContractVersions" WHERE "EmployeeId"=OLD."Id" AND "AcademyId"=OLD."AcademyId")
                    THEN RAISE EXCEPTION 'Dated employment reference is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER contract_reference_preserved BEFORE UPDATE OF "ContractStart","ContractEnd" ON "Employees" FOR EACH ROW EXECUTE FUNCTION academy_contract_reference_preserved();
                CREATE OR REPLACE FUNCTION academy_private_correction_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE posted numeric; revision integer; today date; current_month date;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT COALESCE(SUM("Delta"),0),COALESCE(MAX("Revision"),0) INTO posted,revision FROM "PrivateCompensationEntries"
                    WHERE "AcademyId"=NEW."AcademyId" AND "DeliveryId"=NEW."DeliveryId" AND "Component"=NEW."Component";
                  SELECT timezone("TimeZone",clock_timestamp())::date INTO today FROM "Academies" WHERE "Id"=NEW."AcademyId" AND "IsActive";
                  current_month := date_trunc('month',today)::date;
                  IF current_month IS NULL OR NEW."Revision"<>revision+1 OR NEW."PostedBefore"<>posted
                    OR NEW."DueDate"<>date_trunc('month',NEW."DueDate")::date OR NEW."DueDate"<current_month OR NEW."DueDate">(current_month+interval '12 months')::date
                    OR NEW."CreatedAtUtc">clock_timestamp()+interval '5 seconds' OR NEW."CutoffAtUtc"<>NEW."CreatedAtUtc"
                    OR EXISTS(SELECT 1 FROM "PayrollStatements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=NEW."DueDate")
                    OR EXISTS(SELECT 1 FROM "PayrollSettlements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=NEW."DueDate")
                    OR NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."IsActive"
                      AND e."Currency"=NEW."Currency" AND EXISTS(SELECT 1 FROM academy_employee_contract(e."AcademyId",e."Id",NEW."DueDate") c WHERE c."ContractStart"<NEW."DueDate"+interval '1 month' AND (c."ContractEnd" IS NULL OR c."ContractEnd">=NEW."DueDate")))
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" actor JOIN "Users" u ON u."Id"=actor."UserId"
                      WHERE actor."Id"=NEW."ActorMembershipId" AND actor."AcademyId"=NEW."AcademyId" AND actor."UserId"=NEW."ActorUserId"
                        AND actor."Role"='AcademyOwner' AND actor."IsActive" AND u."IsActive")
                    OR NOT EXISTS(SELECT 1 FROM "PrivateServiceDeliveries" d JOIN "CoachCompensationAgreements" a ON a."Id"=d."AgreementId" AND a."AcademyId"=d."AcademyId"
                      WHERE d."Id"=NEW."DeliveryId" AND d."AcademyId"=NEW."AcademyId" AND a."Currency"=NEW."Currency")
                    OR NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" r WHERE r."Id"=NEW."ReviewEntryId" AND r."AcademyId"=NEW."AcademyId"
                      AND r."DeliveryId"=NEW."DeliveryId" AND r."Kind"='Review' AND r."FactsHash"=NEW."FactsHash"
                      AND r."PostedBefore"=posted AND r."TargetAmount"=NEW."TargetAmount" AND r."TargetAmount"<posted
                      AND NOT COALESCE((r."SourceSnapshot"->>'RequiresReview')::boolean,true)
                      AND NEW."SourceSnapshot"-'correction'=r."SourceSnapshot" AND NEW."CutoffAtUtc">=r."CutoffAtUtc")
                    OR (NEW."SourceSnapshot"->'correction'->>'reviewEntryId')::uuid IS DISTINCT FROM NEW."ReviewEntryId"
                    OR (NEW."SourceSnapshot"->'correction'->>'month')::date IS DISTINCT FROM NEW."DueDate"
                    OR NOT EXISTS(SELECT 1 FROM "PayrollAdjustments" p WHERE p."Id"=NEW."PayrollAdjustmentId" AND p."AcademyId"=NEW."AcademyId"
                      AND p."EmployeeId"=NEW."EmployeeId" AND p."Currency"=NEW."Currency" AND p."Amount"=-NEW."Delta" AND p."DueDate"=NEW."DueDate"
                      AND p."PerformedByUserId"=NEW."ActorUserId" AND p."AdditionRequestId" IS NULL AND p."Kind"='Deduction')
                    -- A current, unfinalized source uses the existing reversal path, not a deferred correction.
                    OR NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" e WHERE e."AcademyId"=NEW."AcademyId" AND e."DeliveryId"=NEW."DeliveryId" AND e."PayrollAdjustmentId" IS NOT NULL
                      AND (date_trunc('month',e."DueDate")::date<>current_month
                        OR EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=e."AcademyId" AND s."EmployeeId"=e."EmployeeId" AND s."Month"=date_trunc('month',e."DueDate")::date)
                        OR EXISTS(SELECT 1 FROM "PayrollSettlements" s WHERE s."AcademyId"=e."AcademyId" AND s."EmployeeId"=e."EmployeeId" AND s."Month"=date_trunc('month',e."DueDate")::date)))
                    THEN RAISE EXCEPTION 'Private correction requires a preserved review, owner and unfinalized payroll month' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "ContractVersions") THEN RAISE EXCEPTION 'Approved employment terms exist; downgrade refused'; END IF; END $$;
                CREATE OR REPLACE FUNCTION academy_private_correction_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE posted numeric; revision integer; today date; current_month date;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT COALESCE(SUM("Delta"),0),COALESCE(MAX("Revision"),0) INTO posted,revision FROM "PrivateCompensationEntries"
                    WHERE "AcademyId"=NEW."AcademyId" AND "DeliveryId"=NEW."DeliveryId" AND "Component"=NEW."Component";
                  SELECT timezone("TimeZone",clock_timestamp())::date INTO today FROM "Academies" WHERE "Id"=NEW."AcademyId" AND "IsActive";
                  current_month := date_trunc('month',today)::date;
                  IF current_month IS NULL OR NEW."Revision"<>revision+1 OR NEW."PostedBefore"<>posted
                    OR NEW."DueDate"<>date_trunc('month',NEW."DueDate")::date OR NEW."DueDate"<current_month OR NEW."DueDate">(current_month+interval '12 months')::date
                    OR NEW."CreatedAtUtc">clock_timestamp()+interval '5 seconds' OR NEW."CutoffAtUtc"<>NEW."CreatedAtUtc"
                    OR EXISTS(SELECT 1 FROM "PayrollStatements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=NEW."DueDate")
                    OR EXISTS(SELECT 1 FROM "PayrollSettlements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=NEW."DueDate")
                    OR NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."IsActive"
                      AND e."Currency"=NEW."Currency" AND e."ContractStart"<NEW."DueDate"+interval '1 month' AND (e."ContractEnd" IS NULL OR e."ContractEnd">=NEW."DueDate"))
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" actor JOIN "Users" u ON u."Id"=actor."UserId"
                      WHERE actor."Id"=NEW."ActorMembershipId" AND actor."AcademyId"=NEW."AcademyId" AND actor."UserId"=NEW."ActorUserId"
                        AND actor."Role"='AcademyOwner' AND actor."IsActive" AND u."IsActive")
                    OR NOT EXISTS(SELECT 1 FROM "PrivateServiceDeliveries" d JOIN "CoachCompensationAgreements" a ON a."Id"=d."AgreementId" AND a."AcademyId"=d."AcademyId"
                      WHERE d."Id"=NEW."DeliveryId" AND d."AcademyId"=NEW."AcademyId" AND a."Currency"=NEW."Currency")
                    OR NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" r WHERE r."Id"=NEW."ReviewEntryId" AND r."AcademyId"=NEW."AcademyId"
                      AND r."DeliveryId"=NEW."DeliveryId" AND r."Kind"='Review' AND r."FactsHash"=NEW."FactsHash"
                      AND r."PostedBefore"=posted AND r."TargetAmount"=NEW."TargetAmount" AND r."TargetAmount"<posted
                      AND NOT COALESCE((r."SourceSnapshot"->>'RequiresReview')::boolean,true)
                      AND NEW."SourceSnapshot"-'correction'=r."SourceSnapshot" AND NEW."CutoffAtUtc">=r."CutoffAtUtc")
                    OR (NEW."SourceSnapshot"->'correction'->>'reviewEntryId')::uuid IS DISTINCT FROM NEW."ReviewEntryId"
                    OR (NEW."SourceSnapshot"->'correction'->>'month')::date IS DISTINCT FROM NEW."DueDate"
                    OR NOT EXISTS(SELECT 1 FROM "PayrollAdjustments" p WHERE p."Id"=NEW."PayrollAdjustmentId" AND p."AcademyId"=NEW."AcademyId"
                      AND p."EmployeeId"=NEW."EmployeeId" AND p."Currency"=NEW."Currency" AND p."Amount"=-NEW."Delta" AND p."DueDate"=NEW."DueDate"
                      AND p."PerformedByUserId"=NEW."ActorUserId" AND p."AdditionRequestId" IS NULL AND p."Kind"='Deduction')
                    -- A current, unfinalized source uses the existing reversal path, not a deferred correction.
                    OR NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" e WHERE e."AcademyId"=NEW."AcademyId" AND e."DeliveryId"=NEW."DeliveryId" AND e."PayrollAdjustmentId" IS NOT NULL
                      AND (date_trunc('month',e."DueDate")::date<>current_month
                        OR EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=e."AcademyId" AND s."EmployeeId"=e."EmployeeId" AND s."Month"=date_trunc('month',e."DueDate")::date)
                        OR EXISTS(SELECT 1 FROM "PayrollSettlements" s WHERE s."AcademyId"=e."AcademyId" AND s."EmployeeId"=e."EmployeeId" AND s."Month"=date_trunc('month',e."DueDate")::date)))
                    THEN RAISE EXCEPTION 'Private correction requires a preserved review, owner and unfinalized payroll month' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                DROP FUNCTION academy_contract_source() CASCADE;
                DROP FUNCTION academy_contract_reference_preserved() CASCADE;
                DROP FUNCTION academy_employee_contract(uuid,uuid,date);
                """);
            migrationBuilder.DropTable(
                name: "ContractVersions");
        }
    }
}
