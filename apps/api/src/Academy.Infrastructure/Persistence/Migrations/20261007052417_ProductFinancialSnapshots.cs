using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductFinancialSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "ProductSales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchName",
                table: "ProductSales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SportId",
                table: "ProductSales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SportName",
                table: "ProductSales",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_BranchId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_SportId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSales_Branches_AcademyId_BranchId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "BranchId" },
                principalTable: "Branches",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSales_Sports_AcademyId_SportId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "SportId" },
                principalTable: "Sports",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductSales_Branches_AcademyId_BranchId",
                table: "ProductSales");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSales_Sports_AcademyId_SportId",
                table: "ProductSales");

            migrationBuilder.DropIndex(
                name: "IX_ProductSales_AcademyId_BranchId",
                table: "ProductSales");

            migrationBuilder.DropIndex(
                name: "IX_ProductSales_AcademyId_SportId",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "BranchName",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "SportId",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "SportName",
                table: "ProductSales");
        }
    }
}
