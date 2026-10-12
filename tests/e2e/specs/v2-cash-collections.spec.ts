import { expect, test } from "@playwright/test";

test("Cashier reviews a price, collects once, sees the receipt and closes an exact shift", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard"); page.on("dialog", d => d.accept());
  const root = "/api/v1/finance/cash-registers";
  const options = await page.request.get(`${root}/options`).then(r => r.json()); const me = await page.request.get("/api/v1/me").then(r => r.json()); const member = options.members.find((m: { name: string }) => m.name === me.displayName); expect(member).toBeTruthy();
  async function write(path: string, body: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data: body, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } }); expect(response.ok()).toBe(true); return response.json();
  }
  const register = await write(root, { branchId: "31000000-0000-0000-0000-000000000001", name: `قبض اختباري ${Date.now()}` });
  await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "50.00" });
  await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`);
  await page.getByLabel("البحث باللاعب أو كوده").fill("NG-0001"); await page.getByRole("button", { name: "بحث في الفرع", exact: true }).click();
  const enrollment = page.getByRole("combobox", { name: "تسجيل اللاعب", exact: true }); await expect(enrollment.locator("option")).toHaveCount(2); await enrollment.selectOption({ index: 1 });
  await page.getByRole("combobox", { name: "باقة التدريب", exact: true }).selectOption("51000000-0000-0000-0000-000000000001");
  await page.getByRole("button", { name: "مراجعة مبلغ القبض", exact: true }).click(); await expect(page.getByLabel("مراجعة القبض", { exact: true })).toBeVisible();
  const preview = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(preview.balance).toBe("50.00"); expect(preview.receipts).toHaveLength(0);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await page.getByLabel("مراجعة القبض", { exact: true }).scrollIntoViewIfNeeded();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`cash-review-${width}.png`) });
  }
  const posting = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`/sessions/${shift.id}/collections`));
  await page.getByRole("button", { name: "تأكيد استلام النقدية وإصدار الإيصال", exact: true }).click(); const response = await posting; expect(response.status()).toBe(200); const saved = await response.json();
  await expect(page.getByText("تم القبض وتحديث رصيد الوردية والاشتراك.", { exact: true })).toBeVisible();
  const detail = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(detail.receipts).toHaveLength(1); expect(detail.receipts[0].receiptId).toBe(saved.receiptId);
  const amountCents = (value: string) => BigInt(value.replace(".", "")); expect(amountCents(detail.balance)).toBe(amountCents(detail.receipts[0].amount) + 5000n);
  const retry = await page.request.fetch(response.url(), { method: "POST", data: response.request().postDataJSON(), headers: { ...response.request().headers() } }); expect(retry.ok()).toBe(true); expect((await retry.json()).receiptId).toBe(saved.receiptId);
  const receipt = await page.request.get(`/api/v1/subscriptions/receipts/${saved.receiptId}`).then(r => r.json()); expect(receipt.paymentMethod).toBe("Cash");
  await page.getByLabel("المبلغ الموجود فعليًا", { exact: true }).fill(detail.balance); await page.getByRole("button", { name: "تثبيت المطابقة وإقفال الوردية", exact: true }).click(); await page.waitForURL("**/dashboard/finance/cash-registers");
  await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`); await expect(page.getByText("إيصالات القبض في الوردية", { exact: true })).toBeVisible(); await expect(page.getByRole("heading", { name: "قبض اشتراك نقدي", exact: true })).toHaveCount(0);
  const closed = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(closed.variance).toBe("0.00"); expect(closed.receipts).toHaveLength(1);
  await page.screenshot({ path: info.outputPath("cash-receipt-closed.png"), animations: "disabled" });
});
