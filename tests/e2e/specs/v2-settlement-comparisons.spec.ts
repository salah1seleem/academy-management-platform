import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Owner previews and saves a manual statement comparison without changing the original receipt", async ({ page }, info) => {
  const root = "/api/v1/finance/settlement-comparisons", route = "/dashboard/finance/settlement-comparisons";
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const fixtureResponse = await page.request.get(`${root}/85000020-0000-0000-0000-000000000001`); expect(fixtureResponse.ok(), await fixtureResponse.text()).toBe(true); const fixture = await fixtureResponse.json();
  const before = await page.request.get(`/api/v1/subscriptions/receipts/${fixture.receiptId}`).then(r => r.json());
  await page.goto(route); await page.getByRole("button", { name: "مسح الفلاتر" }).click(); await expect(page.getByLabel("من تاريخ")).toHaveValue(""); await expect(page.getByLabel("إلى تاريخ")).toHaveValue(""); await expect(page.getByText("مطابق يدويًا", { exact: true }).first()).toBeVisible();
  await page.getByRole("link", { name: "مقارنة سطر كشف", exact: true }).click();
  await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(fixture.branchId); await page.getByRole("combobox", { name: "المزود المسجل", exact: true }).selectOption(fixture.provider);
  for (const [label, value] of [["اسم الحساب المختصر", `DEMO-${Date.now()}`], ["مرجع الكشف", `كشف تجريبي-${Date.now()}`], ["مرجع السطر", "1"], ["مرجع الدفع في الكشف", fixture.paymentReference], ["تاريخ التسوية في الكشف", "2026-09-28"], ["إجمالي الكشف", fixture.gross], ["الرسوم الواردة", "5"], ["صافي الكشف", (Number(fixture.gross) - 5).toFixed(2)]]) await page.getByLabel(label, { exact: true }).fill(value);
  await page.getByRole("button", { name: "معاينة المقارنة" }).click(); await expect(page.getByRole("heading", { name: "مطابق يدويًا" })).toBeVisible(); await expect(page.getByLabel("مرجع الكشف", { exact: true })).toBeDisabled();
  expect(await page.locator("fieldset.form-shell").evaluate(el => getComputedStyle(el).display)).toBe("grid");
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`statement-preview-${width}.png`), fullPage: true, animations: "disabled" }); }
  const sent = page.waitForRequest(r => r.method() === "POST" && new URL(r.url()).pathname === root); await page.getByRole("button", { name: "حفظ نتيجة المقارنة", exact: true }).click(); const body = (await sent).postDataJSON();
  await expect(page.getByText("تم حفظ نتيجة المقارنة دون تغيير التحصيل أو الاشتراك.", { exact: true })).toBeVisible(); await page.getByRole("link", { name: "عرض المقارنة المحفوظة" }).click(); await expect(page.getByRole("heading", { name: "مطابق يدويًا" })).toBeVisible(); await expect(page.getByRole("link", { name: "الإيصال الأصلي" })).toBeVisible();
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const retries = await Promise.all([page.request.post(root, { headers: { "X-CSRF-TOKEN": csrf.token }, data: body }), page.request.post(root, { headers: { "X-CSRF-TOKEN": csrf.token }, data: body })]); for (const r of retries) expect(r.ok(), await r.text()).toBe(true); expect((await retries[0].json()).id).toBe((await retries[1].json()).id);
  expect(await page.request.get(`/api/v1/subscriptions/receipts/${fixture.receiptId}`).then(r => r.json())).toEqual(before);
  const guardian = await page.context().browser()!.newContext(); const p = await guardian.newPage(); await guardianLogin(p); expect((await p.request.get(root)).status()).toBe(403); await guardian.close();
});
