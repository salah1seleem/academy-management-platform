import { expect, test } from "@playwright/test";

test("Existing staff renewal collects through its authorized shift once and reaches the receipt", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard"); page.on("dialog", d => d.accept());
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true); return response.json();
  }
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001";
  const groupId = await write("/api/v1/manage/groups", { arabicName: `طلب نقدي اصطناعي ${Date.now()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  const guardianPhone = `010${Date.now().toString().slice(-8)}`;
  const quote = await write("/api/v1/people/registration-quote", { planId, branchId, guardianPhone });
  const child = await write("/api/v1/people/registrations", { playerArabicName: "لاعب طلب نقدي اصطناعي", dateOfBirth: "2017-01-01", guardianName: "ولي نقدي اصطناعي", guardianPhone, branchId, sportId, groupId, planId, paymentMethod: "Online", expectedAmount: quote.finalAmount });
  const player = await page.request.get(`/api/v1/people/players/${child.playerId}`).then(r => r.json());
  const request = await write("/api/v1/subscriptions/admin-renewals", { sportEnrollmentId: player.enrollments[0].id, subscriptionPlanId: planId });
  const root = "/api/v1/finance/cash-registers";
  const me = await page.request.get("/api/v1/me").then(r => r.json()); const options = await page.request.get(`${root}/options`).then(r => r.json());
  const member = options.members.find((m: { name: string }) => m.name === me.displayName); expect(member).toBeTruthy();
  const register = await write(root, { branchId, name: `وردية طلب قائم ${Date.now()}` });
  await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "50.00" });
  const pending = await page.request.get(`/api/v1/manage/payments/${request.paymentId}/cash-options`).then(r => r.json());
  await page.goto(`/dashboard/subscriptions/payments/${request.paymentId}`);
  const form = page.getByRole("region", { name: "تحصيل طلب التجديد نقدًا" });
  await form.getByRole("combobox", { name: "وردية التحصيل" }).selectOption(shift.id);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await form.scrollIntoViewIfNeeded();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`pending-cash-${width}.png`), animations: "disabled" });
  }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`/manage/payments/${request.paymentId}/collect`));
  await form.getByRole("button", { name: "تأكيد التحصيل النقدي وإصدار الإيصال" }).click(); const response = await saved; expect(response.ok()).toBe(true);
  await expect(page.getByRole("link", { name: "عرض الإيصال", exact: true })).toBeVisible();
  // Read persisted state independently of a proxied browser response whose
  // body may still be streaming after the payment form has unmounted.
  const result = await page.request.get(`/api/v1/subscriptions/payments/${request.paymentId}`).then(r => r.json());
  expect(result.status).toBe("Confirmed"); expect(result.receiptId).toBeTruthy();
  await expect(form).toHaveCount(0);
  const replay = await page.request.fetch(response.url(), { method: "POST", data: response.request().postDataJSON(), headers: response.request().headers() });
  expect(replay.ok()).toBe(true); expect((await replay.json()).receiptId).toBe(result.receiptId);
  const detail = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(detail.receipts).toHaveLength(1);
  expect(detail.receipts[0].amount).toBe(pending.amount);
  const cents = (v: string) => BigInt(v.replace(".", "")); expect(cents(detail.balance)).toBe(cents(pending.amount) + 5000n);
  await page.getByRole("link", { name: "عرض الإيصال", exact: true }).click(); await expect(page).toHaveURL(new RegExp(`/receipts/${result.receiptId}$`));
  const receipt = await page.request.get(`/api/v1/subscriptions/receipts/${result.receiptId}`).then(r => r.json()); expect(receipt.paymentMethod).toBe("Cash");
});
