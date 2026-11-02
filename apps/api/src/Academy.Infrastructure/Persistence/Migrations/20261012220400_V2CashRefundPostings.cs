using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CashRefundPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashRefundPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRefundPostings", x => x.Id);
                    table.UniqueConstraint("AK_CashRefundPostings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CashRefundPosting_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CashRefundPostings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashRefundPostings_CashSessions_AcademyId_SessionId_BranchId",
                        columns: x => new { x.AcademyId, x.SessionId, x.BranchId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashRefundPostings_CollectionRefunds_AcademyId_RefundId",
                        columns: x => new { x.AcademyId, x.RefundId },
                        principalTable: "CollectionRefunds",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashRefundPostings_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashRefundPostings_AcademyId_RefundId",
                table: "CashRefundPostings",
                columns: new[] { "AcademyId", "RefundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashRefundPostings_AcademyId_SessionId_BranchId",
                table: "CashRefundPostings",
                columns: new[] { "AcademyId", "SessionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashRefundPostings_ActorUserId",
                table: "CashRefundPostings",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_cash_refund_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE source_id uuid; source_amount numeric; source_currency text; refund_total numeric; allocated numeric; balance numeric;
                BEGIN
                    SELECT f."CollectionId" INTO source_id FROM "CollectionRefunds" f WHERE f."AcademyId"=NEW."AcademyId" AND f."Id"=NEW."RefundId";
                    IF source_id IS NULL THEN RAISE EXCEPTION 'Refund source missing' USING ERRCODE='23514'; END IF;
                    PERFORM pg_advisory_xact_lock(hashtextextended('collection-funds:' || NEW."AcademyId"::text || ':' || source_id::text,0));
                    PERFORM pg_advisory_xact_lock(hashtextextended('cash:' || NEW."AcademyId"::text || ':session:' || NEW."SessionId"::text,0));
                    PERFORM 1 FROM "CashSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."SessionId" AND s."BranchId"=NEW."BranchId" AND s."ClosedAtUtc" IS NULL FOR UPDATE;
                    IF NOT FOUND THEN RAISE EXCEPTION 'Cash payout requires an open shift' USING ERRCODE='23514'; END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM "CollectionRefunds" f JOIN "Collections" c ON c."AcademyId"=f."AcademyId" AND c."Id"=f."CollectionId"
                        JOIN "SportEnrollments" e ON e."AcademyId"=c."AcademyId" AND e."Id"=c."SportEnrollmentId"
                        JOIN "CashSessions" s ON s."AcademyId"=f."AcademyId" AND s."Id"=NEW."SessionId"
                        JOIN "CashRegisters" r ON r."AcademyId"=s."AcademyId" AND r."Id"=s."RegisterId"
                        WHERE f."AcademyId"=NEW."AcademyId" AND f."Id"=NEW."RefundId" AND f."Amount"=NEW."Amount"
                          AND f."Currency"=NEW."Currency" AND c."Currency"=NEW."Currency" AND r."Currency"=NEW."Currency"
                          AND e."BranchId"=NEW."BranchId" AND e."PlayerId"=f."PlayerId" AND f."PerformedByUserId"=NEW."ActorUserId"
                          AND f."CreatedAtUtc"=NEW."CreatedAtUtc"
                    ) THEN RAISE EXCEPTION 'Cash payout must match its refund' USING ERRCODE='23514'; END IF;
                    SELECT c."Amount" INTO source_amount FROM "Collections" c WHERE c."AcademyId"=NEW."AcademyId" AND c."Id"=source_id;
                    SELECT COALESCE(SUM(f."Amount"),0) INTO refund_total FROM "CollectionRefunds" f WHERE f."AcademyId"=NEW."AcademyId" AND f."CollectionId"=source_id;
                    SELECT COALESCE(SUM(m."Amount"),0) INTO allocated FROM "ReceivableMovements" m WHERE m."AcademyId"=NEW."AcademyId" AND m."CollectionId"=source_id AND m."Kind"='PaymentAllocation';
                    IF refund_total+allocated>source_amount THEN RAISE EXCEPTION 'Refund exceeds unallocated source funds' USING ERRCODE='23514'; END IF;
                    SELECT s."OpeningAmount"
                        + COALESCE((SELECT SUM(m."Amount") FROM "CashMovements" m WHERE m."AcademyId"=s."AcademyId" AND m."SessionId"=s."Id"),0)
                        + COALESCE((SELECT SUM(p."Amount") FROM "CashReceiptPostings" p WHERE p."AcademyId"=s."AcademyId" AND p."SessionId"=s."Id"),0)
                        + COALESCE((SELECT SUM(p."Amount") FROM "CashProductPostings" p WHERE p."AcademyId"=s."AcademyId" AND p."SessionId"=s."Id"),0)
                        - COALESCE((SELECT SUM(p."Amount") FROM "CashRefundPostings" p WHERE p."AcademyId"=s."AcademyId" AND p."SessionId"=s."Id"),0)
                    INTO balance FROM "CashSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."SessionId";
                    IF balance<NEW."Amount" THEN RAISE EXCEPTION 'Insufficient cash shift balance' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER cash_refund_source BEFORE INSERT ON "CashRefundPostings" FOR EACH ROW EXECUTE FUNCTION academy_cash_refund_source();
                CREATE TRIGGER cash_refund_posting_immutable BEFORE UPDATE OR DELETE ON "CashRefundPostings" FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_cash_refund_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "CashRefundPostings" p WHERE p."AcademyId"=OLD."AcademyId" AND p."RefundId"=OLD."Id") THEN
                        RAISE EXCEPTION 'Posted cash refund is immutable' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER cash_refund_immutable BEFORE UPDATE OR DELETE ON "CollectionRefunds" FOR EACH ROW EXECUTE FUNCTION academy_cash_refund_immutable();
                CREATE FUNCTION academy_cash_refund_collection_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "CashRefundPostings" p JOIN "CollectionRefunds" f ON f."AcademyId"=p."AcademyId" AND f."Id"=p."RefundId" WHERE f."AcademyId"=OLD."AcademyId" AND f."CollectionId"=OLD."Id") THEN
                        RAISE EXCEPTION 'Cash refund collection source is immutable' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER cash_refund_collection_immutable BEFORE UPDATE OR DELETE ON "Collections" FOR EACH ROW EXECUTE FUNCTION academy_cash_refund_collection_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER cash_refund_collection_immutable ON "Collections";
                DROP FUNCTION academy_cash_refund_collection_immutable();
                DROP TRIGGER cash_refund_immutable ON "CollectionRefunds";
                DROP FUNCTION academy_cash_refund_immutable();
                """);
            migrationBuilder.DropTable(
                name: "CashRefundPostings");
            migrationBuilder.Sql("DROP FUNCTION academy_cash_refund_source();");
        }
    }
}
