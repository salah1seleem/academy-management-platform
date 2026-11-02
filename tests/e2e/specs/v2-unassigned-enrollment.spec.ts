import { expect, test } from "@playwright/test";
test("one child retains football and waits for swimming, with safe intake and no null-group navigation", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const id = "59000000-0000-0000-0000-000000000001";
  const before = await page.request.get(`/api/v1/people/players/${id}`).then(r => r.json());
  expect(before.enrollments).toHaveLength(2); expect(before.enrollments.find((e: { trainingGroupId: string | null }) => !e.trainingGroupId).placement).toBe("Unassigned");
  await page.goto(`/dashboard/players/${id}?tab=training`); await expect(page.getByText("في انتظار التسكين · لا توجد مجموعة مرتبطة")).toBeVisible();
  await expect(page.locator('a[href*="null"]')).toHaveCount(0); await expect(page.getByRole("link", { name: "ملف المجموعة وجدولها" })).toHaveCount(1);
  await page.getByText("إضافة رياضة في انتظار التسكين", { exact: true }).click(); await page.getByLabel("رياضة التسجيل").selectOption("32000000-0000-0000-0000-000000000002"); await page.getByLabel("فرع التسجيل").selectOption(before.enrollments.find((e: { placement: string }) => e.placement === "Unassigned").branchId);
  const response = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("unassigned-enrollments")); await page.getByRole("button", { name: "حفظ في انتظار التسكين" }).click(); const result = await response; expect(result.ok(), await result.text()).toBe(true); const replay = await result.json(); expect(replay.replayed).toBe(true); expect(replay.subscriptionCreated).toBe(false);
  const after = await page.request.get(`/api/v1/people/players/${id}`).then(r => r.json()); expect(after.enrollments).toHaveLength(2);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.screenshot({ path: info.outputPath(`unassigned-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
});
