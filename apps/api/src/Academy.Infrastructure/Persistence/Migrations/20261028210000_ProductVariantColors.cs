using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Academy.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
[Migration("20261028210000_ProductVariantColors")]
public sealed class ProductVariantColors : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("Color", "ProductStockVariants", type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "");
        m.AddColumn<string>("ColorSnapshot", "ProductSales", type: "character varying(40)", maxLength: 40, nullable: true);
        m.DropIndex("IX_ProductStockVariants_AcademyId_ProductId_Size", "ProductStockVariants");
        m.DropIndex("IX_ProductStockVariants_AcademyId_ProductId_Size_ShirtNumber", "ProductStockVariants");
        m.CreateIndex("IX_ProductStockVariants_AcademyId_ProductId_Color_Size", "ProductStockVariants", new[]{"AcademyId","ProductId","Color","Size"}, unique:true, filter:"\"ShirtNumber\" IS NULL");
        m.CreateIndex("IX_ProductStockVariants_AcademyId_ProductId_Color_Size_ShirtNumber", "ProductStockVariants", new[]{"AcademyId","ProductId","Color","Size","ShirtNumber"}, unique:true, filter:"\"ShirtNumber\" IS NOT NULL");
    }
    protected override void Down(MigrationBuilder m)
    {
        // Rolling back while multiple colors share a size must fail rather than merge stock.
        m.DropIndex("IX_ProductStockVariants_AcademyId_ProductId_Color_Size", "ProductStockVariants");
        m.DropIndex("IX_ProductStockVariants_AcademyId_ProductId_Color_Size_ShirtNumber", "ProductStockVariants");
        m.CreateIndex("IX_ProductStockVariants_AcademyId_ProductId_Size", "ProductStockVariants", new[]{"AcademyId","ProductId","Size"}, unique:true, filter:"\"ShirtNumber\" IS NULL");
        m.CreateIndex("IX_ProductStockVariants_AcademyId_ProductId_Size_ShirtNumber", "ProductStockVariants", new[]{"AcademyId","ProductId","Size","ShirtNumber"}, unique:true, filter:"\"ShirtNumber\" IS NOT NULL");
        m.DropColumn("Color", "ProductStockVariants");
        m.DropColumn("ColorSnapshot", "ProductSales");
    }
}
