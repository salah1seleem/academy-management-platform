using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Academy.Infrastructure.Persistence.Migrations;
[DbContext(typeof(FoundationDbContext))]
[Migration("20261029090000_InternalEvaluationPublication")]
public sealed class InternalEvaluationPublication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE "PlayerEvaluations" DROP CONSTRAINT "CK_PlayerEvaluations_Status";
      ALTER TABLE "PlayerEvaluations" ADD CONSTRAINT "CK_PlayerEvaluations_Status" CHECK ("Status" IN ('Draft','Published','Superseded','PublishedInternal'));
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      UPDATE "PlayerEvaluations" SET "Status"='Draft', "PublishedAtUtc"=NULL, "PublishedByUserId"=NULL WHERE "Status"='PublishedInternal';
      ALTER TABLE "PlayerEvaluations" DROP CONSTRAINT "CK_PlayerEvaluations_Status";
      ALTER TABLE "PlayerEvaluations" ADD CONSTRAINT "CK_PlayerEvaluations_Status" CHECK ("Status" IN ('Draft','Published','Superseded'));
      """);
}
