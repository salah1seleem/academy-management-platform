using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice5PlayerEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EnglishName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    FootballAxis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCriteria", x => x.Id);
                    table.UniqueConstraint("AK_EvaluationCriteria_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_EvaluationCriteria_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_EvaluationCriteria_Weight", "\"Weight\" > 0");
                    table.ForeignKey(
                        name: "FK_EvaluationCriteria_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationCriteria_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportingPeriod = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GeneralNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ReplacesEvaluationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEvaluations", x => x.Id);
                    table.UniqueConstraint("AK_PlayerEvaluations_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerEvaluations_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_PlayerEvaluations_Status", "\"Status\" IN ('Draft','Published','Superseded')");
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_PlayerEvaluations_AcademyId_ReplacesEvalu~",
                        columns: x => new { x.AcademyId, x.ReplacesEvaluationId },
                        principalTable: "PlayerEvaluations",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_SportEnrollments_AcademyId_SportEnrollmen~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_SportEnrollments_AcademyId_SportEnrollme~1",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.TrainingGroupId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_TrainingGroups_AcademyId_TrainingGroupId",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_Users_EvaluatedByUserId",
                        column: x => x.EvaluatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerEvaluations_Users_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationCriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriterionNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    WeightSnapshot = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    FootballAxisSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationScores", x => x.Id);
                    table.UniqueConstraint("AK_EvaluationScores_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_EvaluationScores_Score", "\"Score\" IS NULL OR (\"Score\" >= 0 AND \"Score\" <= 100)");
                    table.CheckConstraint("CK_EvaluationScores_WeightSnapshot", "\"WeightSnapshot\" > 0");
                    table.ForeignKey(
                        name: "FK_EvaluationScores_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationScores_EvaluationCriteria_AcademyId_EvaluationCri~",
                        columns: x => new { x.AcademyId, x.EvaluationCriterionId, x.SportId },
                        principalTable: "EvaluationCriteria",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationScores_PlayerEvaluations_AcademyId_PlayerEvaluati~",
                        columns: x => new { x.AcademyId, x.PlayerEvaluationId, x.SportId },
                        principalTable: "PlayerEvaluations",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCriteria_AcademyId_IsActive",
                table: "EvaluationCriteria",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCriteria_AcademyId_SportId_ArabicName",
                table: "EvaluationCriteria",
                columns: new[] { "AcademyId", "SportId", "ArabicName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCriteria_AcademyId_SportId_DisplayOrder",
                table: "EvaluationCriteria",
                columns: new[] { "AcademyId", "SportId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_AcademyId_EvaluationCriterionId_SportId",
                table: "EvaluationScores",
                columns: new[] { "AcademyId", "EvaluationCriterionId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_AcademyId_IsActive",
                table: "EvaluationScores",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_AcademyId_PlayerEvaluationId_EvaluationCri~",
                table: "EvaluationScores",
                columns: new[] { "AcademyId", "PlayerEvaluationId", "EvaluationCriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_AcademyId_PlayerEvaluationId_SportId",
                table: "EvaluationScores",
                columns: new[] { "AcademyId", "PlayerEvaluationId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_IsActive",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_ReplacesEvaluationId",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "ReplacesEvaluationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_SportEnrollmentId_EvaluationDate",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "SportEnrollmentId", "EvaluationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_SportEnrollmentId_SportId",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_SportEnrollmentId_TrainingGroup~",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_SportId",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_TrainingGroupId",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_EvaluatedByUserId",
                table: "PlayerEvaluations",
                column: "EvaluatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_PublishedByUserId",
                table: "PlayerEvaluations",
                column: "PublishedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationScores");

            migrationBuilder.DropTable(
                name: "EvaluationCriteria");

            migrationBuilder.DropTable(
                name: "PlayerEvaluations");
        }
    }
}
