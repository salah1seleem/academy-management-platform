using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4CoachAttendanceEvaluationTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CoachTaskFollowUpGuards.Authority);
            migrationBuilder.Sql(CoachTaskFollowUpGuards.History);
            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps",
                sql: "\"IsActive\" AND \"Condition\" IN ('RecordFollowUp','GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences','GroupOverdueFeedback','CoachAttendanceTask','CoachEvaluationTask') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(V4RelatedRecordFollowUpGuards.History);
            migrationBuilder.Sql("DROP FUNCTION academy_coach_task_authority(uuid,uuid,uuid,boolean);");
            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps",
                sql: "\"IsActive\" AND \"Condition\" IN ('RecordFollowUp','GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences','GroupOverdueFeedback') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");
        }
    }
}
