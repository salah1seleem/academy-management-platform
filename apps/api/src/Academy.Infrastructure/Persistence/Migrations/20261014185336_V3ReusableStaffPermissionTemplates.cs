using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ReusableStaffPermissionTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PermissionTemplateCustomized",
                table: "AcademyMemberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PermissionTemplateId",
                table: "AcademyMemberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PermissionTemplateRevision",
                table: "AcademyMemberships",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffRoleTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ActionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRoleTemplates", x => x.Id);
                    table.UniqueConstraint("AK_StaffRoleTemplates_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StaffRoleTemplates_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffRoleTemplateAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    After = table.Column<string>(type: "jsonb", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRoleTemplateAudits", x => x.Id);
                    table.UniqueConstraint("AK_StaffRoleTemplateAudits_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StaffRoleTemplateAudits_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffRoleTemplateAudits_StaffRoleTemplates_AcademyId_Templa~",
                        columns: x => new { x.AcademyId, x.TemplateId },
                        principalTable: "StaffRoleTemplates",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffRoleTemplateAudits_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyMemberships_AcademyId_PermissionTemplateId",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "PermissionTemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoleTemplateAudits_AcademyId_TemplateId",
                table: "StaffRoleTemplateAudits",
                columns: new[] { "AcademyId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoleTemplateAudits_ActorUserId",
                table: "StaffRoleTemplateAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoleTemplates_AcademyId_NormalizedName",
                table: "StaffRoleTemplates",
                columns: new[] { "AcademyId", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AcademyMemberships_StaffRoleTemplates_AcademyId_PermissionT~",
                table: "AcademyMemberships",
                columns: new[] { "AcademyId", "PermissionTemplateId" },
                principalTable: "StaffRoleTemplates",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_role_template_audit_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Role template audit is immutable' USING ERRCODE='23514'; END;
                $$;
                CREATE TRIGGER role_template_audit_immutable BEFORE UPDATE OR DELETE ON "StaffRoleTemplateAudits"
                    FOR EACH ROW EXECUTE FUNCTION academy_role_template_audit_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcademyMemberships_StaffRoleTemplates_AcademyId_PermissionT~",
                table: "AcademyMemberships");

            migrationBuilder.DropTable(
                name: "StaffRoleTemplateAudits");
            migrationBuilder.Sql("DROP FUNCTION academy_role_template_audit_immutable();");

            migrationBuilder.DropTable(
                name: "StaffRoleTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AcademyMemberships_AcademyId_PermissionTemplateId",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "PermissionTemplateCustomized",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "PermissionTemplateId",
                table: "AcademyMemberships");

            migrationBuilder.DropColumn(
                name: "PermissionTemplateRevision",
                table: "AcademyMemberships");
        }
    }
}
