using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2DurableHistoricalImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceivableDocument_Positive",
                table: "ReceivableDocuments");

            migrationBuilder.AlterColumn<decimal>(
                name: "PriceSnapshot",
                table: "SubscriptionPeriods",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<Guid>(
                name: "CollectionId",
                table: "SubscriptionPeriods",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "SubscriptionPeriods",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "SubscriptionPeriods",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Collected");

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "ReceivableDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcademyImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferenceStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApprovedBranches = table.Column<string>(type: "jsonb", nullable: false),
                    CutoffDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StagedFile = table.Column<byte[]>(type: "bytea", nullable: true),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedRows = table.Column<int>(type: "integer", nullable: false),
                    ReplayedRows = table.Column<int>(type: "integer", nullable: false),
                    FinanciallyIncomplete = table.Column<int>(type: "integer", nullable: false),
                    GuardianLinksApproved = table.Column<bool>(type: "boolean", nullable: false),
                    OpeningBalancesApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    GuardianResolutions = table.Column<string>(type: "jsonb", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyImportJobs", x => x.Id);
                    table.UniqueConstraint("AK_AcademyImportJobs_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ImportJob_Approval", "\"GuardianLinksApproved\" AND (\"State\" <> 'Completed' OR (\"StagedFile\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"PublishedRows\" + \"ReplayedRows\" = \"RowCount\"))");
                    table.CheckConstraint("CK_ImportJob_Counts", "\"RowCount\" BETWEEN 1 AND 1000 AND \"PublishedRows\" >= 0 AND \"ReplayedRows\" >= 0 AND \"PublishedRows\" + \"ReplayedRows\" <= \"RowCount\"");
                    table.ForeignKey(
                        name: "FK_AcademyImportJobs_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcademyImportJobs_AcademyMemberships_AcademyId_MembershipId",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcademyImportJobs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportedEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LegacyPlayerId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LegacyEnrollmentId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PlayerIdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RowHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedEnrollments", x => x.Id);
                    table.UniqueConstraint("AK_ImportedEnrollments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_AcademyImportJobs_AcademyId_JobId",
                        columns: x => new { x.AcademyId, x.JobId },
                        principalTable: "AcademyImportJobs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_GuardianProfiles_AcademyId_GuardianId",
                        columns: x => new { x.AcademyId, x.GuardianId },
                        principalTable: "GuardianProfiles",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_SportEnrollments_AcademyId_SportEnrollm~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEnrollments_SubscriptionPeriods_AcademyId_Subscript~",
                        columns: x => new { x.AcademyId, x.SubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportedHouseholds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    HouseholdKey = table.Column<string>(type: "character varying(84)", maxLength: 84, nullable: false),
                    IdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GuardianId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedHouseholds", x => x.Id);
                    table.UniqueConstraint("AK_ImportedHouseholds_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ImportedHouseholds_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHouseholds_AcademyImportJobs_AcademyId_JobId",
                        columns: x => new { x.AcademyId, x.JobId },
                        principalTable: "AcademyImportJobs",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedHouseholds_GuardianProfiles_AcademyId_GuardianId",
                        columns: x => new { x.AcademyId, x.GuardianId },
                        principalTable: "GuardianProfiles",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_ImportJobId",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "ImportJobId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionPeriod_Origin",
                table: "SubscriptionPeriods",
                sql: "(\"Origin\" = 'Collected' AND \"CollectionId\" IS NOT NULL AND \"ImportJobId\" IS NULL AND \"PriceSnapshot\" IS NOT NULL AND \"PriceSnapshot\" >= 0) OR (\"Origin\" = 'ImportedHistorical' AND \"CollectionId\" IS NULL AND \"ImportJobId\" IS NOT NULL AND (\"PriceSnapshot\" IS NULL OR \"PriceSnapshot\" >= 0))");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_AcademyId_ImportJobId",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "ImportJobId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceivableDocument_Positive",
                table: "ReceivableDocuments",
                sql: "\"Amount\" > 0 OR (\"Amount\" < 0 AND \"Source\" = 'OpeningBalance' AND \"ImportJobId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_AcademyId_ActorUserId_IdempotencyKey",
                table: "AcademyImportJobs",
                columns: new[] { "AcademyId", "ActorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_AcademyId_MembershipId",
                table: "AcademyImportJobs",
                columns: new[] { "AcademyId", "MembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_AcademyId_SourceSystem_FileHash",
                table: "AcademyImportJobs",
                columns: new[] { "AcademyId", "SourceSystem", "FileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_ActorUserId",
                table: "AcademyImportJobs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyImportJobs_State_CreatedAtUtc",
                table: "AcademyImportJobs",
                columns: new[] { "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_BranchId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_GuardianId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "GuardianId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_JobId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_PlayerId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_SourceSystem_LegacyEnrollment~",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "SourceSystem", "LegacyEnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_SourceSystem_LegacyPlayerId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "SourceSystem", "LegacyPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_SportEnrollmentId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEnrollments_AcademyId_SubscriptionPeriodId",
                table: "ImportedEnrollments",
                columns: new[] { "AcademyId", "SubscriptionPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHouseholds_AcademyId_GuardianId",
                table: "ImportedHouseholds",
                columns: new[] { "AcademyId", "GuardianId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHouseholds_AcademyId_JobId",
                table: "ImportedHouseholds",
                columns: new[] { "AcademyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedHouseholds_AcademyId_SourceSystem_HouseholdKey",
                table: "ImportedHouseholds",
                columns: new[] { "AcademyId", "SourceSystem", "HouseholdKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceivableDocuments_AcademyImportJobs_AcademyId_ImportJobId",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "ImportJobId" },
                principalTable: "AcademyImportJobs",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_AcademyImportJobs_AcademyId_ImportJobId",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "ImportJobId" },
                principalTable: "AcademyImportJobs",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE TRIGGER imported_enrollment_immutable BEFORE UPDATE OR DELETE ON "ImportedEnrollments"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE TRIGGER imported_household_immutable BEFORE UPDATE OR DELETE ON "ImportedHouseholds"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_import_agreement_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."Origin" = 'ImportedHistorical' AND
                       (NEW."Origin", NEW."ImportJobId", NEW."CollectionId", NEW."PriceSnapshot") IS DISTINCT FROM
                       (OLD."Origin", OLD."ImportJobId", OLD."CollectionId", OLD."PriceSnapshot") THEN
                        RAISE EXCEPTION 'import agreement is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER imported_agreement_immutable BEFORE UPDATE ON "SubscriptionPeriods"
                    FOR EACH ROW EXECUTE FUNCTION academy_import_agreement_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AcademyImportJobs") THEN
                        RAISE EXCEPTION 'restore a verified backup; rollback cannot discard import provenance' USING ERRCODE = '23514';
                    END IF;
                END $$;
                DROP TRIGGER imported_agreement_immutable ON "SubscriptionPeriods";
                DROP FUNCTION academy_import_agreement_immutable();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ReceivableDocuments_AcademyImportJobs_AcademyId_ImportJobId",
                table: "ReceivableDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPeriods_AcademyImportJobs_AcademyId_ImportJobId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "ImportedEnrollments");

            migrationBuilder.DropTable(
                name: "ImportedHouseholds");

            migrationBuilder.DropTable(
                name: "AcademyImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPeriods_AcademyId_ImportJobId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionPeriod_Origin",
                table: "SubscriptionPeriods");

            migrationBuilder.DropIndex(
                name: "IX_ReceivableDocuments_AcademyId_ImportJobId",
                table: "ReceivableDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceivableDocument_Positive",
                table: "ReceivableDocuments");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "ReceivableDocuments");

            migrationBuilder.AlterColumn<decimal>(
                name: "PriceSnapshot",
                table: "SubscriptionPeriods",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CollectionId",
                table: "SubscriptionPeriods",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceivableDocument_Positive",
                table: "ReceivableDocuments",
                sql: "\"Amount\" > 0");
        }
    }
}
