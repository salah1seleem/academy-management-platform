using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice8CSubscriptionDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_AcademyId_RenewalRequestId",
                table: "PaymentRequests");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "RenewalRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiscountAppliedAtUtc",
                table: "RenewalRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DiscountAppliedByUserId",
                table: "RenewalRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                table: "RenewalRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "RenewalRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "RenewalRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalAmount",
                table: "RenewalRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "RenewalRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "Receipts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "Receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalAmount",
                table: "Receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "Receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "RenewalRequests"
                SET "OriginalAmount" = "AmountExpected", "FinalAmount" = "AmountExpected";
                UPDATE "Receipts"
                SET "OriginalAmount" = "Amount", "FinalAmount" = "Amount";
                """);

            migrationBuilder.CreateTable(
                name: "RenewalDiscountAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RenewalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousDiscountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PreviousDiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NewDiscountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NewDiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PreviousDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NewDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousFinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NewFinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalDiscountAdjustments", x => x.Id);
                    table.UniqueConstraint("AK_RenewalDiscountAdjustments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_RenewalDiscountAdjustments_Amounts", "\"PreviousDiscountAmount\" >= 0 AND \"NewDiscountAmount\" >= 0 AND \"PreviousFinalAmount\" >= 0 AND \"NewFinalAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_RenewalDiscountAdjustments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenewalDiscountAdjustments_RenewalRequests_AcademyId_Renewa~",
                        columns: x => new { x.AcademyId, x.RenewalRequestId },
                        principalTable: "RenewalRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenewalDiscountAdjustments_Users_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_DiscountAppliedByUserId",
                table: "RenewalRequests",
                column: "DiscountAppliedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RenewalRequests_DiscountSnapshot",
                table: "RenewalRequests",
                sql: "(\"DiscountType\" IS NULL AND \"DiscountValue\" IS NULL AND \"DiscountAmount\" = 0) OR (\"DiscountType\" = 'Percentage' AND \"DiscountValue\" > 0 AND \"DiscountValue\" <= 100 AND \"DiscountAmount\" > 0) OR (\"DiscountType\" = 'FixedAmount' AND \"DiscountValue\" > 0 AND \"DiscountValue\" <= \"OriginalAmount\" AND \"DiscountAmount\" = \"DiscountValue\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RenewalRequests_FinancialAmounts",
                table: "RenewalRequests",
                sql: "\"OriginalAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalAmount\" >= 0 AND \"FinalAmount\" <= \"OriginalAmount\" AND \"FinalAmount\" = \"OriginalAmount\" - \"DiscountAmount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Receipts_FinancialAmounts",
                table: "Receipts",
                sql: "\"OriginalAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalAmount\" >= 0 AND \"FinalAmount\" <= \"OriginalAmount\" AND \"Amount\" = \"FinalAmount\"");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_AcademyId_RenewalRequestId",
                table: "PaymentRequests",
                columns: new[] { "AcademyId", "RenewalRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalDiscountAdjustments_AcademyId_IsActive",
                table: "RenewalDiscountAdjustments",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalDiscountAdjustments_AcademyId_PerformedByUserId_Idem~",
                table: "RenewalDiscountAdjustments",
                columns: new[] { "AcademyId", "PerformedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalDiscountAdjustments_AcademyId_RenewalRequestId_Creat~",
                table: "RenewalDiscountAdjustments",
                columns: new[] { "AcademyId", "RenewalRequestId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalDiscountAdjustments_PerformedByUserId",
                table: "RenewalDiscountAdjustments",
                column: "PerformedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_Users_DiscountAppliedByUserId",
                table: "RenewalRequests",
                column: "DiscountAppliedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_Users_DiscountAppliedByUserId",
                table: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "RenewalDiscountAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_DiscountAppliedByUserId",
                table: "RenewalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RenewalRequests_DiscountSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RenewalRequests_FinancialAmounts",
                table: "RenewalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Receipts_FinancialAmounts",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_AcademyId_RenewalRequestId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountAppliedAtUtc",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountAppliedByUserId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "FinalAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "FinalAmount",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "Receipts");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_AcademyId_RenewalRequestId",
                table: "PaymentRequests",
                columns: new[] { "AcademyId", "RenewalRequestId" },
                unique: true);
        }
    }
}
