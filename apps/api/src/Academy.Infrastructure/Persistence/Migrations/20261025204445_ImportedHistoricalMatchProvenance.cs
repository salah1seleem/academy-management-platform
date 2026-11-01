using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportedHistoricalMatchProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportedHistoricalMatchs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OriginalPayload = table.Column<string>(type: "jsonb", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedHistoricalMatchs", x => x.Id);
                    table.UniqueConstraint("AK_ImportedHistoricalMatchs_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalMatchs_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalMatchs_AcademyImportJobs_AcademyId_JobId",
                        columns: x => new { x.AcademyId, x.JobId },
                        principalTable: "AcademyImportJobs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalMatchs_AcademyMatchs_AcademyId_MatchId",
                        columns: x => new { x.AcademyId, x.MatchId },
                        principalTable: "AcademyMatchs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHistoricalMatchs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalMatchs_AcademyId_JobId",
                table: "ImportedHistoricalMatchs",
                columns: new[] { "AcademyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalMatchs_AcademyId_MatchId",
                table: "ImportedHistoricalMatchs",
                columns: new[] { "AcademyId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalMatchs_AcademyId_SourceSystem_SourceRefer~",
                table: "ImportedHistoricalMatchs",
                columns: new[] { "AcademyId", "SourceSystem", "SourceReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHistoricalMatchs_ActorUserId",
                table: "ImportedHistoricalMatchs",
                column: "ActorUserId");
            migrationBuilder.Sql("CREATE TRIGGER imported_match_immutable BEFORE UPDATE OR DELETE ON \"ImportedHistoricalMatchs\" FOR EACH ROW EXECUTE FUNCTION academy_match_audit_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportedHistoricalMatchs");
        }
    }
}
