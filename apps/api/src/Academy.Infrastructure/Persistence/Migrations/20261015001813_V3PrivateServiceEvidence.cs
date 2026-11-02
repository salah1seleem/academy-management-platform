using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PrivateServiceEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrivateServicePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionCount = table.Column<int>(type: "integer", nullable: false),
                    TrainingFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReviewedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateServicePackages", x => x.Id);
                    table.UniqueConstraint("AK_PrivateServicePackages_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PrivatePackage_Terms", "\"SessionCount\" BETWEEN 1 AND 1000 AND \"TrainingFormat\" IN ('Private','SemiPrivate')");
                    table.ForeignKey(
                        name: "FK_PrivateServicePackages_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServicePackages_AcademyMemberships_AcademyId_Reviewe~",
                        columns: x => new { x.AcademyId, x.ReviewedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServicePackages_ReceivableDocuments_AcademyId_Docume~",
                        columns: x => new { x.AcademyId, x.DocumentId },
                        principalTable: "ReceivableDocuments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServicePackages_SportEnrollments_AcademyId_SportEnro~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateServiceDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ApprovedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateServiceDeliveries", x => x.Id);
                    table.UniqueConstraint("AK_PrivateServiceDeliveries_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PrivateDelivery_Ordinal", "\"Ordinal\" > 0 AND \"EndTime\" > \"StartTime\"");
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_AcademyMemberships_AcademyId_Approv~",
                        columns: x => new { x.AcademyId, x.ApprovedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_AcademyMemberships_AcademyId_CoachM~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_CoachCompensationAgreements_Academy~",
                        columns: x => new { x.AcademyId, x.AgreementId, x.EmployeeId, x.BranchId, x.SportId },
                        principalTable: "CoachCompensationAgreements",
                        principalColumns: new[] { "AcademyId", "Id", "EmployeeId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_PrivateServicePackages_AcademyId_Pa~",
                        columns: x => new { x.AcademyId, x.PackageId },
                        principalTable: "PrivateServicePackages",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_SportEnrollments_AcademyId_SportEnr~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateServiceDeliveries_TrainingSessions_AcademyId_Training~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServiceDeliveries_AcademyId_AgreementId_EmployeeId_Br~",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "AgreementId", "EmployeeId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServiceDeliveries_AcademyId_ApprovedByMembershipId",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "ApprovedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServiceDeliveries_AcademyId_CoachMembershipId",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServiceDeliveries_AcademyId_PackageId_Ordinal",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "PackageId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServiceDeliveries_AcademyId_SportEnrollmentId",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServiceDeliveries_AcademyId_TrainingSessionId_SportEn~",
                table: "PrivateServiceDeliveries",
                columns: new[] { "AcademyId", "TrainingSessionId", "SportEnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServicePackages_AcademyId_DocumentId",
                table: "PrivateServicePackages",
                columns: new[] { "AcademyId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServicePackages_AcademyId_ReviewedByMembershipId",
                table: "PrivateServicePackages",
                columns: new[] { "AcademyId", "ReviewedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateServicePackages_AcademyId_SportEnrollmentId",
                table: "PrivateServicePackages",
                columns: new[] { "AcademyId", "SportEnrollmentId" });
            migrationBuilder.Sql("""
                CREATE TRIGGER private_package_immutable BEFORE UPDATE OR DELETE ON "PrivateServicePackages" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE TRIGGER private_delivery_immutable BEFORE UPDATE OR DELETE ON "PrivateServiceDeliveries" FOR EACH ROW EXECUTE FUNCTION academy_coach_terms_immutable();
                CREATE FUNCTION academy_private_package_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NOT EXISTS(SELECT 1 FROM "ReceivableDocuments" d JOIN "SportEnrollments" e ON e."Id"=d."SportEnrollmentId" AND e."AcademyId"=d."AcademyId"
                    JOIN "SubscriptionPlans" p ON p."Id"=d."PlanId" AND p."AcademyId"=d."AcademyId"
                    JOIN "AcademyMemberships" actor ON actor."Id"=NEW."ReviewedByMembershipId" AND actor."AcademyId"=d."AcademyId"
                    WHERE d."Id"=NEW."DocumentId" AND d."AcademyId"=NEW."AcademyId" AND d."Source"='ManualCharge' AND d."Amount">0 AND d."Currency"=NEW."Currency"
                      AND d."SportEnrollmentId"=NEW."SportEnrollmentId" AND d."BranchId"=NEW."BranchId" AND e."BranchId"=NEW."BranchId" AND e."SportId"=NEW."SportId"
                      AND p."PlanType"='Sessions' AND p."SessionCount"=NEW."SessionCount" AND p."TrainingFormat"=NEW."TrainingFormat" AND e."RegistrationFormat"=NEW."TrainingFormat"
                      AND actor."Role"='AcademyOwner' AND actor."IsActive") THEN RAISE EXCEPTION 'Invalid private package allocation source' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER private_package_source BEFORE INSERT ON "PrivateServicePackages" FOR EACH ROW EXECUTE FUNCTION academy_private_package_source();
                CREATE FUNCTION academy_private_delivery_source() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('payroll:'||NEW."AcademyId"::text||':'||NEW."EmployeeId"::text,0));
                  PERFORM pg_advisory_xact_lock(hashtextextended('private-package:'||NEW."AcademyId"::text||':'||NEW."PackageId"::text,0));
                  PERFORM 1 FROM "TrainingSessions" WHERE "Id"=NEW."TrainingSessionId" AND "AcademyId"=NEW."AcademyId" FOR UPDATE;
                  IF NOT EXISTS(SELECT 1 FROM "PrivateServicePackages" p
                    JOIN "TrainingSessions" s ON s."Id"=NEW."TrainingSessionId" AND s."AcademyId"=p."AcademyId"
                    JOIN "TrainingGroups" g ON g."Id"=s."TrainingGroupId" AND g."AcademyId"=p."AcademyId"
                    JOIN "Academies" tenant ON tenant."Id"=p."AcademyId"
                    JOIN "CoachCompensationAgreements" a ON a."Id"=NEW."AgreementId" AND a."AcademyId"=p."AcademyId"
                    JOIN "CoachCompensationApprovals" approval ON approval."AgreementId"=a."Id" AND approval."AcademyId"=a."AcademyId"
                    JOIN "AcademyMemberships" actor ON actor."Id"=NEW."ApprovedByMembershipId" AND actor."AcademyId"=p."AcademyId"
                    WHERE p."Id"=NEW."PackageId" AND p."AcademyId"=NEW."AcademyId" AND p."SportEnrollmentId"=NEW."SportEnrollmentId"
                      AND p."BranchId"=NEW."BranchId" AND p."SportId"=NEW."SportId" AND NEW."Ordinal" BETWEEN 1 AND p."SessionCount"
                      AND s."BranchId"=p."BranchId" AND s."SportId"=p."SportId" AND s."SessionDate"=NEW."ServiceDate" AND s."StartTime"=NEW."StartTime" AND s."EndTime"=NEW."EndTime"
                      AND s."Status"<>'Cancelled' AND g."RegistrationFormat"=p."TrainingFormat" AND (s."SessionDate"+s."EndTime") <= timezone(tenant."TimeZone",CURRENT_TIMESTAMP)
                      AND a."CoachMembershipId"=NEW."CoachMembershipId" AND a."EmployeeId"=NEW."EmployeeId" AND a."Currency"=p."Currency"
                      AND a."EffectiveFrom"<=NEW."ServiceDate" AND (a."EffectiveTo" IS NULL OR a."EffectiveTo">=NEW."ServiceDate") AND approval."ApprovedAtUtc"<=NEW."CreatedAtUtc"
                      AND actor."Role"='AcademyOwner' AND actor."IsActive"
                      AND academy_session_assigned(NEW."AcademyId",NEW."SportEnrollmentId",NEW."TrainingSessionId")
                      AND academy_session_staff_assigned(NEW."AcademyId",NEW."CoachMembershipId",NEW."TrainingSessionId"))
                    OR EXISTS(SELECT 1 FROM "CompensationBookings" b WHERE b."AcademyId"=NEW."AcademyId" AND b."TrainingSessionId"=NEW."TrainingSessionId" AND b."SportEnrollmentId"=NEW."SportEnrollmentId" AND b."State"='Reserved')
                    THEN RAISE EXCEPTION 'Invalid approved private delivery source' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER private_delivery_source BEFORE INSERT ON "PrivateServiceDeliveries" FOR EACH ROW EXECUTE FUNCTION academy_private_delivery_source();
                CREATE FUNCTION academy_private_session_preserved() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF ROW(NEW."TrainingGroupId",NEW."BranchId",NEW."SportId",NEW."SessionDate",NEW."StartTime",NEW."EndTime") IS DISTINCT FROM ROW(OLD."TrainingGroupId",OLD."BranchId",OLD."SportId",OLD."SessionDate",OLD."StartTime",OLD."EndTime") OR NEW."Status"='Cancelled' THEN
                    IF EXISTS(SELECT 1 FROM "PrivateServiceDeliveries" d WHERE d."AcademyId"=OLD."AcademyId" AND d."TrainingSessionId"=OLD."Id") THEN RAISE EXCEPTION 'Approved private service requires a documented correction, not a session rewrite' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER private_session_preserved BEFORE UPDATE ON "TrainingSessions" FOR EACH ROW EXECUTE FUNCTION academy_private_session_preserved();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PrivateServicePackages") THEN RAISE EXCEPTION 'Private service evidence exists; downgrade refused'; END IF; END $$;
                DROP FUNCTION academy_private_package_source() CASCADE;
                DROP FUNCTION academy_private_delivery_source() CASCADE;
                DROP FUNCTION academy_private_session_preserved() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "PrivateServiceDeliveries");

            migrationBuilder.DropTable(
                name: "PrivateServicePackages");
        }
    }
}
