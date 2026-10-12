using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CashReceiptVoids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashReceiptVoids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashReceiptVoids", x => x.Id);
                    table.UniqueConstraint("AK_CashReceiptVoids_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_CashRefundPostings_AcademyId_RefundPosting~",
                        columns: x => new { x.AcademyId, x.RefundPostingId },
                        principalTable: "CashRefundPostings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_CollectionRefunds_AcademyId_RefundId",
                        columns: x => new { x.AcademyId, x.RefundId },
                        principalTable: "CollectionRefunds",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_Receipts_AcademyId_ReceiptId",
                        columns: x => new { x.AcademyId, x.ReceiptId },
                        principalTable: "Receipts",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_SubscriptionAdjustments_AcademyId_Adjustme~",
                        columns: x => new { x.AcademyId, x.AdjustmentId },
                        principalTable: "SubscriptionAdjustments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_SubscriptionPeriods_AcademyId_Subscription~",
                        columns: x => new { x.AcademyId, x.SubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptVoids_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_ActorUserId_RequestKey",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_AdjustmentId",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "AdjustmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_BranchId",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_ReceiptId",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "ReceiptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_RefundId",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "RefundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_RefundPostingId",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "RefundPostingId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_AcademyId_SubscriptionPeriodId",
                table: "CashReceiptVoids",
                columns: new[] { "AcademyId", "SubscriptionPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptVoids_ActorUserId",
                table: "CashReceiptVoids",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_cash_receipt_void_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM "Receipts" r
                    JOIN "Collections" c ON c."AcademyId"=r."AcademyId" AND c."Id"=r."CollectionId"
                    JOIN "CashReceiptPostings" source ON source."AcademyId"=r."AcademyId" AND source."ReceiptId"=r."Id" AND source."CollectionId"=c."Id"
                    JOIN "CollectionRefunds" f ON f."AcademyId"=r."AcademyId" AND f."Id"=NEW."RefundId" AND f."CollectionId"=c."Id"
                    JOIN "CashRefundPostings" p ON p."AcademyId"=f."AcademyId" AND p."Id"=NEW."RefundPostingId" AND p."RefundId"=f."Id"
                    JOIN "SubscriptionPeriods" s ON s."AcademyId"=c."AcademyId" AND s."Id"=NEW."SubscriptionPeriodId" AND s."CollectionId"=c."Id"
                    JOIN "SubscriptionAdjustments" a ON a."AcademyId"=s."AcademyId" AND a."Id"=NEW."AdjustmentId" AND a."SubscriptionPeriodId"=s."Id"
                    WHERE r."AcademyId"=NEW."AcademyId" AND r."Id"=NEW."ReceiptId" AND r."PaymentMethod"='Cash'
                      AND r."Amount"=c."Amount" AND f."Amount"=c."Amount" AND f."PayoutMethod"='Cash'
                      AND f."Currency"=c."Currency" AND p."BranchId"=NEW."BranchId"
                      AND f."PerformedByUserId"=NEW."ActorUserId" AND p."ActorUserId"=NEW."ActorUserId"
                      AND a."PerformedByUserId"=NEW."ActorUserId" AND a."AdjustmentType"='Cancelled' AND s."Status"='Cancelled'
                      AND f."CreatedAtUtc"=NEW."CreatedAtUtc" AND a."PerformedAtUtc"=NEW."CreatedAtUtc"
                  ) THEN RAISE EXCEPTION 'Void must match a full cash reversal and cancelled period' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER cash_receipt_void_source BEFORE INSERT ON "CashReceiptVoids" FOR EACH ROW EXECUTE FUNCTION academy_cash_receipt_void_source();
                CREATE TRIGGER cash_receipt_void_immutable BEFORE UPDATE OR DELETE ON "CashReceiptVoids" FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_void_period_cancelled() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."Status"<>'Cancelled' AND EXISTS (SELECT 1 FROM "CashReceiptVoids" v WHERE v."AcademyId"=OLD."AcademyId" AND v."SubscriptionPeriodId"=OLD."Id") THEN
                    RAISE EXCEPTION 'Voided subscription period cannot be reactivated' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER void_period_cancelled BEFORE UPDATE ON "SubscriptionPeriods" FOR EACH ROW EXECUTE FUNCTION academy_void_period_cancelled();
                CREATE FUNCTION academy_void_adjustment_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "CashReceiptVoids" v WHERE v."AcademyId"=OLD."AcademyId" AND v."AdjustmentId"=OLD."Id") THEN
                    RAISE EXCEPTION 'Void cancellation audit is immutable' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER void_adjustment_immutable BEFORE UPDATE OR DELETE ON "SubscriptionAdjustments" FOR EACH ROW EXECUTE FUNCTION academy_void_adjustment_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER void_adjustment_immutable ON "SubscriptionAdjustments";
                DROP FUNCTION academy_void_adjustment_immutable();
                DROP TRIGGER void_period_cancelled ON "SubscriptionPeriods";
                DROP FUNCTION academy_void_period_cancelled();
                """);
            migrationBuilder.DropTable(
                name: "CashReceiptVoids");
            migrationBuilder.Sql("DROP FUNCTION academy_cash_receipt_void_source();");
        }
    }
}
