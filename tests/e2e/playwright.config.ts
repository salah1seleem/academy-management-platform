import { defineConfig, devices } from "@playwright/test";

const webPort = process.env.ACADEMY_E2E_WEB_PORT ?? "3000";
const apiPort = process.env.ACADEMY_E2E_API_PORT ?? "5080";
const webUrl = `http://127.0.0.1:${webPort}`;
const apiUrl = `http://127.0.0.1:${apiPort}`;
// Explicit opt-in fixtures for the entire regression suite. Never enable these
// in the manual Football profile or through application defaults.
const regressionSeeds = [
  "CashRegisters", "CollectionFunds", "Facilities", "ResourceTimetable",
  "UnassignedEnrollment", "Observation", "Visit", "CoachOverride",
  "GroupPlacement", "PastAttendance", "AttendanceDraft",
  "ProviderQuarantine",
  "SettlementComparisons",
  "FamilyRenewal",
  "FreeTraining",
  "ReceivableCredit",
  "CrossBranchCash",
].map(name => `Demo__${name}SeedEnabled=true`).join(" ");

const localConnectionFallback = process.env.ConnectionStrings__Default
  ? ""
  : "ConnectionStrings__Default='Host=localhost;Port=5432;Database=academy;Username=academy;Password=academy_local_only' ";

export default defineConfig({
  testDir: "./specs",
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: "line",
  use: {
    baseURL: webUrl,
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "mobile-chromium",
      use: { ...devices["Pixel 7"] },
    },
  ],
  webServer: [
    {
      command:
        `${localConnectionFallback}ASPNETCORE_ENVIRONMENT=Demo Demo__SeedProfile=LegacyRegression Demo__SeedEnabled=true ${regressionSeeds} Demo__FixedOtpEnabled=true Demo__FixedOtp=246810 Demo__StaffPassword='Demo-Only-123!' Demo__ReferenceDate=2026-09-28 Payments__InternalTest__Enabled=true Payments__InternalTest__SigningKey='Demo-Test-Signing-Key-Only-123456' ASPNETCORE_URLS=${apiUrl} dotnet run --project ../../apps/api/src/Academy.Api --no-launch-profile`,
      url: `${apiUrl}/health/live`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: `API_INTERNAL_URL=${apiUrl} ACADEMY_WEB_DIST_DIR=.next-e2e npm run build --prefix ../../apps/web && API_INTERNAL_URL=${apiUrl} ACADEMY_WEB_DIST_DIR=.next-e2e npm run start --prefix ../../apps/web -- --hostname 127.0.0.1 --port ${webPort}`,
      url: webUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
    },
  ],
});
