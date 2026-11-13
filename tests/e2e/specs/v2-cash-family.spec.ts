import { expect, test } from "@playwright/test";

test("Owner collects a family in one shift, opens both receipts and replays without double collection", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول" }).click(); await page.waitForURL("**/dashboard");
  const root = "/api/v1/finance/cash-registers";
  async function write(path: string, body: unknown, method = "POST") { const { token } = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const r = await page.request.fetch(path, { method, data: body, headers: { "X-CSRF-TOKEN": token, "Idempotency-Key": crypto.randomUUID() } }); expect(r.ok(), await r.text()).toBe(true); return r.json(); }
  // Own family: a failed proof journey in another test must not leave a pending payment here.
  const phone = `010${Date.now().toString().slice(-8)}`;
  const familyName = `أسرة تحصيل اصطناعية ${crypto.randomUUID()}`;
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001";
  const groupId = await write("/api/v1/manage/groups", { arabicName: familyName, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  for (const playerArabicName of ["طفل تحصيل اصطناعي أول", "طفل تحصيل اصطناعي ثان"]) {
    const quote = await write("/api/v1/people/registration-quote", { planId, branchId, guardianPhone: phone });
    await write("/api/v1/people/registrations", { playerArabicName, dateOfBirth: "2017-01-01", guardianName: familyName, guardianPhone: phone, branchId, sportId, groupId, planId, paymentMethod: "Online", expectedAmount: quote.finalAmount });
  }
  const options = await page.request.get(`${root}/options`).then(r => r.json()); const me = await page.request.get("/api/v1/me").then(r => r.json()); const member = options.members.find((m: { name: string }) => m.name === me.displayName);
  const register = await write(root, { branchId: "31000000-0000-0000-0000-000000000001", name: `أسرة اختبار ${Date.now()}` }); await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "100.00" });
  await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`); await page.getByRole("link", { name: "تحصيل وتجديد عدة أبناء معًا" }).click(); await page.getByLabel("اسم ولي الأمر أو رقم هاتفه").fill(phone); await page.getByRole("button", { name: "البحث عن الأسرة" }).click(); await page.getByRole("button", { name: `اختيار ${familyName}`, exact: true }).click(); await expect(page.getByRole("checkbox")).toHaveCount(2);
  const families = await page.request.get(`/api/v1/manage/family-renewals/quotes/families?search=${phone}`).then(r => r.json()); const family = await page.request.get(`/api/v1/manage/family-renewals/quotes/${families.items[0].id}/options`).then(r => r.json());
  for (const e of family.items) { await page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true }).check(); await page.getByRole("combobox", { name: `الباقة لـ${e.player} · ${e.sport}`, exact: true }).selectOption("51000000-0000-0000-0000-000000000001"); }
  await page.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }).click(); await expect(page.getByRole("region", { name: "تحصيل الأسرة النقدي" })).toBeVisible(); expect((await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json())).receipts).toHaveLength(0);
  await page.getByRole("checkbox", { name: "استلمت إجمالي الأسرة نقدًا وراجعت الباقات" }).check(); const posting = page.waitForResponse(r => r.url().endsWith(`/sessions/${shift.id}/family-collections`) && r.request().method() === "POST"); await page.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات", exact: true }).click(); const result = await posting; expect(result.ok(), await result.text()).toBe(true); const saved = await result.json(); await expect(page.getByRole("heading", { name: "تم تحصيل وتجديد الأسرة" })).toBeVisible(); expect(saved.items).toHaveLength(2);
  const receipts = page.getByRole("region", { name: "تحصيل الأسرة النقدي" });
  for (const item of saved.items) {
    await expect(receipts.getByRole("link", { name: /^إيصال / }).and(page.locator(`a[href="/dashboard/subscriptions/receipts/${item.receiptId}"]`))).toBeVisible();
  }
  const detail = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(detail.receipts).toHaveLength(2); expect(BigInt(detail.balance.replace(".", ""))).toBe(BigInt(saved.amount.replace(".", "")) + 10000n);
  const retry = await page.request.fetch(result.url(), { method: "POST", data: result.request().postDataJSON(), headers: result.request().headers() }); expect(retry.ok()).toBe(true); expect((await retry.json()).replay).toBe(true); expect((await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json())).receipts).toHaveLength(2);
  for (const line of saved.items) expect((await page.request.get(`/api/v1/subscriptions/receipts/${line.receiptId}`)).ok()).toBe(true);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1100 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`cash-family-${width}.png`), fullPage: true, animations: "disabled" }); }
  await write(`${root}/sessions/${shift.id}/close`, { countedAmount: detail.balance });
});
