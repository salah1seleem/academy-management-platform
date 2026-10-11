using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ImportRetryIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademyImportJobs_AcademyId_SourceSystem_FileHash",
                table: "AcademyImportJobs");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_AcademyId_SourceSystem_FileHash",
                table: "AcademyImportJobs",
                columns: new[] { "AcademyId", "SourceSystem", "FileHash" },
                unique: true,
                filter: "\"State\" IN ('Queued', 'Running', 'Completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademyImportJobs_AcademyId_SourceSystem_FileHash",
                table: "AcademyImportJobs");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_AcademyId_SourceSystem_FileHash",
                table: "AcademyImportJobs",
                columns: new[] { "AcademyId", "SourceSystem", "FileHash" },
                unique: true);
        }
    }
}
