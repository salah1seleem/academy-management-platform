using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3DatedSalaryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalaryVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                    table.PrimaryKey("PK_SalaryVersions", x => x.Id);
                    table.UniqueConstraint("AK_SalaryVersions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SalaryVersion_Terms", "\"Revision\">0 AND \"MonthlySalary\" BETWEEN 0 AND 100000000 AND EXTRACT(DAY FROM \"EffectiveFrom\")=1 AND \"Kind\" IN ('Initial','LegacyBaseline','Change') AND ((\"Revision\"=1 AND \"PreviousVersionId\" IS NULL AND \"Kind\"<>'Change') OR (\"Revision\">1 AND \"PreviousVersionId\" IS NOT NULL AND \"Kind\"='Change'))");
                    table.ForeignKey(
                        name: "FK_SalaryVersions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVersions_AcademyMemberships_AcademyId_ApprovedByMembe~",
                        columns: x => new { x.AcademyId, x.ApprovedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVersions_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVersions_Employees_AcademyId_EmployeeId",
                        columns: x => new { x.AcademyId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVersions_SalaryVersions_AcademyId_PreviousVersionId",
                        columns: x => new { x.AcademyId, x.PreviousVersionId },
                        principalTable: "SalaryVersions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVersions_AcademyId_ApprovedByMembershipId_RequestKey",
                table: "SalaryVersions",
                columns: new[] { "AcademyId", "ApprovedByMembershipId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVersions_AcademyId_BranchId",
                table: "SalaryVersions",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVersions_AcademyId_EmployeeId_EffectiveFrom",
                table: "SalaryVersions",
                columns: new[] { "AcademyId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVersions_AcademyId_EmployeeId_Revision",
                table: "SalaryVersions",
                columns: new[] { "AcademyId", "EmployeeId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVersions_AcademyId_PreviousVersionId",
                table: "SalaryVersions",
                columns: new[] { "AcademyId", "PreviousVersionId" });
            migrationBuilder.Sql("""
                CREATE TRIGGER salary_version_immutable BEFORE UPDATE OR DELETE ON "SalaryVersions" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE FUNCTION academy_salary_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE last_version "SalaryVersions"%ROWTYPE; month date;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  SELECT * INTO last_version FROM "SalaryVersions" WHERE "AcademyId"=NEW."AcademyId" AND "EmployeeId"=NEW."EmployeeId" ORDER BY "Revision" DESC LIMIT 1;
                  SELECT date_trunc('month',timezone("TimeZone",clock_timestamp()))::date INTO month FROM "Academies" WHERE "Id"=NEW."AcademyId" AND "IsActive";
                  IF month IS NULL OR NEW."Revision"<>COALESCE(last_version."Revision",0)+1 OR NEW."PreviousVersionId" IS DISTINCT FROM last_version."Id"
                    OR NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" actor ON actor."Id"=NEW."ApprovedByMembershipId" AND actor."AcademyId"=e."AcademyId"
                      JOIN "Users" u ON u."Id"=actor."UserId" WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND e."BranchId"=NEW."BranchId" AND e."Currency"=NEW."Currency"
                        AND actor."UserId"=NEW."ActorUserId" AND actor."IsActive" AND u."IsActive" AND actor."Role" IN ('AcademyOwner','AcademyAdmin')
                        AND (actor."Role"='AcademyOwner' OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" self WHERE self."Id"=e."MembershipId" AND self."UserId"=actor."UserId")))
                    THEN RAISE EXCEPTION 'Invalid salary approval source or revision chain' USING ERRCODE='23514'; END IF;
                  IF NEW."Kind"='Initial' THEN
                    IF NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."MonthlySalary"=NEW."MonthlySalary"
                      AND date_trunc('month',e."ContractStart")::date=NEW."EffectiveFrom"
                      AND current_setting('academy.salary_initial_employee',true)=e."Id"::text)
                      THEN RAISE EXCEPTION 'Initial salary requires the new explicit employee contract' USING ERRCODE='23514'; END IF;
                  ELSIF NEW."Kind"='LegacyBaseline' THEN
                    IF NEW."EffectiveFrom"<>month OR NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=NEW."EmployeeId" AND e."MonthlySalary"=NEW."MonthlySalary")
                      THEN RAISE EXCEPTION 'Legacy salary baseline starts at review month only' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF NEW."EffectiveFrom"<month OR NEW."EffectiveFrom"<last_version."EffectiveFrom"
                      OR EXISTS(SELECT 1 FROM "PayrollSettlements" p WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."Month">=NEW."EffectiveFrom")
                      THEN RAISE EXCEPTION 'Salary change cannot rewrite prior or paid months' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER salary_source BEFORE INSERT ON "SalaryVersions" FOR EACH ROW EXECUTE FUNCTION academy_salary_source();
                CREATE FUNCTION academy_salary_reference_preserved() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW."MonthlySalary" IS DISTINCT FROM OLD."MonthlySalary" AND EXISTS(SELECT 1 FROM "SalaryVersions" v WHERE v."EmployeeId"=OLD."Id" AND v."AcademyId"=OLD."AcademyId")
                    THEN RAISE EXCEPTION 'Employee salary reference with approved history is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER salary_reference_preserved BEFORE UPDATE OF "MonthlySalary" ON "Employees" FOR EACH ROW EXECUTE FUNCTION academy_salary_reference_preserved();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "SalaryVersions") THEN RAISE EXCEPTION 'Approved salary history exists; downgrade refused'; END IF; END $$;
                DROP FUNCTION academy_salary_source() CASCADE;
                DROP FUNCTION academy_salary_reference_preserved() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "SalaryVersions");
        }
    }
}
