using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PlayerLevelReviewHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerLevelReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    FromLevelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLevelId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromLevelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ToLevelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Action = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    PrivateNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousRecordedReviewAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerLevelReviews", x => x.Id);
                    table.UniqueConstraint("AK_PlayerLevelReviews_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerLevelReview_Action", "\"IsActive\" AND \"Action\" IN ('Reviewed','Changed','Cleared') AND (\"ToLevelId\" IS NULL AND \"ToLevelName\" IS NULL OR \"ToLevelId\" IS NOT NULL AND length(trim(\"ToLevelName\"))>0) AND (\"FromLevelId\" IS NULL AND \"FromLevelName\" IS NULL OR \"FromLevelId\" IS NOT NULL AND length(trim(\"FromLevelName\"))>0)");
                    table.CheckConstraint("CK_PlayerLevelReview_Revision", "\"Revision\">0");
                    table.ForeignKey(
                        name: "FK_PlayerLevelReviews_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerLevelReviews_SportEnrollments_AcademyId_SportEnrollme~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerLevelReviews_TrainingLevels_AcademyId_FromLevelId_Spo~",
                        columns: x => new { x.AcademyId, x.FromLevelId, x.SportId },
                        principalTable: "TrainingLevels",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerLevelReviews_TrainingLevels_AcademyId_ToLevelId_Sport~",
                        columns: x => new { x.AcademyId, x.ToLevelId, x.SportId },
                        principalTable: "TrainingLevels",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerLevelReviews_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_ActorUserId_RequestId",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_FromLevelId_SportId",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "FromLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_SportEnrollmentId_Revision",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "SportEnrollmentId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_SportEnrollmentId_SportId",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_AcademyId_ToLevelId_SportId",
                table: "PlayerLevelReviews",
                columns: new[] { "AcademyId", "ToLevelId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLevelReviews_ActorUserId",
                table: "PlayerLevelReviews",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_level_review_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  RAISE EXCEPTION 'Preserve dated level reviews; append a correction' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER level_review_immutable BEFORE UPDATE OR DELETE ON "PlayerLevelReviews" FOR EACH ROW EXECUTE FUNCTION academy_level_review_immutable();
                CREATE FUNCTION academy_level_review_source() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE e "SportEnrollments"%ROWTYPE; f text; t text;
                BEGIN
                  SELECT * INTO e FROM "SportEnrollments" WHERE "Id"=NEW."SportEnrollmentId" AND "AcademyId"=NEW."AcademyId" FOR SHARE;
                  SELECT "ArabicName" INTO f FROM "TrainingLevels" WHERE "Id"=NEW."FromLevelId" AND "AcademyId"=NEW."AcademyId";
                  SELECT "ArabicName" INTO t FROM "TrainingLevels" WHERE "Id"=NEW."ToLevelId" AND "AcademyId"=NEW."AcademyId";
                  IF e."TrainingLevelId" IS DISTINCT FROM NEW."ToLevelId" OR e."UpdatedAtUtc" IS DISTINCT FROM NEW."CreatedAtUtc" OR
                    NEW."FromLevelName" IS DISTINCT FROM f OR NEW."ToLevelName" IS DISTINCT FROM t OR
                    NEW."ToLevelId" IS NOT NULL AND (e."LevelReviewedByUserId" IS DISTINCT FROM NEW."ActorUserId" OR e."LevelReviewedAtUtc" IS DISTINCT FROM NEW."CreatedAtUtc") OR
                    NEW."Action" IS DISTINCT FROM (CASE WHEN NEW."ToLevelId" IS NULL THEN 'Cleared' WHEN NEW."FromLevelId"=NEW."ToLevelId" THEN 'Reviewed' ELSE 'Changed' END) THEN
                    RAISE EXCEPTION 'Level review must describe its saved source' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER level_review_source AFTER INSERT ON "PlayerLevelReviews" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_level_review_source();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PlayerLevelReviews") THEN RAISE EXCEPTION 'Preserve level reviews before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION IF EXISTS academy_level_review_immutable() CASCADE; DROP FUNCTION IF EXISTS academy_level_review_source() CASCADE;""");
            migrationBuilder.DropTable(
                name: "PlayerLevelReviews");
        }
    }
}
