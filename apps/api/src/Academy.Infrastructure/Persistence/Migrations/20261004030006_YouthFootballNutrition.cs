using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class YouthFootballNutrition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NutritionItems_Values",
                table: "NutritionItems");

            migrationBuilder.AddColumn<int>(
                name: "MaximumAge",
                table: "NutritionItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumAge",
                table: "NutritionItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServingProfile",
                table: "NutritionItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServingWeightGrams",
                table: "NutritionItems",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "NutritionItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SuitableForRestDay",
                table: "NutritionItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuitableForTrainingDay",
                table: "NutritionItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuitablePostTraining",
                table: "NutritionItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuitablePreTraining",
                table: "NutritionItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NutritionItems_AgeRange",
                table: "NutritionItems",
                sql: "(\"MinimumAge\" IS NULL AND \"MaximumAge\" IS NULL) OR (\"MinimumAge\" >= 0 AND \"MaximumAge\" >= \"MinimumAge\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NutritionItems_Values",
                table: "NutritionItems",
                sql: "(\"Calories\" IS NULL OR \"Calories\" >= 0) AND (\"ProteinGrams\" IS NULL OR \"ProteinGrams\" >= 0) AND (\"CarbohydratesGrams\" IS NULL OR \"CarbohydratesGrams\" >= 0) AND (\"FatGrams\" IS NULL OR \"FatGrams\" >= 0) AND (\"ServingWeightGrams\" IS NULL OR \"ServingWeightGrams\" > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NutritionItems_AgeRange",
                table: "NutritionItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NutritionItems_Values",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "MaximumAge",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "MinimumAge",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "ServingProfile",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "ServingWeightGrams",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "SuitableForRestDay",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "SuitableForTrainingDay",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "SuitablePostTraining",
                table: "NutritionItems");

            migrationBuilder.DropColumn(
                name: "SuitablePreTraining",
                table: "NutritionItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NutritionItems_Values",
                table: "NutritionItems",
                sql: "(\"Calories\" IS NULL OR \"Calories\" >= 0) AND (\"ProteinGrams\" IS NULL OR \"ProteinGrams\" >= 0) AND (\"CarbohydratesGrams\" IS NULL OR \"CarbohydratesGrams\" >= 0) AND (\"FatGrams\" IS NULL OR \"FatGrams\" >= 0)");
        }
    }
}
