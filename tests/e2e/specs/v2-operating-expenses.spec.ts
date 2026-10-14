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

test("Owner can prepare a shared branch cost without selecting a fake group", async ({ page }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  await page.goto("/dashboard/finance/operating-expenses");
  await expect(page.getByRole("heading", { name: "تسجيل مصروف" })).toBeVisible();
  const form = page.locator("form.form-shell");
  await form.locator("select").nth(0).selectOption({ index: 1 });
  await form.locator("select").nth(1).selectOption("Branch");
  await expect(form.getByLabel("المجموعة")).toHaveCount(0);
  await form.getByLabel(/المبلغ/).fill("100");
  await expect(form.getByRole("button", { name: "حفظ مسودة المصروف" })).toBeEnabled();
  await form.locator("select").nth(1).selectOption("Sport");
  await expect(form.getByLabel("الرياضة")).toBeVisible();
  await page.setViewportSize({ width: 390, height: 900 });
  await page.screenshot({ path: info.outputPath("shared-expense-scope-390.png"), fullPage: true, animations: "disabled" });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});
