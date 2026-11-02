import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Paid player registration posts once to the selected cashier shift and reaches the linked guardian", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard"); page.on("dialog", d => d.accept());
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true); return response.json();
  }
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001";
  const groupId = await write("/api/v1/manage/groups", { arabicName: `تسجيل نقدي اصطناعي ${Date.now()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  const root = "/api/v1/finance/cash-registers";
  const me = await page.request.get("/api/v1/me").then(r => r.json()); const options = await page.request.get(`${root}/options`).then(r => r.json());
  const member = options.members.find((m: { name: string }) => m.name === me.displayName); expect(member).toBeTruthy();
  const register = await write(root, { branchId, name: `وردية تسجيل اصطناعي ${Date.now()}` });
  await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "50.00" });
  const guardianPhone = `010${Date.now().toString().slice(-8)}`, playerName = `لاعب نقدي اصطناعي ${Date.now()}`;
  const quote = await write("/api/v1/people/registration-quote", { planId, branchId, guardianPhone });
  await page.goto("/dashboard/players/new");
  await page.getByLabel("اسم اللاعب بالعربية").fill(playerName); await page.getByLabel("تاريخ الميلاد").fill("2017-01-01");
  await page.getByLabel("اسم ولي الأمر").fill("ولي تسجيل نقدي اصطناعي"); await page.getByLabel("رقم الهاتف").fill(guardianPhone);
  await page.getByLabel("الفرع").selectOption(branchId); await page.getByLabel("الرياضة").selectOption(sportId);
  await page.getByLabel("الفئة العمرية").selectOption("33000000-0000-0000-0000-000000000002"); await page.getByLabel("المجموعة").selectOption(groupId);
  await page.getByLabel("الباقة", { exact: true }).selectOption(planId); await page.getByRole("button", { name: "حساب المبلغ المستحق" }).click();
  await expect(page.getByText(/المبلغ المطلوب تحصيله/)).toBeVisible();
  const submit = page.getByRole("button", { name: "تسجيل وتأكيد التحصيل" }); await expect(submit).toBeDisabled();
  await page.getByLabel("طريقة التحصيل").selectOption("Cash"); await expect(submit).toBeDisabled();
  const cash = page.getByRole("region", { name: "وردية قبض التسجيل" }); await cash.getByLabel("وردية التحصيل").selectOption(shift.id);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await cash.scrollIntoViewIfNeeded();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`cash-registration-${width}.png`), animations: "disabled" });
  }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/people/registrations"));
  await submit.click(); const response = await saved; expect(response.ok()).toBe(true); await page.waitForURL("**/dashboard/players");
  const body = response.request().postDataJSON(); expect(body.paymentMethod).toBe("Cash"); expect(body.cashSessionId).toBe(shift.id);
  const replay = await page.request.fetch(response.url(), { method: "POST", data: body, headers: response.request().headers() });
  expect(replay.ok()).toBe(true); const result = await replay.json(); expect(result.replay).toBe(true);
  const detail = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(detail.receipts).toHaveLength(1);
  expect(detail.receipts[0].playerNameSnapshot).toBe(playerName);
  const cents = (v: string | number) => BigInt(Math.round(Number(v) * 100));
  expect(cents(detail.receipts[0].amount)).toBe(cents(quote.finalAmount)); expect(cents(detail.balance)).toBe(cents(quote.finalAmount) + 5000n);
  await guardianLogin(page, guardianPhone); await expect(page.getByRole("heading", { name: playerName, exact: true })).toBeVisible();
  const receipt = await page.request.get(`/api/v1/guardian/subscriptions/receipts/${detail.receipts[0].receiptId}`); expect(receipt.ok()).toBe(true);
  expect((await receipt.json()).paymentMethod).toBe("Cash");
});
