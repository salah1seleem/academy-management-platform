namespace Academy.Infrastructure.Persistence.Migrations;
internal static class V4VipRewardGuards
{
    internal const string Sql = """
        CREATE FUNCTION academy_vip_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE r "VipRewards"; root_kind text; root_id uuid;
        BEGIN
          IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Preserve VIP history' USING ERRCODE='23514',CONSTRAINT='vip_history'; END IF;
          IF TG_TABLE_NAME='DiscountRules' THEN
            IF EXISTS(SELECT 1 FROM "VipRewards" v WHERE v."AcademyId"=NEW."AcademyId" AND v."DiscountRuleId"=NEW."Id") THEN RAISE EXCEPTION 'VIP terms are immutable' USING ERRCODE='23514',CONSTRAINT='vip_history'; END IF;
            RETURN NEW;
          END IF;
          IF TG_TABLE_NAME='VipRewards' THEN
            PERFORM pg_advisory_xact_lock(hashtextextended('vip-reward:'||NEW."AcademyId"::text||':'||NEW."Id"::text,0));
            IF TG_OP='INSERT' THEN
              IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" JOIN "Academies" a ON a."Id"=m."AcademyId" JOIN "GuardianProfiles" g ON g."AcademyId"=a."Id" AND g."Id"=NEW."GuardianId" JOIN "DiscountRules" d ON d."AcademyId"=a."Id" AND d."Id"=NEW."DiscountRuleId" JOIN "AcademyNotices" n ON n."AcademyId"=a."Id" AND n."Id"=NEW."NoticeId" AND n."UserId"=g."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."IssuedByMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive" AND a."IsActive" AND g."IsActive" AND d."IsActive" AND NOT d."IsSiblingOffer" AND d."Percentage"=NEW."Percentage" AND d."Type"='Percentage' AND d."Revision"=1 AND d."Currency"=NEW."Currency" AND a."DefaultCurrency"=NEW."Currency" AND d."StartsOn"=NEW."IssuedOn" AND d."EndsOn"=NEW."ExpiresOn") THEN RAISE EXCEPTION 'VIP source or issuer mismatch' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
            ELSE
              IF OLD."CancelledAtUtc" IS NOT NULL OR NEW."CancelledAtUtc" IS NULL OR (to_jsonb(OLD)-ARRAY['CancelledAtUtc','CancelledByMembershipId','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['CancelledAtUtc','CancelledByMembershipId','UpdatedAtUtc']) OR EXISTS(SELECT 1 FROM "VipRewardUses" u WHERE u."AcademyId"=NEW."AcademyId" AND u."RewardId"=NEW."Id" AND u."ReleasedAtUtc" IS NULL) OR NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."CancelledByMembershipId" AND m."Role"='AcademyOwner' AND m."IsActive" AND u."IsActive") THEN RAISE EXCEPTION 'Only unused unreserved VIP cancellation allowed' USING ERRCODE='23514',CONSTRAINT='vip_history'; END IF;
            END IF;
          ELSE
            PERFORM pg_advisory_xact_lock(hashtextextended('vip-reward:'||NEW."AcademyId"::text||':'||NEW."RewardId"::text,0));
            IF TG_OP='UPDATE' AND (OLD."UsedAtUtc" IS NOT NULL OR OLD."ReleasedAtUtc" IS NOT NULL OR (to_jsonb(OLD)-ARRAY['UsedAtUtc','ReleasedAtUtc','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['UsedAtUtc','ReleasedAtUtc','UpdatedAtUtc'])) THEN RAISE EXCEPTION 'VIP use history is immutable' USING ERRCODE='23514',CONSTRAINT='vip_history'; END IF;
            SELECT * INTO r FROM "VipRewards" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."RewardId";
            IF r."CancelledAtUtc" IS NOT NULL THEN RAISE EXCEPTION 'VIP cancelled' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
            IF NEW."ReleasedAtUtc" IS NOT NULL AND NOT (NEW."SourceKind"='Family' AND EXISTS(SELECT 1 FROM "FamilyRenewalRequests" f WHERE f."AcademyId"=NEW."AcademyId" AND f."Id"=NEW."SourceId" AND f."Status" IN ('Cancelled','Rejected')) OR NEW."SourceKind"='Enrollment' AND EXISTS(SELECT 1 FROM "NewEnrollmentRequests" x WHERE x."AcademyId"=NEW."AcademyId" AND x."Id"=NEW."SourceId" AND x."Status" IN ('Cancelled','Rejected') AND x."PaymentRequestId" IS NULL) OR NEW."SourceKind"='Renewal' AND EXISTS(SELECT 1 FROM "RenewalRequests" x JOIN "PaymentRequests" p ON p."AcademyId"=x."AcademyId" AND p."Id"=x."PaymentRequestId" WHERE x."AcademyId"=NEW."AcademyId" AND x."Id"=NEW."SourceId" AND p."Status" IN ('Cancelled','Failed','Expired') AND NOT EXISTS(SELECT 1 FROM "Collections" c WHERE c."PaymentRequestId"=p."Id"))) THEN RAISE EXCEPTION 'Release only terminal unpaid sources' USING ERRCODE='23514',CONSTRAINT='vip_history'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER vip_reward_guard BEFORE INSERT OR UPDATE OR DELETE ON "VipRewards" FOR EACH ROW EXECUTE FUNCTION academy_vip_guard();
        CREATE TRIGGER vip_use_guard BEFORE INSERT OR UPDATE OR DELETE ON "VipRewardUses" FOR EACH ROW EXECUTE FUNCTION academy_vip_guard();
        CREATE TRIGGER vip_rule_guard BEFORE UPDATE ON "DiscountRules" FOR EACH ROW EXECUTE FUNCTION academy_vip_guard();

        CREATE FUNCTION academy_vip_final_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE v "VipRewards"; u "VipRewardUses"; source_player uuid; family uuid;
        BEGIN
          IF TG_TABLE_NAME='NewEnrollmentRequests' THEN
            IF EXISTS(SELECT 1 FROM "VipRewards" reward WHERE reward."AcademyId"=NEW."AcademyId" AND reward."DiscountRuleId"=NEW."DiscountRuleIdSnapshot") AND NOT EXISTS(SELECT 1 FROM "VipRewardUses" reservation WHERE reservation."AcademyId"=NEW."AcademyId" AND reservation."SourceKind"='Enrollment' AND reservation."SourceId"=NEW."Id") THEN RAISE EXCEPTION 'VIP enrollment requires atomic reservation' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
            RETURN NEW;
          ELSIF TG_TABLE_NAME='VipRewardUses' THEN
            SELECT * INTO u FROM "VipRewardUses" WHERE "Id"=NEW."Id";
          ELSE
            SELECT * INTO v FROM "VipRewards" WHERE "AcademyId"=NEW."AcademyId" AND "DiscountRuleId"=NEW."DiscountRuleIdSnapshot";
            IF v."Id" IS NULL THEN IF NEW."VipRewardUseId" IS NOT NULL THEN RAISE EXCEPTION 'Non VIP renewal cannot consume VIP' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF; RETURN NEW; END IF;
            SELECT * INTO u FROM "VipRewardUses" WHERE "AcademyId"=NEW."AcademyId" AND "Id"=NEW."VipRewardUseId" AND "RewardId"=v."Id" AND "ReleasedAtUtc" IS NULL;
            IF u."Id" IS NULL OR NEW."DiscountType"<>'Percentage' OR NEW."DiscountValue"<>v."Percentage" OR NEW."Currency"<>v."Currency" OR NEW."DiscountAmount"<>round(NEW."OriginalAmount"*v."Percentage"/100,2) OR NEW."FinalAmount"<>NEW."OriginalAmount"-NEW."DiscountAmount" THEN RAISE EXCEPTION 'VIP renewal snapshot mismatch' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
            IF EXISTS(SELECT 1 FROM "Collections" c WHERE c."AcademyId"=NEW."AcademyId" AND c."PaymentRequestId"=NEW."PaymentRequestId") AND u."UsedAtUtc" IS NULL THEN RAISE EXCEPTION 'VIP consumption and collection must commit atomically' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
            IF u."SourceKind"='Enrollment' AND NOT EXISTS(SELECT 1 FROM "NewEnrollmentRequests" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=u."SourceId" AND e."CreatedSportEnrollmentId"=NEW."SportEnrollmentId" AND e."PaymentRequestId"=NEW."PaymentRequestId" AND e."Status"='Approved') OR u."SourceKind"='Renewal' AND u."SourceId"<>NEW."Id" OR u."SourceKind"='Family' AND NOT EXISTS(SELECT 1 FROM "FamilyRenewalLines" l WHERE l."AcademyId"=NEW."AcademyId" AND l."RequestId"=u."SourceId" AND l."ResultPaymentId"=NEW."PaymentRequestId") THEN RAISE EXCEPTION 'VIP renewal must match its exact source' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
          END IF;
          SELECT * INTO v FROM "VipRewards" WHERE "AcademyId"=u."AcademyId" AND "Id"=u."RewardId";
          IF u."SourceKind"='Renewal' THEN
            SELECT e."PlayerId" INTO source_player FROM "RenewalRequests" r JOIN "SportEnrollments" e ON e."AcademyId"=r."AcademyId" AND e."Id"=r."SportEnrollmentId" WHERE r."AcademyId"=u."AcademyId" AND r."Id"=u."SourceId" AND r."VipRewardUseId"=u."Id" AND r."DiscountRuleIdSnapshot"=v."DiscountRuleId";
            IF source_player IS NULL THEN RAISE EXCEPTION 'VIP renewal source missing' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
          ELSIF u."SourceKind"='Enrollment' THEN
            SELECT g."Id" INTO family FROM "NewEnrollmentRequests" e JOIN "GuardianProfiles" g ON g."AcademyId"=e."AcademyId" AND g."UserId"=e."RequestedByGuardianUserId" WHERE e."AcademyId"=u."AcademyId" AND e."Id"=u."SourceId" AND e."DiscountRuleIdSnapshot"=v."DiscountRuleId" AND e."OriginalAmount"-e."DiscountAmount"=e."FinalAmount" AND e."DiscountValue"=v."Percentage";
            IF family IS DISTINCT FROM v."GuardianId" THEN RAISE EXCEPTION 'VIP new enrollment family mismatch' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
          ELSE
            SELECT f."GuardianId" INTO family FROM "FamilyRenewalRequests" f WHERE f."AcademyId"=u."AcademyId" AND f."Id"=u."SourceId" AND f."VipRewardUseId"=u."Id";
            IF family IS DISTINCT FROM v."GuardianId" OR (SELECT count(*) FROM "FamilyRenewalLines" l WHERE l."AcademyId"=u."AcademyId" AND l."RequestId"=u."SourceId" AND (l."FinancialSnapshotJson"::jsonb->>'DiscountRuleId')::uuid=v."DiscountRuleId")<>1 THEN RAISE EXCEPTION 'VIP applies to one family package only' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
          END IF;
          IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM "GuardianProfiles" g WHERE g."AcademyId"=u."AcademyId" AND g."Id"=v."GuardianId" AND g."IsActive" AND (source_player IS NULL OR EXISTS(SELECT 1 FROM "GuardianPlayerLinks" l JOIN "Players" p ON p."AcademyId"=l."AcademyId" AND p."Id"=l."PlayerId" WHERE l."AcademyId"=u."AcademyId" AND l."GuardianId"=g."Id" AND l."PlayerId"=source_player AND l."IsActive" AND p."IsActive")) AND (g."UserId"=u."ActorUserId" OR EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" x ON x."Id"=m."UserId" WHERE m."AcademyId"=u."AcademyId" AND m."UserId"=u."ActorUserId" AND m."IsActive" AND x."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin')))) THEN RAISE EXCEPTION 'VIP trusted family or actor missing' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
          IF u."UsedAtUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "RenewalRequests" r JOIN "Collections" c ON c."AcademyId"=r."AcademyId" AND c."PaymentRequestId"=r."PaymentRequestId" WHERE r."AcademyId"=u."AcademyId" AND r."VipRewardUseId"=u."Id") THEN RAISE EXCEPTION 'VIP use requires actual collection' USING ERRCODE='23514',CONSTRAINT='vip_source'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER vip_use_final AFTER INSERT OR UPDATE ON "VipRewardUses" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_vip_final_guard();
        CREATE CONSTRAINT TRIGGER vip_renewal_final AFTER INSERT OR UPDATE ON "RenewalRequests" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_vip_final_guard();
        CREATE CONSTRAINT TRIGGER vip_enrollment_final AFTER INSERT OR UPDATE ON "NewEnrollmentRequests" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_vip_final_guard();
        """;
}
