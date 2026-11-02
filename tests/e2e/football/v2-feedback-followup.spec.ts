import { expect, test } from "@playwright/test";

test("Owner opens an overdue synthetic family follow-up on mobile", async ({ page }, info) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  const overdue = await (await page.request.get('/api/v1/manage/feedback?overdue=true')).json();
  expect(overdue.length).toBeGreaterThan(0);
  const menu = page.getByRole("button", { name: "فتح القائمة", exact: true });
  if (await menu.isVisible()) await menu.click();
  await page.getByRole("navigation", { name: "مساحة الإدارة" }).getByRole("link", { name: "الشكاوى والاقتراحات", exact: true }).click();
  await page.getByRole("combobox", { name: "النوع", exact: true }).selectOption("");
  await page.getByRole("combobox", { name: "الحالة", exact: true }).selectOption("");
  await page.getByText("الموضوع والاستعجال وفترة الملاحظات", { exact: true }).click();
  await page.getByRole("combobox", { name: "الموعد", exact: true }).selectOption("overdue");
  await expect(page.getByRole("combobox", { name: "الموعد", exact: true })).toHaveValue("overdue");
  await expect(page.getByRole("table", { name: "ملاحظات أولياء الأمور" }).getByRole("rowgroup").getByRole("row")).toHaveCount(overdue.length);
  await expect(page.getByRole("table", { name: "ملاحظات أولياء الأمور" })).toContainText("شكوى اصطناعية: انتظار مراجعة إشعار تغيير التدريب");
  await expect(page.getByRole("table", { name: "ملاحظات أولياء الأمور" })).toContainText("متأخرة عن الموعد");
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath("owner-overdue-feedback-390.png"), fullPage: true, animations: "disabled" });
});
