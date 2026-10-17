using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3MatchCoachHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchCoachAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCoachAssignments", x => x.Id);
                    table.UniqueConstraint("AK_MatchCoachAssignments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_MatchCoachAssignment_Identity", "\"Sequence\">0 AND \"IsActive\" AND \"Source\" IN ('DraftSelection','FirstPublication','Reassignment','LegacyReview','DemoFixture')");
                    table.ForeignKey(
                        name: "FK_MatchCoachAssignments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCoachAssignments_AcademyMatchs_AcademyId_MatchId",
                        columns: x => new { x.AcademyId, x.MatchId },
                        principalTable: "AcademyMatchs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCoachAssignments_AcademyMemberships_AcademyId_Assigned~",
                        columns: x => new { x.AcademyId, x.AssignedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCoachAssignments_AcademyMemberships_AcademyId_CoachMem~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchCoachAssignments_AcademyId_AssignedByMembershipId",
                table: "MatchCoachAssignments",
                columns: new[] { "AcademyId", "AssignedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchCoachAssignments_AcademyId_CoachMembershipId",
                table: "MatchCoachAssignments",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchCoachAssignments_AcademyId_MatchId_Sequence",
                table: "MatchCoachAssignments",
                columns: new[] { "AcademyId", "MatchId", "Sequence" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_match_coach_history_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Match coach history is append-only' USING ERRCODE='23514'; END IF;
                  PERFORM pg_advisory_xact_lock(hashtextextended('match:'||NEW."AcademyId"::text||':'||NEW."MatchId"::text,0));
                  IF NEW."Sequence"<>(SELECT COALESCE(MAX(a."Sequence"),0)+1 FROM "MatchCoachAssignments" a WHERE a."AcademyId"=NEW."AcademyId" AND a."MatchId"=NEW."MatchId")
                    THEN RAISE EXCEPTION 'Match coach sequence changed' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."CoachMembershipId" AND m."Role"='Coach')
                    OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."AssignedByMembershipId" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                    THEN RAISE EXCEPTION 'Match coach or reviewer identity invalid' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER match_coach_history_guard BEFORE INSERT OR UPDATE OR DELETE ON "MatchCoachAssignments" FOR EACH ROW EXECUTE FUNCTION academy_match_coach_history_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "MatchCoachAssignments") THEN RAISE EXCEPTION 'Preserve match coach history before rollback' USING ERRCODE='23514'; END IF; END $$; DROP TRIGGER match_coach_history_guard ON "MatchCoachAssignments"; DROP FUNCTION academy_match_coach_history_guard();""");
            migrationBuilder.DropTable(
                name: "MatchCoachAssignments");
        }
    }
}
