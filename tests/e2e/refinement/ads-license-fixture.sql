-- Synthetic acceptance fixture only. Not a purchase, platform provisioning flow,
-- production license or change to the manual Demo. All ordinary API gates remain.
BEGIN;
DO $$ BEGIN
  IF current_database() <> 'academy_v4_refinement_20261020' OR NOT EXISTS (
    SELECT 1 FROM "Academies" WHERE "Id"='30000000-0000-0000-0000-000000000003'
      AND "Slug"='football-demo' AND "EnglishName"='Football Demo Dataset v1'
  ) THEN RAISE EXCEPTION 'This fixture requires the explicitly isolated marked V41 database'; END IF;
END $$;
UPDATE "AcademyFeatureEntitlements" SET "Enabled"=true,"PlanCode"='Premium'
WHERE "AcademyId"='30000000-0000-0000-0000-000000000003' AND "Feature"='community_ads';
COMMIT;
