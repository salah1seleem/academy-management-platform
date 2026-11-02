using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2DatedGroupTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_BranchId_SportId",
                table: "SportEnrollments",
                columns: new[] { "AcademyId", "Id", "BranchId", "SportId" });

            migrationBuilder.CreateTable(
                name: "EnrollmentGroupTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentGroupTransfers", x => x.Id);
                    table.UniqueConstraint("AK_EnrollmentGroupTransfers_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_GroupTransfer_DifferentGroups", "\"SourceGroupId\" <> \"TargetGroupId\"");
                    table.ForeignKey(
                        name: "FK_EnrollmentGroupTransfers_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentGroupTransfers_SportEnrollments_AcademyId_SportEn~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId, x.BranchId, x.SportId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentGroupTransfers_TrainingGroups_AcademyId_SourceGro~",
                        columns: x => new { x.AcademyId, x.SourceGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentGroupTransfers_TrainingGroups_AcademyId_TargetGro~",
                        columns: x => new { x.AcademyId, x.TargetGroupId, x.BranchId, x.SportId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentGroupTransfers_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_AcademyId_ActorUserId_RequestKey",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_AcademyId_SourceGroupId_BranchId_S~",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "SourceGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_AcademyId_SportEnrollmentId_Branch~",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "SportEnrollmentId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_AcademyId_SportEnrollmentId_Effect~",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "SportEnrollmentId", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_AcademyId_TargetGroupId_BranchId_S~",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "TargetGroupId", "BranchId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_AcademyId_TargetGroupId_EffectiveD~",
                table: "EnrollmentGroupTransfers",
                columns: new[] { "AcademyId", "TargetGroupId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentGroupTransfers_ActorUserId",
                table: "EnrollmentGroupTransfers",
                column: "ActorUserId");

            migrationBuilder.Sql("""
                CREATE FUNCTION academy_assigned_group_at(academy uuid, enrollment uuid, at_date date)
                RETURNS uuid LANGUAGE sql STABLE AS $$
                  SELECT CASE WHEN e."GroupAssignedFrom" IS NOT NULL AND at_date < e."GroupAssignedFrom" THEN NULL
                    ELSE COALESCE((SELECT t."TargetGroupId" FROM "EnrollmentGroupTransfers" t
                      WHERE t."AcademyId" = academy AND t."SportEnrollmentId" = enrollment AND t."EffectiveDate" <= at_date
                      ORDER BY t."EffectiveDate" DESC LIMIT 1), e."TrainingGroupId") END
                  FROM "SportEnrollments" e WHERE e."AcademyId" = academy AND e."Id" = enrollment
                $$;
                CREATE TRIGGER enrollment_group_transfer_immutable BEFORE UPDATE OR DELETE ON "EnrollmentGroupTransfers"
                  FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_group_anchor_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."TrainingGroupId" IS DISTINCT FROM NEW."TrainingGroupId" OR OLD."GroupAssignedFrom" IS DISTINCT FROM NEW."GroupAssignedFrom")
                    AND EXISTS (SELECT 1 FROM "EnrollmentGroupTransfers" t WHERE t."AcademyId" = OLD."AcademyId" AND t."SportEnrollmentId" = OLD."Id")
                  THEN RAISE EXCEPTION 'Dated assignment anchor is immutable'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER enrollment_group_anchor_immutable BEFORE UPDATE ON "SportEnrollments"
                  FOR EACH ROW EXECUTE FUNCTION academy_group_anchor_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS enrollment_group_anchor_immutable ON "SportEnrollments";
                DROP FUNCTION IF EXISTS academy_group_anchor_immutable();
                DROP FUNCTION IF EXISTS academy_assigned_group_at(uuid,uuid,date);
                DROP TRIGGER IF EXISTS enrollment_group_transfer_immutable ON "EnrollmentGroupTransfers";
                """);
            migrationBuilder.DropTable(
                name: "EnrollmentGroupTransfers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SportEnrollments_AcademyId_Id_BranchId_SportId",
                table: "SportEnrollments");
        }
    }
}
