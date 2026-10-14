using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3CorrelatedAdministrativeContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships");

            migrationBuilder.AddColumn<string>(
                name: "AccessContextName",
                table: "AcademyMemberships",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContextRevision",
                table: "AcademyMemberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedContextName",
                table: "AcademyMemberships",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryMembershipId",
                table: "AcademyMemberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AcademyMemberships_AcademyId_UserId_Id",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_PrimaryMembershipId",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "PrimaryMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "Role" },
                unique: true,
                filter: "\"PrimaryMembershipId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_PrimaryMembershipId_NormalizedContextName",
                table: "AcademyMemberships",
                columns: new[] { "PrimaryMembershipId", "NormalizedContextName" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Membership_ContextShape",
                table: "AcademyMemberships",
                sql: "(\"PrimaryMembershipId\" IS NULL AND \"AccessContextName\" IS NULL AND \"NormalizedContextName\" IS NULL) OR (\"PrimaryMembershipId\" IS NOT NULL AND \"PrimaryMembershipId\" <> \"Id\" AND \"Role\" = 'AcademyAdmin' AND \"UseActionGrants\" AND length(trim(\"AccessContextName\")) > 0 AND length(trim(\"NormalizedContextName\")) > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyMemberships_AcademyMemberships_AcademyId_UserId_Prim~",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "PrimaryMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "UserId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_membership_context_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."PrimaryMembershipId" IS NOT NULL THEN
                    IF NEW."AccessContextName" IS NULL OR NEW."NormalizedContextName" IS NULL OR NEW."ContextRevision" < 1 OR NOT EXISTS (
                      SELECT 1 FROM "AcademyMemberships" p WHERE p."Id"=NEW."PrimaryMembershipId" AND p."AcademyId"=NEW."AcademyId" AND p."UserId"=NEW."UserId" AND p."Role"='AcademyAdmin' AND p."PrimaryMembershipId" IS NULL
                    ) THEN RAISE EXCEPTION 'Invalid administrative context parent' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF TG_OP='UPDATE' AND (OLD."PrimaryMembershipId" IS NOT NULL OR EXISTS(SELECT 1 FROM "AcademyMemberships" c WHERE c."PrimaryMembershipId"=OLD."Id")) AND
                    (OLD."AcademyId",OLD."UserId",OLD."Role",OLD."PrimaryMembershipId") IS DISTINCT FROM (NEW."AcademyId",NEW."UserId",NEW."Role",NEW."PrimaryMembershipId")
                  THEN RAISE EXCEPTION 'Context identity cannot be reassigned' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER academy_membership_context_guard BEFORE INSERT OR UPDATE ON "AcademyMemberships" FOR EACH ROW EXECUTE FUNCTION academy_membership_context_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "AcademyMemberships" WHERE "PrimaryMembershipId" IS NOT NULL) THEN RAISE EXCEPTION 'Review existing access contexts before downgrade'; END IF; END $$;
                DROP TRIGGER academy_membership_context_guard ON "AcademyMemberships";
                DROP FUNCTION academy_membership_context_guard();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_AcademyMemberships_AcademyMemberships_AcademyId_UserId_Prim~",
                table: "AcademyMemberships");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AcademyMemberships_AcademyId_UserId_Id",
                table: "AcademyMemberships");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_PrimaryMembershipId",
                table: "AcademyMemberships");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_PrimaryMembershipId_NormalizedContextName",
                table: "AcademyMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Membership_ContextShape",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "AccessContextName",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "ContextRevision",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "NormalizedContextName",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "PrimaryMembershipId",
                table: "AcademyMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_UserId_Role",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "UserId", "Role" },
                unique: true);
        }
    }
}
