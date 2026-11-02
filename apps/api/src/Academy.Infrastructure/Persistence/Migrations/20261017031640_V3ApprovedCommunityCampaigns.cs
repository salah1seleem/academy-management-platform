using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3ApprovedCommunityCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MatchId",
                table: "AcademyNotices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UrgentUntilUtc",
                table: "AcademyNotices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommunityCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Advertiser = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CurrentRevision = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityCampaigns", x => x.Id);
                    table.UniqueConstraint("AK_CommunityCampaigns_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CommunityCampaigns_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuardianSponsorPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdultDeclaredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InAppOptIn = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentVersion = table.Column<int>(type: "integer", nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianSponsorPreferences", x => x.Id);
                    table.UniqueConstraint("AK_GuardianSponsorPreferences_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_GuardianSponsorPreferences_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianSponsorPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommunityCampaignRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Advertiser = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ButtonText = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageAlt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityCampaignRevisions", x => x.Id);
                    table.UniqueConstraint("AK_CommunityCampaignRevisions_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CommunityCampaignRevision_Window", "\"IsActive\" AND \"Revision\">0 AND \"EndsAtUtc\">\"StartsAtUtc\" AND \"Priority\" BETWEEN 0 AND 10 AND \"State\" IN ('Draft','PendingApproval','Approved','Paused','Rejected')");
                    table.ForeignKey(
                        name: "FK_CommunityCampaignRevisions_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignRevisions_AcademyMemberships_AcademyId_Cre~",
                        columns: x => new { x.AcademyId, x.CreatedByMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignRevisions_CommunityCampaigns_AcademyId_Cam~",
                        columns: x => new { x.AcademyId, x.CampaignId },
                        principalTable: "CommunityCampaigns",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignRevisions_PrivateAssets_AcademyId_ImageAss~",
                        columns: x => new { x.AcademyId, x.ImageAssetId },
                        principalTable: "PrivateAssets",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommunityCampaignTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityCampaignTargets", x => x.Id);
                    table.UniqueConstraint("AK_CommunityCampaignTargets_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.UniqueConstraint("AK_CommunityCampaignTargets_AcademyId_RevisionId_BranchId", x => new { x.AcademyId, x.RevisionId, x.BranchId });
                    table.CheckConstraint("CK_CommunityCampaignTarget_Decision", "\"Decision\" IN ('Pending','Approved','Rejected','Revoked')");
                    table.ForeignKey(
                        name: "FK_CommunityCampaignTargets_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignTargets_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignTargets_CommunityCampaignRevisions_Academy~",
                        columns: x => new { x.AcademyId, x.RevisionId },
                        principalTable: "CommunityCampaignRevisions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommunityAdEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityAdEvents", x => x.Id);
                    table.UniqueConstraint("AK_CommunityAdEvents_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_CommunityAdEvent_Kind", "\"Kind\" IN ('Impression','Click','Hide') AND \"IsActive\"");
                    table.ForeignKey(
                        name: "FK_CommunityAdEvents_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityAdEvents_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityAdEvents_CommunityCampaignRevisions_AcademyId_Revi~",
                        columns: x => new { x.AcademyId, x.RevisionId },
                        principalTable: "CommunityCampaignRevisions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityAdEvents_CommunityCampaignTargets_AcademyId_Revisi~",
                        columns: x => new { x.AcademyId, x.RevisionId, x.BranchId },
                        principalTable: "CommunityCampaignTargets",
                        principalColumns: new[] { "AcademyId", "RevisionId", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityAdEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommunityCampaignReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityCampaignReviews", x => x.Id);
                    table.UniqueConstraint("AK_CommunityCampaignReviews_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CommunityCampaignReviews_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignReviews_AcademyMemberships_AcademyId_Actor~",
                        columns: x => new { x.AcademyId, x.ActorMembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignReviews_Branches_AcademyId_BranchId",
                        columns: x => new { x.AcademyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignReviews_CommunityCampaignRevisions_Academy~",
                        columns: x => new { x.AcademyId, x.RevisionId },
                        principalTable: "CommunityCampaignRevisions",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityCampaignReviews_CommunityCampaignTargets_AcademyId~",
                        columns: x => new { x.AcademyId, x.RevisionId, x.BranchId },
                        principalTable: "CommunityCampaignTargets",
                        principalColumns: new[] { "AcademyId", "RevisionId", "BranchId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyNotices_AcademyId_MatchId",
                table: "AcademyNotices",
                columns: new[] { "AcademyId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAdEvents_AcademyId_BranchId",
                table: "CommunityAdEvents",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAdEvents_AcademyId_RevisionId_BranchId",
                table: "CommunityAdEvents",
                columns: new[] { "AcademyId", "RevisionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAdEvents_AcademyId_RevisionId_UserId_ViewDate_Kind",
                table: "CommunityAdEvents",
                columns: new[] { "AcademyId", "RevisionId", "UserId", "ViewDate", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAdEvents_UserId",
                table: "CommunityAdEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignReviews_AcademyId_ActorMembershipId_Reques~",
                table: "CommunityCampaignReviews",
                columns: new[] { "AcademyId", "ActorMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignReviews_AcademyId_BranchId",
                table: "CommunityCampaignReviews",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignReviews_AcademyId_RevisionId_BranchId",
                table: "CommunityCampaignReviews",
                columns: new[] { "AcademyId", "RevisionId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignRevisions_AcademyId_CampaignId_Revision",
                table: "CommunityCampaignRevisions",
                columns: new[] { "AcademyId", "CampaignId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignRevisions_AcademyId_CreatedByMembershipId_~",
                table: "CommunityCampaignRevisions",
                columns: new[] { "AcademyId", "CreatedByMembershipId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignRevisions_AcademyId_ImageAssetId",
                table: "CommunityCampaignRevisions",
                columns: new[] { "AcademyId", "ImageAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityCampaignTargets_AcademyId_BranchId",
                table: "CommunityCampaignTargets",
                columns: new[] { "AcademyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianSponsorPreferences_AcademyId_UserId",
                table: "GuardianSponsorPreferences",
                columns: new[] { "AcademyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianSponsorPreferences_UserId",
                table: "GuardianSponsorPreferences",
                column: "UserId");

            migrationBuilder.Sql("""
                CREATE FUNCTION academy_community_evidence_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE stripped text[];
                BEGIN
                  IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                  IF TG_OP='DELETE' OR TG_OP='UPDATE' AND TG_TABLE_NAME IN ('CommunityCampaignReviews','CommunityAdEvents') THEN RAISE EXCEPTION 'Preserve community evidence' USING ERRCODE='23514'; END IF;
                  IF TG_OP='UPDATE' THEN
                    stripped=CASE TG_TABLE_NAME WHEN 'CommunityCampaigns' THEN ARRAY['InternalName','Advertiser','CurrentRevision','UpdatedAtUtc'] WHEN 'CommunityCampaignRevisions' THEN ARRAY['State','UpdatedAtUtc'] WHEN 'CommunityCampaignTargets' THEN ARRAY['Decision','UpdatedAtUtc'] WHEN 'GuardianSponsorPreferences' THEN ARRAY['AdultDeclaredAtUtc','InAppOptIn','UpdatedAtUtc'] ELSE ARRAY[]::text[] END;
                    IF (to_jsonb(OLD)-stripped) IS DISTINCT FROM (to_jsonb(NEW)-stripped) THEN RAISE EXCEPTION 'Creative, audience or consent identity is immutable' USING ERRCODE='23514'; END IF;
                    IF TG_TABLE_NAME='CommunityCampaigns' THEN
                      IF NEW."CurrentRevision"<OLD."CurrentRevision" OR NEW."CurrentRevision">OLD."CurrentRevision"+1 OR NEW."CurrentRevision"=OLD."CurrentRevision" AND (NEW."InternalName"<>OLD."InternalName" OR NEW."Advertiser"<>OLD."Advertiser") THEN RAISE EXCEPTION 'Campaign changes need a new revision' USING ERRCODE='23514'; END IF;
                    END IF;
                  END IF;
                  IF TG_OP='INSERT' AND TG_TABLE_NAME IN ('CommunityCampaignRevisions','CommunityCampaignReviews') THEN
                    IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=CASE WHEN TG_TABLE_NAME='CommunityCampaignRevisions' THEN (to_jsonb(NEW)->>'CreatedByMembershipId')::uuid ELSE (to_jsonb(NEW)->>'ActorMembershipId')::uuid END AND m."Role"='AcademyOwner') THEN RAISE EXCEPTION 'Campaign actor must be academy owner' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF TG_OP='INSERT' AND TG_TABLE_NAME IN ('GuardianSponsorPreferences','CommunityAdEvents') THEN
                    IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."UserId" AND m."Role"='Guardian') THEN RAISE EXCEPTION 'Sponsor consent/event requires guardian membership in this academy' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER community_campaign_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommunityCampaigns" FOR EACH ROW EXECUTE FUNCTION academy_community_evidence_guard();
                CREATE TRIGGER community_revision_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommunityCampaignRevisions" FOR EACH ROW EXECUTE FUNCTION academy_community_evidence_guard();
                CREATE TRIGGER community_target_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommunityCampaignTargets" FOR EACH ROW EXECUTE FUNCTION academy_community_evidence_guard();
                CREATE TRIGGER community_review_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommunityCampaignReviews" FOR EACH ROW EXECUTE FUNCTION academy_community_evidence_guard();
                CREATE TRIGGER community_preference_guard BEFORE INSERT OR UPDATE OR DELETE ON "GuardianSponsorPreferences" FOR EACH ROW EXECUTE FUNCTION academy_community_evidence_guard();
                CREATE TRIGGER community_event_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommunityAdEvents" FOR EACH ROW EXECUTE FUNCTION academy_community_evidence_guard();
                """);
            migrationBuilder.AddForeignKey(
                name: "FK_AcademyNotices_AcademyMatchs_AcademyId_MatchId",
                table: "AcademyNotices",
                columns: new[] { "AcademyId", "MatchId" },
                principalTable: "AcademyMatchs",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "CommunityCampaigns") OR EXISTS(SELECT 1 FROM "GuardianSponsorPreferences") OR EXISTS(SELECT 1 FROM "CommunityAdEvents") OR EXISTS(SELECT 1 FROM "AcademyNotices" WHERE "MatchId" IS NOT NULL OR "UrgentUntilUtc" IS NOT NULL) THEN RAISE EXCEPTION 'Preserve campaign, consent and notice evidence before rollback' USING ERRCODE='23514'; END IF; END $$;
                DROP FUNCTION academy_community_evidence_guard() CASCADE;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_AcademyNotices_AcademyMatchs_AcademyId_MatchId",
                table: "AcademyNotices");

            migrationBuilder.DropTable(
                name: "CommunityAdEvents");

            migrationBuilder.DropTable(
                name: "CommunityCampaignReviews");

            migrationBuilder.DropTable(
                name: "GuardianSponsorPreferences");

            migrationBuilder.DropTable(
                name: "CommunityCampaignTargets");

            migrationBuilder.DropTable(
                name: "CommunityCampaignRevisions");

            migrationBuilder.DropTable(
                name: "CommunityCampaigns");

            migrationBuilder.DropIndex(
                name: "IX_AcademyNotices_AcademyId_MatchId",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "MatchId",
                table: "AcademyNotices");

            migrationBuilder.DropColumn(
                name: "UrgentUntilUtc",
                table: "AcademyNotices");
        }
    }
}
