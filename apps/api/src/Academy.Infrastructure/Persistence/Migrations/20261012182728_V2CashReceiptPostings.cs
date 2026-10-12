using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CashReceiptPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashReceiptPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashReceiptPostings", x => x.Id);
                    table.UniqueConstraint("AK_CashReceiptPostings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CashReceiptPosting_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CashReceiptPostings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptPostings_CashSessions_AcademyId_SessionId_Branch~",
                        columns: x => new { x.AcademyId, x.SessionId, x.BranchId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptPostings_Collections_AcademyId_CollectionId",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptPostings_Receipts_AcademyId_ReceiptId",
                        columns: x => new { x.AcademyId, x.ReceiptId },
                        principalTable: "Receipts",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceiptPostings_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptPostings_AcademyId_CollectionId",
                table: "CashReceiptPostings",
                columns: new[] { "AcademyId", "CollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptPostings_AcademyId_ReceiptId",
                table: "CashReceiptPostings",
                columns: new[] { "AcademyId", "ReceiptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptPostings_AcademyId_RequestKey",
                table: "CashReceiptPostings",
                columns: new[] { "AcademyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptPostings_AcademyId_SessionId_BranchId",
                table: "CashReceiptPostings",
                columns: new[] { "AcademyId", "SessionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashReceiptPostings_ActorUserId",
                table: "CashReceiptPostings",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_cash_receipt_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('cash:' || NEW."AcademyId"::text || ':session:' || NEW."SessionId"::text,0));
                    PERFORM 1 FROM "CashSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."SessionId" AND s."BranchId"=NEW."BranchId" AND s."ClosedAtUtc" IS NULL FOR UPDATE;
                    IF NOT FOUND THEN RAISE EXCEPTION 'Cash posting requires an open shift' USING ERRCODE='23514'; END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM "CashSessions" s
                        JOIN "CashRegisters" r ON r."AcademyId"=s."AcademyId" AND r."Id"=s."RegisterId"
                        JOIN "Collections" c ON c."AcademyId"=s."AcademyId" AND c."Id"=NEW."CollectionId"
                        JOIN "Receipts" p ON p."AcademyId"=c."AcademyId" AND p."CollectionId"=c."Id" AND p."Id"=NEW."ReceiptId"
                        JOIN "PaymentRequests" q ON q."AcademyId"=c."AcademyId" AND q."Id"=c."PaymentRequestId"
                        WHERE s."Id"=NEW."SessionId" AND s."AcademyId"=NEW."AcademyId"
                          AND r."Currency"=NEW."Currency" AND c."Currency"=NEW."Currency" AND p."Currency"=NEW."Currency"
                          AND c."Amount"=NEW."Amount" AND p."Amount"=NEW."Amount" AND p."FinalAmount"=NEW."Amount"
                          AND c."PaymentMethod"='Cash' AND p."PaymentMethod"='Cash' AND q."Status"='Confirmed'
                          AND c."ConfirmedBy"=NEW."ActorUserId"::text
                    ) THEN RAISE EXCEPTION 'Cash posting must match its confirmed receipt and collection' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER cash_receipt_source BEFORE INSERT ON "CashReceiptPostings"
                    FOR EACH ROW EXECUTE FUNCTION academy_cash_receipt_source();
                CREATE TRIGGER cash_receipt_posting_immutable BEFORE UPDATE OR DELETE ON "CashReceiptPostings"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_cash_linked_source_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "CashReceiptPostings" p WHERE p."AcademyId"=OLD."AcademyId"
                        AND ((TG_TABLE_NAME='Collections' AND p."CollectionId"=OLD."Id") OR (TG_TABLE_NAME='Receipts' AND p."ReceiptId"=OLD."Id"))) THEN
                        RAISE EXCEPTION 'A posted cash source cannot be changed or removed' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER cash_collection_source_immutable BEFORE UPDATE OR DELETE ON "Collections"
                    FOR EACH ROW EXECUTE FUNCTION academy_cash_linked_source_immutable();
                CREATE TRIGGER cash_receipt_source_immutable BEFORE UPDATE OR DELETE ON "Receipts"
                    FOR EACH ROW EXECUTE FUNCTION academy_cash_linked_source_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER cash_collection_source_immutable ON "Collections";
                DROP TRIGGER cash_receipt_source_immutable ON "Receipts";
                DROP FUNCTION academy_cash_linked_source_immutable();
                """);
            migrationBuilder.DropTable(
                name: "CashReceiptPostings");
            migrationBuilder.Sql("DROP FUNCTION academy_cash_receipt_source();");
        }
    }
}
