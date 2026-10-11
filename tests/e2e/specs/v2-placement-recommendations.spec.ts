import { expect, test } from "@playwright/test";
test("Owner sees explained eligible alternatives and keeps the selected child through explicit placement preview", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const player = "5a000000-0000-0000-0000-000000000020", enrollment = "5a000000-0000-0000-0000-000000000021";
  const before = await page.request.get(`/api/v1/people/players/${player}`).then(r => r.json()); expect(before.enrollments[0].trainingGroupId).toBeNull();
  await page.goto(`/dashboard/players/${player}?tab=training`); await page.getByText("اقتراح مجموعات مناسبة", { exact: true }).click(); await page.getByRole("button", { name: "عرض البدائل المؤهلة" }).click();
  const choices = page.getByRole("region", { name: "بدائل التسكين" }); await expect(choices.getByRole("link", { name: "فتح المجموعة ومراجعة التسكين" })).toHaveCount(1); await expect(choices.getByText("مطابقة للفرع والرياضة والعمر والمستوى المراجع")).toBeVisible();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.locator(".placement-recommendations").screenshot({ path: info.outputPath(`recommendations-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
  await choices.getByRole("link").click(); await expect(page).toHaveURL(new RegExp(`candidateId=${enrollment}`)); const placement = page.getByRole("region", { name: "التسكين الجماعي" }); await expect(placement.getByRole("checkbox")).toHaveCount(1); await expect(placement.getByRole("checkbox")).toBeChecked(); await expect(placement.getByRole("button", { name: "تأكيد تسكين الدفعة" })).toHaveCount(0);
  await placement.getByRole("button", { name: "معاينة التسكين" }).click(); await expect(placement.getByRole("button", { name: "تأكيد تسكين الدفعة" })).toBeEnabled();
  // This smoke journey is intentionally read-only: a suggestion never consumes a seat.
  const after = await page.request.get(`/api/v1/people/players/${player}`).then(r => r.json()); expect(after.enrollments[0].trainingGroupId).toBeNull();
  await page.goto("/dashboard/players/5a000000-0000-0000-0000-000000000030?tab=training"); await page.getByText("اقتراح مجموعات مناسبة", { exact: true }).click(); await page.getByRole("button", { name: "عرض البدائل المؤهلة" }).click(); await expect(page.getByRole("region", { name: "بدائل التسكين" }).getByText(/لا توجد مجموعة تستوفي/)).toBeVisible();
});
