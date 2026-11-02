import { test, expect } from "@playwright/test";
test("A golden season saves five service months for four monthly payments", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const options = await (await page.request.get("/api/v1/manage/structure/options")).json(); const sport = options.sports.find((s: { arabicName: string }) => s.arabicName.includes("سباحة"));
  await page.goto("/dashboard/subscriptions/plans/new"); const name = `موسم ذهبي تحقق ${Date.now()}`;
  await page.getByLabel("الاسم العربي", { exact: true }).fill(name); await page.getByRole("combobox", { name: "الرياضة", exact: true }).selectOption(sport.id);
  await page.getByRole("combobox", { name: "طريقة التدريب", exact: true }).selectOption("Season"); await page.getByLabel("السعر الشهري المرجعي").fill("600.00"); await page.getByLabel("عدد الأشهر المدفوعة").fill("4"); await expect(page.getByLabel("أشهر الخدمة")).toHaveValue("5"); await expect(page.getByLabel(/السعر \(/)).toHaveValue("2400.00");
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.evaluate(() => scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`season-${width}.png`), fullPage: true, animations: "disabled" }); }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/subscriptions/plans")); await page.getByRole("button", { name: "حفظ الباقة" }).click(); expect((await saved).status()).toBe(201); await page.waitForURL("**/subscriptions/plans");
  const plans = await (await page.request.get("/api/v1/subscriptions/plans")).json(); const plan = plans.find((p: { arabicName: string }) => p.arabicName === name); expect(plan.durationMonths).toBe(5); expect(plan.durationDays).toBeNull(); expect(plan.trainingFormat).toBe("Season");
  await page.goto(`/dashboard/subscriptions/plans/${plan.id}`); await expect(page.getByText("5 شهر تقويمي · 4 أشهر مدفوعة")).toBeVisible();
});
