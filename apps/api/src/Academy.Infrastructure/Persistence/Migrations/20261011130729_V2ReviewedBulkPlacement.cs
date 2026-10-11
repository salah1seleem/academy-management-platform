using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ReviewedBulkPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TrainingLevelId",
                table: "TrainingGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "GroupAssignedFrom",
                table: "SportEnrollments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LevelReviewedAtUtc",
                table: "SportEnrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LevelReviewedByUserId",
                table: "SportEnrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrainingLevelId",
                table: "SportEnrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupPlacementRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    Placed = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPlacementRevisions", x => x.Id);
                    table.UniqueConstraint("AK_GroupPlacementRevisions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_GroupPlacementRevisions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupPlacementRevisions_TrainingGroups_AcademyId_TrainingGr~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupPlacementRevisions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingLevels", x => x.Id);
                    table.UniqueConstraint("AK_TrainingLevels_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_TrainingLevels_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_TrainingLevel_Rank", "\"Rank\" BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "FK_TrainingLevels_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingLevels_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingGroups_AcademyId_TrainingLevelId_SportId",
                table: "TrainingGroups",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_AcademyId_TrainingLevelId_SportId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SportEnrollments_LevelReviewedByUserId",
                table: "SportEnrollments",
                column: "LevelReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupPlacementRevisions_AcademyId_ActorUserId_RequestKey",
                table: "GroupPlacementRevisions",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPlacementRevisions_AcademyId_TrainingGroupId",
                table: "GroupPlacementRevisions",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupPlacementRevisions_ActorUserId",
                table: "GroupPlacementRevisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingLevels_AcademyId_SportId_ArabicName",
                table: "TrainingLevels",
                columns: new[] { "AcademyId", "SportId", "ArabicName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SportEnrollments_TrainingLevels_AcademyId_TrainingLevelId_S~",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" },
                principalTable: "TrainingLevels",
                principalColumns: new[] { "AcademyId", "Id", "SportId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SportEnrollments_Users_LevelReviewedByUserId",
                table: "SportEnrollments",
                column: "LevelReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingGroups_TrainingLevels_AcademyId_TrainingLevelId_Spo~",
                table: "TrainingGroups",
                columns: new[] { "AcademyId", "TrainingLevelId", "SportId" },
                principalTable: "TrainingLevels",
                principalColumns: new[] { "AcademyId", "Id", "SportId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE TRIGGER group_placement_revision_immutable BEFORE UPDATE OR DELETE ON "GroupPlacementRevisions"
                FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS group_placement_revision_immutable ON \"GroupPlacementRevisions\";");
            migrationBuilder.DropForeignKey(
                name: "FK_SportEnrollments_TrainingLevels_AcademyId_TrainingLevelId_S~",
                table: "SportEnrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_SportEnrollments_Users_LevelReviewedByUserId",
                table: "SportEnrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingGroups_TrainingLevels_AcademyId_TrainingLevelId_Spo~",
                table: "TrainingGroups");

            migrationBuilder.DropTable(
                name: "GroupPlacementRevisions");

            migrationBuilder.DropTable(
                name: "TrainingLevels");

            migrationBuilder.DropIndex(
                name: "IX_TrainingGroups_AcademyId_TrainingLevelId_SportId",
                table: "TrainingGroups");

            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_AcademyId_TrainingLevelId_SportId",
                table: "SportEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_SportEnrollments_LevelReviewedByUserId",
                table: "SportEnrollments");

            migrationBuilder.DropColumn(
                name: "TrainingLevelId",
                table: "TrainingGroups");

            migrationBuilder.DropColumn(
                name: "GroupAssignedFrom",
                table: "SportEnrollments");

            migrationBuilder.DropColumn(
                name: "LevelReviewedAtUtc",
                table: "SportEnrollments");

            migrationBuilder.DropColumn(
                name: "LevelReviewedByUserId",
                table: "SportEnrollments");

            migrationBuilder.DropColumn(
                name: "TrainingLevelId",
                table: "SportEnrollments");
        }
    }
}
