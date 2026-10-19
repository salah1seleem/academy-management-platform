using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4ExternalAgreementReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalAgreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CounterpartyName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAgreements", x => x.Id);
                    table.UniqueConstraint("AK_ExternalAgreements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ExternalAgreement_Shape", "\"IsActive\" AND \"Amount\">0 AND \"Amount\"<=999999999 AND \"FromDate\"<=\"ToDate\" AND \"Kind\" IN ('ExternalProgram','PoolRental') AND length(trim(\"CounterpartyName\"))>0 AND length(trim(\"Title\"))>0 AND (\"CancelledAtUtc\" IS NULL)=(\"CancelledByUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ExternalAgreements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreements_AcademyMemberships_AcademyId_ActorMember~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreements_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreements_Employees_AcademyId_CounterpartyEmployee~",
                        columns: x => new { x.AcademyId, x.CounterpartyEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreements_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAgreementBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAgreementBookings", x => x.Id);
                    table.UniqueConstraint("AK_ExternalAgreementBookings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ExternalAgreementBookings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreementBookings_ExternalAgreements_AcademyId_Agre~",
                        columns: x => new { x.AcademyId, x.AgreementId },
                        principalTable: "ExternalAgreements",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreementBookings_ResourceBookings_AcademyId_Resour~",
                        columns: x => new { x.AcademyId, x.ResourceBookingId },
                        principalTable: "ResourceBookings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAgreementReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    CashPostingRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAgreementReceipts", x => x.Id);
                    table.UniqueConstraint("AK_ExternalAgreementReceipts_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ExternalReceipt_Shape", "\"IsActive\" AND \"Amount\">0 AND \"Amount\"<=999999999 AND \"PaymentMethod\" IN ('Cash','Online') AND (NOT \"CashPostingRequired\" OR \"PaymentMethod\"='Cash')");
                    table.ForeignKey(
                        name: "FK_ExternalAgreementReceipts_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreementReceipts_AcademyMemberships_AcademyId_Acto~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreementReceipts_ExternalAgreements_AcademyId_Agre~",
                        columns: x => new { x.AcademyId, x.AgreementId },
                        principalTable: "ExternalAgreements",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalAgreementReceipts_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashExternalReceiptPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashExternalReceiptPostings", x => x.Id);
                    table.UniqueConstraint("AK_CashExternalReceiptPostings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CashExternalReceipt_Positive", "\"IsActive\" AND \"Amount\">0");
                    table.ForeignKey(
                        name: "FK_CashExternalReceiptPostings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashExternalReceiptPostings_CashSessions_AcademyId_SessionId",
                        columns: x => new { x.AcademyId, x.SessionId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashExternalReceiptPostings_ExternalAgreementReceipts_Acade~",
                        columns: x => new { x.AcademyId, x.ReceiptId },
                        principalTable: "ExternalAgreementReceipts",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashExternalReceiptPostings_AcademyId_ReceiptId",
                table: "CashExternalReceiptPostings",
                columns: new[] { "AcademyId", "ReceiptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashExternalReceiptPostings_AcademyId_SessionId",
                table: "CashExternalReceiptPostings",
                columns: new[] { "AcademyId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreementBookings_AcademyId_AgreementId",
                table: "ExternalAgreementBookings",
                columns: new[] { "AcademyId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreementBookings_AcademyId_ResourceBookingId",
                table: "ExternalAgreementBookings",
                columns: new[] { "AcademyId", "ResourceBookingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreementReceipts_AcademyId_ActorMembershipId_Reque~",
                table: "ExternalAgreementReceipts",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreementReceipts_AcademyId_AgreementId",
                table: "ExternalAgreementReceipts",
                columns: new[] { "AcademyId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreementReceipts_AcademyId_ReceiptNumber",
                table: "ExternalAgreementReceipts",
                columns: new[] { "AcademyId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreementReceipts_ActorUserId",
                table: "ExternalAgreementReceipts",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreements_AcademyId_ActorMembershipId_RequestId",
                table: "ExternalAgreements",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreements_AcademyId_BranchId",
                table: "ExternalAgreements",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreements_AcademyId_CounterpartyEmployeeId",
                table: "ExternalAgreements",
                columns: new[] { "AcademyId", "CounterpartyEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreements_AcademyId_SportId",
                table: "ExternalAgreements",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgreements_ActorUserId",
                table: "ExternalAgreements",
                column: "ActorUserId");
            migrationBuilder.Sql(V4ExternalAgreementGuards.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"ExternalAgreements\") THEN RAISE EXCEPTION 'Preserve external agreements and receipts before rollback' USING ERRCODE='23514'; END IF; END $$; DROP FUNCTION academy_external_agreement_guard() CASCADE; DROP FUNCTION academy_external_agreement_final_guard() CASCADE;");
            migrationBuilder.DropTable(
                name: "CashExternalReceiptPostings");

            migrationBuilder.DropTable(
                name: "ExternalAgreementBookings");

            migrationBuilder.DropTable(
                name: "ExternalAgreementReceipts");

            migrationBuilder.DropTable(
                name: "ExternalAgreements");
        }
    }
}
