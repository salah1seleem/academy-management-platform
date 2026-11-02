using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MobilePushDeliveryAndCoachAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MobilePushDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MobileSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedToken = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilePushDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobilePushDevices_MobileSessions_MobileSessionId",
                        column: x => x.MobileSessionId,
                        principalTable: "MobileSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilePushDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MobilePushDeliverys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilePushDeliverys", x => x.Id);
                    table.UniqueConstraint("AK_MobilePushDeliverys_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MobilePushDeliverys_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilePushDeliverys_AcademyNotices_AcademyId_NoticeId",
                        columns: x => new { x.AcademyId, x.NoticeId },
                        principalTable: "AcademyNotices",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilePushDeliverys_MobilePushDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "MobilePushDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDeliverys_AcademyId_NoticeId_DeviceId",
                table: "MobilePushDeliverys",
                columns: new[] { "AcademyId", "NoticeId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDeliverys_DeviceId",
                table: "MobilePushDeliverys",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDevices_MobileSessionId",
                table: "MobilePushDevices",
                column: "MobileSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDevices_TokenHash",
                table: "MobilePushDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushDevices_UserId",
                table: "MobilePushDevices",
                column: "UserId");
            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION academy_match_coach_history_guard() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
            IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
            IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Match coach history is append-only' USING ERRCODE='23514'; END IF;
            PERFORM pg_advisory_xact_lock(hashtextextended('match:'||NEW."AcademyId"::text||':'||NEW."MatchId"::text,0));
            IF NEW."Sequence"<>(SELECT COALESCE(MAX(a."Sequence"),0)+1 FROM "MatchCoachAssignments" a WHERE a."AcademyId"=NEW."AcademyId" AND a."MatchId"=NEW."MatchId")
            THEN RAISE EXCEPTION 'Match coach sequence changed' USING ERRCODE='23514'; END IF;
            IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."CoachMembershipId" AND m."Role"='Coach')
            OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."AssignedByMembershipId" AND (m."Role" IN ('AcademyOwner','AcademyAdmin') OR (m."Role"='Coach' AND m."IsActive" AND NEW."AssignedByMembershipId"=NEW."CoachMembershipId" AND NEW."Sequence"=1 AND EXISTS(SELECT 1 FROM "AcademyMatchs" x JOIN "MatchTeams" t ON t."Id"=x."TeamId" AND t."AcademyId"=x."AcademyId" WHERE x."Id"=NEW."MatchId" AND x."AcademyId"=NEW."AcademyId" AND t."CoachCanManageMatches" AND t."IsActive" AND t."CoachMembershipId"=m."Id"))))
            THEN RAISE EXCEPTION 'Match coach or reviewer identity invalid' USING ERRCODE='23514'; END IF;
            RETURN NEW;
            END $$;

            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobilePushDeliverys");

            migrationBuilder.DropTable(
                name: "MobilePushDevices");
        }
    }
}
