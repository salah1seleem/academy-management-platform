using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2AccessVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonKind = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SportEnrollmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuardianId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompensationBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccompanyingVisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Basis = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessEntitlements", x => x.Id);
                    table.UniqueConstraint("AK_AccessEntitlements_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_Access_Kind", "\"ValidFromUtc\"<\"ValidToUtc\" AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"SportEnrollmentId\" IS NOT NULL AND \"GuardianId\" IS NULL AND \"AccompanyingVisitId\" IS NULL AND ((\"SubscriptionPeriodId\" IS NOT NULL)::int+(\"CompensationBookingId\" IS NOT NULL)::int)=1) OR (\"PersonKind\"=2 AND \"PlayerId\" IS NULL AND \"SportEnrollmentId\" IS NULL AND \"GuardianId\" IS NOT NULL AND \"AccompanyingVisitId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_CompensationBookings_AcademyId_Compensat~",
                        columns: x => new { x.AcademyId, x.CompensationBookingId },
                        principalTable: "CompensationBookings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_FacilityResources_AcademyId_ResourceId",
                        columns: x => new { x.AcademyId, x.ResourceId },
                        principalTable: "FacilityResources",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_GuardianProfiles_AcademyId_GuardianId",
                        columns: x => new { x.AcademyId, x.GuardianId },
                        principalTable: "GuardianProfiles",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_Players_AcademyId_PlayerId",
                        columns: x => new { x.AcademyId, x.PlayerId },
                        principalTable: "Players",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_ResourceBookings_AcademyId_ResourceBooki~",
                        columns: x => new { x.AcademyId, x.ResourceBookingId },
                        principalTable: "ResourceBookings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_SportEnrollments_AcademyId_SportEnrollme~",
                        columns: x => new { x.AcademyId, x.SportEnrollmentId },
                        principalTable: "SportEnrollments",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_SubscriptionPeriods_AcademyId_Subscripti~",
                        columns: x => new { x.AcademyId, x.SubscriptionPeriodId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessEntitlements_Users_AuthorizedByUserId",
                        column: x => x.AuthorizedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntryVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessEntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonKind = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuardianId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EnteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnteredByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntryHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExitedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExitKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExitHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryVisits", x => x.Id);
                    table.UniqueConstraint("AK_EntryVisits_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_Visit_Exit", "((\"ExitedAtUtc\" IS NULL AND \"ExitedByUserId\" IS NULL AND \"ExitKey\" IS NULL AND \"ExitHash\" IS NULL) OR (\"ExitedAtUtc\" IS NOT NULL AND \"ExitedAtUtc\">=\"EnteredAtUtc\" AND \"ExitedByUserId\" IS NOT NULL AND \"ExitKey\" IS NOT NULL AND \"ExitHash\" IS NOT NULL)) AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"GuardianId\" IS NULL) OR (\"PersonKind\"=2 AND \"GuardianId\" IS NOT NULL AND \"PlayerId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_EntryVisits_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryVisits_AccessEntitlements_AcademyId_AccessEntitlementId",
                        columns: x => new { x.AcademyId, x.AccessEntitlementId },
                        principalTable: "AccessEntitlements",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryVisits_ResourceBookings_AcademyId_ResourceBookingId",
                        columns: x => new { x.AcademyId, x.ResourceBookingId },
                        principalTable: "ResourceBookings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryVisits_Users_EnteredByUserId",
                        column: x => x.EnteredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryVisits_Users_ExitedByUserId",
                        column: x => x.ExitedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_AccompanyingVisitId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "AccompanyingVisitId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_CompensationBookingId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "CompensationBookingId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_GuardianId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "GuardianId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_PlayerId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_ResourceBookingId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "ResourceBookingId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_ResourceId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_SportEnrollmentId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "SportEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AcademyId_SubscriptionPeriodId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "SubscriptionPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEntitlements_AuthorizedByUserId",
                table: "AccessEntitlements",
                column: "AuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_AcademyId_AccessEntitlementId",
                table: "EntryVisits",
                columns: new[] { "AcademyId", "AccessEntitlementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_AcademyId_EnteredByUserId_EntryKey",
                table: "EntryVisits",
                columns: new[] { "AcademyId", "EnteredByUserId", "EntryKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_AcademyId_ExitedByUserId_ExitKey",
                table: "EntryVisits",
                columns: new[] { "AcademyId", "ExitedByUserId", "ExitKey" },
                unique: true,
                filter: "\"ExitKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_AcademyId_GuardianId",
                table: "EntryVisits",
                columns: new[] { "AcademyId", "GuardianId" },
                unique: true,
                filter: "\"ExitedAtUtc\" IS NULL AND \"GuardianId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_AcademyId_PlayerId",
                table: "EntryVisits",
                columns: new[] { "AcademyId", "PlayerId" },
                unique: true,
                filter: "\"ExitedAtUtc\" IS NULL AND \"PlayerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_AcademyId_ResourceBookingId",
                table: "EntryVisits",
                columns: new[] { "AcademyId", "ResourceBookingId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_EnteredByUserId",
                table: "EntryVisits",
                column: "EnteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryVisits_ExitedByUserId",
                table: "EntryVisits",
                column: "ExitedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessEntitlements_EntryVisits_AcademyId_AccompanyingVisitId",
                table: "AccessEntitlements",
                columns: new[] { "AcademyId", "AccompanyingVisitId" },
                principalTable: "EntryVisits",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION academity_access_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER access_guard BEFORE INSERT OR UPDATE OR DELETE ON "AccessEntitlements" FOR EACH ROW EXECUTE FUNCTION academity_access_guard();
                CREATE FUNCTION academity_visit_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Visit history cannot be deleted' USING ERRCODE='23514'; END IF;
                  IF TG_OP='UPDATE' THEN
                    IF OLD."ExitedAtUtc" IS NOT NULL OR NEW."ExitedAtUtc" IS NULL OR (to_jsonb(NEW)-ARRAY['ExitedAtUtc','ExitedByUserId','ExitKey','ExitHash','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['ExitedAtUtc','ExitedByUserId','ExitKey','ExitHash','UpdatedAtUtc']) THEN RAISE EXCEPTION 'Only first exit may be recorded' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF NEW."ExitedAtUtc" IS NOT NULL OR NOT EXISTS (SELECT 1 FROM "AccessEntitlements" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."AccessEntitlementId" AND e."ResourceBookingId"=NEW."ResourceBookingId" AND e."ResourceId"=NEW."ResourceId" AND e."BranchId"=NEW."BranchId" AND e."PersonKind"=NEW."PersonKind" AND e."PlayerId" IS NOT DISTINCT FROM NEW."PlayerId" AND e."GuardianId" IS NOT DISTINCT FROM NEW."GuardianId" AND e."BookingDate"=NEW."BookingDate" AND e."AuthorizedByUserId"=NEW."EnteredByUserId" AND NEW."EnteredAtUtc">=e."ValidFromUtc" AND NEW."EnteredAtUtc"<e."ValidToUtc") THEN RAISE EXCEPTION 'Visit requires matching valid access' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER visit_guard BEFORE INSERT OR UPDATE OR DELETE ON "EntryVisits" FOR EACH ROW EXECUTE FUNCTION academity_visit_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER visit_guard ON \"EntryVisits\"; DROP FUNCTION academity_visit_guard(); DROP TRIGGER access_guard ON \"AccessEntitlements\"; DROP FUNCTION academity_access_guard();");
            migrationBuilder.DropForeignKey(
                name: "FK_AccessEntitlements_EntryVisits_AcademyId_AccompanyingVisitId",
                table: "AccessEntitlements");

            migrationBuilder.DropTable(
                name: "EntryVisits");

            migrationBuilder.DropTable(
                name: "AccessEntitlements");
        }
    }
}
