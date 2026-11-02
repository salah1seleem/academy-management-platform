using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ResourceLinkedTimetables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupResourceSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedSeats = table.Column<int>(type: "integer", nullable: false),
                    Exclusive = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupResourceSchedules", x => x.Id);
                    table.UniqueConstraint("AK_GroupResourceSchedules_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_GroupResourceSchedule_Range", "\"ReservedSeats\" BETWEEN 1 AND 10000 AND \"ValidFrom\" <= \"ValidTo\"");
                    table.ForeignKey(
                        name: "FK_GroupResourceSchedules_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupResourceSchedules_AcademyMemberships_AcademyId_CoachMe~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupResourceSchedules_FacilityResources_AcademyId_Resource~",
                        columns: x => new { x.AcademyId, x.ResourceId },
                        principalTable: "FacilityResources",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupResourceSchedules_TrainingGroups_AcademyId_TrainingGro~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingScheduleRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedSessions = table.Column<int>(type: "integer", nullable: false),
                    PreservedSessions = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingScheduleRevisions", x => x.Id);
                    table.UniqueConstraint("AK_TrainingScheduleRevisions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_TrainingScheduleRevisions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingScheduleRevisions_TrainingGroups_AcademyId_Training~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingScheduleRevisions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupResourceSchedules_AcademyId_CoachMembershipId",
                table: "GroupResourceSchedules",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupResourceSchedules_AcademyId_ResourceId",
                table: "GroupResourceSchedules",
                columns: new[] { "AcademyId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupResourceSchedules_AcademyId_TrainingGroupId",
                table: "GroupResourceSchedules",
                columns: new[] { "AcademyId", "TrainingGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingScheduleRevisions_AcademyId_ActorUserId_RequestKey",
                table: "TrainingScheduleRevisions",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingScheduleRevisions_AcademyId_TrainingGroupId",
                table: "TrainingScheduleRevisions",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingScheduleRevisions_ActorUserId",
                table: "TrainingScheduleRevisions",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE TRIGGER training_schedule_revision_immutable BEFORE UPDATE OR DELETE ON "TrainingScheduleRevisions"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "TrainingScheduleRevisions") THEN
                        RAISE EXCEPTION 'restore a verified backup; rollback cannot discard schedule history' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "GroupResourceSchedules");

            migrationBuilder.DropTable(
                name: "TrainingScheduleRevisions");
        }
    }
}
