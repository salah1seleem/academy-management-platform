using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice3SubscriptionsPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_SportId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "Id", "SportId" });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EnglishName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PlanType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    SessionCount = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionPlans_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_SubscriptionPlans_AcademyId_Id_SportId", x => new { x.AcademyId, x.Id, x.SportId });
                    table.CheckConstraint("CK_SubscriptionPlans_Configuration", "(\"PlanType\" = 'Duration' AND \"DurationDays\" > 0 AND \"SessionCount\" IS NULL) OR (\"PlanType\" = 'Sessions' AND \"DurationDays\" IS NULL AND \"SessionCount\" > 0) OR (\"PlanType\" = 'Combined' AND \"DurationDays\" > 0 AND \"SessionCount\" > 0)");
                    table.ForeignKey(
                        name: "FK_SubscriptionPlans_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlans_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RenewalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AmountExpected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalRequests", x => x.Id);
                    table.UniqueConstraint("AK_RenewalRequests_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_RenewalRequests_AcademyId_Id_SportEnrollmentId", x => new { x.AcademyId, x.Id, x.SportEnrollmentId });
                    table.ForeignKey(
                        name: "FK_RenewalRequests_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenewalRequests_SportEnrollments_AcademyId_SportEnrollmentI~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenewalRequests_SubscriptionPlans_AcademyId_SubscriptionPla~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId, x.SportId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RenewalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProviderEnvironment = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CheckoutReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastProviderEventAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRequests", x => x.Id);
                    table.UniqueConstraint("AK_PaymentRequests_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentRequests_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_RenewalRequests_AcademyId_RenewalRequestId",
                        columns: x => new { x.AcademyId, x.RenewalRequestId },
                        principalTable: "RenewalRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RenewalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.UniqueConstraint("AK_Collections_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Collections_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Collections_PaymentRequests_AcademyId_PaymentRequestId",
                        columns: x => new { x.AcademyId, x.PaymentRequestId },
                        principalTable: "PaymentRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Collections_RenewalRequests_AcademyId_RenewalRequestId_Spor~",
                        columns: x => new { x.AcademyId, x.RenewalRequestId, x.SportEnrollmentId },
                        principalTable: "RenewalRequests",
                        principalColumns: new[] { "AcademyId", "Id", "SportEnrollmentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Collections_SportEnrollments_AcademyId_SportEnrollmentId",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProviderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RawStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessingResult = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderEvents", x => x.Id);
                    table.UniqueConstraint("AK_PaymentProviderEvents_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentProviderEvents_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentProviderEvents_PaymentRequests_AcademyId_PaymentRequ~",
                        columns: x => new { x.AcademyId, x.PaymentRequestId },
                        principalTable: "PaymentRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerNameSnapshot = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    SportNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PlanNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.UniqueConstraint("AK_Receipts_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Receipts_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Collections_AcademyId_CollectionId",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InitialSessions = table.Column<int>(type: "integer", nullable: true),
                    RemainingSessions = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriceSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencySnapshot = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPeriods", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionPeriods_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Collections_AcademyId_CollectionId",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_SportEnrollments_AcademyId_SportEnrollm~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_SubscriptionPlans_AcademyId_Subscriptio~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId, x.SportId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_AcademyId_IsActive",
                table: "Collections",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_AcademyId_PaymentRequestId",
                table: "Collections",
                columns: new[] { "AcademyId", "PaymentRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_AcademyId_RenewalRequestId_SportEnrollmentId",
                table: "Collections",
                columns: new[] { "AcademyId", "RenewalRequestId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_AcademyId_SportEnrollmentId",
                table: "Collections",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_AcademyId_IsActive",
                table: "PaymentProviderEvents",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_AcademyId_PaymentRequestId",
                table: "PaymentProviderEvents",
                columns: new[] { "AcademyId", "PaymentRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_Provider_ProviderEventId",
                table: "PaymentProviderEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_AcademyId_IsActive",
                table: "PaymentRequests",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_AcademyId_RenewalRequestId",
                table: "PaymentRequests",
                columns: new[] { "AcademyId", "RenewalRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Provider_ProviderReference",
                table: "PaymentRequests",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_AcademyId_CollectionId",
                table: "Receipts",
                columns: new[] { "AcademyId", "CollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_AcademyId_IsActive",
                table: "Receipts",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_AcademyId_ReceiptNumber",
                table: "Receipts",
                columns: new[] { "AcademyId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_AcademyId_IsActive",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_AcademyId_RequestedByUserId_IdempotencyKey",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "RequestedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_AcademyId_SportEnrollmentId_SportId",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_AcademyId_SubscriptionPlanId_SportId",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "SubscriptionPlanId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_CollectionId",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "CollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_IsActive",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_SportEnrollmentId_SportId",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "SportEnrollmentId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_SportEnrollmentId_StartDate",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "SportEnrollmentId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_AcademyId_SubscriptionPlanId_SportId",
                table: "SubscriptionPeriods",
                columns: new[] { "AcademyId", "SubscriptionPlanId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_AcademyId_IsActive",
                table: "SubscriptionPlans",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_AcademyId_SportId_ArabicName",
                table: "SubscriptionPlans",
                columns: new[] { "AcademyId", "SportId", "ArabicName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentProviderEvents");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "PaymentRequests");

            migrationBuilder.DropTable(
                name: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_SportId",
                table: "SportEnrollments");
        }
    }
}
