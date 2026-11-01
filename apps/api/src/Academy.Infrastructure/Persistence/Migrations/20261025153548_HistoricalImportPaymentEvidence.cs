using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalImportPaymentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportedHistoricalPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedHistoricalPayments", x => x.Id);
                    table.UniqueConstraint("AK_ImportedHistoricalPayments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalPayments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalPayments_AcademyImportJobs_AcademyId_JobId",
                        columns: x => new { x.AcademyId, x.JobId },
                        principalTable: "AcademyImportJobs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalPayments_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalPayments_Collections_AcademyId_Collection~",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalPayments_SubscriptionPeriods_AcademyId_Su~",
                        columns: x => new { x.AcademyId, x.SubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalPayments_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_AcademyId_BranchId",
                table: "ImportedHistoricalPayments",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_AcademyId_CollectionId",
                table: "ImportedHistoricalPayments",
                columns: new[] { "AcademyId", "CollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_AcademyId_JobId",
                table: "ImportedHistoricalPayments",
                columns: new[] { "AcademyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_AcademyId_SourceSystem_Evidence",
                table: "ImportedHistoricalPayments",
                columns: new[] { "AcademyId", "SourceSystem", "Evidence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_AcademyId_SourceSystem_ExternalC~",
                table: "ImportedHistoricalPayments",
                columns: new[] { "AcademyId", "SourceSystem", "ExternalCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_AcademyId_SubscriptionPeriodId",
                table: "ImportedHistoricalPayments",
                columns: new[] { "AcademyId", "SubscriptionPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalPayments_ApprovedByUserId",
                table: "ImportedHistoricalPayments",
                column: "ApprovedByUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_imported_payment_evidence_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM "SubscriptionPeriods" p
                    JOIN "SportEnrollments" e ON e."AcademyId"=p."AcademyId" AND e."Id"=p."SportEnrollmentId"
                    JOIN "Collections" c ON c."AcademyId"=p."AcademyId" AND c."Id"=NEW."CollectionId" AND c."SportEnrollmentId"=e."Id"
                    JOIN "AcademyImportJobs" j ON j."AcademyId"=p."AcademyId" AND j."Id"=NEW."JobId"
                    WHERE p."AcademyId"=NEW."AcademyId" AND p."Id"=NEW."SubscriptionPeriodId"
                      AND p."Origin"='ImportedHistorical' AND p."ImportJobId"=j."Id" AND p."CollectionId" IS NULL
                      AND p."PriceSnapshot"=c."Amount" AND p."CurrencySnapshot"=c."Currency" AND e."BranchId"=NEW."BranchId"
                      AND j."SourceSystem"=NEW."SourceSystem" AND j."ActorUserId"=NEW."ApprovedByUserId" AND j."OpeningBalancesApproved"
                      AND EXISTS(SELECT 1 FROM "Receipts" r WHERE r."AcademyId"=p."AcademyId" AND r."CollectionId"=c."Id" AND r."Amount"=c."Amount" AND r."SportEnrollmentId"=e."Id")
                  ) THEN RAISE EXCEPTION 'Historical payment evidence must match its approved job, agreement and receipt'; END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER imported_payment_evidence_guard AFTER INSERT ON "ImportedHistoricalPayments"
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_imported_payment_evidence_guard();
                CREATE TRIGGER imported_payment_evidence_immutable BEFORE UPDATE OR DELETE ON "ImportedHistoricalPayments"
                  FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS imported_payment_evidence_guard ON \"ImportedHistoricalPayments\"; DROP TRIGGER IF EXISTS imported_payment_evidence_immutable ON \"ImportedHistoricalPayments\"; DROP FUNCTION IF EXISTS academy_imported_payment_evidence_guard();");
            migrationBuilder.DropTable(
                name: "ImportedHistoricalPayments");
        }
    }
}
