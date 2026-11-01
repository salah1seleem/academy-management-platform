using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffAdvanceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PayrollAdditionRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Addition");

            migrationBuilder.AddColumn<decimal>(
                name: "RequestedAmount",
                table: "PayrollAdditionRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollRequest_Kind",
                table: "PayrollAdditionRequests",
                sql: "\"Kind\" IN ('Addition','Advance') AND ((\"Kind\"='Addition' AND \"RequestedAmount\" IS NULL AND \"Status\" IN ('Pending','Approved','Rejected')) OR (\"Kind\"='Advance' AND \"RequestedAmount\" > 0 AND \"RequestedAmount\" <= 100000000 AND \"RequestedAmount\" IS NOT NULL AND \"Status\" IN ('Pending','Approved','Rejected','Paid')))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollRequest_Kind",
                table: "PayrollAdditionRequests");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PayrollAdditionRequests");

            migrationBuilder.DropColumn(
                name: "RequestedAmount",
                table: "PayrollAdditionRequests");
        }
    }
}
