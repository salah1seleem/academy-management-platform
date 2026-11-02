using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchPushLifecycleGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MobilePushDevices_MobileSessions_MobileSessionId",
                table: "MobilePushDevices");

            migrationBuilder.DropIndex(
                name: "IX_MobilePushDevices_MobileSessionId",
                table: "MobilePushDevices");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MobileSessions_Id_UserId",
                table: "MobileSessions",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDevices_MobileSessionId_UserId",
                table: "MobilePushDevices",
                columns: new[] { "MobileSessionId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MobilePushDevices_MobileSessions_MobileSessionId_UserId",
                table: "MobilePushDevices",
                columns: new[] { "MobileSessionId", "UserId" },
                principalTable: "MobileSessions",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_match_audit_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  RAISE EXCEPTION 'Match audit is immutable' USING ERRCODE='23514';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MobilePushDevices_MobileSessions_MobileSessionId_UserId",
                table: "MobilePushDevices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MobileSessions_Id_UserId",
                table: "MobileSessions");

            migrationBuilder.DropIndex(
                name: "IX_MobilePushDevices_MobileSessionId_UserId",
                table: "MobilePushDevices");

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDevices_MobileSessionId",
                table: "MobilePushDevices",
                column: "MobileSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_MobilePushDevices_MobileSessions_MobileSessionId",
                table: "MobilePushDevices",
                column: "MobileSessionId",
                principalTable: "MobileSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
