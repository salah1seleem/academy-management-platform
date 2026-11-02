using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3StaffPasskeyLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffPasskeyChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RpId = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    OriginsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPasskeyChallenges", x => x.Id);
                    table.UniqueConstraint("AK_StaffPasskeyChallenges_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_StaffPasskeyChallenge_State", "\"IsActive\" AND \"Purpose\" IN ('Register','Verify') AND \"ExpiresAtUtc\">\"CreatedAtUtc\" AND (\"ConsumedAtUtc\" IS NULL OR \"ConsumedAtUtc\">=\"CreatedAtUtc\") AND (\"VerifiedAtUtc\" IS NULL OR (\"ConsumedAtUtc\" IS NOT NULL AND \"VerifiedAtUtc\">=\"ConsumedAtUtc\"))");
                    table.ForeignKey(
                        name: "FK_StaffPasskeyChallenges_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPasskeyChallenges_AcademyMemberships_AcademyId_Members~",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPasskeyChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffPasskeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CredentialHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    UserHandle = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    BackupEligible = table.Column<bool>(type: "boolean", nullable: false),
                    BackedUp = table.Column<bool>(type: "boolean", nullable: false),
                    RpId = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    RegistrationChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcademyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPasskeys", x => x.Id);
                    table.UniqueConstraint("AK_StaffPasskeys_AcademyId_Id", x => new { x.AcademyId, x.Id });
                    table.CheckConstraint("CK_StaffPasskey_State", "\"SignCount\">=0 AND length(\"CredentialHash\")=64 AND octet_length(\"CredentialId\") BETWEEN 1 AND 1024 AND octet_length(\"PublicKey\") BETWEEN 1 AND 4096 AND octet_length(\"UserHandle\")=16 AND ((\"IsActive\" AND \"RevokedAtUtc\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_StaffPasskeys_Academies_AcademyId",
                        column: x => x.AcademyId,
                        principalTable: "Academies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPasskeys_AcademyMemberships_AcademyId_MembershipId",
                        columns: x => new { x.AcademyId, x.MembershipId },
                        principalTable: "AcademyMemberships",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPasskeys_StaffPasskeyChallenges_AcademyId_Registration~",
                        columns: x => new { x.AcademyId, x.RegistrationChallengeId },
                        principalTable: "StaffPasskeyChallenges",
                        principalColumns: new[] { "AcademyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPasskeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasskeyChallenges_AcademyId_MembershipId_ExpiresAtUtc",
                table: "StaffPasskeyChallenges",
                columns: new[] { "AcademyId", "MembershipId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasskeyChallenges_UserId",
                table: "StaffPasskeyChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasskeys_AcademyId_CredentialHash",
                table: "StaffPasskeys",
                columns: new[] { "AcademyId", "CredentialHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasskeys_AcademyId_MembershipId",
                table: "StaffPasskeys",
                columns: new[] { "AcademyId", "MembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasskeys_AcademyId_RegistrationChallengeId",
                table: "StaffPasskeys",
                columns: new[] { "AcademyId", "RegistrationChallengeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasskeys_UserId",
                table: "StaffPasskeys",
                column: "UserId");
            migrationBuilder.Sql("""
              CREATE FUNCTION academy_staff_passkey_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Verification credentials preserve their audit identity' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."Id"=NEW."MembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."UserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin','Coach'))
                  THEN RAISE EXCEPTION 'Invalid staff credential identity' USING ERRCODE='23514'; END IF;
                IF TG_OP='UPDATE' AND (NOT OLD."IsActive" OR NEW."SignCount"<OLD."SignCount"
                  OR (to_jsonb(OLD)-ARRAY['SignCount','BackedUp','LastVerifiedAtUtc','IsActive','RevokedAtUtc','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['SignCount','BackedUp','LastVerifiedAtUtc','IsActive','RevokedAtUtc','UpdatedAtUtc']))
                  THEN RAISE EXCEPTION 'Credential identity and revocation are immutable' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER staff_passkey_guard BEFORE INSERT OR UPDATE OR DELETE ON "StaffPasskeys" FOR EACH ROW EXECUTE FUNCTION academy_staff_passkey_guard();
              CREATE FUNCTION academy_staff_passkey_challenge_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
                IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Verification challenge source is immutable' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m WHERE m."Id"=NEW."MembershipId" AND m."AcademyId"=NEW."AcademyId" AND m."UserId"=NEW."UserId" AND m."Role" IN ('AcademyOwner','AcademyAdmin','Coach'))
                  THEN RAISE EXCEPTION 'Invalid staff challenge identity' USING ERRCODE='23514'; END IF;
                IF TG_OP='INSERT' AND (NEW."ConsumedAtUtc" IS NOT NULL OR NEW."VerifiedAtUtc" IS NOT NULL) THEN RAISE EXCEPTION 'Challenge begins unused' USING ERRCODE='23514'; END IF;
                IF TG_OP='UPDATE' AND (OLD."ConsumedAtUtc" IS NOT NULL OR NEW."ConsumedAtUtc" IS NULL
                  OR (to_jsonb(OLD)-ARRAY['ConsumedAtUtc','VerifiedAtUtc','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['ConsumedAtUtc','VerifiedAtUtc','UpdatedAtUtc']))
                  THEN RAISE EXCEPTION 'Challenge is immutable and single-use' USING ERRCODE='23514'; END IF;
                IF NEW."VerifiedAtUtc" IS NOT NULL AND NEW."ConsumedAtUtc">=NEW."ExpiresAtUtc" THEN RAISE EXCEPTION 'Expired challenge cannot be verified' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER staff_passkey_challenge_guard BEFORE INSERT OR UPDATE OR DELETE ON "StaffPasskeyChallenges" FOR EACH ROW EXECUTE FUNCTION academy_staff_passkey_challenge_guard();
              CREATE FUNCTION academy_staff_passkey_registration_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF NOT EXISTS(SELECT 1 FROM "StaffPasskeyChallenges" c WHERE c."Id"=NEW."RegistrationChallengeId" AND c."AcademyId"=NEW."AcademyId" AND c."MembershipId"=NEW."MembershipId" AND c."UserId"=NEW."UserId"
                  AND c."Purpose"='Register' AND c."VerifiedAtUtc" IS NOT NULL AND c."RpId"=NEW."RpId" AND c."Name"=NEW."Name")
                  THEN RAISE EXCEPTION 'Credential must preserve its verified registration evidence' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE CONSTRAINT TRIGGER staff_passkey_registration_guard AFTER INSERT ON "StaffPasskeys" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_staff_passkey_registration_guard();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"StaffPasskeys\") OR EXISTS(SELECT 1 FROM \"StaffPasskeyChallenges\") THEN RAISE EXCEPTION 'Cannot remove credential verification evidence' USING ERRCODE='23514'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "StaffPasskeys");

            migrationBuilder.DropTable(
                name: "StaffPasskeyChallenges");
            migrationBuilder.Sql("DROP FUNCTION academy_staff_passkey_guard(); DROP FUNCTION academy_staff_passkey_challenge_guard(); DROP FUNCTION academy_staff_passkey_registration_guard();");
        }
    }
}
