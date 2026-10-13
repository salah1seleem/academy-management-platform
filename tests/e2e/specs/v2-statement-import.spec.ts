import { expect, test } from "@playwright/test";

test("Owner imports a bounded CSV statement atomically and replays without changing receipts", async ({ page }, info) => {
  const root = "/api/v1/finance/settlement-comparisons", route = "/dashboard/finance/settlement-comparisons";
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const fixture = await page.request.get(`${root}/85000020-0000-0000-0000-000000000001`).then(r => r.json());
  const before = await page.request.get(`/api/v1/subscriptions/receipts/${fixture.receiptId}`).then(r => r.json());
  await page.goto(route); await page.getByRole("link", { name: "استيراد كشف CSV", exact: true }).click();
  await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(fixture.branchId); await page.getByRole("combobox", { name: "المزود المسجل", exact: true }).selectOption(fixture.provider);
  await page.getByLabel("اسم الحساب المختصر", { exact: true }).fill(`DEMO-CSV-${Date.now()}`); await page.getByLabel("مرجع الكشف", { exact: true }).fill(`كشف اصطناعي CSV-${Date.now()}`);
  const template = await page.request.get(`${root}/import/template`); expect(template.ok()).toBe(true); expect(template.headers()["content-type"]).toContain("text/csv");
  const header = "line_reference,payment_reference,settlement_date,gross,fee,net,currency,note";
  const csv = `${header}\n1,${fixture.paymentReference},2026-09-28,${fixture.gross},5,${(Number(fixture.gross) - 5).toFixed(2)},${fixture.currency},مراجعة اصطناعية\n2,DEMO-UNKNOWN-CSV,2026-09-28,100,0,100,EGP,مرجع غير موجود`;
  await page.getByLabel("ملف الكشف CSV").setInputFiles({ name: "demo-statement.csv", mimeType: "text/csv", buffer: Buffer.from(csv) });
  await expect(page.getByText("الملف المختار: demo-statement.csv")).toBeVisible(); await page.getByRole("button", { name: "معاينة الكشف", exact: true }).click();
  await expect(page.getByRole("heading", { name: "3. راجع نتائج الكشف" })).toBeVisible(); await expect(page.getByText("لا يوجد تحصيل مطابق للمرجع", { exact: true })).toBeVisible(); await expect(page.getByLabel("ملف الكشف CSV")).toBeDisabled();
  expect(await page.locator("fieldset.form-shell").evaluate(el => getComputedStyle(el).display)).toBe("grid");
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); await page.evaluate(() => scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`statement-import-${width}.png`), fullPage: true, animations: "disabled" }); }
  const write = page.waitForRequest(r => r.method() === "POST" && new URL(r.url()).pathname === `${root}/import`); await page.getByRole("button", { name: "حفظ نتائج الكشف كاملة", exact: true }).click(); const body = (await write).postDataJSON();
  await expect(page.getByRole("heading", { name: "تم حفظ مقارنة الكشف" })).toBeVisible(); await expect(page.getByText("2 سطر جديد · 0 سطر محفوظ سابقًا · دون تغيير التحصيلات", { exact: true })).toBeVisible();
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const retry = await page.request.post(`${root}/import`, { headers: { "X-CSRF-TOKEN": csrf.token }, data: body }); expect(retry.ok(), await retry.text()).toBe(true); expect((await retry.json()).reused).toBe(2);
  expect(await page.request.get(`/api/v1/subscriptions/receipts/${fixture.receiptId}`).then(r => r.json())).toEqual(before);
  await page.getByRole("link", { name: "عرض المقارنة", exact: true }).first().click(); await expect(page.getByRole("heading", { name: "مطابق يدويًا" })).toBeVisible(); await expect(page.getByRole("link", { name: "الإيصال الأصلي" })).toBeVisible();
});
