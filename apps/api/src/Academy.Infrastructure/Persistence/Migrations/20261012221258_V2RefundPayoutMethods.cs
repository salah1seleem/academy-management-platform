using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2RefundPayoutMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutMethod",
                table: "CollectionRefunds",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionRefund_PayoutMethod",
                table: "CollectionRefunds",
                sql: "\"PayoutMethod\" IS NULL OR \"PayoutMethod\" IN ('Cash','Online')");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_cash_refund_method_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM "CollectionRefunds" r WHERE r."AcademyId"=NEW."AcademyId" AND r."Id"=NEW."RefundId" AND r."PayoutMethod"='Cash') THEN
                    RAISE EXCEPTION 'Cash refund requires a cash payout source' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER cash_refund_method_guard BEFORE INSERT ON "CashRefundPostings"
                FOR EACH ROW EXECUTE FUNCTION academy_cash_refund_method_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS cash_refund_method_guard ON "CashRefundPostings";
                DROP FUNCTION IF EXISTS academy_cash_refund_method_guard();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionRefund_PayoutMethod",
                table: "CollectionRefunds");

            migrationBuilder.DropColumn(
                name: "PayoutMethod",
                table: "CollectionRefunds");
        }
    }
}
