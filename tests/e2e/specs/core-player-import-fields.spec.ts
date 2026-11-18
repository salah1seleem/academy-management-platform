import { expect, test } from "@playwright/test";
import { placementRead, placementWrite, setPlacementAppearance } from "../support/placement";

test.describe.configure({ retries: 0 });
test("Visual inventory of Owner import before field and correction cases", async ({ page }, info) => {
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
  await page.goto("/dashboard/import");
  await expect(page.getByRole("heading", { name: "استيراد بيانات الأكاديمية", exact: true })).toBeVisible();
  await placementRead(page.request, "/api/v1/people/import/schema");
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    for (const mode of ["dark", "light"] as const) {
      await setPlacementAppearance(page, mode);
      await page.screenshot({ path: info.outputPath(`owner-import-before-${mode}-${width}.png`), fullPage: true, animations: "disabled" });
    }
  }
  await page.getByText("الحقول المطلوبة والاختيارية", { exact: true }).click();
  await page.locator("details").filter({ has: page.getByText("الحقول المطلوبة والاختيارية", { exact: true }) }).screenshot({ path: info.outputPath("owner-import-schema-before.png"), animations: "disabled" });
});
