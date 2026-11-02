using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2SelectedSessionTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_TrainingSessions_AcademyId_Id_TrainingGroupId_BranchId_Spor~",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "Id", "TrainingGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateTable(
                name: "EnrollmentSessionMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentSessionMoves", x => x.Id);
                    table.UniqueConstraint("AK_EnrollmentSessionMoves_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SessionMove_DifferentSessions", "\"SourceSessionId\" <> \"TargetSessionId\"");
                    table.ForeignKey(
                        name: "FK_EnrollmentSessionMoves_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentSessionMoves_SportEnrollments_AcademyId_SportEnro~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.BranchId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentSessionMoves_TrainingSessions_AcademyId_SourceSes~",
                        columns: x => new { x.AcademyId, x.SourceSessionId, x.SourceGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentSessionMoves_TrainingSessions_AcademyId_TargetSes~",
                        columns: x => new { x.AcademyId, x.TargetSessionId, x.TargetGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentSessionMoves_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_ActorUserId_RequestKey",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_SourceSessionId_SourceGrou~",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "SourceSessionId", "SourceGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_SportEnrollmentId_BranchId~",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "SportEnrollmentId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_SportEnrollmentId_SourceSe~",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SourceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_SportEnrollmentId_TargetSe~",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TargetSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_TargetGroupId",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "TargetGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_AcademyId_TargetSessionId_TargetGrou~",
                table: "EnrollmentSessionMoves",
                columns: new[] { "AcademyId", "TargetSessionId", "TargetGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentSessionMoves_ActorUserId",
                table: "EnrollmentSessionMoves",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_session_assigned(academy uuid, enrollment uuid, session uuid)
                RETURNS boolean LANGUAGE sql STABLE AS $$
                  SELECT EXISTS (
                    SELECT 1 FROM "SportEnrollments" e JOIN "TrainingSessions" s
                      ON s."AcademyId" = e."AcademyId" AND s."BranchId" = e."BranchId" AND s."SportId" = e."SportId"
                    WHERE e."AcademyId" = academy AND e."Id" = enrollment AND s."Id" = session
                    AND (EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = academy AND m."SportEnrollmentId" = enrollment AND m."TargetSessionId" = session)
                      OR (NOT EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = academy AND m."SportEnrollmentId" = enrollment AND m."SourceSessionId" = session)
                        AND s."TrainingGroupId" = academy_assigned_group_at(academy, enrollment, s."SessionDate")))
                  )
                $$;
                CREATE TRIGGER enrollment_session_move_immutable BEFORE UPDATE OR DELETE ON "EnrollmentSessionMoves"
                  FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_session_move_unique() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM 1 FROM "SportEnrollments" WHERE "AcademyId" = NEW."AcademyId" AND "Id" = NEW."SportEnrollmentId" FOR UPDATE;
                  IF EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = NEW."AcademyId" AND m."SportEnrollmentId" = NEW."SportEnrollmentId"
                    AND (m."SourceSessionId" IN (NEW."SourceSessionId",NEW."TargetSessionId") OR m."TargetSessionId" IN (NEW."SourceSessionId",NEW."TargetSessionId")))
                  THEN RAISE EXCEPTION 'Session already has a reviewed replacement' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER enrollment_session_move_unique BEFORE INSERT ON "EnrollmentSessionMoves" FOR EACH ROW EXECUTE FUNCTION academy_session_move_unique();
                CREATE FUNCTION academy_moved_session_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."SessionDate",OLD."StartTime",OLD."EndTime",OLD."Status",OLD."TrainingGroupId") IS DISTINCT FROM (NEW."SessionDate",NEW."StartTime",NEW."EndTime",NEW."Status",NEW."TrainingGroupId")
                    AND EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = OLD."AcademyId" AND (m."SourceSessionId" = OLD."Id" OR m."TargetSessionId" = OLD."Id"))
                  THEN RAISE EXCEPTION 'Reviewed session replacement is immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER moved_session_immutable BEFORE UPDATE ON "TrainingSessions" FOR EACH ROW EXECUTE FUNCTION academy_moved_session_immutable();
                CREATE OR REPLACE FUNCTION academy_group_anchor_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."TrainingGroupId" IS DISTINCT FROM NEW."TrainingGroupId" OR OLD."GroupAssignedFrom" IS DISTINCT FROM NEW."GroupAssignedFrom")
                    AND (EXISTS (SELECT 1 FROM "EnrollmentGroupTransfers" t WHERE t."AcademyId" = OLD."AcademyId" AND t."SportEnrollmentId" = OLD."Id")
                      OR EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId" = OLD."AcademyId" AND m."SportEnrollmentId" = OLD."Id"))
                  THEN RAISE EXCEPTION 'Dated assignment anchor is immutable'; END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER moved_session_immutable ON "TrainingSessions";
                DROP FUNCTION academy_moved_session_immutable();
                DROP TRIGGER enrollment_session_move_unique ON "EnrollmentSessionMoves";
                DROP FUNCTION academy_session_move_unique();
                DROP FUNCTION academy_session_assigned(uuid,uuid,uuid);
                CREATE OR REPLACE FUNCTION academy_group_anchor_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."TrainingGroupId" IS DISTINCT FROM NEW."TrainingGroupId" OR OLD."GroupAssignedFrom" IS DISTINCT FROM NEW."GroupAssignedFrom")
                    AND EXISTS (SELECT 1 FROM "EnrollmentGroupTransfers" t WHERE t."AcademyId" = OLD."AcademyId" AND t."SportEnrollmentId" = OLD."Id")
                  THEN RAISE EXCEPTION 'Dated assignment anchor is immutable'; END IF;
                  RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "EnrollmentSessionMoves");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TrainingSessions_AcademyId_Id_TrainingGroupId_BranchId_Spor~",
                table: "TrainingSessions");
        }
    }
}
