using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedProductVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShirtNumber",
                table: "SportEnrollments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NumberedUniform",
                table: "SportCatalogItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StockRevision",
                table: "SportCatalogItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UsesVariants",
                table: "SportCatalogItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ShirtNumberSnapshot",
                table: "ProductSales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeSnapshot",
                table: "ProductSales",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SportEnrollmentId",
                table: "ProductSales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "ProductSales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductStockVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Size = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShirtNumber = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductStockVariants", x => x.Id);
                    table.UniqueConstraint("AK_ProductStockVariants_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_ProductStockVariants_AcademyId_Id_ProductId", x => new { x.AcademyId, x.Id, x.ProductId });
                    table.CheckConstraint("CK_ProductStockVariant_Values", "\"Quantity\" >= 0 AND (\"ShirtNumber\" IS NULL OR \"ShirtNumber\" BETWEEN 0 AND 999) AND length(btrim(\"Size\")) > 0");
                    table.ForeignKey(
                        name: "FK_ProductStockVariants_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductStockVariants_SportCatalogItems_AcademyId_ProductId",
                        columns: x => new { x.AcademyId, x.ProductId },
                        principalTable: "SportCatalogItems",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SportEnrollment_ShirtNumber",
                table: "SportEnrollments",
                sql: "\"ShirtNumber\" IS NULL OR \"ShirtNumber\" BETWEEN 0 AND 999");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_SportEnrollmentId_SportId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_VariantId_ProductId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "VariantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductStockVariants_AcademyId_IsActive",
                table: "ProductStockVariants",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductStockVariants_AcademyId_ProductId_Size",
                table: "ProductStockVariants",
                columns: new[] { "AcademyId", "ProductId", "Size" },
                unique: true,
                filter: "\"ShirtNumber\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductStockVariants_AcademyId_ProductId_Size_ShirtNumber",
                table: "ProductStockVariants",
                columns: new[] { "AcademyId", "ProductId", "Size", "ShirtNumber" },
                unique: true,
                filter: "\"ShirtNumber\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSales_ProductStockVariants_AcademyId_VariantId_Produ~",
                table: "ProductSales",
                columns: new[] { "AcademyId", "VariantId", "ProductId" },
                principalTable: "ProductStockVariants",
                principalColumns: new[] { "AcademyId", "Id", "ProductId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSales_SportEnrollments_AcademyId_SportEnrollmentId_S~",
                table: "ProductSales",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" },
                principalTable: "SportEnrollments",
                principalColumns: new[] { "AcademyId", "Id", "SportId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductSales_ProductStockVariants_AcademyId_VariantId_Produ~",
                table: "ProductSales");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSales_SportEnrollments_AcademyId_SportEnrollmentId_S~",
                table: "ProductSales");

            migrationBuilder.DropTable(
                name: "ProductStockVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SportEnrollment_ShirtNumber",
                table: "SportEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_ProductSales_AcademyId_SportEnrollmentId_SportId",
                table: "ProductSales");

            migrationBuilder.DropIndex(
                name: "IX_ProductSales_AcademyId_VariantId_ProductId",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "ShirtNumber",
                table: "SportEnrollments");

            migrationBuilder.DropColumn(
                name: "NumberedUniform",
                table: "SportCatalogItems");

            migrationBuilder.DropColumn(
                name: "StockRevision",
                table: "SportCatalogItems");

            migrationBuilder.DropColumn(
                name: "UsesVariants",
                table: "SportCatalogItems");

            migrationBuilder.DropColumn(
                name: "ShirtNumberSnapshot",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "SizeSnapshot",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "SportEnrollmentId",
                table: "ProductSales");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "ProductSales");
        }
    }
}
