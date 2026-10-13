using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ReceivableCreditApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceivableCreditApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceBalanceBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TargetBalanceBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivableCreditApplications", x => x.Id);
                    table.UniqueConstraint("AK_ReceivableCreditApplications_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ReceivableCredit_Conserved", "\"SourceDocumentId\" <> \"TargetDocumentId\" AND \"Amount\" > 0 AND \"SourceBalanceBefore\" < 0 AND \"TargetBalanceBefore\" > 0 AND \"Amount\" <= -\"SourceBalanceBefore\" AND \"Amount\" <= \"TargetBalanceBefore\"");
                    table.ForeignKey(
                        name: "FK_ReceivableCreditApplications_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableCreditApplications_AcademyMemberships_AcademyId_A~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableCreditApplications_ReceivableDocuments_AcademyId_~",
                        columns: x => new { x.AcademyId, x.SourceDocumentId },
                        principalTable: "ReceivableDocuments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableCreditApplications_ReceivableDocuments_AcademyId~1",
                        columns: x => new { x.AcademyId, x.TargetDocumentId },
                        principalTable: "ReceivableDocuments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableCreditApplications_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableCreditApplications_AcademyId_ActorMembershipId",
                table: "ReceivableCreditApplications",
                columns: new[] { "AcademyId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableCreditApplications_AcademyId_ActorUserId_RequestK~",
                table: "ReceivableCreditApplications",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableCreditApplications_AcademyId_SourceDocumentId",
                table: "ReceivableCreditApplications",
                columns: new[] { "AcademyId", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableCreditApplications_AcademyId_TargetDocumentId",
                table: "ReceivableCreditApplications",
                columns: new[] { "AcademyId", "TargetDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableCreditApplications_ActorUserId",
                table: "ReceivableCreditApplications",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_receivable_balance(a uuid, d uuid) RETURNS numeric LANGUAGE sql VOLATILE AS $$
                    SELECT r."Amount"
                      - COALESCE((SELECT sum(m."Amount") FROM "ReceivableMovements" m WHERE m."AcademyId"=a AND m."DocumentId"=d),0)
                      + COALESCE((SELECT sum(c."Amount") FROM "ReceivableCreditApplications" c WHERE c."AcademyId"=a AND c."SourceDocumentId"=d),0)
                      - COALESCE((SELECT sum(c."Amount") FROM "ReceivableCreditApplications" c WHERE c."AcademyId"=a AND c."TargetDocumentId"=d),0)
                    FROM "ReceivableDocuments" r WHERE r."AcademyId"=a AND r."Id"=d
                $$;
                CREATE FUNCTION academy_receivable_credit_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE s "ReceivableDocuments"%ROWTYPE; t "ReceivableDocuments"%ROWTYPE; d uuid; sb numeric; tb numeric;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        IF TG_OP = 'DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                        RAISE EXCEPTION 'Credit applications are immutable' USING ERRCODE='23514';
                    END IF;
                    FOR d IN SELECT x FROM unnest(ARRAY[NEW."SourceDocumentId",NEW."TargetDocumentId"]) x ORDER BY x LOOP
                        PERFORM pg_advisory_xact_lock(hashtextextended('receivable-document:' || NEW."AcademyId"::text || ':' || d::text,0));
                    END LOOP;
                    SELECT * INTO s FROM "ReceivableDocuments" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."SourceDocumentId";
                    SELECT * INTO t FROM "ReceivableDocuments" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."TargetDocumentId";
                    sb := academy_receivable_balance(NEW."AcademyId", NEW."SourceDocumentId");
                    tb := academy_receivable_balance(NEW."AcademyId", NEW."TargetDocumentId");
                    IF s."Id" IS NULL OR t."Id" IS NULL OR s."PlayerId"<>t."PlayerId" OR s."Currency"<>t."Currency"
                        OR NEW."Currency"<>s."Currency" OR sb IS NULL OR tb IS NULL OR sb>=0 OR tb<=0
                        OR NEW."SourceBalanceBefore"<>sb OR NEW."TargetBalanceBefore"<>tb
                        OR NEW."Amount">-sb OR NEW."Amount">tb
                        OR length(btrim(NEW."Reason")) NOT BETWEEN 1 AND 400
                        OR length(NEW."RequestKey") NOT BETWEEN 8 AND 80 OR length(NEW."RequestHash")<>64 OR length(NEW."QuoteHash")<>64
                        OR NOT NEW."IsActive" OR NOT EXISTS (
                            SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId"
                            WHERE m."Id"=NEW."ActorMembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."ActorUserId"
                              AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive")
                    THEN RAISE EXCEPTION 'Credit source, balance or authorization mismatch' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER receivable_credit_guard BEFORE INSERT OR UPDATE OR DELETE ON "ReceivableCreditApplications"
                    FOR EACH ROW EXECUTE FUNCTION academy_receivable_credit_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM "ReceivableCreditApplications") THEN
                    RAISE EXCEPTION 'Preserve posted credit applications; downgrade refused'; END IF; END $$;
                DROP TRIGGER receivable_credit_guard ON "ReceivableCreditApplications";
                DROP FUNCTION academy_receivable_credit_guard();
                DROP FUNCTION academy_receivable_balance(uuid,uuid);
                """);
            migrationBuilder.DropTable(
                name: "ReceivableCreditApplications");
        }
    }
}
