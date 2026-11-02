using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ScopedDemoLedgerReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_posted_receivable_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN
                    IF TG_OP = 'DELETE' AND OLD."AcademyId" = '30000000-0000-0000-0000-000000000003'::uuid
                      AND current_setting('academy.reset_demo_tenant', true) = OLD."AcademyId"::text
                      AND EXISTS (SELECT 1 FROM "Academies" a WHERE a."Id" = OLD."AcademyId" AND a."Slug" = 'football-demo' AND a."EnglishName" = 'Football Demo Dataset v1') THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'Posted records are immutable; append a correcting movement'
                        USING ERRCODE = '23514';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_posted_receivable_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN
                    RAISE EXCEPTION 'Posted records are immutable; append a correcting movement'
                        USING ERRCODE = '23514';
                END $$;
                """);
        }
    }
}
