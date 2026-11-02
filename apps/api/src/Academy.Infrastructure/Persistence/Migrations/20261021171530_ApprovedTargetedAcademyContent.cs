using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedTargetedAcademyContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "AudienceRoles",
                table: "AcademyNewss",
                type: "text[]",
                nullable: false,
                defaultValue: new[] { "AcademyAdmin", "Coach", "Guardian" });

            migrationBuilder.AddColumn<string>(
                name: "CreationHash",
                table: "AcademyNewss",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreationRequestId",
                table: "AcademyNewss",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndsAtUtc",
                table: "AcademyNewss",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "AcademyNewss",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "News");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartsAtUtc",
                table: "AcademyNewss",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "TargetBranchIds",
                table: "AcademyNewss",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNewss_AcademyId_CreationRequestId",
                table: "AcademyNewss",
                columns: new[] { "AcademyId", "CreationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNewss_AcademyId_IsActive_PublishedAtUtc",
                table: "AcademyNewss",
                columns: new[] { "AcademyId", "IsActive", "PublishedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "academy_content_audience",
                table: "AcademyNewss",
                sql: "cardinality(\"AudienceRoles\") BETWEEN 1 AND 3 AND \"AudienceRoles\" <@ ARRAY['AcademyAdmin','Coach','Guardian']::text[]");

            migrationBuilder.AddCheckConstraint(
                name: "academy_content_dates",
                table: "AcademyNewss",
                sql: "(\"StartsAtUtc\" IS NULL AND \"EndsAtUtc\" IS NULL) OR (\"StartsAtUtc\" IS NOT NULL AND \"EndsAtUtc\" > \"StartsAtUtc\" AND \"EndsAtUtc\" <= \"StartsAtUtc\" + interval '90 days')");

            migrationBuilder.AddCheckConstraint(
                name: "academy_content_kind",
                table: "AcademyNewss",
                sql: "\"Kind\" IN ('News','Event','Announcement')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "AcademyNewss" WHERE "Kind" <> 'News' OR cardinality("TargetBranchIds") > 0
                    OR "StartsAtUtc" IS NOT NULL OR "CreationRequestId" IS NOT NULL
                    OR NOT ("AudienceRoles" @> ARRAY['AcademyAdmin','Coach','Guardian']::text[]))
                  THEN RAISE EXCEPTION 'Targeted content must be preserved before reverting this migration'; END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_AcademyNewss_AcademyId_CreationRequestId",
                table: "AcademyNewss");

            migrationBuilder.DropIndex(
                name: "IX_AcademyNewss_AcademyId_IsActive_PublishedAtUtc",
                table: "AcademyNewss");

            migrationBuilder.DropCheckConstraint(
                name: "academy_content_audience",
                table: "AcademyNewss");

            migrationBuilder.DropCheckConstraint(
                name: "academy_content_dates",
                table: "AcademyNewss");

            migrationBuilder.DropCheckConstraint(
                name: "academy_content_kind",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "AudienceRoles",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "CreationHash",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "CreationRequestId",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "EndsAtUtc",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "AcademyNewss");

            migrationBuilder.DropColumn(
                name: "TargetBranchIds",
                table: "AcademyNewss");
        }
    }
}
