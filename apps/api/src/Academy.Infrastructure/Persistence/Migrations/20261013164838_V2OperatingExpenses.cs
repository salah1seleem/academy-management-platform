using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2OperatingExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperatingExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Note = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostRequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostQuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingExpenses", x => x.Id);
                    table.UniqueConstraint("AK_OperatingExpenses_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_OperatingExpense_Terms", "\"Amount\">0 AND \"PeriodFrom\"<=\"PeriodTo\" AND \"Status\" BETWEEN 1 AND 4 AND ((\"Scope\"=1 AND \"TrainingGroupId\" IS NOT NULL AND \"ResourceId\" IS NULL) OR (\"Scope\"=2 AND \"ResourceId\" IS NOT NULL AND \"TrainingGroupId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_FacilityResources_AcademyId_ResourceId",
                        columns: x => new { x.AcademyId, x.ResourceId },
                        principalTable: "FacilityResources",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_TrainingGroups_AcademyId_TrainingGroupId",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenses_Users_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatingExpenseAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingExpenseAllocations", x => x.Id);
                    table.UniqueConstraint("AK_OperatingExpenseAllocations_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_OperatingExpenseAllocation_Terms", "\"Amount\">=0 AND \"Weight\">=0");
                    table.ForeignKey(
                        name: "FK_OperatingExpenseAllocations_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenseAllocations_OperatingExpenses_AcademyId_Exp~",
                        columns: x => new { x.AcademyId, x.ExpenseId },
                        principalTable: "OperatingExpenses",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatingExpenseAllocations_TrainingGroups_AcademyId_Traini~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenseAllocations_AcademyId_ExpenseId_TrainingGro~",
                table: "OperatingExpenseAllocations",
                columns: new[] { "AcademyId", "ExpenseId", "TrainingGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenseAllocations_AcademyId_TrainingGroupId",
                table: "OperatingExpenseAllocations",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_AcademyId_BranchId",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_AcademyId_CreatedByUserId_RequestKey",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "CreatedByUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_AcademyId_PeriodFrom_PeriodTo_Status",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "PeriodFrom", "PeriodTo", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_AcademyId_ResourceId",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_AcademyId_TrainingGroupId",
                table: "OperatingExpenses",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_ApprovedByUserId",
                table: "OperatingExpenses",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_CreatedByUserId",
                table: "OperatingExpenses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_PostedByUserId",
                table: "OperatingExpenses",
                column: "PostedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatingExpenseAllocations");

            migrationBuilder.DropTable(
                name: "OperatingExpenses");
        }
    }
}
