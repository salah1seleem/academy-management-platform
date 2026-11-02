using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2DayPassTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Access_Kind",
                table: "AccessEntitlements");

            migrationBuilder.AddColumn<Guid>(
                name: "DayPassTicketId",
                table: "AccessEntitlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DayPassTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevision = table.Column<int>(type: "integer", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProofAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayPassTickets", x => x.Id);
                    table.UniqueConstraint("AK_DayPassTickets_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_DayPass_Amount", "\"Amount\">0 AND \"PlanRevision\">0 AND \"Status\" BETWEEN 1 AND 4 AND ((\"Status\" IN (1,2) AND \"ReviewedByUserId\" IS NULL AND \"ReviewedAtUtc\" IS NULL) OR (\"Status\" IN (3,4) AND \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_DayPassTickets_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DayPassTickets_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DayPassTickets_ResourceBookings_AcademyId_ResourceBookingId",
                        columns: x => new { x.AcademyId, x.ResourceBookingId },
                        principalTable: "ResourceBookings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DayPassTickets_SubscriptionPlans_AcademyId_SubscriptionPlan~",
                        columns: x => new { x.AcademyId, x.SubscriptionPlanId },
                        principalTable: "SubscriptionPlans",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DayPassTickets_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DayPassTickets_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_DayPassTicketId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "DayPassTicketId" },
                unique: true,
                filter: "\"DayPassTicketId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Access_Kind",
                table: "AccessEntitlements",
                sql: "\"ValidFromUtc\"<\"ValidToUtc\" AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"GuardianId\" IS NULL AND \"AccompanyingVisitId\" IS NULL AND ((\"SportEnrollmentId\" IS NOT NULL AND \"DayPassTicketId\" IS NULL AND ((\"SubscriptionPeriodId\" IS NOT NULL)::int+(\"CompensationBookingId\" IS NOT NULL)::int)=1) OR (\"SportEnrollmentId\" IS NULL AND \"DayPassTicketId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))) OR (\"PersonKind\"=2 AND \"PlayerId\" IS NULL AND \"SportEnrollmentId\" IS NULL AND \"DayPassTicketId\" IS NULL AND \"GuardianId\" IS NOT NULL AND \"AccompanyingVisitId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_DayPassTickets_AcademyId_PlayerId",
                table: "DayPassTickets",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_DayPassTickets_AcademyId_RequestedByUserId_RequestKey",
                table: "DayPassTickets",
                columns: new[] { "AcademyId", "RequestedByUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayPassTickets_AcademyId_ResourceBookingId_Status",
                table: "DayPassTickets",
                columns: new[] { "AcademyId", "ResourceBookingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DayPassTickets_AcademyId_SubscriptionPlanId",
                table: "DayPassTickets",
                columns: new[] { "AcademyId", "SubscriptionPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_DayPassTickets_RequestedByUserId",
                table: "DayPassTickets",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DayPassTickets_ReviewedByUserId",
                table: "DayPassTickets",
                column: "ReviewedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessEntitlements_DayPassTickets_AcademyId_DayPassTicketId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "DayPassTicketId" },
                principalTable: "DayPassTickets",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION academity_access_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Access decision is immutable' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS (SELECT 1 FROM "ResourceBookings" b WHERE b."AcademyId"=NEW."AcademyId" AND b."Id"=NEW."ResourceBookingId" AND b."ResourceId"=NEW."ResourceId" AND b."BranchId"=NEW."BranchId" AND b."Date"=NEW."BookingDate") THEN RAISE EXCEPTION 'Access booking mismatch' USING ERRCODE='23514'; END IF;
                  IF NEW."PersonKind"=1 THEN
                    IF NEW."DayPassTicketId" IS NOT NULL THEN
                      IF NOT EXISTS (SELECT 1 FROM "DayPassTickets" t JOIN "ResourceBookings" b ON b."AcademyId"=t."AcademyId" AND b."Id"=t."ResourceBookingId" WHERE t."AcademyId"=NEW."AcademyId" AND t."Id"=NEW."DayPassTicketId" AND t."PlayerId"=NEW."PlayerId" AND t."ResourceBookingId"=NEW."ResourceBookingId" AND t."BranchId"=NEW."BranchId" AND t."Status"=3 AND b."ServiceKind"='DayPass') THEN RAISE EXCEPTION 'Access ticket mismatch' USING ERRCODE='23514'; END IF;
                    ELSE
                      IF NOT EXISTS (SELECT 1 FROM "SportEnrollments" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."SportEnrollmentId" AND e."PlayerId"=NEW."PlayerId") THEN RAISE EXCEPTION 'Access player mismatch' USING ERRCODE='23514'; END IF;
                      IF NEW."SubscriptionPeriodId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "SubscriptionPeriods" p WHERE p."AcademyId"=NEW."AcademyId" AND p."Id"=NEW."SubscriptionPeriodId" AND p."SportEnrollmentId"=NEW."SportEnrollmentId") THEN RAISE EXCEPTION 'Access subscription mismatch' USING ERRCODE='23514'; END IF;
                      IF NEW."CompensationBookingId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "CompensationBookings" c JOIN "ResourceBookings" b ON b."AcademyId"=c."AcademyId" AND b."TrainingSessionId"=c."TrainingSessionId" WHERE c."AcademyId"=NEW."AcademyId" AND c."Id"=NEW."CompensationBookingId" AND c."SportEnrollmentId"=NEW."SportEnrollmentId" AND b."Id"=NEW."ResourceBookingId") THEN RAISE EXCEPTION 'Access compensation mismatch' USING ERRCODE='23514'; END IF;
                    END IF;
                  ELSE
                    IF NOT EXISTS (SELECT 1 FROM "EntryVisits" v JOIN "GuardianPlayerLinks" l ON l."AcademyId"=v."AcademyId" AND l."PlayerId"=v."PlayerId" AND l."GuardianId"=NEW."GuardianId" AND l."IsActive" WHERE v."AcademyId"=NEW."AcademyId" AND v."Id"=NEW."AccompanyingVisitId" AND v."ResourceBookingId"=NEW."ResourceBookingId" AND v."PersonKind"=1 AND v."ExitedAtUtc" IS NULL) THEN RAISE EXCEPTION 'Companion link mismatch' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE FUNCTION academity_day_pass_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Day pass history cannot be deleted' USING ERRCODE='23514'; END IF;
                  IF TG_OP='UPDATE' THEN
                    IF (NEW."AcademyId",NEW."BranchId",NEW."ResourceBookingId",NEW."PlayerId",NEW."SubscriptionPlanId",NEW."PlanRevision",NEW."PlanName",NEW."Amount",NEW."Currency",NEW."RequestedByUserId",NEW."RequestKey",NEW."RequestHash",NEW."CreatedAtUtc") IS DISTINCT FROM (OLD."AcademyId",OLD."BranchId",OLD."ResourceBookingId",OLD."PlayerId",OLD."SubscriptionPlanId",OLD."PlanRevision",OLD."PlanName",OLD."Amount",OLD."Currency",OLD."RequestedByUserId",OLD."RequestKey",OLD."RequestHash",OLD."CreatedAtUtc") THEN RAISE EXCEPTION 'Day pass terms are immutable' USING ERRCODE='23514'; END IF;
                    IF NOT ((OLD."Status"=1 AND NEW."Status"=2 AND NEW."ProofAssetId" IS NOT NULL AND NEW."ReviewedByUserId" IS NULL) OR (OLD."Status"=2 AND NEW."Status" IN (3,4) AND NEW."ProofAssetId"=OLD."ProofAssetId" AND NEW."ReviewedByUserId" IS NOT NULL)) THEN RAISE EXCEPTION 'Invalid day pass transition' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF NEW."Status"<>1 OR NEW."ProofAssetId" IS NOT NULL THEN RAISE EXCEPTION 'Day pass must start pending proof' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF NOT EXISTS (SELECT 1 FROM "ResourceBookings" b WHERE b."AcademyId"=NEW."AcademyId" AND b."Id"=NEW."ResourceBookingId" AND b."BranchId"=NEW."BranchId" AND b."ServiceKind"='DayPass' AND b."TrainingSessionId" IS NULL) THEN RAISE EXCEPTION 'Day pass booking mismatch' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER day_pass_guard BEFORE INSERT OR UPDATE OR DELETE ON "DayPassTickets" FOR EACH ROW EXECUTE FUNCTION academity_day_pass_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS day_pass_guard ON "DayPassTickets";
                DROP FUNCTION IF EXISTS academity_day_pass_guard();
                CREATE OR REPLACE FUNCTION academity_access_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Access decision is immutable' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS (SELECT 1 FROM "ResourceBookings" b WHERE b."AcademyId"=NEW."AcademyId" AND b."Id"=NEW."ResourceBookingId" AND b."ResourceId"=NEW."ResourceId" AND b."BranchId"=NEW."BranchId" AND b."Date"=NEW."BookingDate") THEN RAISE EXCEPTION 'Access booking mismatch' USING ERRCODE='23514'; END IF;
                  IF NEW."PersonKind"=1 THEN
                    IF NOT EXISTS (SELECT 1 FROM "SportEnrollments" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."SportEnrollmentId" AND e."PlayerId"=NEW."PlayerId") THEN RAISE EXCEPTION 'Access player mismatch' USING ERRCODE='23514'; END IF;
                    IF NEW."SubscriptionPeriodId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "SubscriptionPeriods" p WHERE p."AcademyId"=NEW."AcademyId" AND p."Id"=NEW."SubscriptionPeriodId" AND p."SportEnrollmentId"=NEW."SportEnrollmentId") THEN RAISE EXCEPTION 'Access subscription mismatch' USING ERRCODE='23514'; END IF;
                    IF NEW."CompensationBookingId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "CompensationBookings" c JOIN "ResourceBookings" b ON b."AcademyId"=c."AcademyId" AND b."TrainingSessionId"=c."TrainingSessionId" WHERE c."AcademyId"=NEW."AcademyId" AND c."Id"=NEW."CompensationBookingId" AND c."SportEnrollmentId"=NEW."SportEnrollmentId" AND b."Id"=NEW."ResourceBookingId") THEN RAISE EXCEPTION 'Access compensation mismatch' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF NOT EXISTS (SELECT 1 FROM "EntryVisits" v JOIN "GuardianPlayerLinks" l ON l."AcademyId"=v."AcademyId" AND l."PlayerId"=v."PlayerId" AND l."GuardianId"=NEW."GuardianId" AND l."IsActive" WHERE v."AcademyId"=NEW."AcademyId" AND v."Id"=NEW."AccompanyingVisitId" AND v."ResourceBookingId"=NEW."ResourceBookingId" AND v."PersonKind"=1 AND v."ExitedAtUtc" IS NULL) THEN RAISE EXCEPTION 'Companion link mismatch' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_AccessEntitlements_DayPassTickets_AcademyId_DayPassTicketId",
                table: "AccessEntitlements");

            migrationBuilder.DropTable(
                name: "DayPassTickets");

            migrationBuilder.DropIndex(
                name: "IX_AccessEntitlements_AcademyId_DayPassTicketId",
                table: "AccessEntitlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Access_Kind",
                table: "AccessEntitlements");

            migrationBuilder.DropColumn(
                name: "DayPassTicketId",
                table: "AccessEntitlements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Access_Kind",
                table: "AccessEntitlements",
                sql: "\"ValidFromUtc\"<\"ValidToUtc\" AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"SportEnrollmentId\" IS NOT NULL AND \"GuardianId\" IS NULL AND \"AccompanyingVisitId\" IS NULL AND ((\"SubscriptionPeriodId\" IS NOT NULL)::int+(\"CompensationBookingId\" IS NOT NULL)::int)=1) OR (\"PersonKind\"=2 AND \"PlayerId\" IS NULL AND \"SportEnrollmentId\" IS NULL AND \"GuardianId\" IS NOT NULL AND \"AccompanyingVisitId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))");
        }
    }
}
