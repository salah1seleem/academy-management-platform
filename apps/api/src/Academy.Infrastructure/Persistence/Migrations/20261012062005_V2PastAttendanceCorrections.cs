using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2PastAttendanceCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TrainingSessionVersion = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrections", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceCorrections_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_AcademyMemberships_AcademyId_DecidedB~",
                        columns: x => new { x.AcademyId, x.DecidedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_AcademyMemberships_AcademyId_Requeste~",
                        columns: x => new { x.AcademyId, x.RequestedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_TrainingSessions_AcademyId_TrainingSe~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AcademyId_DecidedByMembershipId",
                table: "AttendanceCorrections",
                columns: new[] { "AcademyId", "DecidedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AcademyId_RequestedByMembershipId",
                table: "AttendanceCorrections",
                columns: new[] { "AcademyId", "RequestedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AcademyId_RequestedByUserId_RequestKey",
                table: "AttendanceCorrections",
                columns: new[] { "AcademyId", "RequestedByUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AcademyId_State_CreatedAtUtc",
                table: "AttendanceCorrections",
                columns: new[] { "AcademyId", "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AcademyId_TrainingSessionId_TrainingG~",
                table: "AttendanceCorrections",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_DecidedByUserId",
                table: "AttendanceCorrections",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_RequestedByUserId",
                table: "AttendanceCorrections",
                column: "RequestedByUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_attendance_correction_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Attendance correction history is immutable' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' THEN
                    IF NEW."State"<>'Pending' OR NEW."DecidedAtUtc" IS NOT NULL OR NEW."DecidedByUserId" IS NOT NULL OR NEW."DecidedByMembershipId" IS NOT NULL OR NEW."AppliedAtUtc" IS NOT NULL OR NEW."DecisionReason" IS NOT NULL OR NOT NEW."IsActive"
                      OR NEW."Kind" NOT IN ('Players','Staff') OR length(trim(NEW."Reason"))=0 OR jsonb_typeof(NEW."Payload")<>'array' OR jsonb_array_length(NEW."Payload") NOT BETWEEN 1 AND 100
                      OR NEW."ExpiresAtUtc"<=NEW."CreatedAtUtc" OR NEW."ExpiresAtUtc">NEW."CreatedAtUtc"+interval '7 days'
                      OR NOT EXISTS (SELECT 1 FROM "TrainingSessions" s WHERE s."AcademyId"=NEW."AcademyId" AND s."Id"=NEW."TrainingSessionId" AND s."TrainingGroupId"=NEW."TrainingGroupId" AND s."BranchId"=NEW."BranchId" AND s."SessionDate"=NEW."SessionDate" AND s.xmin::text::bigint=NEW."TrainingSessionVersion")
                      OR NOT EXISTS (SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."RequestedByMembershipId" AND m."UserId"=NEW."RequestedByUserId" AND m."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin'))
                      THEN RAISE EXCEPTION 'Invalid correction source' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF (OLD."AcademyId",OLD."TrainingSessionId",OLD."TrainingGroupId",OLD."BranchId",OLD."SessionDate",OLD."TrainingSessionVersion",OLD."Kind",OLD."Payload",OLD."Reason",OLD."RequestedByUserId",OLD."RequestedByMembershipId",OLD."RequestKey",OLD."RequestHash",OLD."ExpiresAtUtc",OLD."IsActive",OLD."CreatedAtUtc") IS DISTINCT FROM (NEW."AcademyId",NEW."TrainingSessionId",NEW."TrainingGroupId",NEW."BranchId",NEW."SessionDate",NEW."TrainingSessionVersion",NEW."Kind",NEW."Payload",NEW."Reason",NEW."RequestedByUserId",NEW."RequestedByMembershipId",NEW."RequestKey",NEW."RequestHash",NEW."ExpiresAtUtc",NEW."IsActive",NEW."CreatedAtUtc") THEN RAISE EXCEPTION 'Correction source is immutable' USING ERRCODE='23514'; END IF;
                    IF OLD."State"='Pending' AND NEW."State" IN ('Applying','Rejected') AND NEW."AppliedAtUtc" IS NULL AND NEW."DecidedAtUtc" IS NOT NULL AND NEW."DecidedAtUtc"<NEW."ExpiresAtUtc" AND EXISTS (SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."DecidedByMembershipId" AND m."UserId"=NEW."DecidedByUserId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive") THEN RETURN NEW; END IF;
                    IF OLD."State"='Applying' AND NEW."State"='Approved' AND NEW."AppliedAtUtc" IS NOT NULL AND (OLD."DecidedByUserId",OLD."DecidedByMembershipId",OLD."DecidedAtUtc",OLD."DecisionReason") IS NOT DISTINCT FROM (NEW."DecidedByUserId",NEW."DecidedByMembershipId",NEW."DecidedAtUtc",NEW."DecisionReason") THEN RETURN NEW; END IF;
                    RAISE EXCEPTION 'Correction decision is terminal or invalid' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER attendance_correction_guard BEFORE INSERT OR UPDATE OR DELETE ON "AttendanceCorrections" FOR EACH ROW EXECUTE FUNCTION academy_attendance_correction_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM "AttendanceCorrections") THEN RAISE EXCEPTION 'Rollback cannot erase correction history; restore a backup to a separate database instead'; END IF; END $$;
                DROP TRIGGER attendance_correction_guard ON "AttendanceCorrections"; DROP FUNCTION academy_attendance_correction_guard();
                """);
            migrationBuilder.DropTable(
                name: "AttendanceCorrections");
        }
    }
}
