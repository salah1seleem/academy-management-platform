using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ReviewedPrivateCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Keep the original posting/reversal guard unchanged for its original kinds.
                DROP TRIGGER private_entry_source ON "PrivateCompensationEntries";
                CREATE TRIGGER private_entry_source BEFORE INSERT ON "PrivateCompensationEntries"
                  FOR EACH ROW WHEN (NEW."Kind" <> 'Correction') EXECUTE FUNCTION academy_private_entry_source();
                CREATE FUNCTION academy_private_correction_source() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER private_correction_source BEFORE INSERT ON "PrivateCompensationEntries"
                  FOR EACH ROW WHEN (NEW."Kind" = 'Correction') EXECUTE FUNCTION academy_private_correction_source();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries",
                sql: "\"Component\"='PrivateVariable' AND \"Revision\">0 AND \"PostedBefore\">=0 AND (\"TargetAmount\" IS NULL OR \"TargetAmount\">=0) AND ((\"Kind\"='Review' AND \"Delta\"=0 AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Accrual' AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\" IN ('Reversal','Correction') AND \"Delta\"<0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PrivateCompensationEntries" WHERE "Kind"='Correction') THEN RAISE EXCEPTION 'Preserved private corrections exist; downgrade refused'; END IF; END $$;
                DROP FUNCTION academy_private_correction_source() CASCADE;
                DROP TRIGGER private_entry_source ON "PrivateCompensationEntries";
                CREATE TRIGGER private_entry_source BEFORE INSERT ON "PrivateCompensationEntries" FOR EACH ROW EXECUTE FUNCTION academy_private_entry_source();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrivateEntry_Amounts",
                table: "PrivateCompensationEntries",
                sql: "\"Component\"='PrivateVariable' AND \"Revision\">0 AND \"PostedBefore\">=0 AND (\"TargetAmount\" IS NULL OR \"TargetAmount\">=0) AND ((\"Kind\"='Review' AND \"Delta\"=0 AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Accrual' AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Reversal' AND \"Delta\"<0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NOT NULL))");
        }
    }
}
