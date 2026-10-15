using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ReviewedOpeningCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollPayableCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Delta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAtApproval = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    QuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_PayrollPayableCorrections", x => x.Id);
                    table.UniqueConstraint("AK_PayrollPayableCorrections_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PayrollCorrection_Amounts", "\"Revision\">0 AND \"PreviousAmount\" BETWEEN 0 AND 100000000 AND \"TargetAmount\" BETWEEN 0 AND 100000000 AND \"Delta\"<>0 AND \"Delta\"=\"TargetAmount\"-\"PreviousAmount\" AND \"PaidAtApproval\">=0 AND \"TargetAmount\">=\"PaidAtApproval\" AND length(trim(\"Reference\")) BETWEEN 3 AND 120 AND length(\"QuoteHash\")=64 AND length(\"RequestHash\")=64 AND length(\"RequestKey\") BETWEEN 8 AND 100");
                    table.ForeignKey(
                        name: "FK_PayrollPayableCorrections_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPayableCorrections_AcademyMemberships_AcademyId_Acto~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPayableCorrections_PayrollStatements_AcademyId_State~",
                        columns: x => new { x.AcademyId, x.StatementId, x.EmployeeId, x.BranchId, x.Currency },
                        principalTable: "PayrollStatements",
                        principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "Currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPayableCorrections_AcademyId_ActorMembershipId_Reque~",
                table: "PayrollPayableCorrections",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestKey" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_payroll_opening_correction_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE original numeric; previous numeric; paid numeric; revision integer; source_time timestamptz; last_time timestamptz;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT s."NetDue",s."CreatedAtUtc" INTO original,source_time FROM "PayrollStatements" s
                    WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."StatementId" AND s."EmployeeId"=NEW."EmployeeId"
                      AND s."BranchId"=NEW."BranchId" AND s."Currency"=NEW."Currency" AND s."Kind"='Opening';
                  SELECT original+COALESCE(SUM(c."Delta"),0),COALESCE(MAX(c."Revision"),0),MAX(c."CreatedAtUtc") INTO previous,revision,last_time
                    FROM "PayrollPayableCorrections" c WHERE c."AcademyId"=NEW."AcademyId" AND c."StatementId"=NEW."StatementId";
                  SELECT COALESCE(SUM(p."Amount"),0),GREATEST(last_time,MAX(p."CreatedAtUtc")) INTO paid,last_time
                    FROM "PayrollDisbursements" p WHERE p."AcademyId"=NEW."AcademyId" AND p."StatementId"=NEW."StatementId";
                  IF original IS NULL OR NEW."Revision"<>revision+1 OR NEW."PreviousAmount"<>previous OR NEW."PaidAtApproval"<>paid
                    OR NEW."TargetAmount"<paid OR NEW."CreatedAtUtc"<source_time OR NEW."CreatedAtUtc"<last_time OR NEW."CreatedAtUtc">clock_timestamp()+INTERVAL '5 seconds'
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" JOIN "Academies" a ON a."Id"=m."AcademyId"
                      WHERE m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."ActorUserId"
                        AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND a."IsActive")
                    THEN RAISE EXCEPTION 'Invalid reviewed opening correction, actor or balance chain' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_opening_correction_source BEFORE INSERT ON "PayrollPayableCorrections" FOR EACH ROW EXECUTE FUNCTION academy_payroll_opening_correction_source();
                CREATE TRIGGER payroll_opening_correction_immutable BEFORE UPDATE OR DELETE ON "PayrollPayableCorrections" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE OR REPLACE FUNCTION academy_payroll_disbursement_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE due numeric; paid numeric; seq integer;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT s."NetDue"+CASE WHEN s."Kind"='Opening' THEN COALESCE((SELECT SUM(c."Delta") FROM "PayrollPayableCorrections" c WHERE c."AcademyId"=s."AcademyId" AND c."StatementId"=s."Id"),0) ELSE 0 END INTO due
                    FROM "PayrollStatements" s WHERE s."Id"=NEW."StatementId" AND s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."BranchId"=NEW."BranchId" AND s."Currency"=NEW."Currency";
                  SELECT COALESCE(SUM("Amount"),0),COALESCE(MAX("Sequence"),0) INTO paid,seq FROM "PayrollDisbursements" WHERE "StatementId"=NEW."StatementId" AND "AcademyId"=NEW."AcademyId";
                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."Id"=NEW."StatementId" AND s."AcademyId"=NEW."AcademyId" AND s."Kind"='Opening'
                    AND (NEW."CreatedAtUtc"<s."CreatedAtUtc" OR NEW."CreatedAtUtc">clock_timestamp()+INTERVAL '5 seconds' OR EXISTS(SELECT 1 FROM "PayrollPayableCorrections" c WHERE c."AcademyId"=s."AcademyId" AND c."StatementId"=s."Id" AND c."CreatedAtUtc">NEW."CreatedAtUtc")))
                    THEN RAISE EXCEPTION 'Opening payout cannot precede its approved source or correction' USING ERRCODE='23514'; END IF;
                  IF due IS NULL OR NEW."PaidBefore"<>paid OR NEW."Sequence"<>seq+1 OR NEW."Amount">due-paid OR NEW."RemainingAfter"<>due-paid-NEW."Amount"
                    OR EXISTS(SELECT 1 FROM "PayrollSettlements" p JOIN "PayrollStatements" s ON s."EmployeeId"=p."EmployeeId" AND s."AcademyId"=p."AcademyId" AND s."Month"=p."Month" WHERE s."Id"=NEW."StatementId" AND s."Kind"<>'PrivateFinal')
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."IsActive" AND u."IsActive" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                    THEN RAISE EXCEPTION 'Invalid payroll payout sequence, balance or actor' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPayableCorrections_AcademyId_StatementId_EmployeeId_~",
                table: "PayrollPayableCorrections",
                columns: new[] { "AcademyId", "StatementId", "EmployeeId", "BranchId", "Currency" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPayableCorrections_AcademyId_StatementId_Revision",
                table: "PayrollPayableCorrections",
                columns: new[] { "AcademyId", "StatementId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PayrollPayableCorrections") THEN RAISE EXCEPTION 'Reviewed corrections exist; preserve approved history' USING ERRCODE='23514'; END IF; END $$;
                DROP TRIGGER payroll_opening_correction_source ON "PayrollPayableCorrections";
                DROP TRIGGER payroll_opening_correction_immutable ON "PayrollPayableCorrections";
                DROP FUNCTION academy_payroll_opening_correction_source();
                CREATE OR REPLACE FUNCTION academy_payroll_disbursement_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE due numeric; paid numeric; seq integer;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT "NetDue" INTO due FROM "PayrollStatements" WHERE "Id"=NEW."StatementId" AND "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "BranchId"=NEW."BranchId" AND "Currency"=NEW."Currency";
                  SELECT COALESCE(SUM("Amount"),0),COALESCE(MAX("Sequence"),0) INTO paid,seq FROM "PayrollDisbursements" WHERE "StatementId"=NEW."StatementId" AND "AcademyId"=NEW."AcademyId";
                  IF due IS NULL OR NEW."PaidBefore"<>paid OR NEW."Sequence"<>seq+1 OR NEW."Amount">due-paid OR NEW."RemainingAfter"<>due-paid-NEW."Amount"
                    OR EXISTS(SELECT 1 FROM "PayrollSettlements" p JOIN "PayrollStatements" s ON s."EmployeeId"=p."EmployeeId" AND s."AcademyId"=p."AcademyId" AND s."Month"=p."Month" WHERE s."Id"=NEW."StatementId" AND s."Kind"<>'PrivateFinal')
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."IsActive" AND u."IsActive" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                    THEN RAISE EXCEPTION 'Invalid payroll payout sequence, balance or actor' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "PayrollPayableCorrections");
        }
    }
}
