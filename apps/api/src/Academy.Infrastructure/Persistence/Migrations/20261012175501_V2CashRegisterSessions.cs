using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2CashRegisterSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashRegisters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRegisters", x => x.Id);
                    table.UniqueConstraint("AK_CashRegisters_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_CashRegisters_AcademyId_Id_BranchId", x => new { x.AcademyId, x.Id, x.BranchId });
                    table.ForeignKey(
                        name: "FK_CashRegisters_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashRegisters_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashRegisters_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashierAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashierAssignments", x => x.Id);
                    table.UniqueConstraint("AK_CashierAssignments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CashierAssignments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashierAssignments_AcademyMemberships_AcademyId_MembershipId",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashierAssignments_CashRegisters_AcademyId_RegisterId_Branc~",
                        columns: x => new { x.AcademyId, x.RegisterId, x.BranchId },
                        principalTable: "CashRegisters",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OpenKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OpenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosingReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CloseKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CloseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessions", x => x.Id);
                    table.UniqueConstraint("AK_CashSessions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_CashSessions_AcademyId_Id_BranchId", x => new { x.AcademyId, x.Id, x.BranchId });
                    table.CheckConstraint("CK_CashSession_Close", "(\"ClosedAtUtc\" IS NULL AND \"ClosedByUserId\" IS NULL AND \"CountedAmount\" IS NULL AND \"ExpectedAmount\" IS NULL AND \"CloseKey\" IS NULL AND \"CloseHash\" IS NULL) OR (\"ClosedAtUtc\" IS NOT NULL AND \"ClosedByUserId\" IS NOT NULL AND \"CountedAmount\" IS NOT NULL AND \"ExpectedAmount\" IS NOT NULL AND \"CloseKey\" IS NOT NULL AND \"CloseHash\" IS NOT NULL)");
                    table.CheckConstraint("CK_CashSession_Money", "\"OpeningAmount\" >= 0 AND (\"CountedAmount\" IS NULL OR \"CountedAmount\" >= 0)");
                    table.ForeignKey(
                        name: "FK_CashSessions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSessions_AcademyMemberships_AcademyId_OpenedByMembershi~",
                        columns: x => new { x.AcademyId, x.OpenedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSessions_CashRegisters_AcademyId_RegisterId_BranchId",
                        columns: x => new { x.AcademyId, x.RegisterId, x.BranchId },
                        principalTable: "CashRegisters",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSessions_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransfers", x => x.Id);
                    table.UniqueConstraint("AK_CashTransfers_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CashTransfer_Valid", "\"Amount\" > 0 AND \"FromSessionId\" <> \"ToSessionId\"");
                    table.ForeignKey(
                        name: "FK_CashTransfers_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransfers_CashSessions_AcademyId_FromSessionId_BranchId",
                        columns: x => new { x.AcademyId, x.FromSessionId, x.BranchId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransfers_CashSessions_AcademyId_ToSessionId_ToBranchId",
                        columns: x => new { x.AcademyId, x.ToSessionId, x.ToBranchId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransfers_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.UniqueConstraint("AK_CashMovements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CashMovement_Sign", "(\"Kind\" = 'TransferIn' AND \"Amount\" > 0) OR (\"Kind\" = 'TransferOut' AND \"Amount\" < 0)");
                    table.ForeignKey(
                        name: "FK_CashMovements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashSessions_AcademyId_SessionId_BranchId",
                        columns: x => new { x.AcademyId, x.SessionId, x.BranchId },
                        principalTable: "CashSessions",
                        principalColumns: new[] { "AcademyId", "Id", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashTransfers_AcademyId_TransferId",
                        columns: x => new { x.AcademyId, x.TransferId },
                        principalTable: "CashTransfers",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashierAssignments_AcademyId_MembershipId",
                table: "CashierAssignments",
                columns: new[] { "AcademyId", "MembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashierAssignments_AcademyId_RegisterId_BranchId",
                table: "CashierAssignments",
                columns: new[] { "AcademyId", "RegisterId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashierAssignments_AcademyId_RegisterId_MembershipId",
                table: "CashierAssignments",
                columns: new[] { "AcademyId", "RegisterId", "MembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_AcademyId_SessionId",
                table: "CashMovements",
                columns: new[] { "AcademyId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_AcademyId_SessionId_BranchId",
                table: "CashMovements",
                columns: new[] { "AcademyId", "SessionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_AcademyId_TransferId_Kind",
                table: "CashMovements",
                columns: new[] { "AcademyId", "TransferId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ActorUserId",
                table: "CashMovements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_AcademyId_BranchId_Name",
                table: "CashRegisters",
                columns: new[] { "AcademyId", "BranchId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_AcademyId_RequestKey",
                table: "CashRegisters",
                columns: new[] { "AcademyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_CreatedByUserId",
                table: "CashRegisters",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_AcademyId_OpenedByMembershipId",
                table: "CashSessions",
                columns: new[] { "AcademyId", "OpenedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_AcademyId_OpenKey",
                table: "CashSessions",
                columns: new[] { "AcademyId", "OpenKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_AcademyId_RegisterId",
                table: "CashSessions",
                columns: new[] { "AcademyId", "RegisterId" },
                unique: true,
                filter: "\"ClosedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_AcademyId_RegisterId_BranchId",
                table: "CashSessions",
                columns: new[] { "AcademyId", "RegisterId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_ClosedByUserId",
                table: "CashSessions",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_AcademyId_FromSessionId_BranchId",
                table: "CashTransfers",
                columns: new[] { "AcademyId", "FromSessionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_AcademyId_RequestKey",
                table: "CashTransfers",
                columns: new[] { "AcademyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_AcademyId_ToSessionId_ToBranchId",
                table: "CashTransfers",
                columns: new[] { "AcademyId", "ToSessionId", "ToBranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_ActorUserId",
                table: "CashTransfers",
                column: "ActorUserId");

            migrationBuilder.Sql("""
                CREATE TRIGGER cash_register_immutable BEFORE UPDATE OR DELETE ON "CashRegisters"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE TRIGGER cash_transfer_immutable BEFORE UPDATE OR DELETE ON "CashTransfers"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE TRIGGER cash_movement_immutable BEFORE UPDATE OR DELETE ON "CashMovements"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE FUNCTION academy_cash_session_close_only() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD."AcademyId" = '30000000-0000-0000-0000-000000000003'::uuid
                          AND current_setting('academy.reset_demo_tenant',true) = OLD."AcademyId"::text
                          AND EXISTS (SELECT 1 FROM "Academies" a WHERE a."Id"=OLD."AcademyId" AND a."Slug"='football-demo' AND a."EnglishName"='Football Demo Dataset v1') THEN RETURN OLD; END IF;
                    ELSIF OLD."ClosedAtUtc" IS NULL AND NEW."ClosedAtUtc" IS NOT NULL
                      AND (to_jsonb(NEW) - ARRAY['ClosedAtUtc','ClosedByUserId','CountedAmount','ExpectedAmount','ClosingReason','CloseKey','CloseHash','UpdatedAtUtc'])
                        = (to_jsonb(OLD) - ARRAY['ClosedAtUtc','ClosedByUserId','CountedAmount','ExpectedAmount','ClosingReason','CloseKey','CloseHash','UpdatedAtUtc']) THEN RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'Cash session is immutable; close once with reconciliation' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER cash_session_close_only BEFORE UPDATE OR DELETE ON "CashSessions"
                    FOR EACH ROW EXECUTE FUNCTION academy_cash_session_close_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashierAssignments");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "CashTransfers");

            migrationBuilder.DropTable(
                name: "CashSessions");

            migrationBuilder.DropTable(
                name: "CashRegisters");
            migrationBuilder.Sql("DROP FUNCTION academy_cash_session_close_only();");
        }
    }
}
