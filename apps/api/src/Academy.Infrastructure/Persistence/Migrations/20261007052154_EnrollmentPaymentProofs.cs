using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrollmentPaymentProofs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "NewEnrollmentRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalAmount",
                table: "NewEnrollmentRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "NewEnrollmentRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentRequestId",
                table: "NewEnrollmentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProofAssetId",
                table: "NewEnrollmentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId",
                table: "NewEnrollmentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_PlayerId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_ProofAssetId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "ProofAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_PaymentRequestId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "PaymentRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_ProofAssetId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "ProofAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_SubscriptionPlanId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "SubscriptionPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNewss_AcademyId_ImageAssetId",
                table: "AcademyNewss",
                columns: new[] { "AcademyId", "ImageAssetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyNewss_PrivateAssets_AcademyId_ImageAssetId",
                table: "AcademyNewss",
                columns: new[] { "AcademyId", "ImageAssetId" },
                principalTable: "PrivateAssets",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NewEnrollmentRequests_PaymentRequests_AcademyId_PaymentRequ~",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "PaymentRequestId" },
                principalTable: "PaymentRequests",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NewEnrollmentRequests_PrivateAssets_AcademyId_ProofAssetId",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "ProofAssetId" },
                principalTable: "PrivateAssets",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NewEnrollmentRequests_SubscriptionPlans_AcademyId_Subscript~",
                table: "NewEnrollmentRequests",
                columns: new[] { "AcademyId", "SubscriptionPlanId" },
                principalTable: "SubscriptionPlans",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSales_Players_AcademyId_PlayerId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "PlayerId" },
                principalTable: "Players",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSales_PrivateAssets_AcademyId_ProofAssetId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "ProofAssetId" },
                principalTable: "PrivateAssets",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcademyNewss_PrivateAssets_AcademyId_ImageAssetId",
                table: "AcademyNewss");

            migrationBuilder.DropForeignKey(
                name: "FK_NewEnrollmentRequests_PaymentRequests_AcademyId_PaymentRequ~",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_NewEnrollmentRequests_PrivateAssets_AcademyId_ProofAssetId",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_NewEnrollmentRequests_SubscriptionPlans_AcademyId_Subscript~",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSales_Players_AcademyId_PlayerId",
                table: "ProductSales");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSales_PrivateAssets_AcademyId_ProofAssetId",
                table: "ProductSales");

            migrationBuilder.DropIndex(
                name: "IX_ProductSales_AcademyId_PlayerId",
                table: "ProductSales");

            migrationBuilder.DropIndex(
                name: "IX_ProductSales_AcademyId_ProofAssetId",
                table: "ProductSales");

            migrationBuilder.DropIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_PaymentRequestId",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_ProofAssetId",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_NewEnrollmentRequests_AcademyId_SubscriptionPlanId",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_AcademyNewss_AcademyId_ImageAssetId",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "FinalAmount",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "PaymentRequestId",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "ProofAssetId",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "NewEnrollmentRequests");
        }
    }
}
