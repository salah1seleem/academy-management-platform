using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2ResourceObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    ExpectedSnapshot = table.Column<int>(type: "integer", nullable: false),
                    RecordedPresentSnapshot = table.Column<int>(type: "integer", nullable: true),
                    RecordedTotalSnapshot = table.Column<int>(type: "integer", nullable: true),
                    ExpectedBasis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutsideBookedHours = table.Column<bool>(type: "boolean", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceObservations", x => x.Id);
                    table.UniqueConstraint("AK_ResourceObservations_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_Observation_Counts", "\"Count\" BETWEEN 0 AND 10000 AND \"Sequence\">0 AND \"ExpectedSnapshot\">=0 AND (\"RecordedPresentSnapshot\" IS NULL OR \"RecordedPresentSnapshot\">=0) AND (\"RecordedTotalSnapshot\" IS NULL OR \"RecordedTotalSnapshot\">=\"RecordedPresentSnapshot\") AND \"ExpectedBasis\" IN ('Roster','ReservedSeats') AND \"Source\" IN ('Manual','DemoSeed')");
                    table.ForeignKey(
                        name: "FK_ResourceObservations_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceObservations_FacilityResources_AcademyId_ResourceId",
                        columns: x => new { x.AcademyId, x.ResourceId },
                        principalTable: "FacilityResources",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceObservations_ResourceBookings_AcademyId_ResourceBoo~",
                        columns: x => new { x.AcademyId, x.ResourceBookingId },
                        principalTable: "ResourceBookings",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceObservations_TrainingSessions_AcademyId_TrainingSes~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceObservations_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceObservations_AcademyId_ActorUserId_RequestKey",
                table: "ResourceObservations",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceObservations_AcademyId_ResourceBookingId_Sequence",
                table: "ResourceObservations",
                columns: new[] { "AcademyId", "ResourceBookingId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceObservations_AcademyId_ResourceId",
                table: "ResourceObservations",
                columns: new[] { "AcademyId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceObservations_AcademyId_TrainingSessionId",
                table: "ResourceObservations",
                columns: new[] { "AcademyId", "TrainingSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceObservations_ActorUserId",
                table: "ResourceObservations",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                CREATE FUNCTION academy_observation_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Observations are append-only' USING ERRCODE='23514'; END IF;
                  IF NOT EXISTS(SELECT 1 FROM "ResourceBookings" b WHERE b."AcademyId"=NEW."AcademyId" AND b."Id"=NEW."ResourceBookingId" AND b."ResourceId"=NEW."ResourceId" AND b."BranchId"=NEW."BranchId" AND b."Date"=NEW."BookingDate" AND b."TrainingSessionId" IS NOT DISTINCT FROM NEW."TrainingSessionId")
                    OR (NEW."TrainingSessionId" IS NULL AND (NEW."ExpectedBasis"<>'ReservedSeats' OR NEW."RecordedPresentSnapshot" IS NOT NULL OR NEW."RecordedTotalSnapshot" IS NOT NULL))
                    OR (NEW."TrainingSessionId" IS NOT NULL AND (NEW."ExpectedBasis"<>'Roster' OR NEW."RecordedPresentSnapshot" IS NULL OR NEW."RecordedTotalSnapshot" IS NULL))
                  THEN RAISE EXCEPTION 'Observation source or count basis does not match booking' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER observation_guard BEFORE INSERT OR UPDATE OR DELETE ON "ResourceObservations" FOR EACH ROW EXECUTE FUNCTION academy_observation_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceObservations");
            migrationBuilder.Sql("DROP FUNCTION academy_observation_guard();");
        }
    }
}
