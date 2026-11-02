import { expect, test } from "@playwright/test";

test("Active dashboard identity cannot be overwritten; explicit logout then login starts an administrator session", async ({ page }, info) => {
  async function login(email: string) {
    await page.getByLabel("البريد الإلكتروني").fill(email);
    await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await page.getByRole("button", { name: "دخول آمن" }).click();
  }
  await page.goto("/login"); await login("owner.nogoom@example.test"); await page.waitForURL("**/dashboard");
  await page.goto("/login"); await login("admin.nogoom@example.test");
  const sessionAlert = page.getByRole("alert").filter({ hasText: "توجد جلسة دخول نشطة" });
  await expect(sessionAlert).toContainText("سجّل الخروج أولًا");
  expect((await page.request.get("/api/v1/me").then(r => r.json())).role).toBe("AcademyOwner");
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.screenshot({ path: info.outputPath(`session-lifecycle-${width}.png`), animations: "disabled" });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  }
  await Promise.all([page.waitForEvent("load"), page.getByRole("button", { name: "تسجيل خروج الحساب الحالي" }).click()]);
  await expect(sessionAlert).toHaveCount(0);
  expect((await page.request.get("/api/v1/me")).status()).toBe(401);
  await login("admin.nogoom@example.test"); await page.waitForURL("**/dashboard");
  expect((await page.request.get("/api/v1/me").then(r => r.json())).role).toBe("AcademyAdmin");
});
