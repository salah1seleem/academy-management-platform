using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CashProductPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashProductPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductSaleId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_CashProductPostings", x => x.Id);
                    table.UniqueConstraint("AK_CashProductPostings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CashProductPosting_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CashProductPostings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashProductPostings_CashSessions_AcademyId_SessionId_Branch~",
                        columns: x => new { x.AcademyId, x.SessionId, x.BranchId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashProductPostings_ProductSales_AcademyId_ProductSaleId",
                        columns: x => new { x.AcademyId, x.ProductSaleId },
                        principalTable: "ProductSales",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashProductPostings_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashProductPostings_AcademyId_ProductSaleId",
                table: "CashProductPostings",
                columns: new[] { "AcademyId", "ProductSaleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashProductPostings_AcademyId_SessionId_BranchId",
                table: "CashProductPostings",
                columns: new[] { "AcademyId", "SessionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashProductPostings_ActorUserId",
                table: "CashProductPostings",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_cash_product_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('cash:' || NEW."AcademyId"::text || ':session:' || NEW."SessionId"::text,0));
                    PERFORM 1 FROM "CashSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."SessionId" AND s."BranchId"=NEW."BranchId" AND s."ClosedAtUtc" IS NULL FOR UPDATE;
                    IF NOT FOUND THEN RAISE EXCEPTION 'Cash product posting requires an open shift' USING ERRCODE='23514'; END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM "CashSessions" s JOIN "CashRegisters" r ON r."AcademyId"=s."AcademyId" AND r."Id"=s."RegisterId"
                        JOIN "ProductSales" p ON p."AcademyId"=s."AcademyId" AND p."Id"=NEW."ProductSaleId"
                        WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."SessionId"
                          AND p."BranchId"=NEW."BranchId" AND p."Currency"=NEW."Currency" AND r."Currency"=NEW."Currency"
                          AND p."Amount"=NEW."Amount" AND p."Quantity"*p."UnitPrice"=NEW."Amount"
                          AND p."Status"='Confirmed' AND p."PaymentMethod"='Cash' AND p."ConfirmedAtUtc" IS NOT NULL
                          AND p."ConfirmedByUserId"=NEW."ActorUserId"
                    ) THEN RAISE EXCEPTION 'Cash product posting must match its confirmed sale' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER cash_product_source BEFORE INSERT ON "CashProductPostings" FOR EACH ROW EXECUTE FUNCTION academy_cash_product_source();
                CREATE TRIGGER cash_product_posting_immutable BEFORE UPDATE OR DELETE ON "CashProductPostings" FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_cash_product_sale_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "CashProductPostings" p WHERE p."AcademyId"=OLD."AcademyId" AND p."ProductSaleId"=OLD."Id") THEN
                        RAISE EXCEPTION 'A posted cash product source cannot be changed or removed' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER cash_product_sale_immutable BEFORE UPDATE OR DELETE ON "ProductSales" FOR EACH ROW EXECUTE FUNCTION academy_cash_product_sale_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER cash_product_sale_immutable ON "ProductSales";
                DROP FUNCTION academy_cash_product_sale_immutable();
                """);
            migrationBuilder.DropTable(
                name: "CashProductPostings");
            migrationBuilder.Sql("DROP FUNCTION academy_cash_product_source();");
        }
    }
}
