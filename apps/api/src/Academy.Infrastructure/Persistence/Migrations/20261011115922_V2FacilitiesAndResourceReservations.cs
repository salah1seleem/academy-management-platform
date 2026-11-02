using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2FacilitiesAndResourceReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Facilitys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilitys", x => x.Id);
                    table.UniqueConstraint("AK_Facilitys_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Facilitys_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Facilitys_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FacilitySpaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilitySpaces", x => x.Id);
                    table.UniqueConstraint("AK_FacilitySpaces_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_FacilitySpaces_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacilitySpaces_Facilitys_AcademyId_FacilityId",
                        columns: x => new { x.AcademyId, x.FacilityId },
                        principalTable: "Facilitys",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacilitySpaces_Sports_AcademyId_SportId",
                        columns: x => new { x.AcademyId, x.SportId },
                        principalTable: "Sports",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FacilityResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilitySpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    SharedBookingsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    OperatingDays = table.Column<int[]>(type: "integer[]", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ServiceKinds = table.Column<string[]>(type: "text[]", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovalEvidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityResources", x => x.Id);
                    table.UniqueConstraint("AK_FacilityResources_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_Resource_Approval", "(\"ApprovedByUserId\" IS NULL AND \"ApprovedAtUtc\" IS NULL) OR (\"ApprovedByUserId\" IS NOT NULL AND \"ApprovedAtUtc\" IS NOT NULL AND length(\"ApprovalEvidence\") > 0)");
                    table.CheckConstraint("CK_Resource_CapacityHours", "\"Capacity\" BETWEEN 1 AND 10000 AND \"OpensAt\" < \"ClosesAt\" AND cardinality(\"OperatingDays\") BETWEEN 1 AND 7 AND \"OperatingDays\" <@ ARRAY[0,1,2,3,4,5,6]");
                    table.ForeignKey(
                        name: "FK_FacilityResources_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacilityResources_FacilitySpaces_AcademyId_FacilitySpaceId",
                        columns: x => new { x.AcademyId, x.FacilitySpaceId },
                        principalTable: "FacilitySpaces",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacilityResources_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrainingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoachMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ServiceKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReservedSeats = table.Column<int>(type: "integer", nullable: false),
                    ApprovedCapacitySnapshot = table.Column<int>(type: "integer", nullable: false),
                    Exclusive = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceBookings", x => x.Id);
                    table.UniqueConstraint("AK_ResourceBookings_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ResourceBooking_SeatsHours", "\"ReservedSeats\" BETWEEN 1 AND \"ApprovedCapacitySnapshot\" AND \"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_ResourceBookings_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceBookings_AcademyMemberships_AcademyId_CoachMembersh~",
                        columns: x => new { x.AcademyId, x.CoachMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceBookings_FacilityResources_AcademyId_ResourceId",
                        columns: x => new { x.AcademyId, x.ResourceId },
                        principalTable: "FacilityResources",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceBookings_TrainingGroups_AcademyId_TrainingGroupId",
                        columns: x => new { x.AcademyId, x.TrainingGroupId },
                        principalTable: "TrainingGroups",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceBookings_TrainingSessions_AcademyId_TrainingSession~",
                        columns: x => new { x.AcademyId, x.TrainingSessionId },
                        principalTable: "TrainingSessions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceBookings_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceClosures", x => x.Id);
                    table.UniqueConstraint("AK_ResourceClosures_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_ResourceClosure_Range", "\"FromDate\" <= \"ToDate\" AND \"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_ResourceClosures_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceClosures_FacilityResources_AcademyId_ResourceId",
                        columns: x => new { x.AcademyId, x.ResourceId },
                        principalTable: "FacilityResources",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceClosures_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacilityResources_AcademyId_FacilitySpaceId_Name",
                table: "FacilityResources",
                columns: new[] { "AcademyId", "FacilitySpaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityResources_ApprovedByUserId",
                table: "FacilityResources",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Facilitys_AcademyId_BranchId_Name",
                table: "Facilitys",
                columns: new[] { "AcademyId", "BranchId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySpaces_AcademyId_FacilityId_Name",
                table: "FacilitySpaces",
                columns: new[] { "AcademyId", "FacilityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySpaces_AcademyId_SportId",
                table: "FacilitySpaces",
                columns: new[] { "AcademyId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceBookings_AcademyId_ActorUserId_RequestKey",
                table: "ResourceBookings",
                columns: new[] { "AcademyId", "ActorUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceBookings_AcademyId_CoachMembershipId",
                table: "ResourceBookings",
                columns: new[] { "AcademyId", "CoachMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceBookings_AcademyId_ResourceId_Date",
                table: "ResourceBookings",
                columns: new[] { "AcademyId", "ResourceId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceBookings_AcademyId_TrainingGroupId",
                table: "ResourceBookings",
                columns: new[] { "AcademyId", "TrainingGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceBookings_AcademyId_TrainingSessionId",
                table: "ResourceBookings",
                columns: new[] { "AcademyId", "TrainingSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceBookings_ActorUserId",
                table: "ResourceBookings",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceClosures_AcademyId_ResourceId",
                table: "ResourceClosures",
                columns: new[] { "AcademyId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceClosures_ActorUserId",
                table: "ResourceClosures",
                column: "ActorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceBookings");

            migrationBuilder.DropTable(
                name: "ResourceClosures");

            migrationBuilder.DropTable(
                name: "FacilityResources");

            migrationBuilder.DropTable(
                name: "FacilitySpaces");

            migrationBuilder.DropTable(
                name: "Facilitys");
        }
    }
}
