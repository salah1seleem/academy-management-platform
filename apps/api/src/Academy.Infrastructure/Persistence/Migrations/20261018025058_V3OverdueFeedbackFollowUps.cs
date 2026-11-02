using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3OverdueFeedbackFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps",
                sql: "\"IsActive\" AND \"Condition\" IN ('GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences','GroupOverdueFeedback') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS(SELECT 1 FROM "OwnerFollowUps" WHERE "Condition"='GroupOverdueFeedback') THEN RAISE EXCEPTION 'Preserve family review evidence before rollback' USING ERRCODE='23514'; END IF; END $$;""");
            migrationBuilder.DropCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OwnerFollowUp_State",
                table: "OwnerFollowUps",
                sql: "\"IsActive\" AND \"Condition\" IN ('GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)");
        }
    }
}
