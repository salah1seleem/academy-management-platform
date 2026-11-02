using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice8BSubscriptionAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FrozenFromDate",
                table: "SubscriptionPeriods",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustmentType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DaysDelta = table.Column<int>(type: "integer", nullable: true),
                    OldEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelatedAdjustmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAdjustments", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionAdjustments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_SubscriptionAdjustments_DaysDelta", "(\"AdjustmentType\" = 'DaysAdded' AND \"DaysDelta\" > 0) OR (\"AdjustmentType\" = 'DaysDeducted' AND \"DaysDelta\" < 0) OR (\"AdjustmentType\" = 'FreezeEnded' AND \"DaysDelta\" > 0) OR (\"AdjustmentType\" IN ('FreezeStarted', 'Cancelled') AND \"DaysDelta\" IS NULL)");
                    table.CheckConstraint("CK_SubscriptionAdjustments_EndDates", "(\"AdjustmentType\" IN ('FreezeEnded', 'DaysAdded', 'DaysDeducted') AND \"OldEndDate\" IS NOT NULL AND \"NewEndDate\" IS NOT NULL) OR (\"AdjustmentType\" IN ('FreezeStarted', 'Cancelled') AND \"NewEndDate\" IS NULL)");
                    table.CheckConstraint("CK_SubscriptionAdjustments_FreezeLink", "(\"AdjustmentType\" = 'FreezeEnded' AND \"RelatedAdjustmentId\" IS NOT NULL) OR (\"AdjustmentType\" <> 'FreezeEnded' AND \"RelatedAdjustmentId\" IS NULL)");
                    table.CheckConstraint("CK_SubscriptionAdjustments_Type", "\"AdjustmentType\" IN ('FreezeStarted', 'FreezeEnded', 'DaysAdded', 'DaysDeducted', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_SubscriptionAdjustments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionAdjustments_SubscriptionAdjustments_AcademyId_R~",
                        columns: x => new { x.AcademyId, x.RelatedAdjustmentId },
                        principalTable: "SubscriptionAdjustments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionAdjustments_SubscriptionPeriods_AcademyId_Subsc~",
                        columns: x => new { x.AcademyId, x.SubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionAdjustments_Users_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAdjustments_AcademyId_IsActive",
                table: "SubscriptionAdjustments",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAdjustments_AcademyId_PerformedByUserId_Adjustm~",
                table: "SubscriptionAdjustments",
                columns: new[] { "AcademyId", "PerformedByUserId", "AdjustmentType", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAdjustments_AcademyId_RelatedAdjustmentId",
                table: "SubscriptionAdjustments",
                columns: new[] { "AcademyId", "RelatedAdjustmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAdjustments_AcademyId_SubscriptionPeriodId_Perf~",
                table: "SubscriptionAdjustments",
                columns: new[] { "AcademyId", "SubscriptionPeriodId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAdjustments_PerformedByUserId",
                table: "SubscriptionAdjustments",
                column: "PerformedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionAdjustments");

            migrationBuilder.DropColumn(
                name: "FrozenFromDate",
                table: "SubscriptionPeriods");
        }
    }
}
