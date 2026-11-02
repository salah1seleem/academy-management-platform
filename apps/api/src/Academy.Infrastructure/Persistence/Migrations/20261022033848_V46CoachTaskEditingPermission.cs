using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V46CoachTaskEditingPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Assignment is a named permission within the existing Read/Edit
            // follow-up module; its generic Add operation remains forbidden.
            migrationBuilder.Sql(CoachTaskFollowUpGuards.Authority
                .Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION")
                .Replace("AND x.\"Read\" AND x.\"Add\" AND x.\"Edit\"", "AND x.\"Read\" AND x.\"Edit\""));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CoachTaskFollowUpGuards.Authority.Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION"));
        }
    }
}
