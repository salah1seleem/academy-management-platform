using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3SessionMediaPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TrainingSessionId",
                table: "PrivateAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrivateMediaWithdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaWithdrawals", x => x.Id);
                    table.UniqueConstraint("AK_PrivateMediaWithdrawals_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PrivateMediaWithdrawal_Active", "\"IsActive\"");
                    table.ForeignKey(
                        name: "FK_PrivateMediaWithdrawals_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaWithdrawals_PrivateAssets_AcademyId_AssetId",
                        columns: x => new { x.AcademyId, x.AssetId },
                        principalTable: "PrivateAssets",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaWithdrawals_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateAssets_AcademyId_TrainingSessionId_TrainingGroupId",
                table: "PrivateAssets",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaWithdrawals_AcademyId_ActorUserId_RequestId",
                table: "PrivateMediaWithdrawals",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaWithdrawals_AcademyId_AssetId",
                table: "PrivateMediaWithdrawals",
                columns: new[] { "AcademyId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaWithdrawals_ActorUserId",
                table: "PrivateMediaWithdrawals",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrivateAssets_TrainingSessions_AcademyId_TrainingSessionId_~",
                table: "PrivateAssets",
                columns: new[] { "AcademyId", "TrainingSessionId", "TrainingGroupId" },
                principalTable: "TrainingSessions",
                principalColumns: new[] { "AcademyId", "Id", "TrainingGroupId" },
                onDelete: ReferentialAction.Restrict);
            AddSessionPublicationGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveSessionPublicationGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_PrivateAssets_TrainingSessions_AcademyId_TrainingSessionId_~",
                table: "PrivateAssets");

            migrationBuilder.DropTable(
                name: "PrivateMediaWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_PrivateAssets_AcademyId_TrainingSessionId_TrainingGroupId",
                table: "PrivateAssets");

            migrationBuilder.DropColumn(
                name: "TrainingSessionId",
                table: "PrivateAssets");
        }
    }
}
