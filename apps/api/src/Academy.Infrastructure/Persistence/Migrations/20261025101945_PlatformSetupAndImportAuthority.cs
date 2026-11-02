using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformSetupAndImportAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "AcademyImportJobs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "PlatformInitiated",
                table: "AcademyImportJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AcademyPlatformProfiles",
                columns: table => new
                {
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationalState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Location = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CommercialState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlatformFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FeeCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BillingPeriod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SubscriptionFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    SubscriptionTo = table.Column<DateOnly>(type: "date", nullable: true),
                    EditVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyPlatformProfiles", x => x.AcademyId);
                    table.CheckConstraint("CK_PlatformProfile_Fee", "(\"PlatformFee\" IS NULL OR \"PlatformFee\" >= 0) AND \"FeeCurrency\" IN ('EGP','USD','SAR','AED') AND \"BillingPeriod\" IN ('Monthly','Yearly') AND (\"SubscriptionTo\" IS NULL OR \"SubscriptionFrom\" IS NOT NULL AND \"SubscriptionTo\" >= \"SubscriptionFrom\")");
                    table.CheckConstraint("CK_PlatformProfile_State", "\"OperationalState\" IN ('Draft','Active','Suspended') AND \"CommercialState\" IN ('Unspecified','Trial','Subscribed','Expired')");
                    table.ForeignKey(
                        name: "FK_AcademyPlatformProfiles_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportJob_Authority",
                table: "AcademyImportJobs",
                sql: "(\"PlatformInitiated\" AND \"MembershipId\" IS NULL) OR (NOT \"PlatformInitiated\" AND \"MembershipId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS (SELECT 1 FROM "AcademyImportJobs" WHERE "PlatformInitiated") THEN RAISE EXCEPTION 'Platform import provenance must be retained; rollback requires an explicit data migration.'; END IF; END $$;""");
            migrationBuilder.DropTable(
                name: "AcademyPlatformProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportJob_Authority",
                table: "AcademyImportJobs");

            migrationBuilder.DropColumn(
                name: "PlatformInitiated",
                table: "AcademyImportJobs");

            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "AcademyImportJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
