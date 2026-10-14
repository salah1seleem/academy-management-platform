using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlanSportScopeLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_plan_in_sports(academy uuid, plan uuid, sports uuid[])
                RETURNS boolean LANGUAGE sql STABLE AS $$
                    SELECT EXISTS (SELECT 1 FROM "SubscriptionPlans" p
                        WHERE p."AcademyId" = academy AND p."Id" = plan AND p."SportId" = ANY(sports));
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION academy_plan_in_sports(uuid, uuid, uuid[]);");
        }
    }
}
