using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedSubscriptionSessionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_DaysDelta",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_EndDates",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_Type",
                table: "SubscriptionAdjustments");

            migrationBuilder.AddColumn<int>(
                name: "NewRemainingSessions",
                table: "SubscriptionAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OldRemainingSessions",
                table: "SubscriptionAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionsDelta",
                table: "SubscriptionAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_DaysDelta",
                table: "SubscriptionAdjustments",
                sql: "(\"AdjustmentType\" = 'DaysAdded' AND \"DaysDelta\" > 0) OR (\"AdjustmentType\" = 'DaysDeducted' AND \"DaysDelta\" < 0) OR (\"AdjustmentType\" = 'FreezeEnded' AND \"DaysDelta\" > 0) OR (\"AdjustmentType\" IN ('FreezeStarted', 'Cancelled', 'SessionsAdded') AND \"DaysDelta\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_EndDates",
                table: "SubscriptionAdjustments",
                sql: "(\"AdjustmentType\" IN ('FreezeEnded', 'DaysAdded', 'DaysDeducted') AND \"OldEndDate\" IS NOT NULL AND \"NewEndDate\" IS NOT NULL) OR (\"AdjustmentType\" = 'SessionsAdded' AND \"NewEndDate\" IS NOT NULL) OR (\"AdjustmentType\" IN ('FreezeStarted', 'Cancelled') AND \"NewEndDate\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_Sessions",
                table: "SubscriptionAdjustments",
                sql: "(\"AdjustmentType\" = 'SessionsAdded' AND \"SessionsDelta\" IS NOT NULL AND \"SessionsDelta\" BETWEEN 1 AND 10000 AND \"OldRemainingSessions\" IS NOT NULL AND \"NewRemainingSessions\" IS NOT NULL AND \"OldRemainingSessions\" >= 0 AND \"NewRemainingSessions\" = \"OldRemainingSessions\" + \"SessionsDelta\") OR (\"AdjustmentType\" <> 'SessionsAdded' AND \"SessionsDelta\" IS NULL AND \"OldRemainingSessions\" IS NULL AND \"NewRemainingSessions\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_Type",
                table: "SubscriptionAdjustments",
                sql: "\"AdjustmentType\" IN ('FreezeStarted', 'FreezeEnded', 'DaysAdded', 'DaysDeducted', 'Cancelled', 'SessionsAdded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_DaysDelta",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_EndDates",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_Sessions",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAdjustments_Type",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropColumn(
                name: "NewRemainingSessions",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropColumn(
                name: "OldRemainingSessions",
                table: "SubscriptionAdjustments");

            migrationBuilder.DropColumn(
                name: "SessionsDelta",
                table: "SubscriptionAdjustments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_DaysDelta",
                table: "SubscriptionAdjustments",
                sql: "(\"AdjustmentType\" = 'DaysAdded' AND \"DaysDelta\" > 0) OR (\"AdjustmentType\" = 'DaysDeducted' AND \"DaysDelta\" < 0) OR (\"AdjustmentType\" = 'FreezeEnded' AND \"DaysDelta\" > 0) OR (\"AdjustmentType\" IN ('FreezeStarted', 'Cancelled') AND \"DaysDelta\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_EndDates",
                table: "SubscriptionAdjustments",
                sql: "(\"AdjustmentType\" IN ('FreezeEnded', 'DaysAdded', 'DaysDeducted') AND \"OldEndDate\" IS NOT NULL AND \"NewEndDate\" IS NOT NULL) OR (\"AdjustmentType\" IN ('FreezeStarted', 'Cancelled') AND \"NewEndDate\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAdjustments_Type",
                table: "SubscriptionAdjustments",
                sql: "\"AdjustmentType\" IN ('FreezeStarted', 'FreezeEnded', 'DaysAdded', 'DaysDeducted', 'Cancelled')");
        }
    }
}
