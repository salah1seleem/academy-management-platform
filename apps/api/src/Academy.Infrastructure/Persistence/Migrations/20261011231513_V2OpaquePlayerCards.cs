using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2OpaquePlayerCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedToken = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousVersion = table.Column<long>(type: "bigint", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCards", x => x.Id);
                    table.UniqueConstraint("AK_PlayerCards_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_PlayerCard_State", "length(\"TokenHash\")=64 AND \"ExpiresAtUtc\">\"CreatedAtUtc\" AND ((\"IsActive\" AND \"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_PlayerCards_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerCards_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerCards_Users_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerCards_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCards_AcademyId_IssuedByUserId_RequestKey",
                table: "PlayerCards",
                columns: new[] { "AcademyId", "IssuedByUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCards_AcademyId_PlayerId",
                table: "PlayerCards",
                columns: new[] { "AcademyId", "PlayerId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCards_IssuedByUserId",
                table: "PlayerCards",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCards_RevokedByUserId",
                table: "PlayerCards",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCards_TokenHash",
                table: "PlayerCards",
                column: "TokenHash",
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_player_card_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Player card history cannot be deleted' USING ERRCODE='23514'; END IF;
                  IF NOT OLD."IsActive" OR NEW."IsActive"
                    OR (NEW."AcademyId",NEW."PlayerId",NEW."TokenHash",NEW."ProtectedToken",NEW."ExpiresAtUtc",NEW."IssuedByUserId",NEW."RequestKey",NEW."PreviousVersion",NEW."CreatedAtUtc")
                    IS DISTINCT FROM (OLD."AcademyId",OLD."PlayerId",OLD."TokenHash",OLD."ProtectedToken",OLD."ExpiresAtUtc",OLD."IssuedByUserId",OLD."RequestKey",OLD."PreviousVersion",OLD."CreatedAtUtc")
                  THEN RAISE EXCEPTION 'Player card may only be revoked' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER player_card_immutable BEFORE UPDATE OR DELETE ON "PlayerCards" FOR EACH ROW EXECUTE FUNCTION academy_player_card_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCards");
            migrationBuilder.Sql("DROP FUNCTION academy_player_card_immutable();");
        }
    }
}
