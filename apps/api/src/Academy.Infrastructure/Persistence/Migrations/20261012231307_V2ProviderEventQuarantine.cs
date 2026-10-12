using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ProviderEventQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnvelopeHash",
                table: "PaymentProviderEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderEventQuarantines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EnvelopeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ReceivedStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReceivedReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpectedReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExpectedCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEventQuarantines", x => x.Id);
                    table.UniqueConstraint("AK_ProviderEventQuarantines_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProviderEventQuarantines_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderEventQuarantines_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderEventQuarantines_PaymentRequests_AcademyId_PaymentR~",
                        columns: x => new { x.AcademyId, x.PaymentRequestId },
                        principalTable: "PaymentRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEventQuarantines_AcademyId_BranchId",
                table: "ProviderEventQuarantines",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEventQuarantines_AcademyId_CreatedAtUtc",
                table: "ProviderEventQuarantines",
                columns: new[] { "AcademyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEventQuarantines_AcademyId_IsActive",
                table: "ProviderEventQuarantines",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEventQuarantines_AcademyId_PaymentRequestId",
                table: "ProviderEventQuarantines",
                columns: new[] { "AcademyId", "PaymentRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEventQuarantines_AcademyId_Provider_ProviderEventId~",
                table: "ProviderEventQuarantines",
                columns: new[] { "AcademyId", "Provider", "ProviderEventId", "EnvelopeHash" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER provider_quarantine_immutable BEFORE UPDATE OR DELETE ON "ProviderEventQuarantines"
                  FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_provider_quarantine_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM "PaymentRequests" p
                    JOIN "RenewalRequests" r ON r."AcademyId"=p."AcademyId" AND r."Id"=p."RenewalRequestId"
                    JOIN "SportEnrollments" e ON e."AcademyId"=r."AcademyId" AND e."Id"=r."SportEnrollmentId"
                    WHERE p."AcademyId"=NEW."AcademyId" AND p."Id"=NEW."PaymentRequestId" AND e."BranchId"=NEW."BranchId"
                  ) THEN RAISE EXCEPTION 'Quarantine must reference its tenant payment branch' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER provider_quarantine_source BEFORE INSERT ON "ProviderEventQuarantines"
                  FOR EACH ROW EXECUTE FUNCTION academy_provider_quarantine_source();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderEventQuarantines");
            migrationBuilder.Sql("DROP FUNCTION academy_provider_quarantine_source();");

            migrationBuilder.DropColumn(
                name: "EnvelopeHash",
                table: "PaymentProviderEvents");
        }
    }
}
