namespace Academy.Infrastructure.Persistence.Migrations;
internal static class V4ExternalAgreementGuards
{
    internal const string Sql="""
        CREATE FUNCTION academy_external_agreement_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE a "ExternalAgreements"; cash_enabled boolean;
        BEGIN
          IF TG_OP='DELETE' AND academy_free_training_demo_reset(OLD."AcademyId") THEN RETURN OLD; END IF;
          IF TG_OP='DELETE' OR TG_OP='UPDATE' AND TG_TABLE_NAME<>'ExternalAgreements' THEN RAISE EXCEPTION 'Preserve external agreement financial history' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' AND TG_TABLE_NAME IN ('ExternalAgreements','ExternalAgreementReceipts') THEN
            IF NOT EXISTS(SELECT 1 FROM "AcademyMemberships" m JOIN "Users" u ON u."Id"=m."UserId" WHERE m."AcademyId"=NEW."AcademyId" AND m."Id"=NEW."ActorMembershipId" AND m."UserId"=NEW."ActorUserId" AND m."IsActive" AND u."IsActive" AND m."Role" IN ('AcademyOwner','AcademyAdmin')) THEN RAISE EXCEPTION 'External financial actor must be a live same-academy staff membership' USING ERRCODE='23514'; END IF;
          END IF;
          IF TG_TABLE_NAME='ExternalAgreements' THEN
            IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM "Academies" x WHERE x."Id"=NEW."AcademyId" AND x."DefaultCurrency"=NEW."Currency") THEN RAISE EXCEPTION 'Agreement currency must match academy' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' THEN
              PERFORM pg_advisory_xact_lock(hashtextextended('external-agreement:'||NEW."AcademyId"::text||':'||NEW."Id"::text,0));
              IF OLD."CancelledAtUtc" IS NOT NULL OR NEW."CancelledAtUtc" IS NULL OR (to_jsonb(OLD)-ARRAY['CancelledAtUtc','CancelledByUserId','CancellationReason','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['CancelledAtUtc','CancelledByUserId','CancellationReason','UpdatedAtUtc']) OR EXISTS(SELECT 1 FROM "ExternalAgreementReceipts" r WHERE r."AcademyId"=NEW."AcademyId" AND r."AgreementId"=NEW."Id") THEN RAISE EXCEPTION 'Only unpaid agreement cancellation is allowed' USING ERRCODE='23514'; END IF;
            END IF;
          ELSIF TG_TABLE_NAME='ExternalAgreementBookings' THEN
            IF NOT EXISTS(SELECT 1 FROM "ExternalAgreements" e JOIN "ResourceBookings" r ON r."AcademyId"=e."AcademyId" JOIN "FacilityResources" fr ON fr."AcademyId"=r."AcademyId" AND fr."Id"=r."ResourceId" JOIN "FacilitySpaces" sp ON sp."AcademyId"=fr."AcademyId" AND sp."Id"=fr."FacilitySpaceId" JOIN "Sports" s ON s."AcademyId"=sp."AcademyId" AND s."Id"=sp."SportId" WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."AgreementId" AND e."Kind"='PoolRental' AND e."CancelledAtUtc" IS NULL AND e."BranchId"=NEW."BranchId" AND e."SportId"=NEW."SportId" AND r."Id"=NEW."ResourceBookingId" AND r."BranchId"=e."BranchId" AND sp."SportId"=e."SportId" AND sp."Kind"='Pool' AND s."Kind"='Swimming' AND r."ServiceKind"='FacilityBooking' AND r."State"='Reserved' AND r."Date">=e."FromDate" AND r."Date"<=e."ToDate" AND r."ActorUserId"=e."ActorUserId") THEN RAISE EXCEPTION 'Rental must reference its actual same-scope pool booking' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='ExternalAgreementReceipts' THEN
            PERFORM pg_advisory_xact_lock(hashtextextended('external-agreement:'||NEW."AcademyId"::text||':'||NEW."AgreementId"::text,0));
            SELECT * INTO a FROM "ExternalAgreements" e WHERE e."AcademyId"=NEW."AcademyId" AND e."Id"=NEW."AgreementId";
            IF a."Id" IS NULL OR a."CancelledAtUtc" IS NOT NULL OR a."BranchId"<>NEW."BranchId" OR a."SportId"<>NEW."SportId" OR a."Currency"<>NEW."Currency" OR (SELECT COALESCE(SUM(r."Amount"),0) FROM "ExternalAgreementReceipts" r WHERE r."AcademyId"=NEW."AcademyId" AND r."AgreementId"=NEW."AgreementId")+NEW."Amount">a."Amount" THEN RAISE EXCEPTION 'Receipt exceeds agreement or differs from source scope' USING ERRCODE='23514'; END IF;
            SELECT COALESCE((v."Payload"::jsonb->>'CashEnabled')::boolean,false) INTO cash_enabled FROM "AcademyConfigurationVersions" v WHERE v."AcademyId"=NEW."AcademyId" AND v."State"='Active';
            IF NEW."CashPostingRequired" IS DISTINCT FROM (NEW."PaymentMethod"='Cash' AND COALESCE(cash_enabled,false)) THEN RAISE EXCEPTION 'Cash posting requirement must match active configuration' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='CashExternalReceiptPostings' THEN
            IF NOT EXISTS(SELECT 1 FROM "ExternalAgreementReceipts" r JOIN "CashSessions" s ON s."AcademyId"=r."AcademyId" AND s."Id"=NEW."SessionId" JOIN "CashRegisters" cr ON cr."AcademyId"=s."AcademyId" AND cr."Id"=s."RegisterId" JOIN "AcademyMemberships" m ON m."AcademyId"=s."AcademyId" AND m."Id"=s."OpenedByMembershipId" WHERE r."AcademyId"=NEW."AcademyId" AND r."Id"=NEW."ReceiptId" AND r."PaymentMethod"='Cash' AND r."CashPostingRequired" AND r."Amount"=NEW."Amount" AND r."Currency"=NEW."Currency" AND r."ActorUserId"=NEW."ActorUserId" AND m."UserId"=r."ActorUserId" AND m."Id"=r."ActorMembershipId" AND m."IsActive" AND r."BranchId"=NEW."BranchId" AND s."BranchId"=NEW."BranchId" AND cr."Currency"=NEW."Currency" AND cr."IsActive" AND s."ClosedAtUtc" IS NULL AND EXISTS(SELECT 1 FROM "CashierAssignments" ca WHERE ca."AcademyId"=s."AcademyId" AND ca."MembershipId"=m."Id" AND ca."RegisterId"=cr."Id" AND ca."IsActive")) THEN RAISE EXCEPTION 'Cash receipt posting must match collector, open session and source' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER external_agreement_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExternalAgreements" FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_guard();
        CREATE TRIGGER external_agreement_booking_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExternalAgreementBookings" FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_guard();
        CREATE TRIGGER external_agreement_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExternalAgreementReceipts" FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_guard();
        CREATE TRIGGER cash_external_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON "CashExternalReceiptPostings" FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_guard();

        CREATE FUNCTION academy_external_agreement_final_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME='ExternalAgreements' THEN
            IF NEW."Kind"='PoolRental' AND NEW."CancelledAtUtc" IS NULL AND NOT EXISTS(SELECT 1 FROM "ExternalAgreementBookings" b WHERE b."AcademyId"=NEW."AcademyId" AND b."AgreementId"=NEW."Id") THEN RAISE EXCEPTION 'Pool rental requires a real committed booking' USING ERRCODE='23514'; END IF;
            IF NEW."CancelledAtUtc" IS NOT NULL AND EXISTS(SELECT 1 FROM "ExternalAgreementBookings" b JOIN "ResourceBookings" r ON r."AcademyId"=b."AcademyId" AND r."Id"=b."ResourceBookingId" WHERE b."AcademyId"=NEW."AcademyId" AND b."AgreementId"=NEW."Id" AND r."State"<>'Cancelled') THEN RAISE EXCEPTION 'Cancelled agreement must release its bookings atomically' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='ExternalAgreementReceipts' THEN
            IF NEW."CashPostingRequired" AND NOT EXISTS(SELECT 1 FROM "CashExternalReceiptPostings" p WHERE p."AcademyId"=NEW."AcademyId" AND p."ReceiptId"=NEW."Id") THEN RAISE EXCEPTION 'Receipt and cash posting must commit atomically' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='ResourceBookings' AND NEW."State"='Cancelled' THEN
            IF EXISTS(SELECT 1 FROM "ExternalAgreementBookings" b JOIN "ExternalAgreements" e ON e."AcademyId"=b."AcademyId" AND e."Id"=b."AgreementId" WHERE b."AcademyId"=NEW."AcademyId" AND b."ResourceBookingId"=NEW."Id" AND e."CancelledAtUtc" IS NULL) THEN RAISE EXCEPTION 'Cancel a rental from its unpaid agreement, not the raw resource' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER external_agreement_final_guard AFTER INSERT OR UPDATE ON "ExternalAgreements" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_final_guard();
        CREATE CONSTRAINT TRIGGER external_receipt_final_guard AFTER INSERT ON "ExternalAgreementReceipts" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_final_guard();
        CREATE CONSTRAINT TRIGGER external_booking_cancel_guard AFTER UPDATE ON "ResourceBookings" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION academy_external_agreement_final_guard();
        """;
}
