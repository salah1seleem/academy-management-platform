using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice7GuardianContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NutritionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ArabicDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ImageReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ServingDescription = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Calories = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    ProteinGrams = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    CarbohydratesGrams = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    FatGrams = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    DataStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SourceDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionItems", x => x.Id);
                    table.UniqueConstraint("AK_NutritionItems_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_NutritionItems_Values", "(\"Calories\" IS NULL OR \"Calories\" >= 0) AND (\"ProteinGrams\" IS NULL OR \"ProteinGrams\" >= 0) AND (\"CarbohydratesGrams\" IS NULL OR \"CarbohydratesGrams\" >= 0) AND (\"FatGrams\" IS NULL OR \"FatGrams\" >= 0)");
                    table.ForeignKey(
                        name: "FK_NutritionItems_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MediaReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ThumbnailReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ArabicCaption = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPublishedToGuardian = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMedia", x => x.Id);
                    table.UniqueConstraint("AK_PlayerMedia_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlayerMedia_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMedia_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMedia_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMedicalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ArabicTitle = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    RecordDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ArabicDescription = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StaffNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GuardianVisibleNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsPublishedToGuardian = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMedicalRecords", x => x.Id);
                    table.UniqueConstraint("AK_PlayerMedicalRecords_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlayerMedicalRecords_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMedicalRecords_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMedicalRecords_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerMedicalRecords_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SportCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EnglishName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ArabicDescription = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    ImageReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DisplayPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SportCatalogItems", x => x.Id);
                    table.UniqueConstraint("AK_SportCatalogItems_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SportCatalogItems_Display", "(\"DisplayPrice\" IS NULL OR \"DisplayPrice\" >= 0) AND (\"DiscountPercentage\" IS NULL OR (\"DiscountPercentage\" >= 0 AND \"DiscountPercentage\" <= 100))");
                    table.ForeignKey(
                        name: "FK_SportCatalogItems_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SportCatalogItems_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NutritionCategoryLinks",
                columns: table => new
                {
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    NutritionItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionCategoryLinks", x => new { x.AcademyId, x.NutritionItemId, x.Category });
                    table.ForeignKey(
                        name: "FK_NutritionCategoryLinks_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NutritionCategoryLinks_NutritionItems_AcademyId_NutritionIt~",
                        columns: x => new { x.AcademyId, x.NutritionItemId },
                        principalTable: "NutritionItems",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NutritionCategoryLinks_AcademyId_Category_DisplayOrder",
                table: "NutritionCategoryLinks",
                columns: new[] { "AcademyId", "Category", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_NutritionItems_AcademyId_ArabicName",
                table: "NutritionItems",
                columns: new[] { "AcademyId", "ArabicName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NutritionItems_AcademyId_IsActive",
                table: "NutritionItems",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedia_AcademyId_IsActive",
                table: "PlayerMedia",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedia_AcademyId_PlayerId_DisplayOrder",
                table: "PlayerMedia",
                columns: new[] { "AcademyId", "PlayerId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedia_CreatedByUserId",
                table: "PlayerMedia",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedicalRecords_AcademyId_IsActive",
                table: "PlayerMedicalRecords",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedicalRecords_AcademyId_PlayerId_RecordDate",
                table: "PlayerMedicalRecords",
                columns: new[] { "AcademyId", "PlayerId", "RecordDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedicalRecords_CreatedByUserId",
                table: "PlayerMedicalRecords",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedicalRecords_UpdatedByUserId",
                table: "PlayerMedicalRecords",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SportCatalogItems_AcademyId_IsActive",
                table: "SportCatalogItems",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SportCatalogItems_AcademyId_SportId_ArabicName",
                table: "SportCatalogItems",
                columns: new[] { "AcademyId", "SportId", "ArabicName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NutritionCategoryLinks");

            migrationBuilder.DropTable(
                name: "PlayerMedia");

            migrationBuilder.DropTable(
                name: "PlayerMedicalRecords");

            migrationBuilder.DropTable(
                name: "SportCatalogItems");

            migrationBuilder.DropTable(
                name: "NutritionItems");
        }
    }
}
