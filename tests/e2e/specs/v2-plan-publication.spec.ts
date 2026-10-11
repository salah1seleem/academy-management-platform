import { expect, test } from "@playwright/test";

test("A swimming plan is published to one branch, appears in New Player and quotes exactly the saved price", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const options = await (await page.request.get("/api/v1/manage/structure/options")).json();
  const sport = options.sports.find((s: { arabicName: string }) => s.arabicName === "السباحة"); const [a, b] = options.branches; expect(sport).toBeTruthy(); expect(b).toBeTruthy();
  await page.goto("/dashboard/subscriptions/plans/new"); const name = `سباحة تحقق المتصفح ${Date.now()}`;
  await page.getByLabel("الاسم العربي", { exact: true }).fill(name); await page.getByRole("combobox", { name: "الرياضة", exact: true }).selectOption(sport.id); await page.getByRole("combobox", { name: "نوع الخدمة", exact: true }).selectOption("Combined"); await page.getByLabel(/السعر \(/).fill("600.00"); await page.getByLabel("عدد الأيام").fill("30"); await page.getByLabel("عدد الحصص").fill("8");
  await page.getByLabel("كل فروع الأكاديمية").uncheck(); await page.getByLabel(a.arabicName, { exact: true }).check();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`plan-publication-${width}.png`), fullPage: true }); }
  const created = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/subscriptions/plans")); await page.getByRole("button", { name: "حفظ الباقة" }).click(); expect((await created).status()).toBe(201); await page.waitForURL("**/subscriptions/plans"); await expect(page.getByRole("heading", { name })).toBeVisible();
  const plans = await (await page.request.get("/api/v1/subscriptions/plans")).json(); const plan = plans.find((p: { arabicName: string }) => p.arabicName === name);
  await page.goto("/dashboard/players/new"); await page.getByRole("combobox", { name: "الرياضة", exact: true }).selectOption(sport.id); await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(a.id); await page.getByRole("combobox", { name: "الباقة", exact: true }).selectOption(plan.id); await expect(page.getByRole("combobox", { name: "الباقة", exact: true })).toHaveValue(plan.id);
  await page.getByLabel("رقم الهاتف", { exact: true }).fill("010" + String(Date.now()).slice(-8)); await page.getByRole("button", { name: "حساب المبلغ المستحق" }).click(); await expect(page.getByText(/المبلغ المطلوب تحصيله:/)).toContainText("600");
  await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(b.id); await expect(page.getByRole("option", { name: new RegExp(name) })).toHaveCount(0); await expect(page.getByRole("combobox", { name: "الباقة", exact: true })).toHaveValue(""); await expect(page.getByText(/المبلغ المطلوب تحصيله:/)).toHaveCount(0);
  await page.goto(`/dashboard/subscriptions/plans/${plan.id}`); await expect(page.getByRole("heading", { name: "تاريخ الأسعار وشروط الخدمة" })).toBeVisible(); await expect(page.getByText(/نسخة 1/)).toBeVisible();
});
