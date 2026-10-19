using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V4VipRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VipRewardUseId",
                table: "RenewalRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VipRewardUseId",
                table: "FamilyRenewalRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VipRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledByMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipRewards", x => x.Id);
                    table.UniqueConstraint("AK_VipRewards_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_VipReward_Terms", "\"IsActive\" AND \"Percentage\">0 AND \"Percentage\"<100 AND \"ExpiresOn\">=\"IssuedOn\" AND (\"CancelledAtUtc\" IS NULL)=(\"CancelledByMembershipId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_VipRewards_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VipRewards_AcademyMemberships_AcademyId_CancelledByMembersh~",
                        columns: x => new { x.AcademyId, x.CancelledByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VipRewards_AcademyMemberships_AcademyId_IssuedByMembershipId",
                        columns: x => new { x.AcademyId, x.IssuedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VipRewards_AcademyNotices_AcademyId_NoticeId",
                        columns: x => new { x.AcademyId, x.NoticeId },
                        principalTable: "AcademyNotices",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VipRewards_DiscountRules_AcademyId_DiscountRuleId",
                        columns: x => new { x.AcademyId, x.DiscountRuleId },
                        principalTable: "DiscountRules",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VipRewards_GuardianProfiles_AcademyId_GuardianId",
                        columns: x => new { x.AcademyId, x.GuardianId },
                        principalTable: "GuardianProfiles",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VipRewardUses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RewardId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipRewardUses", x => x.Id);
                    table.UniqueConstraint("AK_VipRewardUses_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_VipRewardUse_Terms", "\"IsActive\" AND \"SourceKind\" IN ('Renewal','Family','Enrollment') AND NOT (\"UsedAtUtc\" IS NOT NULL AND \"ReleasedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_VipRewardUses_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VipRewardUses_VipRewards_AcademyId_RewardId",
                        columns: x => new { x.AcademyId, x.RewardId },
                        principalTable: "VipRewards",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_AcademyId_VipRewardUseId",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "VipRewardUseId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_VipRewardUseId",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "VipRewardUseId" });

            migrationBuilder.CreateIndex(
                name: "IX_VipRewards_AcademyId_CancelledByMembershipId",
                table: "VipRewards",
                columns: new[] { "AcademyId", "CancelledByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_VipRewards_AcademyId_DiscountRuleId",
                table: "VipRewards",
                columns: new[] { "AcademyId", "DiscountRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VipRewards_AcademyId_GuardianId",
                table: "VipRewards",
                columns: new[] { "AcademyId", "GuardianId" });

            migrationBuilder.CreateIndex(
                name: "IX_VipRewards_AcademyId_IssuedByMembershipId_RequestId_Guardia~",
                table: "VipRewards",
                columns: new[] { "AcademyId", "IssuedByMembershipId", "RequestId", "GuardianId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VipRewards_AcademyId_NoticeId",
                table: "VipRewards",
                columns: new[] { "AcademyId", "NoticeId" });

            migrationBuilder.CreateIndex(
                name: "IX_VipRewardUses_AcademyId_RewardId",
                table: "VipRewardUses",
                columns: new[] { "AcademyId", "RewardId" },
                unique: true,
                filter: "\"ReleasedAtUtc\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyRenewalRequests_VipRewardUses_AcademyId_VipRewardUseId",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "VipRewardUseId" },
                principalTable: "VipRewardUses",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_VipRewardUses_AcademyId_VipRewardUseId",
                table: "RenewalRequests",
                columns: new[] { "AcademyId", "VipRewardUseId" },
                principalTable: "VipRewardUses",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(V4VipRewardGuards.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"VipRewards\") THEN RAISE EXCEPTION 'Preserve issued VIP financial history'; END IF; END $$; DROP TRIGGER vip_enrollment_final ON \"NewEnrollmentRequests\"; DROP TRIGGER vip_renewal_final ON \"RenewalRequests\"; DROP TRIGGER vip_use_final ON \"VipRewardUses\"; DROP FUNCTION academy_vip_final_guard(); DROP TRIGGER vip_rule_guard ON \"DiscountRules\"; DROP TRIGGER vip_reward_guard ON \"VipRewards\"; DROP TRIGGER vip_use_guard ON \"VipRewardUses\"; DROP FUNCTION academy_vip_guard();");
            migrationBuilder.DropForeignKey(
                name: "FK_FamilyRenewalRequests_VipRewardUses_AcademyId_VipRewardUseId",
                table: "FamilyRenewalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_VipRewardUses_AcademyId_VipRewardUseId",
                table: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "VipRewardUses");

            migrationBuilder.DropTable(
                name: "VipRewards");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_AcademyId_VipRewardUseId",
                table: "RenewalRequests");

            migrationBuilder.DropIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_VipRewardUseId",
                table: "FamilyRenewalRequests");

            migrationBuilder.DropColumn(
                name: "VipRewardUseId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "VipRewardUseId",
                table: "FamilyRenewalRequests");
        }
    }
}
