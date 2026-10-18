using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3PrivateMediaLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeleteOnExpiry",
                table: "PrivateAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetainUntilUtc",
                table: "PrivateAssets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrivateMediaDownloadBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtcDay = table.Column<DateOnly>(type: "date", nullable: false),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaDownloadBudgets", x => x.Id);
                    table.UniqueConstraint("AK_PrivateMediaDownloadBudgets_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_MediaDownloadBudget_Bytes", "\"IsActive\" AND \"ReservedBytes\">=0");
                    table.ForeignKey(
                        name: "FK_PrivateMediaDownloadBudgets_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaDownloadBudgets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateMediaErasures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaErasures", x => x.Id);
                    table.UniqueConstraint("AK_PrivateMediaErasures_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_PrivateMediaErasures_AcademyId_Id_AssetId", x => new { x.AcademyId, x.Id, x.AssetId });
                    table.CheckConstraint("CK_MediaErasure_Source", "\"IsActive\" AND ((\"Reason\"='Manual' AND \"ActorUserId\" IS NOT NULL) OR (\"Reason\"='Retention' AND \"ActorUserId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_PrivateMediaErasures_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaErasures_PrivateAssets_AcademyId_AssetId",
                        columns: x => new { x.AcademyId, x.AssetId },
                        principalTable: "PrivateAssets",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaErasures_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateMediaPurges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleasedBytes = table.Column<long>(type: "bigint", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaPurges", x => x.Id);
                    table.UniqueConstraint("AK_PrivateMediaPurges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_MediaPurge_Bytes", "\"IsActive\" AND \"ReleasedBytes\">=0");
                    table.ForeignKey(
                        name: "FK_PrivateMediaPurges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateMediaPurges_PrivateMediaErasures_AcademyId_ErasureId~",
                        columns: x => new { x.AcademyId, x.ErasureId, x.AssetId },
                        principalTable: "PrivateMediaErasures",
                        principalColumns: new[] { "AcademyId", "Id", "AssetId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaRetention_Snapshot",
                table: "PrivateAssets",
                sql: "(\"RetainUntilUtc\" IS NULL AND NOT \"DeleteOnExpiry\") OR (\"Purpose\"='Media' AND \"RetainUntilUtc\" IS NOT NULL AND \"RetainUntilUtc\">\"CreatedAtUtc\")");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaDownloadBudgets_AcademyId_UserId_UtcDay",
                table: "PrivateMediaDownloadBudgets",
                columns: new[] { "AcademyId", "UserId", "UtcDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaDownloadBudgets_UserId",
                table: "PrivateMediaDownloadBudgets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaErasures_AcademyId_ActorUserId_RequestId",
                table: "PrivateMediaErasures",
                columns: new[] { "AcademyId", "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaErasures_AcademyId_AssetId",
                table: "PrivateMediaErasures",
                columns: new[] { "AcademyId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaErasures_ActorUserId",
                table: "PrivateMediaErasures",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaPurges_AcademyId_AssetId",
                table: "PrivateMediaPurges",
                columns: new[] { "AcademyId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaPurges_AcademyId_ErasureId_AssetId",
                table: "PrivateMediaPurges",
                columns: new[] { "AcademyId", "ErasureId", "AssetId" });
            AddLifecycleGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveLifecycleGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "PrivateMediaDownloadBudgets");

            migrationBuilder.DropTable(
                name: "PrivateMediaPurges");

            migrationBuilder.DropTable(
                name: "PrivateMediaErasures");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaRetention_Snapshot",
                table: "PrivateAssets");

            migrationBuilder.DropColumn(
                name: "DeleteOnExpiry",
                table: "PrivateAssets");

            migrationBuilder.DropColumn(
                name: "RetainUntilUtc",
                table: "PrivateAssets");
        }
    }
}
