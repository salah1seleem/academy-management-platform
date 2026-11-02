using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4MobileExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoReference",
                table: "GuardianProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemoPortraitReference",
                table: "Employees",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MonthlyPlayerStars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyPlayerStars", x => x.Id);
                    table.UniqueConstraint("AK_MonthlyPlayerStars_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_MonthlyPlayerStar_Budget", "\"IsActive\" AND \"Slot\" BETWEEN 1 AND 3 AND EXTRACT(DAY FROM \"Month\")=1");
                    table.ForeignKey(
                        name: "FK_MonthlyPlayerStars_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MonthlyPlayerStars_AcademyMemberships_AcademyId_CoachMember~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MonthlyPlayerStars_MatchTeams_AcademyId_TeamId",
                        columns: x => new { x.AcademyId, x.TeamId },
                        principalTable: "MatchTeams",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MonthlyPlayerStars_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoticeDispatchs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeDispatchs", x => x.Id);
                    table.UniqueConstraint("AK_NoticeDispatchs_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_NoticeDispatchs_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoticeDispatchs_AcademyMemberships_AcademyId_ActorMembershi~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoticeTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeTemplates", x => x.Id);
                    table.UniqueConstraint("AK_NoticeTemplates_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_NoticeTemplates_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlayerStars_AcademyId_CoachMembershipId_RequestId",
                table: "MonthlyPlayerStars",
                columns: new[] { "AcademyId", "CoachMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlayerStars_AcademyId_CoachMembershipId_TeamId_Month~",
                table: "MonthlyPlayerStars",
                columns: new[] { "AcademyId", "CoachMembershipId", "TeamId", "Month", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlayerStars_AcademyId_PlayerId",
                table: "MonthlyPlayerStars",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlayerStars_AcademyId_TeamId",
                table: "MonthlyPlayerStars",
                columns: new[] { "AcademyId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDispatchs_AcademyId_ActorMembershipId_RequestId",
                table: "NoticeDispatchs",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeTemplates_AcademyId_Key",
                table: "NoticeTemplates",
                columns: new[] { "AcademyId", "Key" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION v4_monthly_star_preserved() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Monthly star history is immutable' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER monthly_star_preserved BEFORE UPDATE OR DELETE ON "MonthlyPlayerStars"
                FOR EACH ROW EXECUTE FUNCTION v4_monthly_star_preserved();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"MonthlyPlayerStars\") OR EXISTS (SELECT 1 FROM \"NoticeDispatchs\") OR EXISTS (SELECT 1 FROM \"NoticeTemplates\") THEN RAISE EXCEPTION 'Saved stars and notification evidence require a reviewed forward migration'; END IF; END $$;");
            migrationBuilder.Sql("DROP TRIGGER monthly_star_preserved ON \"MonthlyPlayerStars\"; DROP FUNCTION v4_monthly_star_preserved();");
            migrationBuilder.DropTable(
                name: "MonthlyPlayerStars");

            migrationBuilder.DropTable(
                name: "NoticeDispatchs");

            migrationBuilder.DropTable(
                name: "NoticeTemplates");

            migrationBuilder.DropColumn(
                name: "PhotoReference",
                table: "GuardianProfiles");

            migrationBuilder.DropColumn(
                name: "DemoPortraitReference",
                table: "Employees");
        }
    }
}
