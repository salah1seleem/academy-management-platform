import { defineConfig, devices } from "@playwright/test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

// Separate deterministic tenant/profile; never reuse a developer's running API or demo DB implicitly.
if (!process.env.ConnectionStrings__Default) throw new Error("Explicit isolated Demo connection required.");
const apiUrl = `http://127.0.0.1:${process.env.ACADEMY_E2E_API_PORT ?? "5190"}`;
const webPort = process.env.ACADEMY_E2E_WEB_PORT ?? "3190";
const webUrl = `http://127.0.0.1:${webPort}`;
// Next rewrites are baked at build time. Runtime API_INTERNAL_URL alone cannot
// prevent a stale production build from reaching the developer's manual Demo.
const manifest = JSON.parse(readFileSync(resolve(__dirname, "../../apps/web/.next/routes-manifest.json"), "utf8"));
const apiRules = [...manifest.rewrites.beforeFiles, ...manifest.rewrites.afterFiles, ...manifest.rewrites.fallback]
  .filter((r: { source: string }) => r.source === "/api/:path*");
if (apiRules.length !== 1 || apiRules[0].destination !== `${apiUrl}/api/:path*`)
  throw new Error(`Build Web with API_INTERNAL_URL=${apiUrl} before running isolated Football E2E; refusing a different API destination.`);
export default defineConfig({
  testDir: "./football", testIgnore: "**/v3-staff-passkeys.spec.ts", workers: 1, fullyParallel: false, retries: 0, reporter: "line",
  use: { baseURL: webUrl, ...devices["Pixel 7"], trace: "retain-on-failure" },
  webServer: [
    {
      command: `ASPNETCORE_ENVIRONMENT=Demo Demo__SeedProfile=Football Demo__SeedEnabled=true Demo__PrivateServiceSeedEnabled=true Demo__PostContractPrivateSeedEnabled=true Demo__FixedOtpEnabled=true Demo__FixedOtp=246810 Demo__StaffPassword='Demo-Only-123!' Demo__ReferenceDate=2026-09-28 Payments__InternalTest__Enabled=true Payments__InternalTest__SigningKey='Demo-Test-Signing-Key-Only-123456' ASPNETCORE_URLS=${apiUrl} dotnet run --project ../../apps/api/src/Academy.Api --no-launch-profile --no-build`,
      url: `${apiUrl}/health/ready`, reuseExistingServer: false, timeout: 120_000,
    },
    {
      command: `API_INTERNAL_URL=${apiUrl} npm run start --prefix ../../apps/web -- --hostname 127.0.0.1 --port ${webPort}`,
      url: `${webUrl}/login`, reuseExistingServer: false, timeout: 120_000,
    },
  ],
});
