using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2PostedReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceivableDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PlanPriceSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivableDocuments", x => x.Id);
                    table.UniqueConstraint("AK_ReceivableDocuments_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ReceivableDocument_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ReceivableDocuments_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableDocuments_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableDocuments_SportEnrollments_AcademyId_SportEnrollm~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableDocuments_SubscriptionPlans_AcademyId_PlanId",
                        columns: x => new { x.AcademyId, x.PlanId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableDocuments_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivableMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivableMovements", x => x.Id);
                    table.UniqueConstraint("AK_ReceivableMovements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ReceivableMovement_Collection", "(\"Kind\" = 'PaymentAllocation' AND \"CollectionId\" IS NOT NULL) OR (\"Kind\" = 'CreditNote' AND \"CollectionId\" IS NULL)");
                    table.CheckConstraint("CK_ReceivableMovement_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ReceivableMovements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableMovements_Collections_AcademyId_CollectionId",
                        columns: x => new { x.AcademyId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableMovements_ReceivableDocuments_AcademyId_DocumentId",
                        columns: x => new { x.AcademyId, x.DocumentId },
                        principalTable: "ReceivableDocuments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableMovements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_AcademyId_BranchId_DueDate",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "BranchId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_AcademyId_PlanId",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_AcademyId_SourceKey",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_AcademyId_SportEnrollmentId",
                table: "ReceivableDocuments",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableDocuments_ActorUserId",
                table: "ReceivableDocuments",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableMovements_AcademyId_CollectionId",
                table: "ReceivableMovements",
                columns: new[] { "AcademyId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableMovements_AcademyId_DocumentId",
                table: "ReceivableMovements",
                columns: new[] { "AcademyId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableMovements_AcademyId_SourceKey",
                table: "ReceivableMovements",
                columns: new[] { "AcademyId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableMovements_ActorUserId",
                table: "ReceivableMovements",
                column: "ActorUserId");

            migrationBuilder.Sql("""
                CREATE FUNCTION academy_posted_receivable_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN
                    RAISE EXCEPTION 'Posted receivables are immutable; append a correcting movement'
                        USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER posted_receivable_document_immutable
                    BEFORE UPDATE OR DELETE ON "ReceivableDocuments"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                CREATE TRIGGER posted_receivable_movement_immutable
                    BEFORE UPDATE OR DELETE ON "ReceivableMovements"
                    FOR EACH ROW EXECUTE FUNCTION academy_posted_receivable_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceivableMovements");

            migrationBuilder.DropTable(
                name: "ReceivableDocuments");

            migrationBuilder.Sql("DROP FUNCTION academy_posted_receivable_immutable();");
        }
    }
}
