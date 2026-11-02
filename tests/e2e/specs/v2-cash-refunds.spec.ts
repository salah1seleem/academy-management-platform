import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Cash refund reviews exact available funds, posts once, reconciles the shift and preserves the guardian receipt", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true); return response.json();
  }
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001", root = "/api/v1/finance/cash-registers";
  const groupId = await write("/api/v1/manage/groups", { arabicName: `استرداد اصطناعي ${Date.now()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  const me = await page.request.get("/api/v1/me").then(r => r.json()), options = await page.request.get(`${root}/options`).then(r => r.json()); const member = options.members.find((m: { name: string }) => m.name === me.displayName);
  const register = await write(root, { branchId, name: `وردية استرداد اصطناعي ${Date.now()}` }); await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "100.00" });
  const guardianPhone = `010${Date.now().toString().slice(-8)}`; const quote = await write("/api/v1/people/registration-quote", { planId, branchId, guardianPhone });
  const playerName = `لاعب استرداد اصطناعي ${Date.now()}`;
  const player = await write("/api/v1/people/registrations", { playerArabicName: playerName, dateOfBirth: "2017-01-01", guardianName: "ولي استرداد اصطناعي", guardianPhone, branchId, sportId, groupId, planId, paymentMethod: "Cash", expectedAmount: quote.finalAmount, cashSessionId: shift.id });
  const refundRoot = `/api/v1/people/players/${player.playerId}/refunds`, before = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json());
  const collection = (await page.request.get(refundRoot).then(r => r.json())).collections[0]; expect(collection.paymentMethod).toBe("Cash");
  const receiptId = before.receipts[0].receiptId; const receiptBefore = await page.request.get(`/api/v1/subscriptions/receipts/${receiptId}`).then(r => r.json());
  await page.goto(`/dashboard/players/${player.playerId}?tab=subscriptions`); const panel = page.getByRole("region", { name: "استرداد اللاعب" });
  await panel.getByText("تسجيل استرداد جزئي", { exact: true }).click(); await panel.getByRole("combobox", { name: "التحصيل", exact: true }).selectOption(collection.id); await panel.getByLabel("قيمة الاسترداد").fill("40.25");
  await expect(panel.getByLabel("طريقة رد المبلغ")).toBeDisabled(); await panel.getByRole("button", { name: "مراجعة الاسترداد النقدي", exact: true }).click();
  await panel.getByRole("combobox", { name: "وردية الصرف", exact: true }).selectOption(shift.id); await panel.getByRole("button", { name: "مراجعة الاسترداد النقدي", exact: true }).click(); const confirm = panel.getByRole("button", { name: "تأكيد صرف الاسترداد", exact: true }); await expect(confirm).toBeVisible();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await panel.getByRole("region", { name: "مراجعة الاسترداد النقدي" }).scrollIntoViewIfNeeded(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`cash-refund-${width}.png`), animations: "disabled" });
  }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(refundRoot)); await confirm.click(); const response = await saved; expect(response.ok()).toBe(true);
  await expect(panel.getByRole("status")).toContainText("تم تسجيل الاسترداد بنجاح");
  const replay = await page.request.fetch(response.url(), { method: "POST", data: response.request().postDataJSON(), headers: response.request().headers() }); expect(replay.ok()).toBe(true); expect((await replay.json()).replay).toBe(true);
  const after = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(after.refunds).toHaveLength(1); expect(after.receipts).toHaveLength(1);
  const cents = (v: string) => BigInt(v.replace(".", "")); expect(cents(after.balance)).toBe(cents(before.balance) - 4025n); expect(after.refunds[0].amount).toBe("40.25");
  expect(await page.request.get(`/api/v1/subscriptions/receipts/${receiptId}`).then(r => r.json())).toEqual(receiptBefore);
  const payoutDate = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(after.refunds[0].createdAtUtc));
  const financialQuery = new URLSearchParams({ from: payoutDate, to: payoutDate, search: playerName, provider: "Cash" }).toString();
  const report = await page.request.get(`/api/v1/reports/financial?${financialQuery}`).then(r => r.json());
  expect(report.totalRefunds).toBe(40.25); expect(report.items.some((r: { collectionType: string; netAmount: number }) => r.collectionType === "Refund" && r.netAmount === -40.25)).toBe(true);
  const csv = await page.request.get(`/api/v1/reports/financial/export?${financialQuery}`); expect(csv.ok()).toBe(true); expect(await csv.text()).toContain("استرداد اشتراك");
  await page.goto(`/dashboard/reports/financial?${financialQuery}`); await expect(page.getByText("استرداد اشتراك · بتاريخ الصرف", { exact: true })).toBeVisible();
  await write(`${root}/sessions/${shift.id}/close`, { countedAmount: after.balance }); await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`);
  await expect(page.getByRole("heading", { name: "الاستردادات النقدية المصروفة" })).toBeVisible(); await expect(page.getByRole("link", { name: "ملف اللاعب والاسترداد" })).toHaveAttribute("href", `/dashboard/players/${player.playerId}?tab=subscriptions`);
  const closed = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(closed.variance).toBe("0.00");
  await guardianLogin(page, guardianPhone); expect((await page.request.get(`/api/v1/guardian/subscriptions/receipts/${receiptId}`)).ok()).toBe(true);
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); expect((await page.request.post(refundRoot + "/quote", { headers: { "X-CSRF-TOKEN": csrf.token }, data: response.request().postDataJSON() })).status()).toBe(403);
});
