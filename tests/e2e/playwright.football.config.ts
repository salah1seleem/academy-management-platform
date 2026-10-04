import { defineConfig, devices } from "@playwright/test";

// Separate deterministic tenant/profile; never reuse a developer's running API or demo DB implicitly.
if (!process.env.ConnectionStrings__Default) throw new Error("Explicit isolated Demo connection required.");
export default defineConfig({
  testDir: "./football", workers: 1, fullyParallel: false, retries: 0, reporter: "line",
  use: { baseURL: "http://127.0.0.1:3190", ...devices["Pixel 7"], trace: "retain-on-failure" },
  webServer: [
    {
      command: "ASPNETCORE_ENVIRONMENT=Demo Demo__SeedProfile=Football Demo__SeedEnabled=true Demo__FixedOtpEnabled=true Demo__FixedOtp=246810 Demo__StaffPassword='Demo-Only-123!' Demo__ReferenceDate=2026-09-28 Payments__InternalTest__Enabled=true Payments__InternalTest__SigningKey='Demo-Test-Signing-Key-Only-123456' ASPNETCORE_URLS=http://127.0.0.1:5190 dotnet run --project ../../apps/api/src/Academy.Api --no-launch-profile --no-build",
      url: "http://127.0.0.1:5190/health/ready", reuseExistingServer: false, timeout: 120_000,
    },
    {
      command: "API_INTERNAL_URL=http://127.0.0.1:5190 npm run start --prefix ../../apps/web -- --hostname 127.0.0.1 --port 3190",
      url: "http://127.0.0.1:3190/login", reuseExistingServer: false, timeout: 120_000,
    },
  ],
});
