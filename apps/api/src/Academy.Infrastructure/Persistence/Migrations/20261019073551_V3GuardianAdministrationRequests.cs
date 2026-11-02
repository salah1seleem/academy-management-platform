using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3GuardianAdministrationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GuardianFeedback_KindScore",
                table: "GuardianFeedbacks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GuardianFeedback_KindScore",
                table: "GuardianFeedbacks",
                sql: "(\"Kind\" = 'Rating' AND \"CoachMembershipId\" IS NOT NULL AND \"Score\" BETWEEN 1 AND 5) OR (\"Kind\" IN ('Complaint','Suggestion','Request') AND \"Score\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Preserve family requests with a reviewed forward migration' USING ERRCODE='23514'; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_GuardianFeedback_KindScore",
                table: "GuardianFeedbacks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GuardianFeedback_KindScore",
                table: "GuardianFeedbacks",
                sql: "(\"Kind\" = 'Rating' AND \"CoachMembershipId\" IS NOT NULL AND \"Score\" BETWEEN 1 AND 5) OR (\"Kind\" IN ('Complaint','Suggestion') AND \"Score\" IS NULL)");
        }
    }
}
