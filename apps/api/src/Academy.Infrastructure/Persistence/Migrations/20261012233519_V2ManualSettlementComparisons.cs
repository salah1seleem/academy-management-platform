using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ManualSettlementComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SettlementComparisons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountTag = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    StatementReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LineReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    Result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementComparisons", x => x.Id);
                    table.UniqueConstraint("AK_SettlementComparisons_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_SettlementComparisons_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementComparisons_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementComparisons_Collections_AcademyId_CollectionId",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementComparisons_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_AcademyId_BranchId",
                table: "SettlementComparisons",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_AcademyId_CollectionId",
                table: "SettlementComparisons",
                columns: new[] { "AcademyId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_AcademyId_IsActive",
                table: "SettlementComparisons",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_AcademyId_SettlementDate",
                table: "SettlementComparisons",
                columns: new[] { "AcademyId", "SettlementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_AcademyId_SourceKind_AccountTag_Colle~",
                table: "SettlementComparisons",
                columns: new[] { "AcademyId", "SourceKind", "AccountTag", "CollectionId" },
                unique: true,
                filter: "\"Result\" = 'Matched' AND \"CollectionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_AcademyId_SourceKind_AccountTag_State~",
                table: "SettlementComparisons",
                columns: new[] { "AcademyId", "SourceKind", "AccountTag", "StatementReference", "LineReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementComparisons_ActorUserId",
                table: "SettlementComparisons",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE TRIGGER settlement_comparison_immutable BEFORE UPDATE OR DELETE ON "SettlementComparisons"
                  FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_settlement_comparison_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."CollectionId" IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM "Collections" c JOIN "SportEnrollments" e ON e."AcademyId"=c."AcademyId" AND e."Id"=c."SportEnrollmentId"
                    WHERE c."AcademyId"=NEW."AcademyId" AND c."Id"=NEW."CollectionId" AND e."BranchId"=NEW."BranchId"
                    AND c."PaymentMethod"='Online' AND c."Provider"=NEW."Provider" AND c."ProviderReference"=NEW."PaymentReference"
                    AND c."Amount"=NEW."ExpectedAmount" AND c."Currency"=NEW."ExpectedCurrency"
                  ) THEN RAISE EXCEPTION 'statement comparison source mismatch' USING ERRCODE='23514'; END IF;
                  IF NEW."Result"='Matched' AND (NEW."CollectionId" IS NULL OR NEW."GrossAmount" IS DISTINCT FROM NEW."ExpectedAmount" OR NEW."Currency" IS DISTINCT FROM NEW."ExpectedCurrency" OR NEW."NetAmount"<>NEW."GrossAmount"-NEW."FeeAmount")
                  THEN RAISE EXCEPTION 'statement comparison cannot claim a false match' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER settlement_comparison_source BEFORE INSERT ON "SettlementComparisons"
                  FOR EACH ROW EXECUTE FUNCTION academy_settlement_comparison_source();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettlementComparisons");
            migrationBuilder.Sql("DROP FUNCTION academy_settlement_comparison_source();");
        }
    }
}
