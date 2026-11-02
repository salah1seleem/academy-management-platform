using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2FamilyRenewalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyRenewalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ProofAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyRenewalRequests", x => x.Id);
                    table.UniqueConstraint("AK_FamilyRenewalRequests_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_FamilyRenewalRequests_Amounts", "\"LineCount\" BETWEEN 1 AND 20 AND \"OriginalAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"TotalAmount\" >= 0 AND \"TotalAmount\" = \"OriginalAmount\" - \"DiscountAmount\" AND \"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_FamilyRenewalRequests_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalRequests_GuardianProfiles_AcademyId_GuardianId",
                        columns: x => new { x.AcademyId, x.GuardianId },
                        principalTable: "GuardianProfiles",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalRequests_PrivateAssets_AcademyId_ProofAssetId",
                        columns: x => new { x.AcademyId, x.ProofAssetId },
                        principalTable: "PrivateAssets",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalRequests_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FamilyRenewalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    FinancialSnapshotJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PlanTermsJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResultPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyRenewalLines", x => x.Id);
                    table.UniqueConstraint("AK_FamilyRenewalLines_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_FamilyRenewalLines_Amounts", "\"OriginalAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalAmount\" >= 0 AND \"FinalAmount\" = \"OriginalAmount\" - \"DiscountAmount\"");
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_FamilyRenewalRequests_AcademyId_RequestId",
                        columns: x => new { x.AcademyId, x.RequestId },
                        principalTable: "FamilyRenewalRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_PaymentRequests_AcademyId_ResultPaymentId",
                        columns: x => new { x.AcademyId, x.ResultPaymentId },
                        principalTable: "PaymentRequests",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_SportEnrollments_AcademyId_SportEnrollme~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyRenewalLines_SubscriptionPlans_AcademyId_Subscription~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_BranchId",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_IsActive",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_PlayerId",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_RequestId_SportEnrollmentId",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "RequestId", "SportEnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_ResultPaymentId",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "ResultPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_SportEnrollmentId",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalLines_AcademyId_SubscriptionPlanId",
                table: "FamilyRenewalLines",
                columns: new[] { "AcademyId", "SubscriptionPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_GuardianId",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "GuardianId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_IsActive",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_ProofAssetId",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "ProofAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_RequestedByUserId_Idempoten~",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "RequestedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_AcademyId_Status_CreatedAtUtc",
                table: "FamilyRenewalRequests",
                columns: new[] { "AcademyId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_DecidedByUserId",
                table: "FamilyRenewalRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRenewalRequests_RequestedByUserId",
                table: "FamilyRenewalRequests",
                column: "RequestedByUserId");

            migrationBuilder.Sql("""
                CREATE FUNCTION academy_family_request_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Family request cannot be deleted'; END IF;
                  IF TG_OP = 'INSERT' THEN
                    IF NEW."Status" <> 'Pending' OR NEW."Revision" <> 1 OR NEW."ProofAssetId" IS NOT NULL OR NEW."DecidedByUserId" IS NOT NULL
                      OR NEW."DecidedAtUtc" IS NOT NULL OR NEW."DecisionReason" IS NOT NULL OR NOT EXISTS
                      (SELECT 1 FROM "GuardianProfiles" g WHERE g."AcademyId"=NEW."AcademyId" AND g."Id"=NEW."GuardianId" AND g."UserId"=NEW."RequestedByUserId" AND g."IsActive")
                    THEN RAISE EXCEPTION 'Invalid family request source'; END IF;
                  ELSE
                    IF OLD."Status" <> 'Pending' OR NEW."Revision" <> OLD."Revision"+1 OR
                      (to_jsonb(NEW)-ARRAY['Status','Revision','ProofAssetId','DecidedByUserId','DecidedAtUtc','DecisionReason','UpdatedAtUtc']) IS DISTINCT FROM
                      (to_jsonb(OLD)-ARRAY['Status','Revision','ProofAssetId','DecidedByUserId','DecidedAtUtc','DecisionReason','UpdatedAtUtc']) OR
                      (OLD."ProofAssetId" IS NOT NULL AND NEW."ProofAssetId" IS DISTINCT FROM OLD."ProofAssetId")
                    THEN RAISE EXCEPTION 'Immutable family request source or terminal decision'; END IF;
                  END IF;
                  IF NEW."Status" NOT IN ('Pending','Approved','Rejected','Cancelled') OR NEW."Currency" !~ '^[A-Z]{3}$'
                    OR NEW."QuoteHash" !~ '^[A-F0-9]{64}$' OR NEW."RequestHash" !~ '^[A-F0-9]{64}$' OR NEW."TotalAmount">999999999
                    OR (NEW."Status"='Pending' AND (NEW."DecidedByUserId" IS NOT NULL OR NEW."DecidedAtUtc" IS NOT NULL OR NEW."DecisionReason" IS NOT NULL))
                    OR (NEW."Status"<>'Pending' AND (NEW."DecidedByUserId" IS NULL OR NEW."DecidedAtUtc" IS NULL))
                    OR (NEW."Status"='Cancelled' AND NEW."DecidedByUserId"<>NEW."RequestedByUserId")
                    OR (NEW."Status"='Approved' AND NEW."ProofAssetId" IS NULL)
                  THEN RAISE EXCEPTION 'Invalid family decision'; END IF;
                  IF NEW."ProofAssetId" IS NOT NULL AND (TG_OP='INSERT' OR NEW."ProofAssetId" IS DISTINCT FROM OLD."ProofAssetId" OR NEW."Status"='Approved') AND NOT EXISTS
                    (SELECT 1 FROM "PrivateAssets" a WHERE a."AcademyId"=NEW."AcademyId" AND a."Id"=NEW."ProofAssetId" AND a."Purpose"='Proof' AND a."UploadedByUserId"=NEW."RequestedByUserId" AND a."IsActive")
                  THEN RAISE EXCEPTION 'Invalid family proof ownership'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER family_request_guard BEFORE INSERT OR UPDATE OR DELETE ON "FamilyRenewalRequests" FOR EACH ROW EXECUTE FUNCTION academy_family_request_guard();

                CREATE FUNCTION academy_family_line_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE r "FamilyRenewalRequests"%ROWTYPE; e "SportEnrollments"%ROWTYPE; p "SubscriptionPlans"%ROWTYPE; f jsonb; t jsonb;
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Family line cannot be deleted'; END IF;
                  IF TG_OP='UPDATE' THEN
                    IF OLD."ResultPaymentId" IS NOT NULL OR NEW."ResultPaymentId" IS NULL OR
                      (to_jsonb(NEW)-ARRAY['ResultPaymentId','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['ResultPaymentId','UpdatedAtUtc'])
                    THEN RAISE EXCEPTION 'Immutable family line'; END IF;
                    RETURN NEW;
                  END IF;
                  SELECT * INTO r FROM "FamilyRenewalRequests" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."RequestId";
                  SELECT * INTO e FROM "SportEnrollments" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."SportEnrollmentId";
                  SELECT * INTO p FROM "SubscriptionPlans" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."SubscriptionPlanId";
                  f:=NEW."FinancialSnapshotJson"::jsonb; t:=NEW."PlanTermsJson"::jsonb;
                  IF r."Status" IS DISTINCT FROM 'Pending' OR r."ProofAssetId" IS NOT NULL OR NEW."ResultPaymentId" IS NOT NULL OR e."IsActive" IS DISTINCT FROM true OR p."IsActive" IS DISTINCT FROM true OR e."PlayerId" IS DISTINCT FROM NEW."PlayerId" OR e."BranchId" IS DISTINCT FROM NEW."BranchId"
                    OR e."SportId" IS DISTINCT FROM p."SportId" OR p."Price" IS DISTINCT FROM NEW."OriginalAmount" OR p."Currency" IS DISTINCT FROM NEW."Currency"
                    OR (f->>'OriginalAmount')::numeric IS DISTINCT FROM NEW."OriginalAmount" OR (f->>'DiscountAmount')::numeric IS DISTINCT FROM NEW."DiscountAmount" OR (f->>'FinalAmount')::numeric IS DISTINCT FROM NEW."FinalAmount"
                    OR (t->>'Name') IS DISTINCT FROM p."ArabicName" OR (t->>'Revision')::int IS DISTINCT FROM p."Revision" OR (t->>'ServiceKind')::int IS DISTINCT FROM 1
                    OR (t->>'Days')::int IS DISTINCT FROM p."DurationDays" OR (t->>'Months')::int IS DISTINCT FROM p."DurationMonths" OR (t->>'Sessions')::int IS DISTINCT FROM p."SessionCount"
                    OR (t->>'Type')::int IS DISTINCT FROM (CASE p."PlanType" WHEN 'Duration' THEN 1 WHEN 'Sessions' THEN 2 WHEN 'Combined' THEN 3 END)
                    OR (t->>'Format')::int IS DISTINCT FROM (CASE p."TrainingFormat" WHEN 'Group' THEN 1 WHEN 'Private' THEN 2 WHEN 'SemiPrivate' THEN 3 WHEN 'Season' THEN 4 END)
                    OR NOT EXISTS (SELECT 1 FROM "GuardianPlayerLinks" l WHERE l."AcademyId"=NEW."AcademyId" AND l."GuardianId"=r."GuardianId" AND l."PlayerId"=NEW."PlayerId" AND l."IsActive")
                  THEN RAISE EXCEPTION 'Invalid family line source'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER family_line_guard BEFORE INSERT OR UPDATE OR DELETE ON "FamilyRenewalLines" FOR EACH ROW EXECUTE FUNCTION academy_family_line_guard();

                CREATE FUNCTION academy_family_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE rid uuid; r "FamilyRenewalRequests"%ROWTYPE; n int; base numeric; discount numeric; total numeric;
                BEGIN
                  rid:=(CASE WHEN TG_TABLE_NAME='FamilyRenewalRequests' THEN to_jsonb(NEW)->>'Id' ELSE to_jsonb(NEW)->>'RequestId' END)::uuid;
                  SELECT * INTO r FROM "FamilyRenewalRequests" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=rid;
                  SELECT count(*),sum("OriginalAmount"),sum("DiscountAmount"),sum("FinalAmount") INTO n,base,discount,total FROM "FamilyRenewalLines" WHERE "AcademyId"=r."AcademyId" AND "RequestId"=rid;
                  IF n IS DISTINCT FROM r."LineCount" OR base IS DISTINCT FROM r."OriginalAmount" OR discount IS DISTINCT FROM r."DiscountAmount" OR total IS DISTINCT FROM r."TotalAmount"
                    OR EXISTS (SELECT 1 FROM "FamilyRenewalLines" l WHERE l."AcademyId"=r."AcademyId" AND l."RequestId"=rid AND (l."Currency"<>r."Currency" OR (r."Status"='Approved') IS DISTINCT FROM (l."ResultPaymentId" IS NOT NULL)))
                  THEN RAISE EXCEPTION 'Family request must be complete and atomic'; END IF;
                  IF r."Status"='Approved' AND EXISTS (SELECT 1 FROM "FamilyRenewalLines" l WHERE l."AcademyId"=r."AcademyId" AND l."RequestId"=rid AND NOT EXISTS
                    (SELECT 1 FROM "PaymentRequests" p JOIN "RenewalRequests" q ON q."AcademyId"=p."AcademyId" AND q."Id"=p."RenewalRequestId"
                      JOIN "Collections" c ON c."AcademyId"=p."AcademyId" AND c."PaymentRequestId"=p."Id"
                      JOIN "Receipts" rc ON rc."AcademyId"=c."AcademyId" AND rc."CollectionId"=c."Id"
                      JOIN "SubscriptionPeriods" sp ON sp."AcademyId"=c."AcademyId" AND sp."CollectionId"=c."Id"
                    WHERE p."AcademyId"=r."AcademyId" AND p."Id"=l."ResultPaymentId" AND p."Status"='Confirmed' AND p."Amount"=l."FinalAmount" AND p."Currency"=l."Currency"
                      AND q."RequestedByUserId"=r."RequestedByUserId" AND q."SportEnrollmentId"=l."SportEnrollmentId" AND q."SubscriptionPlanId"=l."SubscriptionPlanId"
                      AND q."OriginalAmount"=l."OriginalAmount" AND q."DiscountAmount"=l."DiscountAmount" AND q."FinalAmount"=l."FinalAmount"
                      AND q."IdempotencyKey"='family-'||replace(r."Id"::text,'-','')||'-'||replace(l."Id"::text,'-','')))
                  THEN RAISE EXCEPTION 'Family payment allocation does not match frozen lines'; END IF;
                  RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER family_request_integrity AFTER INSERT OR UPDATE ON "FamilyRenewalRequests" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_family_integrity();
                CREATE CONSTRAINT TRIGGER family_line_integrity AFTER INSERT OR UPDATE ON "FamilyRenewalLines" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_family_integrity();

                CREATE FUNCTION academy_family_payment_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "FamilyRenewalLines" l JOIN "FamilyRenewalRequests" r ON r."AcademyId"=l."AcademyId" AND r."Id"=l."RequestId"
                    WHERE l."AcademyId"=NEW."AcademyId" AND l."SportEnrollmentId"=NEW."SportEnrollmentId" AND
                    (r."Status"='Pending' OR NEW."IdempotencyKey"='family-'||replace(r."Id"::text,'-','')||'-'||replace(l."Id"::text,'-','')) AND
                    NOT (r."Status"='Approved' AND l."ResultPaymentId"=NEW."PaymentRequestId"))
                  THEN RAISE EXCEPTION 'Family renewal cannot be collected individually'; END IF;
                  RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER family_payment_guard AFTER INSERT ON "RenewalRequests" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_family_payment_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER family_payment_guard ON \"RenewalRequests\"; DROP FUNCTION academy_family_payment_guard();");
            migrationBuilder.DropTable(
                name: "FamilyRenewalLines");

            migrationBuilder.DropTable(
                name: "FamilyRenewalRequests");
            migrationBuilder.Sql("DROP FUNCTION academy_family_integrity(); DROP FUNCTION academy_family_line_guard(); DROP FUNCTION academy_family_request_guard();");
        }
    }
}
