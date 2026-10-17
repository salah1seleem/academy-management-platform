using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3OwnerOperationalFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerFollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    Condition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolutionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerFollowUps", x => x.Id);
                    table.UniqueConstraint("AK_OwnerFollowUps_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_OwnerFollowUp_State", "\"IsActive\" AND \"Condition\" IN ('GroupCoachMissing','GroupLowOccupancy') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_OwnerFollowUps_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUps_AcademyMemberships_AcademyId_AssignedToMembe~",
                        columns: x => new { x.AcademyId, x.AssignedToMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUps_AcademyMemberships_AcademyId_CreatedByMember~",
                        columns: x => new { x.AcademyId, x.CreatedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUps_TrainingGroups_AcademyId_TrainingGroupId",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerFollowUpChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FollowUpId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerFollowUpChanges", x => x.Id);
                    table.UniqueConstraint("AK_OwnerFollowUpChanges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_OwnerFollowUpChanges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUpChanges_AcademyMemberships_AcademyId_ActorMemb~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUpChanges_OwnerFollowUps_AcademyId_FollowUpId",
                        columns: x => new { x.AcademyId, x.FollowUpId },
                        principalTable: "OwnerFollowUps",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUpChanges_AcademyId_ActorMembershipId_RequestId",
                table: "OwnerFollowUpChanges",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUpChanges_AcademyId_FollowUpId",
                table: "OwnerFollowUpChanges",
                columns: new[] { "AcademyId", "FollowUpId" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUps_AcademyId_AssignedToMembershipId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "AssignedToMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUps_AcademyId_CreatedByMembershipId_RequestId",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "CreatedByMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUps_AcademyId_TrainingGroupId_Condition",
                table: "OwnerFollowUps",
                columns: new[] { "AcademyId", "TrainingGroupId", "Condition" },
                unique: true,
                filter: "\"State\" NOT IN ('Closed','Rejected')");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_owner_followup_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP='DELETE' OR TG_TABLE_NAME='OwnerFollowUpChanges' AND TG_OP='UPDATE' THEN RAISE EXCEPTION 'Preserve follow-up evidence' USING ERRCODE='23514'; END IF;
                  IF TG_TABLE_NAME='OwnerFollowUps' AND TG_OP='UPDATE' AND
                    (to_jsonb(OLD)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) IS DISTINCT FROM
                    (to_jsonb(NEW)-ARRAY['State','AssignedToMembershipId','DueOn','Note','ResolutionReason','UpdatedAtUtc']) THEN
                    RAISE EXCEPTION 'Follow-up source is immutable' USING ERRCODE='23514'; END IF;
                  IF TG_OP='INSERT' THEN
                    IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=CASE WHEN TG_TABLE_NAME='OwnerFollowUps' THEN (to_jsonb(NEW)->>'CreatedByMembershipId')::uuid ELSE (to_jsonb(NEW)->>'ActorMembershipId')::uuid END AND m."Role"='AcademyOwner') THEN
                    RAISE EXCEPTION 'Follow-up actor must be an academy owner' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER owner_followup_guard BEFORE INSERT OR UPDATE OR DELETE ON "OwnerFollowUps" FOR EACH ROW EXECUTE FUNCTION academy_owner_followup_guard();
                CREATE TRIGGER owner_followup_change_guard BEFORE INSERT OR UPDATE OR DELETE ON "OwnerFollowUpChanges" FOR EACH ROW EXECUTE FUNCTION academy_owner_followup_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "OwnerFollowUps") OR EXISTS(SELECT 1 FROM "OwnerFollowUpChanges") THEN RAISE EXCEPTION 'Preserve follow-ups before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION academy_owner_followup_guard() CASCADE;""");
            migrationBuilder.DropTable(
                name: "OwnerFollowUpChanges");

            migrationBuilder.DropTable(
                name: "OwnerFollowUps");
        }
    }
}
