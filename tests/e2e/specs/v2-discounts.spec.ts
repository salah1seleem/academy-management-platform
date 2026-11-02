import { expect, test } from "@playwright/test";

test("Owner configures a scoped fixed discount without mandatory description and uses its exact quote", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const options = await (await page.request.get("/api/v1/discount-rules/options")).json();
  const plan = options.plans.find((p: { id: string }) => p.id === "a2030000-0000-0000-0000-000000000001"); expect(plan).toBeTruthy(); const branch = plan.branchIds[0];
  const name = `خصم متصفح اصطناعي ${Date.now()}`;
  await page.goto("/dashboard/discounts"); await page.getByRole("button", { name: "إضافة خصم", exact: true }).click(); await page.getByLabel("اسم الخصم").fill(name);
  await page.getByLabel("نوع الخصم").selectOption("FixedAmount"); await page.getByLabel("قيمة الخصم").fill("100.00");
  await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(branch); await page.getByRole("combobox", { name: "الرياضة", exact: true }).selectOption(plan.sportId); await page.getByRole("combobox", { name: "الباقة", exact: true }).selectOption(plan.id);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.evaluate(() => window.scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`discount-editor-${width}.png`), fullPage: true, animations: "disabled" }); }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/discount-rules")); await page.getByRole("button", { name: "حفظ العرض" }).click(); expect((await saved).status()).toBe(200);
  await expect(page.getByRole("button", { name: `سجل ${name}`, exact: true })).toBeVisible(); await page.getByRole("button", { name: `سجل ${name}`, exact: true }).click(); await expect(page.getByRole("heading", { name: "سجل نسخ العرض" })).toBeVisible(); await expect(page.getByText(`نسخة 1 · ${name} · 100 جنيه`)).toBeVisible();
  const catalog = await (await page.request.get("/api/v1/discount-rules/")).json(); const rule = catalog.items.find((r: { arabicName: string }) => r.arabicName === name);
  await page.goto("/dashboard/players/new"); await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(branch); await page.getByRole("combobox", { name: "الباقة", exact: true }).selectOption(plan.id); await page.getByRole("combobox", { name: "عرض دائم للاعب", exact: false }).selectOption(rule.id);
  await page.getByLabel("رقم الهاتف", { exact: true }).fill("010" + String(Date.now()).slice(-8)); const quoted = page.waitForResponse(r => r.url().endsWith("/registration-quote")); await page.getByRole("button", { name: "حساب المبلغ المستحق" }).click(); const quote = await (await quoted).json(); expect(quote.finalAmount).toBe("500.00"); await expect(page.getByText(/المبلغ المطلوب تحصيله:/)).toContainText("500");
});
