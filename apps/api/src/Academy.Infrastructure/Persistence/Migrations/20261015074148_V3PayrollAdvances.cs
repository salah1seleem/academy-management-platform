using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PayrollAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceAppliedBefore",
                table: "PayrollDisbursements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceId",
                table: "PayrollDisbursements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceRemainingAfter",
                table: "PayrollDisbursements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollAdvances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_PayrollAdvances", x => x.Id);
                    table.UniqueConstraint("AK_PayrollAdvances_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PayrollAdvances_AcademyId_Id_EmployeeId_BranchId_Currency", x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.Currency });
                    table.CheckConstraint("CK_PayrollAdvance_Amount", "\"Amount\">0");
                    table.ForeignKey(
                        name: "FK_PayrollAdvances_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdvances_AcademyMemberships_AcademyId_ActorMembershi~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdvances_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdvances_Employees_AcademyId_EmployeeId",
                        columns: x => new { x.AcademyId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDisbursements_AcademyId_AdvanceId_EmployeeId_BranchI~",
                table: "PayrollDisbursements",
                columns: new[] { "AcademyId", "AdvanceId", "EmployeeId", "BranchId", "Currency" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollDisbursement_Advance",
                table: "PayrollDisbursements",
                sql: "(\"AdvanceId\" IS NULL AND \"AdvanceAppliedBefore\" IS NULL AND \"AdvanceRemainingAfter\" IS NULL) OR (\"AdvanceId\" IS NOT NULL AND \"AdvanceAppliedBefore\" IS NOT NULL AND \"AdvanceRemainingAfter\" IS NOT NULL AND \"AdvanceAppliedBefore\">=0 AND \"AdvanceRemainingAfter\">=0)");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdvances_AcademyId_ActorMembershipId_RequestKey",
                table: "PayrollAdvances",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdvances_AcademyId_BranchId",
                table: "PayrollAdvances",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdvances_AcademyId_EmployeeId",
                table: "PayrollAdvances",
                columns: new[] { "AcademyId", "EmployeeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollDisbursements_PayrollAdvances_AcademyId_AdvanceId_Em~",
                table: "PayrollDisbursements",
                columns: new[] { "AcademyId", "AdvanceId", "EmployeeId", "BranchId", "Currency" },
                principalTable: "PayrollAdvances",
                principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "Currency" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE TRIGGER payroll_advance_immutable BEFORE UPDATE OR DELETE ON "PayrollAdvances" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE FUNCTION academy_payroll_advance_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  IF NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" m ON m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=e."AcademyId" JOIN "Users" u ON u."Id"=m."UserId"
                    WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."IsActive" AND e."BranchId"=NEW."BranchId" AND e."Currency"=NEW."Currency"
                    AND m."IsActive" AND u."IsActive" AND m."UserId"=NEW."ActorUserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin')
                    AND (m."Role"='AcademyOwner' OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" self WHERE self."Id"=e."MembershipId" AND self."UserId"=m."UserId")))
                    OR NEW."CreatedAtUtc">clock_timestamp()+interval '5 seconds'
                    THEN RAISE EXCEPTION 'Invalid recorded employee advance source or actor' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_advance_source BEFORE INSERT ON "PayrollAdvances" FOR EACH ROW EXECUTE FUNCTION academy_payroll_advance_source();
                CREATE FUNCTION academy_payroll_advance_application() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE original numeric; applied numeric; recorded timestamptz;
                BEGIN
                  IF NEW."AdvanceId" IS NULL THEN RETURN NEW; END IF;
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT "Amount","CreatedAtUtc" INTO original,recorded FROM "PayrollAdvances" WHERE "Id"=NEW."AdvanceId" AND "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" AND "BranchId"=NEW."BranchId" AND "Currency"=NEW."Currency";
                  SELECT COALESCE(SUM("Amount"),0) INTO applied FROM "PayrollDisbursements" WHERE "AdvanceId"=NEW."AdvanceId" AND "AcademyId"=NEW."AcademyId";
                  IF original IS NULL OR NEW."AdvanceAppliedBefore"<>applied OR NEW."Amount">original-applied OR NEW."AdvanceRemainingAfter"<>original-applied-NEW."Amount" OR NEW."CreatedAtUtc"<recorded
                    OR EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" m ON m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=e."AcademyId" JOIN "AcademyMemberships" self ON self."Id"=e."MembershipId" AND self."UserId"=m."UserId" WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND m."Role"<>'AcademyOwner')
                    THEN RAISE EXCEPTION 'Invalid employee advance allocation or over-application' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_advance_application_source BEFORE INSERT ON "PayrollDisbursements" FOR EACH ROW EXECUTE FUNCTION academy_payroll_advance_application();
                CREATE FUNCTION academy_payroll_advance_identity() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||OLD."AcademyId"::text||':'||OLD."Id"::text,0));
                  IF EXISTS(SELECT 1 FROM "PayrollAdvances" a WHERE a."AcademyId"=OLD."AcademyId" AND a."EmployeeId"=OLD."Id")
                    AND (NEW."AcademyId" IS DISTINCT FROM OLD."AcademyId" OR NEW."BranchId" IS DISTINCT FROM OLD."BranchId" OR NEW."MembershipId" IS DISTINCT FROM OLD."MembershipId" OR NEW."Currency" IS DISTINCT FROM OLD."Currency")
                    THEN RAISE EXCEPTION 'Employee advance identity is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER payroll_advance_identity BEFORE UPDATE ON "Employees" FOR EACH ROW EXECUTE FUNCTION academy_payroll_advance_identity();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PayrollAdvances") THEN RAISE EXCEPTION 'Cannot remove recorded employee advances'; END IF; END $$;
                DROP FUNCTION academy_payroll_advance_source() CASCADE;
                DROP FUNCTION academy_payroll_advance_application() CASCADE;
                DROP FUNCTION academy_payroll_advance_identity() CASCADE;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollDisbursements_PayrollAdvances_AcademyId_AdvanceId_Em~",
                table: "PayrollDisbursements");

            migrationBuilder.DropTable(
                name: "PayrollAdvances");

            migrationBuilder.DropIndex(
                name: "IX_PayrollDisbursements_AcademyId_AdvanceId_EmployeeId_BranchI~",
                table: "PayrollDisbursements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollDisbursement_Advance",
                table: "PayrollDisbursements");

            migrationBuilder.DropColumn(
                name: "AdvanceAppliedBefore",
                table: "PayrollDisbursements");

            migrationBuilder.DropColumn(
                name: "AdvanceId",
                table: "PayrollDisbursements");

            migrationBuilder.DropColumn(
                name: "AdvanceRemainingAfter",
                table: "PayrollDisbursements");
        }
    }
}
