using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ServiceKindsAndCalendarSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChargedMonths",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMonths",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceMonthlyPrice",
                table: "SubscriptionPlans",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceKind",
                table: "SubscriptionPlans",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Training");

            migrationBuilder.AddColumn<string>(
                name: "TrainingFormat",
                table: "SubscriptionPlans",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.AddColumn<int>(
                name: "DurationMonthsSnapshot",
                table: "SubscriptionPeriods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceKindSnapshot",
                table: "SubscriptionPeriods",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainingFormatSnapshot",
                table: "SubscriptionPeriods",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMonthsSnapshot",
                table: "RenewalRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceKindSnapshot",
                table: "RenewalRequests",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainingFormatSnapshot",
                table: "RenewalRequests",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChargedMonths",
                table: "PlanPriceVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMonths",
                table: "PlanPriceVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceMonthlyPrice",
                table: "PlanPriceVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceKind",
                table: "PlanPriceVersions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Training");

            migrationBuilder.AddColumn<string>(
                name: "TrainingFormat",
                table: "PlanPriceVersions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.AddColumn<int>(
                name: "DurationMonthsSnapshot",
                table: "NewEnrollmentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceKindSnapshot",
                table: "NewEnrollmentRequests",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainingFormatSnapshot",
                table: "NewEnrollmentRequests",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
            ReplaceTermsGuard(migrationBuilder, extended: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ReplaceTermsGuard(migrationBuilder, extended: false);
            migrationBuilder.DropColumn(
                name: "ChargedMonths",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "DurationMonths",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "ReferenceMonthlyPrice",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "ServiceKind",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "TrainingFormat",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "DurationMonthsSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "ServiceKindSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "TrainingFormatSnapshot",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "DurationMonthsSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "ServiceKindSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "TrainingFormatSnapshot",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "ChargedMonths",
                table: "PlanPriceVersions");

            migrationBuilder.DropColumn(
                name: "DurationMonths",
                table: "PlanPriceVersions");

            migrationBuilder.DropColumn(
                name: "ReferenceMonthlyPrice",
                table: "PlanPriceVersions");

            migrationBuilder.DropColumn(
                name: "ServiceKind",
                table: "PlanPriceVersions");

            migrationBuilder.DropColumn(
                name: "TrainingFormat",
                table: "PlanPriceVersions");

            migrationBuilder.DropColumn(
                name: "DurationMonthsSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "ServiceKindSnapshot",
                table: "NewEnrollmentRequests");

            migrationBuilder.DropColumn(
                name: "TrainingFormatSnapshot",
                table: "NewEnrollmentRequests");
        }
        private static void ReplaceTermsGuard(MigrationBuilder migrationBuilder, bool extended)
        {
            var oldFields = "OLD.\"PlanNameSnapshot\", OLD.\"PlanTypeSnapshot\", OLD.\"DurationDaysSnapshot\", OLD.\"SessionCountSnapshot\", OLD.\"PlanRevisionSnapshot\"";
            if (extended) oldFields += ", OLD.\"DurationMonthsSnapshot\", OLD.\"ServiceKindSnapshot\", OLD.\"TrainingFormatSnapshot\"";
            migrationBuilder.Sql($"""
                CREATE OR REPLACE FUNCTION academy_plan_terms_immutable() RETURNS trigger AS $$
                BEGIN
                  IF OLD."PlanTypeSnapshot" IS NOT NULL AND ROW({oldFields}) IS DISTINCT FROM ROW({oldFields.Replace("OLD.", "NEW.")}) THEN
                    RAISE EXCEPTION 'Posted subscription terms cannot be changed' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }
    }
}
