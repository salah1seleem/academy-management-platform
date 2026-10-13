import { expect, test, type APIRequestContext } from "@playwright/test";

async function write(api: APIRequestContext, path: string, data: unknown, method = "POST") {
  const { token } = await api.get("/api/v1/auth/csrf").then(r => r.json());
  return api.fetch(path, { method, headers: { "X-CSRF-TOKEN": token }, data });
}
test("One family transfer is approved atomically and exposes both child receipts", async ({ page, browser }, info) => {
  const root = "/api/v1/guardian/subscriptions/family/requests";
  const owner = await browser.newContext({ baseURL: info.project.use.baseURL }); const staff = await owner.newPage();
  expect((await write(owner.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).ok()).toBe(true);
  const prior = await owner.request.get("/api/v1/payment-settings").then(r => r.json());
  try {
    expect((await write(owner.request, "/api/v1/payment-settings", { instaPayAddress: "family-demo@example.test", walletNumber: null, recipientName: "حساب اصطناعي — لا تحول إليه", isActive: true }, "PUT")).ok()).toBe(true);
    const phoneNumber = "01000000014"; const challenge = await write(page.request, "/api/v1/auth/guardian/otp/request", { phoneNumber }); expect(challenge.ok()).toBe(true);
    expect((await write(page.request, "/api/v1/auth/guardian/otp/verify", { phoneNumber, challengeId: (await challenge.json()).challengeId, code: "246810" })).ok()).toBe(true);
    // Only cancel this explicit fixture's leftover pending requests; never reset money/history.
    const previous = await page.request.get(root).then(r => r.json());
    for (const r of previous.items.filter((x: { status: string }) => x.status === "Pending")) { const d = await page.request.get(`${root}/${r.id}`).then(r => r.json()); expect((await write(page.request, `${root}/${r.id}/cancel`, { revision: d.revision })).ok()).toBe(true); }
    const before = await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json());
    await page.goto("/guardian/renew-family"); const options = await page.request.get("/api/v1/guardian/subscriptions/family/options").then(r => r.json()); expect(options.items).toHaveLength(2);
    for (const e of options.items) { await page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true }).check(); await page.getByRole("combobox", { name: `الباقة لـ${e.player} · ${e.sport}`, exact: true }).selectOption("51000000-0000-0000-0000-000000000001"); }
    await page.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }).click(); await expect(page.getByRole("region", { name: "ملخص تجديد الأسرة" })).toBeVisible(); await page.getByRole("button", { name: "متابعة الدفع للأسرة" }).click(); await expect(page).toHaveURL(/\/guardian\/family-renewals\/[a-f0-9-]+$/); const id = page.url().split("/").at(-1)!;
    await expect(page.getByText("family-demo@example.test", { exact: true })).toBeVisible(); expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
    const image = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=", "base64");
    await page.getByLabel("صورة إثبات الأسرة").setInputFiles({ name: "synthetic-family-proof.png", mimeType: "image/png", buffer: image }); await page.getByRole("button", { name: "إرسال إثبات الأسرة", exact: true }).click(); await expect(page.getByText(/وصل إثبات الدفع/)).toBeVisible(); expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
    await page.setViewportSize({ width: 390, height: 1000 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath("family-pending-390.png"), fullPage: true, animations: "disabled" });
    await staff.goto("/dashboard/subscriptions/payments?type=Renewal&status=Pending&allDates=true"); const row = staff.locator(".payment-queue-row").filter({ has: staff.locator(`a[href='/dashboard/family-renewals/${id}']`) }); await expect(row).toHaveCount(1); await row.getByRole("link", { name: "مراجعة" }).click(); await expect(staff.getByRole("heading", { name: "مراجعة تجديد الأسرة" })).toBeVisible(); await expect(staff.getByRole("combobox")).toHaveCount(2);
    await staff.setViewportSize({ width: 1440, height: 1100 }); expect(await staff.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await staff.screenshot({ path: info.outputPath("family-staff-1440.png"), fullPage: true, animations: "disabled" });
    await staff.getByRole("button", { name: "قبول وتجديد كل التسجيلات" }).click(); await expect(staff.getByText("تم تجديد الأسرة", { exact: true })).toBeVisible(); await page.reload(); await expect(page.getByText("تم تجديد الأسرة", { exact: true })).toBeVisible();
    const detail = await page.request.get(`${root}/${id}`).then(r => r.json()); expect(detail.items).toHaveLength(2); for (const line of detail.items) { expect(line.receiptId).toBeTruthy(); expect((await page.request.get(`/api/v1/guardian/subscriptions/receipts/${line.receiptId}`)).ok()).toBe(true); }
    const after = await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json()); expect(after.items.length).toBe(before.items.length + 2);
    await staff.reload(); await expect(staff.getByRole("button", { name: "قبول وتجديد كل التسجيلات" })).toHaveCount(0); expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(after); await page.screenshot({ path: info.outputPath("family-approved-390.png"), fullPage: true, animations: "disabled" });
  } finally { await write(owner.request, "/api/v1/payment-settings", { ...prior, isActive: prior.configured }, "PUT"); await owner.close(); }
});
