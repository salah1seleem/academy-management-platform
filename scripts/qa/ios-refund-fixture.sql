\ir ios-proof-fixture.sql
BEGIN;
DO $$ BEGIN
 IF current_database() <> 'academy_ios_qa' THEN
  RAISE EXCEPTION 'Disposable iOS QA database only';
 END IF;
END $$;
UPDATE "AcademyMemberships" SET "UseActionGrants" = true
WHERE "AcademyId" = '10000000-0000-0000-0000-000000000001'
AND "UserId" IN (SELECT "Id" FROM "Users" WHERE "Email" = 'admin.nogoom@example.test');
UPDATE "StaffActionGrants" SET "IsActive" = false
WHERE "MembershipId" IN (
 SELECT "Id" FROM "AcademyMemberships"
 WHERE "AcademyId" = '10000000-0000-0000-0000-000000000001'
 AND "UserId" IN (SELECT "Id" FROM "Users" WHERE "Email" = 'admin.nogoom@example.test')
);
COMMIT;
