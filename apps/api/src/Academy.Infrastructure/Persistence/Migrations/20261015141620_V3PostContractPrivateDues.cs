using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PostContractPrivateDues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries");

            migrationBuilder.DropIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId_Month",
                table: "PayrollStatements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements");

            migrationBuilder.AddColumn<Guid>(
                name: "FinalStatementId",
                table: "PrivateCompensationEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrivateReviewId",
                table: "PayrollStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_FinalStatementId",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "FinalStatementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_FinalStatementId_Emplo~",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "FinalStatementId", "EmployeeId", "BranchId", "Currency" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries",
                sql: "\"Component\"='PrivateVariable' AND \"Revision\">0 AND \"PostedBefore\">=0 AND (\"TargetAmount\" IS NULL OR \"TargetAmount\">=0) AND ((\"Kind\"='PrivateFinal' AND \"FinalStatementId\" IS NOT NULL AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NOT NULL) OR (\"FinalStatementId\" IS NULL AND ((\"Kind\"='Review' AND \"Delta\"=0 AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Accrual' AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NULL) OR (((\"Kind\"='Reversal' AND \"Delta\"<0) OR (\"Kind\"='Correction' AND \"Delta\"<>0)) AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NOT NULL))))");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId_Month",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "EmployeeId", "Month" },
                unique: true,
                filter: "\"Kind\"<>'PrivateFinal'");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_PrivateReviewId",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "PrivateReviewId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements",
                sql: "EXTRACT(DAY FROM \"Month\")=1 AND (((\"Kind\"='PrivateFinal' AND \"PrivateReviewId\" IS NOT NULL AND \"OpeningAsOf\" IS NULL AND \"OpeningReference\" IS NULL AND \"ContractDays\"=0 AND \"MonthlySalary\"=0 AND \"BaseDue\"=0 AND \"Deductions\"=0 AND \"Additions\"=0 AND \"PrivateCompensation\"=\"NetDue\" AND \"NetDue\">0)) OR (\"PrivateReviewId\" IS NULL AND ((\"Kind\"='MonthlySalary' AND \"OpeningAsOf\" IS NULL AND \"OpeningReference\" IS NULL AND \"ContractDays\" BETWEEN 1 AND 31 AND \"MonthlySalary\">=0 AND \"BaseDue\">=0 AND \"Deductions\">=0 AND \"Additions\">=0 AND \"NetDue\">0 AND \"NetDue\"=\"BaseDue\"-\"Deductions\"+\"Additions\") OR (\"Kind\"='Opening' AND \"OpeningAsOf\" IS NOT NULL AND EXTRACT(DAY FROM \"OpeningAsOf\")=1 AND \"Month\"=(\"OpeningAsOf\"-INTERVAL '1 month')::date AND length(trim(\"OpeningReference\")) BETWEEN 3 AND 120 AND \"OpeningReference\" IS NOT NULL AND \"ContractDays\"=0 AND \"MonthlySalary\"=0 AND \"BaseDue\"=0 AND \"Deductions\"=0 AND \"Additions\"=0 AND \"PrivateCompensation\"=0 AND \"NetDue\">=0))))");

            migrationBuilder.AddForeignKey(
                name: "FK_PrivateCompensationEntries_PayrollStatements_AcademyId_Fina~",
                table: "PrivateCompensationEntries",
                columns: new[] { "AcademyId", "FinalStatementId", "EmployeeId", "BranchId", "Currency" },
                principalTable: "PayrollStatements",
                principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "Currency" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_payroll_statement_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                
                                  IF NEW."Kind"='PrivateFinal' THEN
                                    IF NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" actor ON actor."AcademyId"=e."AcademyId" JOIN "Users" u ON u."Id"=actor."UserId" JOIN "Academies" a ON a."Id"=e."AcademyId"
                                      JOIN "PrivateCompensationEntries" r ON r."Id"=NEW."PrivateReviewId" AND r."AcademyId"=e."AcademyId" AND r."EmployeeId"=e."Id"
                                      WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."BranchId"=NEW."BranchId" AND e."Currency"=NEW."Currency"
                                        AND actor."Id"=NEW."ApprovedByMembershipId" AND actor."UserId"=NEW."ActorUserId" AND actor."Role"='AcademyOwner' AND actor."IsActive" AND u."IsActive" AND a."IsActive"
                                        AND r."Kind"='Review' AND r."TargetAmount">r."PostedBefore" AND r."Currency"=NEW."Currency" AND r."BranchId"=NEW."BranchId"
                                        AND NOT COALESCE((r."SourceSnapshot"->>'RequiresReview')::boolean,true)
                                        AND r."TargetAmount"-r."PostedBefore"=NEW."NetDue"
                                        AND r."PostedBefore"=(SELECT COALESCE(SUM(c."Delta"),0) FROM "PrivateCompensationEntries" c WHERE c."AcademyId"=r."AcademyId" AND c."DeliveryId"=r."DeliveryId")
                                        AND NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" c WHERE c."AcademyId"=r."AcademyId" AND c."ReviewEntryId"=r."Id")
                                        AND EXISTS(SELECT 1 FROM academy_employee_contract(e."AcademyId",e."Id",timezone(a."TimeZone",clock_timestamp())::date) terms WHERE terms."ContractEnd"<date_trunc('month',timezone(a."TimeZone",clock_timestamp()))::date AND terms."ContractEnd"=(NEW."SourceSnapshot"::jsonb->>'contractEnd')::date)
                                        AND NEW."Month"=date_trunc('month',timezone(a."TimeZone",clock_timestamp()))::date
                                        AND NEW."CreatedAtUtc">=r."CreatedAtUtc" AND NEW."CreatedAtUtc"<=clock_timestamp()+interval '5 seconds'
                                        AND NEW."SourceSnapshot"::jsonb->>'kind'='PrivateFinal'
                                        AND (NEW."SourceSnapshot"::jsonb->>'reviewEntryId')::uuid=r."Id"
                                        AND (NEW."SourceSnapshot"::jsonb->>'deliveryId')::uuid=r."DeliveryId"
                                        AND NEW."SourceSnapshot"::jsonb->>'FactsHash'=r."FactsHash")
                                      THEN RAISE EXCEPTION 'Post-contract private due requires owner, ended employment and an unconsumed positive review' USING ERRCODE='23514'; END IF;
                                    RETURN NEW;
                                  END IF;
                                  IF EXISTS(SELECT 1 FROM "PayrollSettlements" p WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."Month"=NEW."Month")
                                    OR NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" m ON m."Id"=NEW."ApprovedByMembershipId" AND m."AcademyId"=e."AcademyId" JOIN "Users" u ON u."Id"=m."UserId" JOIN "Academies" a ON a."Id"=e."AcademyId"
                                      WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."IsActive" AND e."Currency"=NEW."Currency" AND e."BranchId"=NEW."BranchId"
                                      AND m."IsActive" AND u."IsActive" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin')
                                      AND NEW."Month"<=date_trunc('month',timezone(a."TimeZone",clock_timestamp()))::date
                                      AND (m."Role"='AcademyOwner' OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" self WHERE self."Id"=e."MembershipId" AND self."UserId"=m."UserId")))
                                    THEN RAISE EXCEPTION 'Invalid approved payroll source or legacy paid month' USING ERRCODE='23514'; END IF;
                                  RETURN NEW;
                                END $$;
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
                CREATE OR REPLACE FUNCTION academy_payroll_opening_payment_actor() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s JOIN "Employees" e ON e."Id"=s."EmployeeId" AND e."AcademyId"=s."AcademyId"
                                    JOIN "AcademyMemberships" m ON m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=s."AcademyId"
                                    JOIN "AcademyMemberships" self ON self."Id"=e."MembershipId" AND self."UserId"=m."UserId"
                                    WHERE s."Id"=NEW."StatementId" AND s."AcademyId"=NEW."AcademyId" AND s."Kind" IN ('Opening','PrivateFinal') AND m."Role"<>'AcademyOwner')
                                    THEN RAISE EXCEPTION 'Cannot approve own opening payout through another membership' USING ERRCODE='23514'; END IF;RETURN NEW;
                                END $$;
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
                                  IF NEW."Kind"='PrivateFinal' THEN
                                    IF NOT EXISTS(SELECT 1 FROM "PayrollStatements" s JOIN "PrivateCompensationEntries" r ON r."Id"=s."PrivateReviewId" AND r."AcademyId"=s."AcademyId"
                                      WHERE s."Id"=NEW."FinalStatementId" AND s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='PrivateFinal'
                                        AND s."NetDue"=NEW."Delta" AND s."PrivateReviewId"=NEW."ReviewEntryId" AND s."ApprovedByMembershipId"=NEW."ActorMembershipId" AND s."ActorUserId"=NEW."ActorUserId"
                                        AND s."CreatedAtUtc"=NEW."CreatedAtUtc" AND s."SourceHash"=NEW."QuoteHash"
                                        AND r."DeliveryId"=NEW."DeliveryId" AND r."FactsHash"=NEW."FactsHash" AND r."PostedBefore"=NEW."PostedBefore" AND r."TargetAmount"=NEW."TargetAmount" AND r."SourceSnapshot"=NEW."SourceSnapshot")
                                      THEN RAISE EXCEPTION 'Final private source and approved employee due must match' USING ERRCODE='23514'; END IF;
                                    RETURN NEW;
                                  END IF;
                                  IF NEW."Kind"='Review' THEN
                                    IF NOT (COALESCE((NEW."SourceSnapshot"->>'RequiresReview')::boolean,false) OR COALESCE(NEW."TargetAmount"<posted,false) OR (NEW."TargetAmount">posted AND EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND (NOT e."IsActive" OR EXISTS(SELECT 1 FROM academy_employee_contract(e."AcademyId",e."Id",today) c WHERE c."ContractEnd"<today)))) OR (COALESCE(NEW."TargetAmount">posted,false) AND (EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='MonthlySalary' AND s."Month"=date_trunc('month',NEW."DueDate")::date) OR EXISTS(SELECT 1 FROM "PayrollSettlements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Month"=date_trunc('month',NEW."DueDate")::date))))
                                      THEN RAISE EXCEPTION 'Private review must describe a real source exception' USING ERRCODE='23514'; END IF;
                                  ELSE
                                    IF NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."IsActive" AND EXISTS(SELECT 1 FROM academy_employee_contract(e."AcademyId",e."Id",today) c WHERE c."ContractStart"<=today AND (c."ContractEnd" IS NULL OR c."ContractEnd">=today)))
                                      OR EXISTS(SELECT 1 FROM "PayrollSettlements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=date_trunc('month',NEW."DueDate")::date)
                                      OR EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='MonthlySalary' AND s."Month"=date_trunc('month',NEW."DueDate")::date)
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
                CREATE FUNCTION academy_private_final_pair() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NOT EXISTS(SELECT 1 FROM "PrivateCompensationEntries" e WHERE e."AcademyId"=NEW."AcademyId" AND e."FinalStatementId"=NEW."Id" AND e."ReviewEntryId"=NEW."PrivateReviewId" AND e."Kind"='PrivateFinal' AND e."Delta"=NEW."NetDue")
                    THEN RAISE EXCEPTION 'Final private due cannot exist without its immutable source entry' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER private_final_pair AFTER INSERT ON "PayrollStatements" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."Kind"='PrivateFinal') EXECUTE FUNCTION academy_private_final_pair();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PayrollStatements" WHERE "Kind"='PrivateFinal') THEN RAISE EXCEPTION 'Post-contract private sources exist; downgrade refused'; END IF; END $$;
                DROP TRIGGER private_final_pair ON "PayrollStatements"; DROP FUNCTION academy_private_final_pair();
                CREATE OR REPLACE FUNCTION academy_payroll_statement_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                                  IF EXISTS(SELECT 1 FROM "PayrollSettlements" p WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."Month"=NEW."Month")
                                    OR NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" m ON m."Id"=NEW."ApprovedByMembershipId" AND m."AcademyId"=e."AcademyId" JOIN "Users" u ON u."Id"=m."UserId" JOIN "Academies" a ON a."Id"=e."AcademyId"
                                      WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."IsActive" AND e."Currency"=NEW."Currency" AND e."BranchId"=NEW."BranchId"
                                      AND m."IsActive" AND u."IsActive" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin')
                                      AND NEW."Month"<=date_trunc('month',timezone(a."TimeZone",clock_timestamp()))::date
                                      AND (m."Role"='AcademyOwner' OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" self WHERE self."Id"=e."MembershipId" AND self."UserId"=m."UserId")))
                                    THEN RAISE EXCEPTION 'Invalid approved payroll source or legacy paid month' USING ERRCODE='23514'; END IF;
                                  RETURN NEW;
                                END $$;
                CREATE OR REPLACE FUNCTION academy_payroll_disbursement_source() RETURNS trigger LANGUAGE plpgsql AS $$
                                DECLARE due numeric; paid numeric; seq integer;
                                BEGIN
                                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                                  SELECT "NetDue" INTO due FROM "PayrollStatements" WHERE "Id"=NEW."StatementId" AND "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "BranchId"=NEW."BranchId" AND "Currency"=NEW."Currency";
                                  SELECT COALESCE(SUM("Amount"),0),COALESCE(MAX("Sequence"),0) INTO paid,seq FROM "PayrollDisbursements" WHERE "StatementId"=NEW."StatementId" AND "AcademyId"=NEW."AcademyId";
                                  IF due IS NULL OR NEW."PaidBefore"<>paid OR NEW."Sequence"<>seq+1 OR NEW."Amount">due-paid OR NEW."RemainingAfter"<>due-paid-NEW."Amount"
                                    OR EXISTS(SELECT 1 FROM "PayrollSettlements" p JOIN "PayrollStatements" s ON s."EmployeeId"=p."EmployeeId" AND s."AcademyId"=p."AcademyId" AND s."Month"=p."Month" WHERE s."Id"=NEW."StatementId")
                                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."IsActive" AND u."IsActive" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                                    THEN RAISE EXCEPTION 'Invalid payroll payout sequence, balance or actor' USING ERRCODE='23514'; END IF;
                                  RETURN NEW;
                                END $$;
                CREATE OR REPLACE FUNCTION academy_payroll_opening_payment_actor() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s JOIN "Employees" e ON e."Id"=s."EmployeeId" AND e."AcademyId"=s."AcademyId"
                                    JOIN "AcademyMemberships" m ON m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=s."AcademyId"
                                    JOIN "AcademyMemberships" self ON self."Id"=e."MembershipId" AND self."UserId"=m."UserId"
                                    WHERE s."Id"=NEW."StatementId" AND s."AcademyId"=NEW."AcademyId" AND s."Kind"='Opening' AND m."Role"<>'AcademyOwner')
                                    THEN RAISE EXCEPTION 'Cannot approve own opening payout through another membership' USING ERRCODE='23514'; END IF;RETURN NEW;
                                END $$;
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
                                    IF NOT (COALESCE((NEW."SourceSnapshot"->>'RequiresReview')::boolean,false) OR COALESCE(NEW."TargetAmount"<posted,false) OR (COALESCE(NEW."TargetAmount">posted,false) AND (EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='MonthlySalary' AND s."Month"=date_trunc('month',NEW."DueDate")::date) OR EXISTS(SELECT 1 FROM "PayrollSettlements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Month"=date_trunc('month',NEW."DueDate")::date))))
                                      THEN RAISE EXCEPTION 'Private review must describe a real source exception' USING ERRCODE='23514'; END IF;
                                  ELSE
                                    IF EXISTS(SELECT 1 FROM "PayrollSettlements" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "Month"=date_trunc('month',NEW."DueDate")::date)
                                      OR EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Kind"='MonthlySalary' AND s."Month"=date_trunc('month',NEW."DueDate")::date)
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
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_PrivateCompensationEntries_PayrollStatements_AcademyId_Fina~",
                table: "PrivateCompensationEntries");

            migrationBuilder.DropIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_FinalStatementId",
                table: "PrivateCompensationEntries");

            migrationBuilder.DropIndex(
                name: "IX_PrivateCompensationEntries_AcademyId_FinalStatementId_Emplo~",
                table: "PrivateCompensationEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries");

            migrationBuilder.DropIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId_Month",
                table: "PayrollStatements");

            migrationBuilder.DropIndex(
                name: "IX_PayrollStatements_AcademyId_PrivateReviewId",
                table: "PayrollStatements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements");

            migrationBuilder.DropColumn(
                name: "FinalStatementId",
                table: "PrivateCompensationEntries");

            migrationBuilder.DropColumn(
                name: "PrivateReviewId",
                table: "PayrollStatements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries",
                sql: "\"Component\"='PrivateVariable' AND \"Revision\">0 AND \"PostedBefore\">=0 AND (\"TargetAmount\" IS NULL OR \"TargetAmount\">=0) AND ((\"Kind\"='Review' AND \"Delta\"=0 AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Accrual' AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NULL) OR (((\"Kind\"='Reversal' AND \"Delta\"<0) OR (\"Kind\"='Correction' AND \"Delta\"<>0)) AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId_Month",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "EmployeeId", "Month" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollStatement_Amounts",
                table: "PayrollStatements",
                sql: "EXTRACT(DAY FROM \"Month\")=1 AND ((\"Kind\"='MonthlySalary' AND \"OpeningAsOf\" IS NULL AND \"OpeningReference\" IS NULL AND \"ContractDays\" BETWEEN 1 AND 31 AND \"MonthlySalary\">=0 AND \"BaseDue\">=0 AND \"Deductions\">=0 AND \"Additions\">=0 AND \"NetDue\">0 AND \"NetDue\"=\"BaseDue\"-\"Deductions\"+\"Additions\") OR (\"Kind\"='Opening' AND \"OpeningAsOf\" IS NOT NULL AND EXTRACT(DAY FROM \"OpeningAsOf\")=1 AND \"Month\"=(\"OpeningAsOf\"-INTERVAL '1 month')::date AND length(trim(\"OpeningReference\")) BETWEEN 3 AND 120 AND \"OpeningReference\" IS NOT NULL AND \"ContractDays\"=0 AND \"MonthlySalary\"=0 AND \"BaseDue\"=0 AND \"Deductions\"=0 AND \"Additions\"=0 AND \"PrivateCompensation\"=0 AND \"NetDue\">=0))");
        }
    }
}
