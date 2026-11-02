using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CrossBranchCashRefundGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep every original source, currency, amount and pool/shift guard;
            // only the authoritative collection location changes for posted cash.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_cash_refund_source() RETURNS trigger LANGUAGE plpgsql AS $$
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
                          AND COALESCE((SELECT p."BranchId" FROM "CashReceiptPostings" p WHERE p."AcademyId"=c."AcademyId" AND p."CollectionId"=c."Id"),e."BranchId")=NEW."BranchId" AND e."PlayerId"=f."PlayerId" AND f."PerformedByUserId"=NEW."ActorUserId"
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_cash_refund_source() RETURNS trigger LANGUAGE plpgsql AS $$
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
                """);
        }
    }
}
