import { defineConfig, devices } from "@playwright/test";

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
    baseURL: "http://127.0.0.1:3000",
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
        `${localConnectionFallback}ASPNETCORE_ENVIRONMENT=Demo Demo__SeedEnabled=true Demo__FixedOtpEnabled=true Demo__FixedOtp=246810 Demo__StaffPassword='Demo-Only-123!' Demo__ReferenceDate=2026-09-28 Payments__InternalTest__Enabled=true Payments__InternalTest__SigningKey='Demo-Test-Signing-Key-Only-123456' ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet run --project ../../apps/api/src/Academy.Api --no-launch-profile`,
      url: "http://127.0.0.1:5080/health/live",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: "npm run dev --prefix ../../apps/web -- --hostname 127.0.0.1",
      url: "http://127.0.0.1:3000",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
});
