import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Owner explicitly reverses unused cash receipt; shift, dated report and guardian history agree", async ({ page }, info) => {
  await page.goto("/login");await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();await page.waitForURL("**/dashboard");
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true);return response.json();
  }
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001", root = "/api/v1/finance/cash-registers";
  const groupId = await write("/api/v1/manage/groups", { arabicName: `عكس اصطناعي ${Date.now()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  const me = await page.request.get("/api/v1/me").then(r => r.json()), options = await page.request.get(`${root}/options`).then(r => r.json());const member = options.members.find((m: { name: string }) => m.name === me.displayName);
  const register = await write(root, { branchId, name: `وردية عكس اصطناعي ${Date.now()}` });await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT");const shift = await write(`${root}/${register.id}/open`, { openingAmount: "100.00" });
  const guardianPhone = `010${Date.now().toString().slice(-8)}`;const quote = await write("/api/v1/people/registration-quote", { planId, branchId, guardianPhone });
  const playerName = `لاعب عكس اصطناعي ${Date.now()}`;
  await write("/api/v1/people/registrations", { playerArabicName: playerName, dateOfBirth: "2017-01-01", guardianName: "ولي عكس اصطناعي", guardianPhone, branchId, sportId, groupId, planId, paymentMethod: "Cash", expectedAmount: quote.finalAmount, cashSessionId: shift.id });
  const before = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()), receiptId = before.receipts[0].receiptId;
  const original = await page.request.get(`/api/v1/subscriptions/receipts/${receiptId}`).then(r => r.json());
  await page.goto(`/dashboard/subscriptions/receipts/${receiptId}`);await page.getByRole("link", { name: "مراجعة عكس الإيصال (للمالك)" }).click();
  await expect(page.getByRole("heading", { name: playerName })).toBeVisible();await page.getByRole("combobox", { name: "وردية صرف العكس", exact: true }).selectOption(shift.id);
  await page.getByRole("button", { name: "مراجعة عكس الإيصال", exact: true }).click();await expect(page.getByRole("region", { name: "مراجعة عكس الإيصال" })).toBeVisible();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`cash-void-${width}.png`), animations: "disabled" });
  }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`/receipts/${receiptId}/void`));await page.getByRole("button", { name: "تأكيد الصرف وإلغاء الفترة" }).click();
  const response = await saved;expect(response.ok()).toBe(true);await expect(page.getByRole("status").filter({ hasText: "تم عكس الإيصال" })).toBeVisible();
  const replay = await page.request.fetch(response.url(), { method: "POST", data: response.request().postDataJSON(), headers: response.request().headers() });expect(replay.ok()).toBe(true);const voidRecord = await replay.json();expect(voidRecord.replay).toBe(true);
  const after = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json());expect(after.balance).toBe("100.00");expect(after.refunds).toHaveLength(1);expect(after.receipts).toHaveLength(1);
  await page.getByRole("link", { name: "عرض الإيصال وحركة العكس" }).click();await expect(page.getByText("إيصال معكوس — الفترة المرتبطة ملغاة")).toBeVisible();expect(await page.getByRole("link", { name: /مراجعة عكس/ }).count()).toBe(0);
  const date = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const report = await page.request.get(`/api/v1/reports/financial?from=${date}&to=${date}&search=${encodeURIComponent(playerName)}`).then(r => r.json());expect(report.items).toHaveLength(1);expect(report.items[0].collectionType).toBe("Void");expect(report.netAmount).toBe(-original.amount);
  const guardian = await page.context().browser()!.newContext();const guardianPage = await guardian.newPage();await guardianLogin(guardianPage, guardianPhone);
  const receipt = await guardianPage.request.get(`/api/v1/guardian/subscriptions/receipts/${receiptId}`).then(r => r.json());expect(receipt.isVoided).toBe(true);expect(receipt.amount).toBe(original.amount);expect(receipt.paidAtUtc).toBe(original.paidAtUtc);
  expect((await guardianPage.request.get(`${root}/receipts/${receiptId}/void-options`)).status()).toBe(403);
  const periods = await page.request.get("/api/v1/subscriptions/periods").then(r => r.json());expect(periods.find((p: { id: string }) => p.id === voidRecord.subscriptionPeriodId)?.status).toBe("Cancelled");
  await guardian.close();
});
