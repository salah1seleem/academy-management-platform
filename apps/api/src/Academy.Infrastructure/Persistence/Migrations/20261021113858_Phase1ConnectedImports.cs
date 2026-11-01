using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase1ConnectedImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultSummary",
                table: "AcademyImportJobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentRegistrations", x => x.Id);
                    table.UniqueConstraint("AK_TournamentRegistrations_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_TournamentRegistrations_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentRegistrations_AcademyImportJobs_AcademyId_ImportJ~",
                        columns: x => new { x.AcademyId, x.ImportJobId },
                        principalTable: "AcademyImportJobs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentRegistrations_AcademyTournaments_AcademyId_Tourna~",
                        columns: x => new { x.AcademyId, x.TournamentId },
                        principalTable: "AcademyTournaments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentRegistrations_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentRegistrations_SportEnrollments_AcademyId_Enrollme~",
                        columns: x => new { x.AcademyId, x.EnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_AcademyId_EnrollmentId",
                table: "TournamentRegistrations",
                columns: new[] { "AcademyId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_AcademyId_ImportJobId",
                table: "TournamentRegistrations",
                columns: new[] { "AcademyId", "ImportJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_AcademyId_TeamId",
                table: "TournamentRegistrations",
                columns: new[] { "AcademyId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_AcademyId_TournamentId_EnrollmentId",
                table: "TournamentRegistrations",
                columns: new[] { "AcademyId", "TournamentId", "EnrollmentId" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_import_tournament_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM "SportEnrollments" e
                    JOIN "Players" p ON p."AcademyId"=e."AcademyId" AND p."Id"=e."PlayerId"
                    JOIN "MatchTeams" team ON team."AcademyId"=e."AcademyId" AND team."Id"=NEW."TeamId" AND team."SportId"=e."SportId" AND team."BranchId"=e."BranchId"
                    JOIN "AgeCategories" age ON age."AcademyId"=team."AcademyId" AND age."Id"=team."AgeCategoryId"
                    JOIN "AcademyTournaments" tournament ON tournament."AcademyId"=e."AcademyId" AND tournament."Id"=NEW."TournamentId" AND tournament."SportId"=e."SportId"
                    WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."EnrollmentId"
                      AND (age."MinimumBirthYear" IS NULL OR extract(year from p."DateOfBirth")>=age."MinimumBirthYear")
                      AND (age."MaximumBirthYear" IS NULL OR extract(year from p."DateOfBirth")<=age."MaximumBirthYear")
                  ) THEN RAISE EXCEPTION 'Imported tournament membership must match academy, branch, sport and age' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER tournament_import_integrity BEFORE INSERT ON "TournamentRegistrations"
                  FOR EACH ROW EXECUTE FUNCTION academy_import_tournament_integrity();
                CREATE TRIGGER tournament_import_immutable BEFORE UPDATE OR DELETE ON "TournamentRegistrations"
                  FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM "TournamentRegistrations") THEN
                  RAISE EXCEPTION 'Imported tournament history exists; do not downgrade'; END IF; END $$;
                DROP TRIGGER tournament_import_integrity ON "TournamentRegistrations";
                DROP TRIGGER tournament_import_immutable ON "TournamentRegistrations";
                DROP FUNCTION academy_import_tournament_integrity();
                """);
            migrationBuilder.DropTable(
                name: "TournamentRegistrations");

            migrationBuilder.DropColumn(
                name: "ResultSummary",
                table: "AcademyImportJobs");
        }
    }
}
