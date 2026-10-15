using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3MatchDraftCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CopiedByMembershipId",
                table: "AcademyMatchs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CopiedFromMatchId",
                table: "AcademyMatchs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopyRequestHash",
                table: "AcademyMatchs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopyRequestKey",
                table: "AcademyMatchs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMatchs_AcademyId_CopiedByMembershipId_CopyRequestKey",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "CopiedByMembershipId", "CopyRequestKey" },
                unique: true,
                filter: "\"CopiedFromMatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMatchs_AcademyId_CopiedFromMatchId",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "CopiedFromMatchId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_Copy",
                table: "AcademyMatchs",
                sql: "(\"CopiedFromMatchId\" IS NULL AND \"CopiedByMembershipId\" IS NULL AND \"CopyRequestKey\" IS NULL AND \"CopyRequestHash\" IS NULL) OR (\"CopiedFromMatchId\" IS NOT NULL AND \"CopiedFromMatchId\"<>\"Id\" AND \"CopiedByMembershipId\" IS NOT NULL AND \"CopyRequestKey\" IS NOT NULL AND length(\"CopyRequestKey\") BETWEEN 8 AND 100 AND \"CopyRequestHash\" IS NOT NULL AND length(\"CopyRequestHash\")=64)");

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyMatchs_AcademyMatchs_AcademyId_CopiedFromMatchId",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "CopiedFromMatchId" },
                principalTable: "AcademyMatchs",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyMatchs_AcademyMemberships_AcademyId_CopiedByMembersh~",
                table: "AcademyMatchs",
                columns: new[] { "AcademyId", "CopiedByMembershipId" },
                principalTable: "AcademyMemberships",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "AcademyMatchs" WHERE "CopiedFromMatchId" IS NOT NULL) THEN RAISE EXCEPTION 'Preserve copied match provenance before rollback' USING ERRCODE='23514'; END IF; END $$;""");
            migrationBuilder.DropForeignKey(
                name: "FK_AcademyMatchs_AcademyMatchs_AcademyId_CopiedFromMatchId",
                table: "AcademyMatchs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcademyMatchs_AcademyMemberships_AcademyId_CopiedByMembersh~",
                table: "AcademyMatchs");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMatchs_AcademyId_CopiedByMembershipId_CopyRequestKey",
                table: "AcademyMatchs");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMatchs_AcademyId_CopiedFromMatchId",
                table: "AcademyMatchs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_Copy",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CopiedByMembershipId",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CopiedFromMatchId",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CopyRequestHash",
                table: "AcademyMatchs");

            migrationBuilder.DropColumn(
                name: "CopyRequestKey",
                table: "AcademyMatchs");
        }
    }
}
