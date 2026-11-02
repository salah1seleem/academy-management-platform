using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ReceivableIdentitySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "ReceivableDocuments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "PlayerNameSnapshot",
                table: "ReceivableDocuments",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            // Forward-only schema upgrade: backfill the newly added identity fields.
            // ALTER TABLE holds the table lock for this migration transaction; no
            // application write can bypass the immutable trigger during the backfill.
            migrationBuilder.Sql("""
                ALTER TABLE "ReceivableDocuments" DISABLE TRIGGER posted_receivable_document_immutable;
                UPDATE "ReceivableDocuments" d
                    SET "PlayerId" = e."PlayerId", "PlayerNameSnapshot" = p."ArabicName"
                    FROM "SportEnrollments" e JOIN "Players" p
                      ON p."Id" = e."PlayerId" AND p."AcademyId" = e."AcademyId"
                    WHERE e."Id" = d."SportEnrollmentId" AND e."AcademyId" = d."AcademyId";
                ALTER TABLE "ReceivableDocuments" ENABLE TRIGGER posted_receivable_document_immutable;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_AcademyId_PlayerId",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReceivableDocuments_Players_AcademyId_PlayerId",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "PlayerId" },
                principalTable: "Players",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReceivableDocuments_Players_AcademyId_PlayerId",
                table: "ReceivableDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ReceivableDocuments_AcademyId_PlayerId",
                table: "ReceivableDocuments");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "ReceivableDocuments");

            migrationBuilder.DropColumn(
                name: "PlayerNameSnapshot",
                table: "ReceivableDocuments");
        }
    }
}
