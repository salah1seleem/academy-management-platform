using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3CoachPrivateAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachCompensationAgreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    BaseSalarySnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VariableMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VariableValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Eligibility = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BaseCoverage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CalculationBasis = table.Column<string>(type: "text", nullable: false),
                    CollectionAllocationPolicy = table.Column<string>(type: "text", nullable: false),
                    SettlementCycle = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachCompensationAgreements", x => x.Id);
                    table.UniqueConstraint("AK_CoachCompensationAgreements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_CoachCompensationAgreements_AcademyId_Id_EmployeeId_BranchI~", x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId });
                    table.CheckConstraint("CK_CoachAgreement_Terms", "\"VariableMethod\" IN ('Percent','PerSession') AND \"Eligibility\" IN ('DeliveredCollected','Delivered') AND \"BaseCoverage\" IN ('GroupOnly','IncludesPrivate') AND \"VariableValue\" >= 0 AND \"VariableValue\" <= CASE WHEN \"VariableMethod\"='Percent' THEN 100 ELSE 100000000 END AND (\"BaseCoverage\" <> 'IncludesPrivate' OR \"VariableValue\"=0) AND (\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\")");
                    table.ForeignKey(
                        name: "FK_CoachCompensationAgreements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationAgreements_AcademyMemberships_AcademyId_Co~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationAgreements_AcademyMemberships_AcademyId_Cr~",
                        columns: x => new { x.AcademyId, x.CreatedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationAgreements_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationAgreements_Employees_AcademyId_EmployeeId",
                        columns: x => new { x.AcademyId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationAgreements_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CoachCompensationApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachCompensationApprovals", x => x.Id);
                    table.UniqueConstraint("AK_CoachCompensationApprovals_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CoachApproval_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_CoachCompensationApprovals_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationApprovals_AcademyMemberships_AcademyId_App~",
                        columns: x => new { x.AcademyId, x.ApprovedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoachCompensationApprovals_CoachCompensationAgreements_Acad~",
                        columns: x => new { x.AcademyId, x.AgreementId, x.EmployeeId, x.BranchId, x.SportId },
                        principalTable: "CoachCompensationAgreements",
                        principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationAgreements_AcademyId_BranchId",
                table: "CoachCompensationAgreements",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationAgreements_AcademyId_CoachMembershipId",
                table: "CoachCompensationAgreements",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationAgreements_AcademyId_CreatedByMembershipId~",
                table: "CoachCompensationAgreements",
                columns: new[] { "AcademyId", "CreatedByMembershipId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationAgreements_AcademyId_EmployeeId",
                table: "CoachCompensationAgreements",
                columns: new[] { "AcademyId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationAgreements_AcademyId_SportId",
                table: "CoachCompensationAgreements",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationApprovals_AcademyId_AgreementId",
                table: "CoachCompensationApprovals",
                columns: new[] { "AcademyId", "AgreementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationApprovals_AcademyId_AgreementId_EmployeeId~",
                table: "CoachCompensationApprovals",
                columns: new[] { "AcademyId", "AgreementId", "EmployeeId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationApprovals_AcademyId_ApprovedByMembershipId",
                table: "CoachCompensationApprovals",
                columns: new[] { "AcademyId", "ApprovedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCompensationApprovals_AcademyId_EmployeeId_BranchId_Sp~",
                table: "CoachCompensationApprovals",
                columns: new[] { "AcademyId", "EmployeeId", "BranchId", "SportId", "Revision" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_coach_terms_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  RAISE EXCEPTION 'Coach agreement terms and approvals are append-only' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER coach_agreement_immutable BEFORE UPDATE OR DELETE ON "CoachCompensationAgreements" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE TRIGGER coach_approval_immutable BEFORE UPDATE OR DELETE ON "CoachCompensationApprovals" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE FUNCTION academy_coach_agreement_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NOT EXISTS(SELECT 1 FROM "Employees" e JOIN "AcademyMemberships" c ON c."Id"=e."MembershipId" AND c."AcademyId"=e."AcademyId"
                    WHERE e."Id"=NEW."EmployeeId" AND e."AcademyId"=NEW."AcademyId" AND c."Id"=NEW."CoachMembershipId" AND c."Role"='Coach' AND c."PrimaryMembershipId" IS NULL AND e."Currency"=NEW."Currency")
                    OR NEW."CalculationBasis"<>'NetAfterDiscountRefundExTaxBeforeGateway' OR NEW."CollectionAllocationPolicy"<>'ProportionalNetCollections' OR NEW."SettlementCycle"<>'Monthly' OR NEW."BaseSalarySnapshot"<0
                    THEN RAISE EXCEPTION 'Invalid coach agreement source or calculation policy' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER coach_agreement_source BEFORE INSERT ON "CoachCompensationAgreements" FOR EACH ROW EXECUTE FUNCTION academy_coach_agreement_source();
                CREATE FUNCTION academy_coach_approval_source() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE previous integer; previous_start date; BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  IF NOT EXISTS(SELECT 1 FROM "CoachCompensationAgreements" a JOIN "Employees" e ON e."Id"=a."EmployeeId" AND e."AcademyId"=a."AcademyId"
                    JOIN "AcademyMemberships" c ON c."Id"=a."CoachMembershipId" AND c."AcademyId"=a."AcademyId"
                    JOIN "AcademyMemberships" actor ON actor."Id"=NEW."ApprovedByMembershipId" AND actor."AcademyId"=a."AcademyId"
                    WHERE a."Id"=NEW."AgreementId" AND a."AcademyId"=NEW."AcademyId" AND e."MembershipId"=c."Id" AND actor."IsActive" AND c."IsActive"
                      AND actor."Role" IN ('AcademyOwner','AcademyAdmin') AND (actor."Role"='AcademyOwner' OR actor."UserId"<>c."UserId"))
                    THEN RAISE EXCEPTION 'Invalid agreement approver or employee binding' USING ERRCODE='23514'; END IF;
                  SELECT p."Revision",a."EffectiveFrom" INTO previous,previous_start FROM "CoachCompensationApprovals" p JOIN "CoachCompensationAgreements" a ON a."Id"=p."AgreementId"
                    WHERE p."AcademyId"=NEW."AcademyId" AND p."EmployeeId"=NEW."EmployeeId" AND p."BranchId"=NEW."BranchId" AND p."SportId"=NEW."SportId" ORDER BY p."Revision" DESC LIMIT 1;
                  IF NEW."Revision"<>COALESCE(previous,0)+1 OR EXISTS(SELECT 1 FROM "CoachCompensationAgreements" a WHERE a."Id"=NEW."AgreementId" AND a."EffectiveFrom"<previous_start)
                    THEN RAISE EXCEPTION 'Agreement versions must advance without rewriting earlier service dates' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER coach_approval_source BEFORE INSERT ON "CoachCompensationApprovals" FOR EACH ROW EXECUTE FUNCTION academy_coach_approval_source();
                CREATE FUNCTION academy_coach_employee_binding() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW."MembershipId" IS DISTINCT FROM OLD."MembershipId" AND EXISTS(SELECT 1 FROM "CoachCompensationAgreements" WHERE "EmployeeId"=OLD."Id" AND "AcademyId"=OLD."AcademyId")
                    THEN RAISE EXCEPTION 'Employee with coach agreements cannot be rebound' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER coach_employee_binding BEFORE UPDATE OF "MembershipId" ON "Employees" FOR EACH ROW EXECUTE FUNCTION academy_coach_employee_binding();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "CoachCompensationAgreements") THEN RAISE EXCEPTION 'Coach agreements contain preserved financial terms; downgrade refused'; END IF;
                END $$;
                DROP FUNCTION academy_coach_agreement_source() CASCADE;
                DROP FUNCTION academy_coach_approval_source() CASCADE;
                DROP FUNCTION academy_coach_terms_immutable() CASCADE;
                DROP FUNCTION academy_coach_employee_binding() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "CoachCompensationApprovals");

            migrationBuilder.DropTable(
                name: "CoachCompensationAgreements");
        }
    }
}
