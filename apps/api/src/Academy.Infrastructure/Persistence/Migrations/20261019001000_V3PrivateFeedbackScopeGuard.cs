using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Academy.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
[Migration("20261019001000_V3PrivateFeedbackScopeGuard")]
public sealed class V3PrivateFeedbackScopeGuard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ DECLARE definition text; BEGIN
          SELECT pg_get_functiondef('academy_feedback_change_guard()'::regprocedure) INTO definition;
          -- These existing scope tables have singular names, unlike the DbSet properties.
          definition := replace(definition, '"MembershipBranchAccesses"', '"MembershipBranchAccess"');
          definition := replace(definition, '"MembershipSportAccesses"', '"MembershipSportAccess"');
          EXECUTE definition;
        END $$;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Preserve the corrected private scope guard; use a reviewed forward migration.");
}
