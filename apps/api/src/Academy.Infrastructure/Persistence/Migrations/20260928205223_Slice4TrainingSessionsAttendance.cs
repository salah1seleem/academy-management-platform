using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice4TrainingSessionsAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffGroupAssignments_AcademyId_AcademyMembershipId_Trainin~",
                table: "StaffGroupAssignments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_StaffGroupAssignments_AcademyId_AcademyMembershipId_Trainin~",
                table: "StaffGroupAssignments",
                columns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_TrainingGroupId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "Id", "TrainingGroupId" });

            migrationBuilder.CreateTable(
                name: "TrainingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RecurringScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingSessions", x => x.Id);
                    table.UniqueConstraint("AK_TrainingSessions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_TrainingSessions_AcademyId_Id_TrainingGroupId", x => new { x.AcademyId, x.Id, x.TrainingGroupId });
                    table.CheckConstraint("CK_TrainingSessions_Time", "\"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_AgeCategories_AcademyId_AgeCategoryId",
                        columns: x => new { x.AcademyId, x.AgeCategoryId },
                        principalTable: "AgeCategories",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_RecurringSchedules_AcademyId_RecurringSche~",
                        columns: x => new { x.AcademyId, x.RecurringScheduleId },
                        principalTable: "RecurringSchedules",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_TrainingGroups_AcademyId_TrainingGroupId_B~",
                        columns: x => new { x.AcademyId, x.TrainingGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumedSubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAttendances", x => x.Id);
                    table.UniqueConstraint("AK_PlayerAttendances_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlayerAttendances_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerAttendances_SportEnrollments_AcademyId_SportEnrollmen~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.TrainingGroupId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerAttendances_SubscriptionPeriods_AcademyId_ConsumedSub~",
                        columns: x => new { x.AcademyId, x.ConsumedSubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerAttendances_TrainingSessions_AcademyId_TrainingSessio~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendances", x => x.Id);
                    table.UniqueConstraint("AK_StaffAttendances_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StaffAttendances_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendances_AcademyMemberships_AcademyId_AcademyMember~",
                        columns: x => new { x.AcademyId, x.AcademyMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendances_StaffGroupAssignments_AcademyId_AcademyMem~",
                        columns: x => new { x.AcademyId, x.AcademyMembershipId, x.TrainingGroupId },
                        principalTable: "StaffGroupAssignments",
                        principalColumns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendances_TrainingSessions_AcademyId_TrainingSession~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSessionMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerAttendanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversesMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovementType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    BalanceBefore = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSessionMovements", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionSessionMovements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SubscriptionSessionMovements_Balance", "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0");
                    table.CheckConstraint("CK_SubscriptionSessionMovements_Quantity", "(\"MovementType\" = 'AttendanceConsume' AND \"Quantity\" = -1 AND \"BalanceAfter\" = \"BalanceBefore\" - 1) OR (\"MovementType\" = 'AttendanceRestore' AND \"Quantity\" = 1 AND \"BalanceAfter\" = \"BalanceBefore\" + 1)");
                    table.ForeignKey(
                        name: "FK_SubscriptionSessionMovements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionSessionMovements_PlayerAttendances_AcademyId_Pl~",
                        columns: x => new { x.AcademyId, x.PlayerAttendanceId },
                        principalTable: "PlayerAttendances",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionSessionMovements_SubscriptionPeriods_AcademyId_~",
                        columns: x => new { x.AcademyId, x.SubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionSessionMovements_SubscriptionSessionMovements_A~",
                        columns: x => new { x.AcademyId, x.ReversesMovementId },
                        principalTable: "SubscriptionSessionMovements",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionPeriods_RemainingSessions",
                table: "SubscriptionPeriods",
                sql: "\"RemainingSessions\" IS NULL OR \"RemainingSessions\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_ConsumedSubscriptionPeriodId",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "ConsumedSubscriptionPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_IsActive",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_SportEnrollmentId_TrainingGroup~",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_TrainingSessionId_SportEnrollme~",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "TrainingSessionId", "SportEnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_TrainingSessionId_TrainingGroup~",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_AcademyId_AcademyMembershipId_TrainingGrou~",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_AcademyId_IsActive",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_AcademyId_TrainingSessionId_AcademyMembers~",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "TrainingSessionId", "AcademyMembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_AcademyId_TrainingSessionId_TrainingGroupId",
                table: "StaffAttendances",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSessionMovements_AcademyId_IsActive",
                table: "SubscriptionSessionMovements",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSessionMovements_AcademyId_PlayerAttendanceId_O~",
                table: "SubscriptionSessionMovements",
                columns: new[] { "AcademyId", "PlayerAttendanceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSessionMovements_AcademyId_ReversesMovementId",
                table: "SubscriptionSessionMovements",
                columns: new[] { "AcademyId", "ReversesMovementId" },
                unique: true,
                filter: "\"ReversesMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSessionMovements_AcademyId_SubscriptionPeriodId",
                table: "SubscriptionSessionMovements",
                columns: new[] { "AcademyId", "SubscriptionPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_AgeCategoryId",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "AgeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_BranchId",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_IsActive",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_RecurringScheduleId",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "RecurringScheduleId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_SessionDate_Status",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "SessionDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_SportId",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_TrainingGroupId_BranchId_SportId",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "TrainingGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_AcademyId_TrainingGroupId_SessionDate_Star~",
                table: "TrainingSessions",
                columns: new[] { "AcademyId", "TrainingGroupId", "SessionDate", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffAttendances");

            migrationBuilder.DropTable(
                name: "SubscriptionSessionMovements");

            migrationBuilder.DropTable(
                name: "PlayerAttendances");

            migrationBuilder.DropTable(
                name: "TrainingSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionPeriods_RemainingSessions",
                table: "SubscriptionPeriods");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_StaffGroupAssignments_AcademyId_AcademyMembershipId_Trainin~",
                table: "StaffGroupAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_TrainingGroupId",
                table: "SportEnrollments");

            migrationBuilder.CreateIndex(
                name: "IX_StaffGroupAssignments_AcademyId_AcademyMembershipId_Trainin~",
                table: "StaffGroupAssignments",
                columns: new[] { "AcademyId", "AcademyMembershipId", "TrainingGroupId" },
                unique: true);
        }
    }
}
