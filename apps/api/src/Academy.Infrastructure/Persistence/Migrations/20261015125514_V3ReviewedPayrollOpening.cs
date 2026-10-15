using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ReviewedPayrollOpening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PayrollStatements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "MonthlySalary");

            migrationBuilder.AddColumn<DateOnly>(
                name: "OpeningAsOf",
                table: "PayrollStatements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningReference",
                table: "PayrollStatements",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "EmployeeId" },
                unique: true,
                filter: "\"Kind\"='Opening'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements",
                sql: "EXTRACT(DAY FROM \"Month\")=1 AND ((\"Kind\"='MonthlySalary' AND \"OpeningAsOf\" IS NULL AND \"OpeningReference\" IS NULL AND \"ContractDays\" BETWEEN 1 AND 31 AND \"MonthlySalary\">=0 AND \"BaseDue\">=0 AND \"Deductions\">=0 AND \"Additions\">=0 AND \"NetDue\">0 AND \"NetDue\"=\"BaseDue\"-\"Deductions\"+\"Additions\") OR (\"Kind\"='Opening' AND \"OpeningAsOf\" IS NOT NULL AND EXTRACT(DAY FROM \"OpeningAsOf\")=1 AND \"Month\"=(\"OpeningAsOf\"-INTERVAL '1 month')::date AND length(trim(\"OpeningReference\")) BETWEEN 3 AND 120 AND \"OpeningReference\" IS NOT NULL AND \"ContractDays\"=0 AND \"MonthlySalary\"=0 AND \"BaseDue\"=0 AND \"Deductions\"=0 AND \"Additions\"=0 AND \"PrivateCompensation\"=0 AND \"NetDue\">=0))");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_payroll_opening_boundary() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  IF NEW."Kind"='Opening' THEN
                    IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Academies" a ON a."Id"=m."AcademyId"
                      WHERE m."Id"=NEW."ApprovedByMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."Role"='AcademyOwner'
                        AND m."IsActive" AND m."UserId"=NEW."ActorUserId" AND NEW."OpeningAsOf"<=date_trunc('month',timezone(a."TimeZone",clock_timestamp()))::date)
                      OR EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Month"<NEW."OpeningAsOf")
                      OR EXISTS(SELECT 1 FROM "PayrollSettlements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Month"<NEW."OpeningAsOf")
                      OR (NEW."SourceSnapshot"::jsonb->>'kind') IS DISTINCT FROM 'Opening'
                      OR (NEW."SourceSnapshot"::jsonb->>'asOf') IS DISTINCT FROM NEW."OpeningAsOf"::text
                      OR (NEW."SourceSnapshot"::jsonb->>'amount')::numeric IS DISTINCT FROM NEW."NetDue"
                      OR (NEW."SourceSnapshot"::jsonb->>'reference') IS DISTINCT FROM NEW."OpeningReference"
                      OR (NEW."SourceSnapshot"::jsonb->>'scope') IS DISTINCT FROM 'UnrecordedResidualBeforeBoundaryOnly'
                      OR (NEW."SourceSnapshot"::jsonb->'knownSources'->'employee'->>'Id') IS DISTINCT FROM NEW."EmployeeId"::text
                      OR (NEW."SourceSnapshot"::jsonb->'knownSources'->'employee'->>'AcademyId') IS DISTINCT FROM NEW."AcademyId"::text
                      OR (NEW."SourceSnapshot"::jsonb->'knownSources'->'employee'->>'BranchId') IS DISTINCT FROM NEW."BranchId"::text
                      OR (NEW."SourceSnapshot"::jsonb->'knownSources'->'employee'->>'Currency') IS DISTINCT FROM NEW."Currency"
                      THEN RAISE EXCEPTION 'Invalid opening authority, snapshot or overlapping payroll history' USING ERRCODE='23514'; END IF;
                  ELSIF EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='Opening' AND s."OpeningAsOf">NEW."Month")
                    THEN RAISE EXCEPTION 'Salary predates the reviewed opening boundary' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_opening_boundary BEFORE INSERT ON "PayrollStatements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_opening_boundary();
                CREATE FUNCTION academy_payroll_legacy_opening_boundary() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='Opening' AND s."OpeningAsOf">NEW."Month")
                    THEN RAISE EXCEPTION 'Legacy salary predates the reviewed opening boundary' USING ERRCODE='23514'; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_legacy_opening_boundary BEFORE INSERT ON "PayrollSettlements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_legacy_opening_boundary();
                CREATE FUNCTION academy_payroll_opening_payment_actor() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s JOIN "Employees" e ON e."Id"=s."EmployeeId" AND e."AcademyId"=s."AcademyId"
                    JOIN "AcademyMemberships" m ON m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=s."AcademyId"
                    JOIN "AcademyMemberships" self ON self."Id"=e."MembershipId" AND self."UserId"=m."UserId"
                    WHERE s."Id"=NEW."StatementId" AND s."AcademyId"=NEW."AcademyId" AND s."Kind"='Opening' AND m."Role"<>'AcademyOwner')
                    THEN RAISE EXCEPTION 'Cannot approve own opening payout through another membership' USING ERRCODE='23514'; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_opening_payment_actor BEFORE INSERT ON "PayrollDisbursements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_opening_payment_actor();
                CREATE FUNCTION academy_payroll_opening_currency() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||OLD."AcademyId"::text||':'||OLD."Id"::text,0));
                  IF NEW."Currency" IS DISTINCT FROM OLD."Currency" AND EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=OLD."AcademyId" AND s."EmployeeId"=OLD."Id" AND s."Kind"='Opening')
                    THEN RAISE EXCEPTION 'Reviewed opening currency is immutable' USING ERRCODE='23514'; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_opening_currency BEFORE UPDATE ON "Employees" FOR EACH ROW EXECUTE FUNCTION academy_payroll_opening_currency();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PayrollStatements" WHERE "Kind"='Opening') THEN RAISE EXCEPTION 'Reviewed openings exist; downgrade refused'; END IF; END $$;
                DROP FUNCTION academy_payroll_opening_boundary() CASCADE;
                DROP FUNCTION academy_payroll_legacy_opening_boundary() CASCADE;
                DROP FUNCTION academy_payroll_opening_payment_actor() CASCADE;
                DROP FUNCTION academy_payroll_opening_currency() CASCADE;
                """);
            migrationBuilder.DropIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId",
                table: "PayrollStatements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PayrollStatements");

            migrationBuilder.DropColumn(
                name: "OpeningAsOf",
                table: "PayrollStatements");

            migrationBuilder.DropColumn(
                name: "OpeningReference",
                table: "PayrollStatements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements",
                sql: "EXTRACT(DAY FROM \"Month\")=1 AND \"ContractDays\" BETWEEN 1 AND 31 AND \"MonthlySalary\">=0 AND \"BaseDue\">=0 AND \"Deductions\">=0 AND \"Additions\">=0 AND \"NetDue\">0 AND \"NetDue\"=\"BaseDue\"-\"Deductions\"+\"Additions\"");
        }
    }
}
