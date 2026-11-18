BEGIN;
DO $$ BEGIN
 IF current_database() <> 'academy_ios_qa' THEN
   RAISE EXCEPTION 'Disposable iOS QA database only';
 END IF;
 IF (SELECT count(*) FROM "Academies" WHERE "Slug" IN ('nogoom-demo','future-demo')) <> 2 THEN
   RAISE EXCEPTION 'Expected two synthetic academies';
 END IF;
END $$;
UPDATE "Users" SET "PhoneNumber" = '+201000000011', "PhoneNumberConfirmed" = true
WHERE "Email" = 'admin.nogoom@example.test';
UPDATE "Users" SET "PhoneNumber" = '+201000000003', "PhoneNumberConfirmed" = true
WHERE "Email" = 'owner.future@example.test';
INSERT INTO "AcademyMemberships" ("Id", "AcademyId", "UserId", "Role", "ContextRevision", "IsActive", "AllBranches", "AllSports", "UseActionGrants", "PermissionTemplateCustomized", "CreatedAtUtc", "UpdatedAtUtc")
SELECT 'e734c8bd-03b8-4f37-847e-2c34b1b19202', '20000000-0000-0000-0000-000000000002', "Id", 'AcademyOwner', 1, true, true, true, false, false, now(), now()
FROM "Users" WHERE "Email" = 'owner.nogoom@example.test'
ON CONFLICT ("Id") DO NOTHING;
COMMIT;
