import { expect, test } from "@playwright/test";

test("Owner opens an overdue synthetic family follow-up on mobile", async ({ page }, info) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  const followup = page.getByRole("link", { name: /ملاحظات تجاوزت موعد المتابعة/ });
  await expect(followup).toBeVisible();
  await expect(followup).toContainText("1");
  await followup.click();
  await expect(page).toHaveURL(/\/dashboard\/feedback\?overdue=true/);
  await expect(page.getByRole("combobox", { name: "الموعد" })).toHaveValue("overdue");
  await expect(page.getByRole("table", { name: "ملاحظات أولياء الأمور" })).toContainText("شكوى اصطناعية: انتظار مراجعة إشعار تغيير التدريب");
  await expect(page.getByRole("table", { name: "ملاحظات أولياء الأمور" })).toContainText("متأخرة عن الموعد");
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath("owner-overdue-feedback-390.png"), fullPage: true, animations: "disabled" });
});
