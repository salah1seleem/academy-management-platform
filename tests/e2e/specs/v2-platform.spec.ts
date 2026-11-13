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
  await page.getByRole("button", {name:"أكاديمية النجوم الرياضية nogoom-demo",exact:true}).click();
  await page.getByRole("tab", { name: "الهوية", exact: true }).click();
  await page.getByRole("tabpanel", { name: "الهوية", exact: true }).getByRole("button", { name: "حفظ الهوية والشعار", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("تم حفظ الهوية. الشعار نفسه يغذي جميع شاشات التطبيق.");
  await page.getByRole("tab", { name: "سجل التغييرات", exact: true }).click();
  await expect(page.getByRole("tabpanel", { name: "سجل التغييرات", exact: true })).toContainText("تحديث الهوية");
  const licenses = await page.request.get("/api/v1/platform/academies/10000000-0000-0000-0000-000000000001/entitlements");
  expect(licenses.ok()).toBe(true);
  const features: { feature: string; enabled: boolean }[] = await licenses.json();
  expect(features.some(feature => feature.feature === "community_ads" && feature.enabled)).toBe(false);
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
  await page.getByRole("button", { name: "تسجيل الدخول" }).click();
  await page.waitForURL("**/dashboard");
  expect((await page.request.get("/api/v1/platform/academies")).status()).toBe(401);
  await page.goto("/platform");
  await page.waitForURL("**/platform/login");
  await expect(page.getByRole("heading", { name: "دخول مشغّل المنصة" })).toBeVisible();
});
