using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Academy.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
[Migration("20261102050000_AppPlatformDataProtection")]
public sealed class AppPlatformDataProtection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE app_data_protection_keys (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, xml text NOT NULL);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Preserve session key history; restore a reviewed backup instead.");
}
