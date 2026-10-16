using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V3SignedStaffAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffWorkPeriod_Bounds",
                table: "StaffWorkPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffPasskeyChallenge_State",
                table: "StaffPasskeyChallenges");

            migrationBuilder.AddColumn<string>(
                name: "OperationJson",
                table: "StaffPasskeyChallenges",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "StaffPasskeyChallenges",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerificationChallengeId",
                table: "StaffAttendanceChanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffWorkPeriod_Bounds",
                table: "StaffWorkPeriods",
                sql: "\"IsActive\" AND ((\"EndDate\" IS NULL AND \"EndTime\" IS NULL) OR (\"EndDate\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND (\"EndDate\"+\"EndTime\") >= (\"StartDate\"+\"StartTime\")))\nAND ((\"TrustSource\"='LegacyLocal' AND \"StartUtc\" IS NULL AND \"EndUtc\" IS NULL AND \"TimeZoneId\" IS NULL)\n  OR (\"TrustSource\" IN ('AdminDeclared','ServerVerified','MixedVerifiedEnd') AND \"StartUtc\" IS NOT NULL AND \"TimeZoneId\" IS NOT NULL AND ((\"EndTime\" IS NULL AND \"EndUtc\" IS NULL AND \"TrustSource\"<>'MixedVerifiedEnd') OR (\"EndTime\" IS NOT NULL AND \"EndUtc\" IS NOT NULL AND \"EndUtc\" >= \"StartUtc\"))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffPasskeyChallenge_State",
                table: "StaffPasskeyChallenges",
                sql: "\"IsActive\" AND \"Purpose\" IN ('Register','Verify','CheckIn','CheckOut') AND \"ExpiresAtUtc\">\"CreatedAtUtc\" AND (\"ConsumedAtUtc\" IS NULL OR \"ConsumedAtUtc\">=\"CreatedAtUtc\") AND (\"VerifiedAtUtc\" IS NULL OR (\"ConsumedAtUtc\" IS NOT NULL AND \"VerifiedAtUtc\">=\"ConsumedAtUtc\")) AND ((\"Purpose\" IN ('Register','Verify') AND \"OperationJson\" IS NULL AND \"ResultJson\" IS NULL) OR (\"Purpose\" IN ('CheckIn','CheckOut') AND jsonb_typeof(\"OperationJson\")='object' AND (\"ResultJson\" IS NULL OR (\"VerifiedAtUtc\" IS NOT NULL AND jsonb_typeof(\"ResultJson\")='object'))))");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceChanges_AcademyId_VerificationChallengeId",
                table: "StaffAttendanceChanges",
                columns: new[] { "AcademyId", "VerificationChallengeId" },
                unique: true,
                filter: "\"VerificationChallengeId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAttendanceChanges_StaffPasskeyChallenges_AcademyId_Ver~",
                table: "StaffAttendanceChanges",
                columns: new[] { "AcademyId", "VerificationChallengeId" },
                principalTable: "StaffPasskeyChallenges",
                principalColumns: new[] { "AcademyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
              DO $patch$
              DECLARE original text; revised text;
              BEGIN
                original=pg_get_functiondef('academy_staff_passkey_challenge_guard()'::regprocedure);
                revised=replace(original,$old$ARRAY['ConsumedAtUtc','VerifiedAtUtc','UpdatedAtUtc']$old$,$new$ARRAY['ConsumedAtUtc','VerifiedAtUtc','ResultJson','UpdatedAtUtc']$new$);
                IF revised=original THEN RAISE EXCEPTION 'Unexpected challenge guard baseline'; END IF;
                revised=replace(revised,'NEW."ConsumedAtUtc" IS NOT NULL OR NEW."VerifiedAtUtc" IS NOT NULL','NEW."ConsumedAtUtc" IS NOT NULL OR NEW."VerifiedAtUtc" IS NOT NULL OR NEW."ResultJson" IS NOT NULL');
                EXECUTE revised;
                original=pg_get_functiondef('academy_staff_attendance_change_guard()'::regprocedure);
                revised=replace(original,$old$m."Role" IN ('AcademyOwner','AcademyAdmin')$old$,$new$((NEW."VerificationChallengeId" IS NULL AND m."Role" IN ('AcademyOwner','AcademyAdmin')) OR (NEW."VerificationChallengeId" IS NOT NULL AND m."Id"=NEW."MembershipId" AND m."Role" IN ('AcademyOwner','AcademyAdmin','Coach')))$new$);
                IF revised=original THEN RAISE EXCEPTION 'Unexpected attendance evidence guard baseline'; END IF;
                EXECUTE revised;
              END $patch$;

              CREATE FUNCTION academy_signed_attendance_evidence_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              DECLARE cid uuid; c record; a record; p record; k record;
              BEGIN
                IF TG_TABLE_NAME='StaffPasskeyChallenges' THEN
                  IF NEW."ResultJson" IS NULL THEN RETURN NEW; END IF; cid=NEW."Id";
                ELSE
                  IF NEW."VerificationChallengeId" IS NULL THEN RETURN NEW; END IF; cid=NEW."VerificationChallengeId";
                END IF;
                SELECT * INTO c FROM "StaffPasskeyChallenges" WHERE "Id"=cid;
                SELECT * INTO a FROM "StaffAttendanceChanges" WHERE "VerificationChallengeId"=cid AND "AcademyId"=c."AcademyId";
                SELECT * INTO p FROM "StaffWorkPeriods" WHERE "Id"=(c."ResultJson"->>'workPeriodId')::uuid AND "AcademyId"=c."AcademyId";
                SELECT * INTO k FROM "StaffPasskeys" WHERE "Id"=(c."ResultJson"->>'credentialId')::uuid AND "AcademyId"=c."AcademyId";
                IF c."Purpose" NOT IN ('CheckIn','CheckOut') OR c."VerifiedAtUtc" IS NULL OR c."ResultJson" IS NULL OR a."Id" IS NULL OR p."Id" IS NULL OR k."Id" IS NULL OR NOT k."IsActive"
                  OR a."MembershipId"<>c."MembershipId" OR a."ActorMembershipId"<>c."MembershipId" OR a."ActorUserId"<>c."UserId" OR k."MembershipId"<>c."MembershipId" OR k."UserId"<>c."UserId"
                  OR c."ResultJson"->>'operation'<>c."Purpose" OR (c."ResultJson"->>'confirmedAtUtc')::timestamptz<>c."VerifiedAtUtc" OR c."ConsumedAtUtc"<>c."VerifiedAtUtc"
                  OR (c."ResultJson"->>'attendanceId')::uuid<>a."AttendanceId" OR a."AttendanceId"<>p."AttendanceId" OR p."MembershipId"<>c."MembershipId"
                  OR (c."ResultJson"->>'afterVersion')::bigint<>a."AfterVersion" OR (c."ResultJson"->>'workDate')::date<>a."WorkDate"
                  OR c."OperationJson"->>'intentHash'<>a."RequestHash" OR c."ResultJson"->>'intentHash'<>a."RequestHash" OR length(c."ResultJson"->>'proofHash')<>64
                  OR NOT EXISTS(SELECT 1 FROM "Branches" b WHERE b."Id"=(c."OperationJson"->>'branchId')::uuid AND b."AcademyId"=c."AcademyId" AND b."IsActive" AND b."CheckInLatitude" IS NOT NULL AND b."CheckInLongitude" IS NOT NULL AND b.xmin::text::bigint=(c."OperationJson"->>'branchVersion')::bigint)
                  OR NOT EXISTS(SELECT 1 FROM "Employees" e WHERE e."Id"=(c."OperationJson"->>'employeeId')::uuid AND e."AcademyId"=c."AcademyId" AND e."MembershipId"=c."MembershipId" AND e."IsActive")
                  OR (c."Purpose"='CheckIn' AND (p."TrustSource"<>'ServerVerified' OR p."StartUtc"<>c."VerifiedAtUtc" OR p."EndTime" IS NOT NULL))
                  OR (c."Purpose"='CheckOut' AND (p."EndUtc" IS DISTINCT FROM c."VerifiedAtUtc" OR p."TrustSource" NOT IN ('ServerVerified','MixedVerifiedEnd')))
                  THEN RAISE EXCEPTION 'Signed attendance requires matching consumed proof, actor and applied period' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE CONSTRAINT TRIGGER signed_attendance_challenge_guard AFTER INSERT OR UPDATE ON "StaffPasskeyChallenges" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_signed_attendance_evidence_guard();
              CREATE CONSTRAINT TRIGGER signed_attendance_change_guard AFTER INSERT ON "StaffAttendanceChanges" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_signed_attendance_evidence_guard();

              CREATE FUNCTION academy_verified_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF NEW."TrustSource" NOT IN ('ServerVerified','MixedVerifiedEnd') THEN RETURN NEW; END IF;
                IF (NEW."StartUtc" AT TIME ZONE NEW."TimeZoneId") IS DISTINCT FROM NEW."StartDate"+NEW."StartTime"
                  OR (NEW."EndTime" IS NOT NULL AND (NEW."EndUtc" AT TIME ZONE NEW."TimeZoneId") IS DISTINCT FROM NEW."EndDate"+NEW."EndTime")
                  OR NOT EXISTS(SELECT 1 FROM "StaffAttendanceChanges" a JOIN "StaffPasskeyChallenges" c ON c."Id"=a."VerificationChallengeId" AND c."AcademyId"=a."AcademyId"
                    JOIN "StaffDailyAttendances" d ON d."Id"=a."AttendanceId"
                    WHERE a."AttendanceId"=NEW."AttendanceId" AND a."AcademyId"=NEW."AcademyId" AND a."AfterVersion"=d.xmin::text::bigint
                      AND (c."ResultJson"->>'workPeriodId')::uuid=NEW."Id" AND c."VerifiedAtUtc" IS NOT NULL AND a."ActorUserId"=NEW."RecordedByUserId")
                  THEN RAISE EXCEPTION 'Verified period requires its own signed operation evidence' USING ERRCODE='23514'; END IF;
                RETURN NEW;
              END $$;
              CREATE CONSTRAINT TRIGGER verified_period_guard AFTER INSERT OR UPDATE ON "StaffWorkPeriods" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_verified_period_guard();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN RAISE EXCEPTION 'Signed attendance evidence requires an explicit forward correction; no automatic downgrade' USING ERRCODE='23514'; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_StaffAttendanceChanges_StaffPasskeyChallenges_AcademyId_Ver~",
                table: "StaffAttendanceChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffWorkPeriod_Bounds",
                table: "StaffWorkPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffPasskeyChallenge_State",
                table: "StaffPasskeyChallenges");

            migrationBuilder.DropIndex(
                name: "IX_StaffAttendanceChanges_AcademyId_VerificationChallengeId",
                table: "StaffAttendanceChanges");

            migrationBuilder.DropColumn(
                name: "OperationJson",
                table: "StaffPasskeyChallenges");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "StaffPasskeyChallenges");

            migrationBuilder.DropColumn(
                name: "VerificationChallengeId",
                table: "StaffAttendanceChanges");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffWorkPeriod_Bounds",
                table: "StaffWorkPeriods",
                sql: "\"IsActive\" AND ((\"EndDate\" IS NULL AND \"EndTime\" IS NULL) OR (\"EndDate\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND (\"EndDate\"+\"EndTime\") >= (\"StartDate\"+\"StartTime\")))\nAND ((\"TrustSource\"='LegacyLocal' AND \"StartUtc\" IS NULL AND \"EndUtc\" IS NULL AND \"TimeZoneId\" IS NULL)\n  OR (\"TrustSource\"='AdminDeclared' AND \"StartUtc\" IS NOT NULL AND \"TimeZoneId\" IS NOT NULL AND ((\"EndTime\" IS NULL AND \"EndUtc\" IS NULL) OR (\"EndTime\" IS NOT NULL AND \"EndUtc\" IS NOT NULL AND \"EndUtc\" >= \"StartUtc\"))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffPasskeyChallenge_State",
                table: "StaffPasskeyChallenges",
                sql: "\"IsActive\" AND \"Purpose\" IN ('Register','Verify') AND \"ExpiresAtUtc\">\"CreatedAtUtc\" AND (\"ConsumedAtUtc\" IS NULL OR \"ConsumedAtUtc\">=\"CreatedAtUtc\") AND (\"VerifiedAtUtc\" IS NULL OR (\"ConsumedAtUtc\" IS NOT NULL AND \"VerifiedAtUtc\">=\"ConsumedAtUtc\"))");
        }
    }
}
