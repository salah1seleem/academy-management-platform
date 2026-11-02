import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Branch Admin collects for a foreign enrollment and reload recovers a lost committed reply without double collection", async ({ page }, info) => {
  async function login(email: string) {
    await page.context().clearCookies(); await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**");
  }
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } }); expect(response.ok(), await response.text()).toBe(true); return response.status() === 204 ? null : response.json();
  }
  await login("owner.nogoom@example.test");
  const root = "/api/v1/finance/cash-registers", branchId = "31000000-0000-0000-0000-000000000002", playerId = "84000018-0000-0000-0000-000000000001", enrollmentId = "84000018-0000-0000-0000-000000000002", planId = "84000018-0000-0000-0000-000000000004";
  const email = `cross.e2e.${Date.now()}@example.test`, member = await write("/api/v1/manage/staff", { name: "إداري تحصيل عابر للفروع اصطناعي", email, phone: `010${Date.now().toString().slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "سيناريو دفع" });
  const access = await page.request.get(`/api/v1/manage/staff/${member.id}/access`).then(r => r.json());
  await write(`/api/v1/manage/staff/${member.id}/access`, { allBranches: false, branches: [branchId], actions: access.modules.map((module: string) => ({ module, read: ["finance", "players", "subscriptions", "payments", "reports"].includes(module), add: ["finance", "subscriptions", "payments"].includes(module), edit: false, delete: false })) }, "PUT");
  const register = await write(root, { branchId, name: `دفع عبر الفروع اصطناعي ${Date.now()}` }); await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT");
  await login(email); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "0" });
  expect((await page.request.get(`/api/v1/people/players/${playerId}`)).status()).toBe(404);
  const reportUrl = "/api/v1/reports/financial?allDates=true&search=CB-DEMO-1"; const before = await page.request.get(reportUrl).then(r => r.json());
  await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`); await page.getByRole("checkbox", { name: /دفع لتسجيل في فرع آخر/ }).check();
  await page.getByLabel("البحث باللاعب أو كوده").fill("CB-DEMO-1"); await page.getByRole("button", { name: "بحث عن اللاعب", exact: true }).click();
  await page.getByRole("combobox", { name: "تسجيل اللاعب", exact: true }).selectOption(enrollmentId); await page.getByRole("combobox", { name: "باقة التدريب", exact: true }).selectOption(planId);
  await page.getByRole("button", { name: "مراجعة مبلغ القبض", exact: true }).click(); const review = page.getByLabel("مراجعة القبض", { exact: true }); await expect(review.getByText("فرع التحصيل", { exact: true })).toBeVisible();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await review.scrollIntoViewIfNeeded(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`admin-cross-review-${width}.png`), animations: "disabled" });
  }
  let sent = 0;
  await page.route("**/cross-branch/collect", async route => { sent++; const response = await route.fetch(); expect(response.ok()).toBe(true); await route.abort("failed"); });
  page.on("dialog", d => d.accept()); await page.getByRole("button", { name: "تأكيد استلام النقدية وإصدار الإيصال", exact: true }).click();
  await expect(page.getByLabel("استعادة نتيجة التحصيل", { exact: true })).toBeVisible(); await expect(page.getByText(/انقطع الاتصال ولم تتأكد/)).toBeVisible();
  await page.reload(); await expect(page.getByLabel("استعادة نتيجة التحصيل", { exact: true })).toBeVisible(); await expect(page.getByLabel("البحث باللاعب أو كوده")).toHaveCount(0);
  await page.getByRole("button", { name: "فحص نتيجة المحاولة", exact: true }).click(); await expect(page.getByText("تم القبض وتحديث رصيد الوردية والاشتراك.", { exact: true })).toBeVisible(); expect(sent).toBe(1);
  const saved = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(saved.receipts).toHaveLength(1); const receiptId = saved.receipts[0].receiptId;
  const receipt = await page.request.get(`/api/v1/subscriptions/receipts/${receiptId}`).then(r => r.json()); expect(receipt.enrollmentBranch).not.toBe(receipt.collectionBranch);
  const after = await page.request.get(reportUrl).then(r => r.json()); expect(after.totalAmount - before.totalAmount).toBe(receipt.amount); expect(after.items.find((r: { receiptId: string }) => r.receiptId === receiptId).group).toBe("خدمة في فرع تسجيل آخر");
  expect((await page.request.get(`/api/v1/people/players/${playerId}`)).status()).toBe(404);
  await page.setViewportSize({ width: 390, height: 1000 }); await page.screenshot({ path: info.outputPath("admin-cross-recovered.png"), animations: "disabled" });
  await write(`${root}/sessions/${shift.id}/close`, { countedAmount: saved.balance });
  await guardianLogin(page); await page.goto(`/guardian/receipts/${receiptId}`); await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible(); await expect(page.getByText(receipt.collectionBranch, { exact: true })).toBeVisible();
});
