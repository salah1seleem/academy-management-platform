import { test, expect } from "@playwright/test";

test("Owner sees a stored group cost share without treating it as a collection", async ({ page }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  await page.goto("/dashboard/finance/operating-expenses");
  await expect(page.getByRole("heading", { name: "المصروفات التشغيلية" })).toBeVisible();
  await page.getByRole("button", { name: "مسح الفلاتر" }).click();
  await expect(page.getByRole("table", { name: "أنصبة تكلفة المجموعات" })).toContainText("200");
  await expect(page.getByRole("table", { name: "أنصبة تكلفة المجموعات" })).toContainText("مجموعة");
  await page.setViewportSize({ width: 390, height: 900 });
  await page.screenshot({ path: info.outputPath("operating-expenses-390.png"), fullPage: true, animations: "disabled" });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});
