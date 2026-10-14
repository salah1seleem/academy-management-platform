import { expect, test } from "@playwright/test";

test("Owner opens stored operational signals and private family feedback", async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  await expect(page.getByRole("heading", { name: "مجموعات تحتاج متابعة" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "عبء المدربين ورأي أولياء الأمور" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "المطالبات والمصروفات" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "ملاحظات الأسر والرقابة" })).toBeVisible();
  await page.screenshot({ path: info.outputPath("owner-operations.png"), fullPage: true, animations: "disabled" });
  await page.getByRole("link", { name: "تفاصيل تقييمات الأسر" }).click();
  await expect(page).toHaveURL(/\/dashboard\/feedback\?kind=Rating/);
  await expect(page.getByRole("heading", { name: "ملاحظات أولياء الأمور" })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "النوع" })).toHaveValue("Rating");
});
