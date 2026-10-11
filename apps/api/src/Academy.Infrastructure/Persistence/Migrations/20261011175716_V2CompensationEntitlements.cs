using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CompensationEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompensationGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cause = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_CompensationGrants", x => x.Id);
                    table.UniqueConstraint("AK_CompensationGrants_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_CompensationGrants_AcademyId_Id_SportEnrollmentId_BranchId_~", x => new { x.AcademyId, x.Id, x.SportEnrollmentId, x.BranchId, x.SportId });
                    table.CheckConstraint("CK_Compensation_Units", "\"Units\" > 0 AND ((\"Unit\"='Session' AND \"Units\"=1) OR (\"Unit\"='Minute' AND \"Units\"<=1440)) AND \"ValidFrom\"<=\"ValidTo\"");
                    table.ForeignKey(
                        name: "FK_CompensationGrants_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationGrants_SportEnrollments_AcademyId_SportEnrollme~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.BranchId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationGrants_SubscriptionPeriods_AcademyId_SourcePeri~",
                        columns: x => new { x.AcademyId, x.SourcePeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationGrants_TrainingSessions_AcademyId_SourceSession~",
                        columns: x => new { x.AcademyId, x.SourceSessionId, x.SourceGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationGrants_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompensationBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_CompensationBookings", x => x.Id);
                    table.UniqueConstraint("AK_CompensationBookings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_CompensationBookings_AcademyId_Id_GrantId_BranchId", x => new { x.AcademyId, x.Id, x.GrantId, x.BranchId });
                    table.CheckConstraint("CK_CompensationBooking_Units", "\"Units\" > 0 AND \"Units\"<=1440");
                    table.ForeignKey(
                        name: "FK_CompensationBookings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationBookings_CompensationGrants_AcademyId_GrantId_S~",
                        columns: x => new { x.AcademyId, x.GrantId, x.SportEnrollmentId, x.BranchId, x.SportId },
                        principalTable: "CompensationGrants",
                        principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationBookings_TrainingSessions_AcademyId_TrainingSes~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationBookings_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationBookings_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompensationMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerAttendanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversesMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationMovements", x => x.Id);
                    table.UniqueConstraint("AK_CompensationMovements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CompensationMovement_Sign", "(\"ReversesMovementId\" IS NULL AND \"Units\">0) OR (\"ReversesMovementId\" IS NOT NULL AND \"Units\"<0)");
                    table.ForeignKey(
                        name: "FK_CompensationMovements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationMovements_CompensationBookings_AcademyId_Bookin~",
                        columns: x => new { x.AcademyId, x.BookingId, x.GrantId, x.BranchId },
                        principalTable: "CompensationBookings",
                        principalColumns: new[] { "AcademyId", "Id", "GrantId", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationMovements_CompensationMovements_AcademyId_Rever~",
                        columns: x => new { x.AcademyId, x.ReversesMovementId },
                        principalTable: "CompensationMovements",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationMovements_PlayerAttendances_AcademyId_PlayerAtt~",
                        columns: x => new { x.AcademyId, x.PlayerAttendanceId },
                        principalTable: "PlayerAttendances",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationMovements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationBookings_AcademyId_ActorUserId_RequestKey",
                table: "CompensationBookings",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationBookings_AcademyId_GrantId_SportEnrollmentId_Br~",
                table: "CompensationBookings",
                columns: new[] { "AcademyId", "GrantId", "SportEnrollmentId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationBookings_AcademyId_SportEnrollmentId_TrainingSe~",
                table: "CompensationBookings",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingSessionId" },
                unique: true,
                filter: "\"State\"='Reserved'");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationBookings_AcademyId_TrainingSessionId_TrainingGr~",
                table: "CompensationBookings",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationBookings_ActorUserId",
                table: "CompensationBookings",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationBookings_CancelledByUserId",
                table: "CompensationBookings",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationGrants_AcademyId_ActorUserId_RequestKey",
                table: "CompensationGrants",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationGrants_AcademyId_SourcePeriodId",
                table: "CompensationGrants",
                columns: new[] { "AcademyId", "SourcePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationGrants_AcademyId_SourceSessionId_SourceGroupId_~",
                table: "CompensationGrants",
                columns: new[] { "AcademyId", "SourceSessionId", "SourceGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationGrants_AcademyId_SportEnrollmentId_BranchId_Spo~",
                table: "CompensationGrants",
                columns: new[] { "AcademyId", "SportEnrollmentId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationGrants_AcademyId_SportEnrollmentId_SourceSessio~",
                table: "CompensationGrants",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SourceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationGrants_ActorUserId",
                table: "CompensationGrants",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationMovements_AcademyId_BookingId_GrantId_BranchId",
                table: "CompensationMovements",
                columns: new[] { "AcademyId", "BookingId", "GrantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationMovements_AcademyId_PlayerAttendanceId",
                table: "CompensationMovements",
                columns: new[] { "AcademyId", "PlayerAttendanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationMovements_AcademyId_ReversesMovementId",
                table: "CompensationMovements",
                columns: new[] { "AcademyId", "ReversesMovementId" },
                unique: true,
                filter: "\"ReversesMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationMovements_ActorUserId",
                table: "CompensationMovements",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academy_session_assigned(academy uuid, enrollment uuid, session uuid)
                RETURNS boolean LANGUAGE sql STABLE AS $$
                  SELECT EXISTS (SELECT 1 FROM "SportEnrollments" e JOIN "TrainingSessions" s
                    ON s."AcademyId"=e."AcademyId" AND s."BranchId"=e."BranchId" AND s."SportId"=e."SportId"
                    WHERE e."AcademyId"=academy AND e."Id"=enrollment AND s."Id"=session AND (
                      EXISTS (SELECT 1 FROM "CompensationBookings" b JOIN "CompensationGrants" g ON g."AcademyId"=b."AcademyId" AND g."Id"=b."GrantId"
                        WHERE b."AcademyId"=academy AND b."SportEnrollmentId"=enrollment AND b."TrainingSessionId"=session AND b."State"='Reserved' AND s."SessionDate" BETWEEN g."ValidFrom" AND g."ValidTo")
                      OR EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId"=academy AND m."SportEnrollmentId"=enrollment AND m."TargetSessionId"=session)
                      OR (NOT EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId"=academy AND m."SportEnrollmentId"=enrollment AND m."SourceSessionId"=session)
                        AND s."TrainingGroupId"=academy_assigned_group_at(academy,enrollment,s."SessionDate"))))
                $$;
                CREATE TRIGGER compensation_grant_immutable BEFORE UPDATE OR DELETE ON "CompensationGrants" FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE TRIGGER compensation_movement_immutable BEFORE UPDATE OR DELETE ON "CompensationMovements" FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_compensation_booking_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE g "CompensationGrants"; s "TrainingSessions";
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Cancel a booking without deleting its history' USING ERRCODE='23514'; END IF;
                  SELECT * INTO g FROM "CompensationGrants" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."GrantId" FOR UPDATE;
                  SELECT * INTO s FROM "TrainingSessions" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."TrainingSessionId";
                  IF g."Id" IS NULL OR s."Id" IS NULL OR s."SessionDate" NOT BETWEEN g."ValidFrom" AND g."ValidTo" OR s."Id"=g."SourceSessionId"
                    OR (g."Unit"='Session' AND NEW."Units"<>1) OR (g."Unit"='Minute' AND NEW."Units"<>EXTRACT(EPOCH FROM (s."EndTime"-s."StartTime"))/60)
                  THEN RAISE EXCEPTION 'Invalid compensation booking' USING ERRCODE='23514'; END IF;
                  IF TG_OP='UPDATE' AND ((to_jsonb(OLD)-ARRAY['State','CancelledByUserId','CancelledAtUtc','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['State','CancelledByUserId','CancelledAtUtc','UpdatedAtUtc'])
                    OR OLD."State"<>'Reserved' OR NEW."State"<>'Cancelled' OR NEW."CancelledByUserId" IS NULL OR NEW."CancelledAtUtc" IS NULL)
                  THEN RAISE EXCEPTION 'Booking identity is immutable' USING ERRCODE='23514'; END IF;
                  IF NEW."State"='Reserved' AND (TG_OP<>'INSERT' OR s."Status"<>'Scheduled' OR NEW."Units"+(SELECT COALESCE(SUM("Units"),0) FROM "CompensationBookings" WHERE "AcademyId"=g."AcademyId" AND "GrantId"=g."Id" AND "State"='Reserved')>g."Units")
                  THEN RAISE EXCEPTION 'Compensation allocation exceeds its grant' USING ERRCODE='23514'; END IF;
                  IF NEW."State"='Cancelled' AND ((SELECT COALESCE(SUM("Units"),0) FROM "CompensationMovements" WHERE "AcademyId"=NEW."AcademyId" AND "BookingId"=NEW."Id")>0
                    OR EXISTS (SELECT 1 FROM "PlayerAttendances" a WHERE a."AcademyId"=NEW."AcademyId" AND a."SportEnrollmentId"=NEW."SportEnrollmentId" AND a."TrainingSessionId"=NEW."TrainingSessionId" AND a."Status"='Present'))
                  THEN RAISE EXCEPTION 'Correct attendance before cancelling consumed compensation' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER compensation_booking_guard BEFORE INSERT OR UPDATE OR DELETE ON "CompensationBookings" FOR EACH ROW EXECUTE FUNCTION academy_compensation_booking_guard();
                CREATE FUNCTION academy_compensation_movement_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE b "CompensationBookings"; original "CompensationMovements";
                BEGIN
                  SELECT * INTO b FROM "CompensationBookings" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."BookingId" FOR UPDATE;
                  IF b."Id" IS NULL OR NOT EXISTS (SELECT 1 FROM "PlayerAttendances" a WHERE a."AcademyId"=b."AcademyId" AND a."Id"=NEW."PlayerAttendanceId" AND a."SportEnrollmentId"=b."SportEnrollmentId" AND a."TrainingSessionId"=b."TrainingSessionId")
                  THEN RAISE EXCEPTION 'Attendance is not linked to this booking' USING ERRCODE='23514'; END IF;
                  IF NEW."ReversesMovementId" IS NULL THEN
                    IF b."State"<>'Reserved' OR NEW."Units"<>b."Units" OR (SELECT COALESCE(SUM("Units"),0) FROM "CompensationMovements" WHERE "AcademyId"=b."AcademyId" AND "BookingId"=b."Id")<>0
                    THEN RAISE EXCEPTION 'Compensation is already consumed or invalid' USING ERRCODE='23514'; END IF;
                  ELSE
                    SELECT * INTO original FROM "CompensationMovements" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."ReversesMovementId";
                    IF original."Id" IS NULL OR original."ReversesMovementId" IS NOT NULL OR original."BookingId"<>NEW."BookingId" OR original."PlayerAttendanceId"<>NEW."PlayerAttendanceId" OR NEW."Units"<>-original."Units"
                    THEN RAISE EXCEPTION 'Invalid compensation reversal' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER compensation_movement_guard BEFORE INSERT ON "CompensationMovements" FOR EACH ROW EXECUTE FUNCTION academy_compensation_movement_guard();
                CREATE FUNCTION academy_compensation_session_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF ((OLD."SessionDate",OLD."StartTime",OLD."EndTime",OLD."TrainingGroupId") IS DISTINCT FROM (NEW."SessionDate",NEW."StartTime",NEW."EndTime",NEW."TrainingGroupId")
                    AND EXISTS (SELECT 1 FROM "CompensationGrants" g WHERE g."AcademyId"=OLD."AcademyId" AND g."SourceSessionId"=OLD."Id"))
                    OR (((OLD."SessionDate",OLD."StartTime",OLD."EndTime",OLD."TrainingGroupId") IS DISTINCT FROM (NEW."SessionDate",NEW."StartTime",NEW."EndTime",NEW."TrainingGroupId")
                      OR (OLD."Status" IS DISTINCT FROM NEW."Status" AND NOT (OLD."Status"='Scheduled' AND NEW."Status"='Held')))
                      AND EXISTS (SELECT 1 FROM "CompensationBookings" b WHERE b."AcademyId"=OLD."AcademyId" AND b."TrainingSessionId"=OLD."Id" AND b."State"='Reserved'))
                  THEN RAISE EXCEPTION 'Reviewed compensation session is protected' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER compensation_session_guard BEFORE UPDATE ON "TrainingSessions" FOR EACH ROW EXECUTE FUNCTION academy_compensation_session_guard();
                CREATE OR REPLACE FUNCTION academy_group_anchor_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."TrainingGroupId" IS DISTINCT FROM NEW."TrainingGroupId" OR OLD."GroupAssignedFrom" IS DISTINCT FROM NEW."GroupAssignedFrom")
                    AND (EXISTS (SELECT 1 FROM "EnrollmentGroupTransfers" t WHERE t."AcademyId"=OLD."AcademyId" AND t."SportEnrollmentId"=OLD."Id")
                      OR EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId"=OLD."AcademyId" AND m."SportEnrollmentId"=OLD."Id")
                      OR EXISTS (SELECT 1 FROM "CompensationGrants" g WHERE g."AcademyId"=OLD."AcademyId" AND g."SportEnrollmentId"=OLD."Id"))
                  THEN RAISE EXCEPTION 'Dated assignment anchor is immutable'; END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER compensation_session_guard ON "TrainingSessions";
                DROP FUNCTION academy_compensation_session_guard();
                DROP TRIGGER compensation_movement_guard ON "CompensationMovements";
                DROP FUNCTION academy_compensation_movement_guard();
                DROP TRIGGER compensation_booking_guard ON "CompensationBookings";
                DROP FUNCTION academy_compensation_booking_guard();
                CREATE OR REPLACE FUNCTION academy_session_assigned(academy uuid, enrollment uuid, session uuid)
                RETURNS boolean LANGUAGE sql STABLE AS $$
                  SELECT EXISTS (SELECT 1 FROM "SportEnrollments" e JOIN "TrainingSessions" s ON s."AcademyId"=e."AcademyId" AND s."BranchId"=e."BranchId" AND s."SportId"=e."SportId"
                    WHERE e."AcademyId"=academy AND e."Id"=enrollment AND s."Id"=session AND (
                      EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId"=academy AND m."SportEnrollmentId"=enrollment AND m."TargetSessionId"=session)
                      OR (NOT EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId"=academy AND m."SportEnrollmentId"=enrollment AND m."SourceSessionId"=session) AND s."TrainingGroupId"=academy_assigned_group_at(academy,enrollment,s."SessionDate"))))
                $$;
                CREATE OR REPLACE FUNCTION academy_group_anchor_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."TrainingGroupId" IS DISTINCT FROM NEW."TrainingGroupId" OR OLD."GroupAssignedFrom" IS DISTINCT FROM NEW."GroupAssignedFrom")
                    AND (EXISTS (SELECT 1 FROM "EnrollmentGroupTransfers" t WHERE t."AcademyId"=OLD."AcademyId" AND t."SportEnrollmentId"=OLD."Id") OR EXISTS (SELECT 1 FROM "EnrollmentSessionMoves" m WHERE m."AcademyId"=OLD."AcademyId" AND m."SportEnrollmentId"=OLD."Id"))
                  THEN RAISE EXCEPTION 'Dated assignment anchor is immutable'; END IF;
                  RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "CompensationMovements");

            migrationBuilder.DropTable(
                name: "CompensationBookings");

            migrationBuilder.DropTable(
                name: "CompensationGrants");
        }
    }
}
