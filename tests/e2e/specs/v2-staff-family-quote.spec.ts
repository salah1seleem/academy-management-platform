import { test, expect } from "@playwright/test";

const expectedEnrollments = new Set([
  "84000014-0000-0000-0003-000000000001",
  "84000014-0000-0000-0003-000000000002",
]);

test("Owner finds a trusted family and reviews both children without creating a payment or receipt", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول" }).click(); await page.waitForURL("**/dashboard**");
  await page.goto("/dashboard/subscriptions/renew/new"); await page.getByRole("link", { name: "تجديد عدة أبناء", exact: true }).click();
  await page.getByLabel("اسم ولي الأمر أو رقم هاتفه").fill("01000000014"); await page.getByRole("button", { name: "البحث عن الأسرة" }).click(); await page.getByRole("button", { name: "اختيار أسرة التجديد الاصطناعية", exact: true }).click();
  const families = await page.request.get("/api/v1/manage/family-renewals/quotes/families?search=01000000014").then(r => r.json()); const id = families.items[0].id;
  const options = await page.request.get(`/api/v1/manage/family-renewals/quotes/${id}/options`).then(r => r.json()); const writes: string[] = []; page.on("request", r => { if (r.method() === "POST") writes.push(r.url()); });
  const selected = options.items.filter((e: { id: string }) => expectedEnrollments.has(e.id));
  expect(selected).toHaveLength(2);
  for (const e of selected) { await page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true }).check(); await page.getByRole("combobox", { name: `الباقة لـ${e.player} · ${e.sport}`, exact: true }).selectOption("51000000-0000-0000-0000-000000000001"); }
  await expect(page.getByRole("checkbox", { checked: true })).toHaveCount(2);
  await page.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }).click(); await expect(page.getByRole("region", { name: "مراجعة سعر الأسرة" })).toBeVisible(); await expect(page.getByText(/هذه مراجعة سعر فقط/)).toBeVisible(); expect(writes.filter(p => p.includes("/api/v1/"))).toEqual([expect.stringContaining(`/quotes/${id}/quote`)]);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1100 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`staff-family-${width}.png`), fullPage: true, animations: "disabled" }); }
  await page.getByRole("button", { name: "تعديل الأبناء والباقات" }).click();
  for (const e of selected) await expect(page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true })).toBeChecked();
  await expect(page.getByRole("checkbox", { checked: true })).toHaveCount(2);
});
