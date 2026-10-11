import { expect, test } from "@playwright/test";

test("Platform has a separate login, auditable branding and no implicit access to academy children", async ({ page }, info) => {
  await page.goto("/platform/login");
  await page.getByLabel("البريد الإلكتروني").fill("platform.demo@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByLabel("رمز التحقق").fill("246810");
  await page.getByRole("button", { name: "دخول المنصة", exact: true }).click();
  await page.waitForURL("**/platform");
  await expect(page.getByRole("heading", { name: "إدارة المنصة", exact: true })).toBeVisible();
  expect((await page.request.get("/api/v1/guardian/children")).status()).toBe(401);
  await page.locator(".platform-academy").filter({ hasText: "nogoom" }).click();
  const identity = page.locator("section").filter({ has: page.getByRole("heading", { name: /^هوية / }) });
  await identity.getByRole("button", { name: "حفظ الهوية" }).click();
  await expect(page.getByRole("status")).toHaveText("تم حفظ هوية الأكاديمية.");
  await expect(page.locator(".platform-audit")).toContainText("تحديث الهوية");
  await expect(page.getByRole("checkbox", { name: "إعلانات المجتمع" })).not.toBeChecked();
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
  await page.screenshot({ path: info.outputPath("platform-desktop.png"), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: info.outputPath("platform-mobile.png"), fullPage: true });
  await page.getByRole("button", { name: "خروج", exact: true }).click();
  await page.waitForURL("**/platform/login");
  expect((await page.request.get("/api/v1/platform/me")).status()).toBe(401);
});

test("Owner cannot use tenant session to change platform entitlements", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  expect((await page.request.get("/api/v1/platform/academies")).status()).toBe(401);
  await page.goto("/platform");
  await page.waitForURL("**/platform/login");
  await expect(page.getByRole("heading", { name: "دخول مشغّل المنصة" })).toBeVisible();
});
