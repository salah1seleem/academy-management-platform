using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ApprovedPayrollDisbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ContractDays = table.Column<int>(type: "integer", nullable: false),
                    BaseDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Additions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PrivateCompensation = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceSnapshot = table.Column<string>(type: "text", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_PayrollStatements", x => x.Id);
                    table.UniqueConstraint("AK_PayrollStatements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PayrollStatements_AcademyId_Id_EmployeeId_BranchId_Currency", x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.Currency });
                    table.CheckConstraint("CK_PayrollStatement_Amounts", "EXTRACT(DAY FROM \"Month\")=1 AND \"ContractDays\" BETWEEN 1 AND 31 AND \"MonthlySalary\">=0 AND \"BaseDue\">=0 AND \"Deductions\">=0 AND \"Additions\">=0 AND \"NetDue\">0 AND \"NetDue\"=\"BaseDue\"-\"Deductions\"+\"Additions\"");
                    table.ForeignKey(
                        name: "FK_PayrollStatements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatements_AcademyMemberships_AcademyId_ApprovedByMe~",
                        columns: x => new { x.AcademyId, x.ApprovedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatements_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStatements_Employees_AcademyId_EmployeeId",
                        columns: x => new { x.AcademyId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollDisbursements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
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
                    table.PrimaryKey("PK_PayrollDisbursements", x => x.Id);
                    table.UniqueConstraint("AK_PayrollDisbursements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PayrollDisbursement_Amounts", "\"Amount\">0 AND \"PaidBefore\">=0 AND \"RemainingAfter\">=0 AND \"Sequence\">0");
                    table.ForeignKey(
                        name: "FK_PayrollDisbursements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollDisbursements_AcademyMemberships_AcademyId_ActorMemb~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollDisbursements_PayrollStatements_AcademyId_StatementI~",
                        columns: x => new { x.AcademyId, x.StatementId, x.EmployeeId, x.BranchId, x.Currency },
                        principalTable: "PayrollStatements",
                        principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "Currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDisbursements_AcademyId_ActorMembershipId_RequestKey",
                table: "PayrollDisbursements",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDisbursements_AcademyId_StatementId_EmployeeId_Branc~",
                table: "PayrollDisbursements",
                columns: new[] { "AcademyId", "StatementId", "EmployeeId", "BranchId", "Currency" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDisbursements_AcademyId_StatementId_Sequence",
                table: "PayrollDisbursements",
                columns: new[] { "AcademyId", "StatementId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_ApprovedByMembershipId_RequestK~",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "ApprovedByMembershipId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_BranchId",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStatements_AcademyId_EmployeeId_Month",
                table: "PayrollStatements",
                columns: new[] { "AcademyId", "EmployeeId", "Month" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER payroll_statement_immutable BEFORE UPDATE OR DELETE ON "PayrollStatements" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE TRIGGER payroll_disbursement_immutable BEFORE UPDATE OR DELETE ON "PayrollDisbursements" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE FUNCTION academy_payroll_statement_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
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
                CREATE TRIGGER payroll_statement_source BEFORE INSERT ON "PayrollStatements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_statement_source();
                CREATE FUNCTION academy_payroll_disbursement_source() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER payroll_disbursement_source BEFORE INSERT ON "PayrollDisbursements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_disbursement_source();
                CREATE FUNCTION academy_payroll_adjustment_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE a uuid; person record;
                BEGIN
                  a:=COALESCE(NEW."AcademyId",OLD."AcademyId");
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(a) THEN RETURN OLD; END IF;
                  FOR person IN SELECT e."AcademyId",e."Id" FROM "Employees" e WHERE
                    (TG_OP<>'DELETE' AND e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."EmployeeId") OR (TG_OP<>'INSERT' AND e."AcademyId"=OLD."AcademyId" AND e."Id"=OLD."EmployeeId") ORDER BY e."AcademyId",e."Id" LOOP
                    PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||person."AcademyId"::text||':'||person."Id"::text,0));
                  END LOOP;
                  IF (TG_OP<>'INSERT' AND EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=OLD."AcademyId" AND s."EmployeeId"=OLD."EmployeeId" AND s."Month"=date_trunc('month',OLD."DueDate")::date))
                    OR (TG_OP<>'DELETE' AND EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Month"=date_trunc('month',NEW."DueDate")::date))
                    THEN RAISE EXCEPTION 'Approved payroll adjustments are frozen' USING ERRCODE='23514'; END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_adjustment_frozen BEFORE INSERT OR UPDATE OR DELETE ON "PayrollAdjustments" FOR EACH ROW EXECUTE FUNCTION academy_payroll_adjustment_frozen();
                CREATE FUNCTION academy_payroll_attendance_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE a uuid; person record; changed boolean;
                BEGIN
                  a:=COALESCE(NEW."AcademyId",OLD."AcademyId");
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(a) THEN RETURN OLD; END IF;
                  FOR person IN SELECT e."AcademyId",e."Id" FROM "Employees" e WHERE
                    (TG_OP<>'DELETE' AND e."AcademyId"=NEW."AcademyId" AND e."MembershipId"=NEW."MembershipId") OR (TG_OP<>'INSERT' AND e."AcademyId"=OLD."AcademyId" AND e."MembershipId"=OLD."MembershipId") ORDER BY e."AcademyId",e."Id" LOOP
                    PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||person."AcademyId"::text||':'||person."Id"::text,0));
                  END LOOP;
                  changed:=TG_OP<>'UPDATE' OR NEW."ApprovedDeduction" IS DISTINCT FROM OLD."ApprovedDeduction" OR NEW."DeductionReason" IS DISTINCT FROM OLD."DeductionReason" OR NEW."WorkDate" IS DISTINCT FROM OLD."WorkDate" OR NEW."MembershipId" IS DISTINCT FROM OLD."MembershipId";
                  IF changed AND (COALESCE(NEW."ApprovedDeduction",0)>0 OR COALESCE(OLD."ApprovedDeduction",0)>0)
                    AND EXISTS(SELECT 1 FROM "PayrollStatements" s JOIN "Employees" e ON e."Id"=s."EmployeeId" AND e."AcademyId"=s."AcademyId" WHERE
                      (TG_OP<>'DELETE' AND e."AcademyId"=NEW."AcademyId" AND e."MembershipId"=NEW."MembershipId" AND s."Month"=date_trunc('month',NEW."WorkDate")::date)
                      OR (TG_OP<>'INSERT' AND e."AcademyId"=OLD."AcademyId" AND e."MembershipId"=OLD."MembershipId" AND s."Month"=date_trunc('month',OLD."WorkDate")::date))
                    THEN RAISE EXCEPTION 'Approved payroll attendance deduction is frozen' USING ERRCODE='23514'; END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_attendance_frozen BEFORE INSERT OR UPDATE OR DELETE ON "StaffDailyAttendances" FOR EACH ROW EXECUTE FUNCTION academy_payroll_attendance_frozen();
                CREATE FUNCTION academy_payroll_employee_frozen() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||OLD."AcademyId"::text||':'||OLD."Id"::text,0));
                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=OLD."AcademyId" AND s."EmployeeId"=OLD."Id") AND
                    (NEW."ContractStart" IS DISTINCT FROM OLD."ContractStart" OR NEW."ContractEnd" IS DISTINCT FROM OLD."ContractEnd" OR NEW."BranchId" IS DISTINCT FROM OLD."BranchId" OR NEW."MembershipId" IS DISTINCT FROM OLD."MembershipId" OR NEW."MonthlySalary" IS DISTINCT FROM OLD."MonthlySalary")
                    THEN RAISE EXCEPTION 'Approved payroll employment source is frozen' USING ERRCODE='23514'; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_employee_frozen BEFORE UPDATE ON "Employees" FOR EACH ROW EXECUTE FUNCTION academy_payroll_employee_frozen();
                CREATE FUNCTION academy_payroll_salary_frozen() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  IF NEW."Kind"='Change' AND EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."EmployeeId"=NEW."EmployeeId" AND s."AcademyId"=NEW."AcademyId" AND s."Month">=NEW."EffectiveFrom")
                    THEN RAISE EXCEPTION 'Salary version affects an approved statement' USING ERRCODE='23514'; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_salary_frozen BEFORE INSERT ON "SalaryVersions" FOR EACH ROW EXECUTE FUNCTION academy_payroll_salary_frozen();
                CREATE FUNCTION academy_payroll_legacy_exclusive() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  IF EXISTS(SELECT 1 FROM "PayrollStatements" s WHERE s."AcademyId"=NEW."AcademyId" AND s."EmployeeId"=NEW."EmployeeId" AND s."Month"=NEW."Month") THEN RAISE EXCEPTION 'Cannot duplicate an approved payroll payout through legacy settlement' USING ERRCODE='23514'; END IF;RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_legacy_exclusive BEFORE INSERT ON "PayrollSettlements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_legacy_exclusive();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PayrollStatements") THEN RAISE EXCEPTION 'Approved payroll sources exist; downgrade refused'; END IF; END $$;
                DROP FUNCTION academy_payroll_statement_source() CASCADE;
                DROP FUNCTION academy_payroll_disbursement_source() CASCADE;
                DROP FUNCTION academy_payroll_adjustment_frozen() CASCADE;
                DROP FUNCTION academy_payroll_attendance_frozen() CASCADE;
                DROP FUNCTION academy_payroll_employee_frozen() CASCADE;
                DROP FUNCTION academy_payroll_salary_frozen() CASCADE;
                DROP FUNCTION academy_payroll_legacy_exclusive() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "PayrollDisbursements");

            migrationBuilder.DropTable(
                name: "PayrollStatements");
        }
    }
}
