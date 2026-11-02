using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerReviewOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerAttendances_SportEnrollments_AcademyId_SportEnrollmen~",
                table: "PlayerAttendances");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerEvaluations_SportEnrollments_AcademyId_SportEnrollme~1",
                table: "PlayerEvaluations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_TrainingGroupId",
                table: "SportEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_PlayerEvaluations_AcademyId_SportEnrollmentId_TrainingGroup~",
                table: "PlayerEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_PlayerAttendances_AcademyId_SportEnrollmentId_TrainingGroup~",
                table: "PlayerAttendances");

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "SportCatalogItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AcademyNewss",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisibleDays = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyNewss", x => x.Id);
                    table.UniqueConstraint("AK_AcademyNewss_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyNewss_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcademyNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyNotices", x => x.Id);
                    table.UniqueConstraint("AK_AcademyNotices_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyNotices_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcademyTransferSettingss",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstaPayAddress = table.Column<string>(type: "text", nullable: true),
                    WalletNumber = table.Column<string>(type: "text", nullable: true),
                    RecipientName = table.Column<string>(type: "text", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyTransferSettingss", x => x.Id);
                    table.UniqueConstraint("AK_AcademyTransferSettingss_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AcademyTransferSettingss_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRefunds", x => x.Id);
                    table.UniqueConstraint("AK_CollectionRefunds_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CollectionRefunds_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionRefunds_Collections_AcademyId_CollectionId",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscountRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "text", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IsSiblingOffer = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRules", x => x.Id);
                    table.UniqueConstraint("AK_DiscountRules_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_DiscountRules_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    StorageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateAssets", x => x.Id);
                    table.UniqueConstraint("AK_PrivateAssets_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PrivateAssets_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateAssets_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateAssets_TrainingGroups_AcademyId_TrainingGroupId",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    ProofAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSales", x => x.Id);
                    table.UniqueConstraint("AK_ProductSales_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProductSales_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSales_SportCatalogItems_AcademyId_ProductId",
                        columns: x => new { x.AcademyId, x.ProductId },
                        principalTable: "SportCatalogItems",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffDailyAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CheckIn = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CheckOut = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    SuggestedDeduction = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedDeduction = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffDailyAttendances", x => x.Id);
                    table.UniqueConstraint("AK_StaffDailyAttendances_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StaffDailyAttendances_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffDailyAttendances_AcademyMemberships_AcademyId_Membersh~",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "text", nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffGrants", x => x.Id);
                    table.UniqueConstraint("AK_StaffGrants_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StaffGrants_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffGrants_AcademyMemberships_AcademyId_MembershipId",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffWorkSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    LateThresholdMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateDeduction = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffWorkSchedules", x => x.Id);
                    table.UniqueConstraint("AK_StaffWorkSchedules_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StaffWorkSchedules_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkSchedules_AcademyMemberships_AcademyId_MembershipId",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerDiscountLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDiscountLinks", x => x.Id);
                    table.UniqueConstraint("AK_PlayerDiscountLinks_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlayerDiscountLinks_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerDiscountLinks_DiscountRules_AcademyId_DiscountRuleId",
                        columns: x => new { x.AcademyId, x.DiscountRuleId },
                        principalTable: "DiscountRules",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerDiscountLinks_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReviewReason = table.Column<string>(type: "text", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProofs", x => x.Id);
                    table.UniqueConstraint("AK_PaymentProofs_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentProofs_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentProofs_PaymentRequests_AcademyId_PaymentRequestId",
                        columns: x => new { x.AcademyId, x.PaymentRequestId },
                        principalTable: "PaymentRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentProofs_PrivateAssets_AcademyId_AssetId",
                        columns: x => new { x.AcademyId, x.AssetId },
                        principalTable: "PrivateAssets",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_SportEnrollmentId",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyTransferSettingss_AcademyId",
                table: "AcademyTransferSettingss",
                column: "AcademyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRefunds_AcademyId_CollectionId",
                table: "CollectionRefunds",
                columns: new[] { "AcademyId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRefunds_AcademyId_PerformedByUserId_IdempotencyKey",
                table: "CollectionRefunds",
                columns: new[] { "AcademyId", "PerformedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_AcademyId_ArabicName",
                table: "DiscountRules",
                columns: new[] { "AcademyId", "ArabicName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProofs_AcademyId_AssetId",
                table: "PaymentProofs",
                columns: new[] { "AcademyId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProofs_AcademyId_PaymentRequestId",
                table: "PaymentProofs",
                columns: new[] { "AcademyId", "PaymentRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDiscountLinks_AcademyId_DiscountRuleId",
                table: "PlayerDiscountLinks",
                columns: new[] { "AcademyId", "DiscountRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDiscountLinks_AcademyId_PlayerId_DiscountRuleId",
                table: "PlayerDiscountLinks",
                columns: new[] { "AcademyId", "PlayerId", "DiscountRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateAssets_AcademyId_PlayerId",
                table: "PrivateAssets",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateAssets_AcademyId_TrainingGroupId",
                table: "PrivateAssets",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_ProductId",
                table: "ProductSales",
                columns: new[] { "AcademyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSales_AcademyId_RequestedByUserId_IdempotencyKey",
                table: "ProductSales",
                columns: new[] { "AcademyId", "RequestedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffDailyAttendances_AcademyId_MembershipId_WorkDate",
                table: "StaffDailyAttendances",
                columns: new[] { "AcademyId", "MembershipId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffGrants_AcademyId_MembershipId_Permission",
                table: "StaffGrants",
                columns: new[] { "AcademyId", "MembershipId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSchedules_AcademyId_MembershipId",
                table: "StaffWorkSchedules",
                columns: new[] { "AcademyId", "MembershipId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerAttendances_SportEnrollments_AcademyId_SportEnrollmen~",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "SportEnrollmentId" },
                principalTable: "SportEnrollments",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerAttendances_SportEnrollments_AcademyId_SportEnrollmen~",
                table: "PlayerAttendances");

            migrationBuilder.DropTable(
                name: "AcademyNewss");

            migrationBuilder.DropTable(
                name: "AcademyNotices");

            migrationBuilder.DropTable(
                name: "AcademyTransferSettingss");

            migrationBuilder.DropTable(
                name: "CollectionRefunds");

            migrationBuilder.DropTable(
                name: "PaymentProofs");

            migrationBuilder.DropTable(
                name: "PlayerDiscountLinks");

            migrationBuilder.DropTable(
                name: "ProductSales");

            migrationBuilder.DropTable(
                name: "StaffDailyAttendances");

            migrationBuilder.DropTable(
                name: "StaffGrants");

            migrationBuilder.DropTable(
                name: "StaffWorkSchedules");

            migrationBuilder.DropTable(
                name: "PrivateAssets");

            migrationBuilder.DropTable(
                name: "DiscountRules");

            migrationBuilder.DropIndex(
                name: "IX_PlayerAttendances_AcademyId_SportEnrollmentId",
                table: "PlayerAttendances");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "SportCatalogItems");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_TrainingGroupId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "Id", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvaluations_AcademyId_SportEnrollmentId_TrainingGroup~",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAttendances_AcademyId_SportEnrollmentId_TrainingGroup~",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingGroupId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerAttendances_SportEnrollments_AcademyId_SportEnrollmen~",
                table: "PlayerAttendances",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingGroupId" },
                principalTable: "SportEnrollments",
                principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerEvaluations_SportEnrollments_AcademyId_SportEnrollme~1",
                table: "PlayerEvaluations",
                columns: new[] { "AcademyId", "SportEnrollmentId", "TrainingGroupId" },
                principalTable: "SportEnrollments",
                principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
