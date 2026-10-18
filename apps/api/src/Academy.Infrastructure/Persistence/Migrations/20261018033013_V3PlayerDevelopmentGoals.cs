using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlayerDevelopmentGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerDevelopmentGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Target = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReviewOn = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDevelopmentGoals", x => x.Id);
                    table.UniqueConstraint("AK_PlayerDevelopmentGoals_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PlayerDevelopmentGoals_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_PlayerGoal_State", "\"IsActive\" AND \"Revision\" > 0 AND length(trim(\"Title\")) > 0 AND length(trim(\"Target\")) > 0 AND \"State\" IN ('Draft','Published','Achieved','Withdrawn') AND (\"State\" NOT IN ('Published','Achieved') OR \"PublishedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PlayerDevelopmentGoals_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerDevelopmentGoals_SportEnrollments_AcademyId_SportEnro~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerDevelopmentGoals_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerGoalChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGoalChanges", x => x.Id);
                    table.UniqueConstraint("AK_PlayerGoalChanges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerGoalChange_Action", "\"IsActive\" AND \"Revision\" > 0 AND \"Action\" IN ('Created','Edited','Published','Reviewed','Achieved','Withdrawn')");
                    table.ForeignKey(
                        name: "FK_PlayerGoalChanges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerGoalChanges_PlayerDevelopmentGoals_AcademyId_GoalId_S~",
                        columns: x => new { x.AcademyId, x.GoalId, x.SportId },
                        principalTable: "PlayerDevelopmentGoals",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerGoalChanges_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDevelopmentGoals_AcademyId_SportEnrollmentId_SportId",
                table: "PlayerDevelopmentGoals",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDevelopmentGoals_AcademyId_SportEnrollmentId_State",
                table: "PlayerDevelopmentGoals",
                columns: new[] { "AcademyId", "SportEnrollmentId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDevelopmentGoals_CreatedByUserId",
                table: "PlayerDevelopmentGoals",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGoalChanges_AcademyId_ActorUserId_RequestId",
                table: "PlayerGoalChanges",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGoalChanges_AcademyId_GoalId_Revision",
                table: "PlayerGoalChanges",
                columns: new[] { "AcademyId", "GoalId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGoalChanges_AcademyId_GoalId_SportId",
                table: "PlayerGoalChanges",
                columns: new[] { "AcademyId", "GoalId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGoalChanges_ActorUserId",
                table: "PlayerGoalChanges",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_player_goal_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP='DELETE' OR TG_TABLE_NAME='PlayerGoalChanges' AND TG_OP='UPDATE' THEN
                    RAISE EXCEPTION 'Preserve technical goal history' USING ERRCODE='23514'; END IF;
                  IF TG_TABLE_NAME='PlayerDevelopmentGoals' AND TG_OP='UPDATE' THEN
                    IF NEW."Revision"<>OLD."Revision"+1 OR OLD."State" IN ('Achieved','Withdrawn') OR
                      (to_jsonb(OLD)-ARRAY['Title','Target','ReviewOn','State','Revision','PublishedAtUtc','UpdatedAtUtc']) IS DISTINCT FROM
                      (to_jsonb(NEW)-ARRAY['Title','Target','ReviewOn','State','Revision','PublishedAtUtc','UpdatedAtUtc']) OR
                      OLD."State"='Published' AND (
                        NEW."State" NOT IN ('Published','Achieved','Withdrawn') OR
                        (to_jsonb(OLD)-ARRAY['State','Revision','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['State','Revision','UpdatedAtUtc'])) OR
                      OLD."State"='Draft' AND NEW."State" NOT IN ('Draft','Published','Withdrawn') THEN
                      RAISE EXCEPTION 'Published goal and identity are immutable' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER player_goal_guard BEFORE UPDATE OR DELETE ON "PlayerDevelopmentGoals" FOR EACH ROW EXECUTE FUNCTION academy_player_goal_guard();
                CREATE TRIGGER player_goal_change_guard BEFORE UPDATE OR DELETE ON "PlayerGoalChanges" FOR EACH ROW EXECUTE FUNCTION academy_player_goal_guard();
                CREATE FUNCTION academy_player_goal_history_required() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS(SELECT 1 FROM "PlayerGoalChanges" c WHERE c."AcademyId"=NEW."AcademyId" AND c."GoalId"=NEW."Id" AND c."Revision"=NEW."Revision") THEN
                    RAISE EXCEPTION 'Goal update requires an atomic review record' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER player_goal_history_required AFTER INSERT OR UPDATE ON "PlayerDevelopmentGoals"
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_player_goal_history_required();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PlayerDevelopmentGoals") THEN RAISE EXCEPTION 'Preserve player goals before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_player_goal_guard() CASCADE; DROP FUNCTION IF EXISTS academy_player_goal_history_required() CASCADE;""");
            migrationBuilder.DropTable(
                name: "PlayerGoalChanges");

            migrationBuilder.DropTable(
                name: "PlayerDevelopmentGoals");
        }
    }
}
