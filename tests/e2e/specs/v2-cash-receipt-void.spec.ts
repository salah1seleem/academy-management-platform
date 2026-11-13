import { expect, test, type Page } from "@playwright/test";
import { guardianLogin } from "../support/auth";

const cash = "/api/v1/finance/cash-registers";
const receipts = "/api/v1/subscriptions/receipts";
const branchId = "31000000-0000-0000-0000-000000000001";
const sportId = "32000000-0000-0000-0000-000000000001";
const planId = "51000000-0000-0000-0000-000000000001";
const previewText = "مراجعة عكس الإيصال";
const confirmText = "تأكيد الصرف وإلغاء الفترة";
const replayText = "إعادة تأكيد العملية نفسها";

test.setTimeout(120_000);

async function write(page: Page, path: string, data: unknown, method = "POST") {
  const { token } = await (await page.request.get("/api/v1/auth/csrf")).json();
  return page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": token, "Idempotency-Key": crypto.randomUUID() } });
}

async function save(page: Page, path: string, data: unknown, method = "POST") {
  const response = await write(page, path, data, method);
  expect(response.ok(), await response.text()).toBe(true);
  return response.status() === 204 ? null : response.json();
}

async function read(page: Page, path: string) {
  const response = await page.request.get(path);
  expect(response.ok(), await response.text()).toBe(true);
  return response.json();
}

async function openShift(page: Page, branch: string, name: string) {
  const me = await read(page, "/api/v1/me");
  const options = await read(page, `${cash}/options`);
  const member = options.members.find((m: { id: string }) => m.id === me.membershipId);
  expect(member).toBeDefined();
  const register = await save(page, cash, { branchId: branch, name });
  await save(page, `${cash}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT");
  return save(page, `${cash}/${register.id}/open`, { openingAmount: "100.00" });
}

async function fixture(page: Page) {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await expect(page).toHaveURL("/dashboard");
  const suffix = Date.now().toString().slice(-8);
  const groupId = await save(page, "/api/v1/manage/groups", {
    arabicName: `عكس اصطناعي ${suffix}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2,
  });
  const shift = await openShift(page, branchId, `صندوق عكس اصطناعي ${suffix}`);
  const shiftPath = `${cash}/sessions/${shift.id}`;
  async function register(number: string, name: string) {
    const guardianPhone = `010${number}`;
    const quote = await save(page, "/api/v1/people/registration-quote", { planId, branchId, guardianPhone });
    await save(page, "/api/v1/people/registrations", {
      playerArabicName: name, dateOfBirth: "2017-01-01", guardianName: "ولي أمر اصطناعي", guardianPhone, branchId, sportId,
      groupId, planId, paymentMethod: "Cash", expectedAmount: quote.finalAmount, cashSessionId: shift.id,
    });
    return guardianPhone;
  }
  const playerName = `لاعب عكس اصطناعي ${suffix}`;
  const guardianPhone = await register(suffix, playerName);
  const shiftBefore = await read(page, shiftPath);
  const receiptId = shiftBefore.receipts[0].receiptId as string;
  return {
    suffix, playerName, guardianPhone, shift, shiftPath, receiptId,
    original: await read(page, `${receipts}/${receiptId}`),
    voidPath: `${cash}/receipts/${receiptId}/void`,
    receiptRoute: `/dashboard/subscriptions/receipts/${receiptId}`,
    addCollection: () => register(String((Number(suffix) + 1) % 100_000_000).padStart(8, "0"), `لاعب قبض اصطناعي ${suffix}`),
  };
}

async function openReview(page: Page, receiptRoute: string) {
  await page.goto(receiptRoute);
  await page.getByRole("link", { name: "مراجعة عكس الإيصال (للمالك)" }).click();
  await expect(page.getByRole("heading", { name: "عكس إيصال نقدي" })).toBeVisible();
}

function fields(page: Page) {
  const main = page.locator("main");
  return {
    shift: main.getByRole("combobox", { name: "وردية صرف العكس", exact: true }),
    reason: main.getByLabel("السبب (اختياري)"),
    quote: main.getByRole("region", { name: previewText, exact: true }),
    preview: main.getByRole("button", { name: previewText, exact: true }),
    confirm: main.getByRole("button", { name: confirmText, exact: true }),
    replay: main.getByRole("button", { name: replayText, exact: true }),
    error: main.getByRole("alert"),
  };
}

function cairoDate(value: string | Date) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(value));
}

test("CV-01–08 CV-22 CV-23: recover options and refresh a real quote before a deliberate confirmation", async ({ page }, info) => {
  const data = await fixture(page);
  const secondShift = await openShift(page, branchId, `وردية مراجعة بديلة ${data.suffix}`);
  const optionsRoute = `**${data.voidPath}-options`;
  await page.route(optionsRoute, route => route.fulfill({ status: 503, body: "Service Unavailable" }));
  await openReview(page, data.receiptRoute);
  const ui = fields(page);
  await expect(ui.error).toContainText("تعذر تحميل مراجعة العكس");
  await expect(ui.error).not.toContainText(/Service Unavailable|JSON/);
  await page.unroute(optionsRoute);
  await page.getByRole("button", { name: "إعادة تحميل المراجعة" }).click();
  await expect(page.getByRole("heading", { name: data.playerName })).toBeVisible();
  await expect(page.getByText(data.original.receiptNumber, { exact: true })).toBeVisible();
  await expect(page.locator("main").getByRole("link", { name: "العودة إلى الإيصال", exact: true })).toHaveAttribute("href", data.receiptRoute);
  await expect(ui.preview).toBeDisabled();
  await expect(ui.reason).toHaveAttribute("maxlength", "500");
  await ui.shift.selectOption(data.shift.id);
  await page.route(`**${data.voidPath}-quote`, route => route.abort(), { times: 1 });
  await ui.preview.click();
  await expect(ui.error).toContainText("تعذر تحميل نتيجة الطلب. أعد المحاولة.");
  await expect(ui.replay).toHaveCount(0);
  await expect(ui.preview).toBeEnabled();
  await ui.preview.click();
  await expect(ui.quote).toBeFocused();
  await expect(ui.quote).toContainText("1,000");
  const refreshed = page.waitForResponse(r => r.url().endsWith(`${data.voidPath}-quote`) && r.request().method() === "POST");
  await page.getByRole("button", { name: "تحديث المراجعة", exact: true }).click();
  expect((await refreshed).ok()).toBe(true);
  await expect(ui.quote).toBeFocused();
  await expect(ui.confirm).toBeEnabled();
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    if (width !== 320) {
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: info.outputPath(`cash-review-${width}.jpg`), type: "jpeg", quality: 68 });
    }
  }
  await ui.reason.fill("مراجعة القيد");
  await expect(ui.confirm).toHaveCount(0);
  await ui.preview.click();
  await expect(ui.confirm).toBeVisible();
  await ui.shift.selectOption(secondShift.id);
  await expect(ui.confirm).toHaveCount(0);
  const tooLong = await write(page, `${data.voidPath}-quote`, { cashSessionId: data.shift.id, reason: "أ".repeat(501) });
  expect(tooLong.status()).toBe(422);
  const unchanged = await read(page, data.shiftPath);
  expect(unchanged.balance).toBe("1000.00");
  expect(unchanged.refunds).toHaveLength(0);
  await page.route(optionsRoute, async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...(await response.json()), sessions: [] } });
  });
  await page.reload();
  await expect(page.getByRole("link", { name: "فتح إدارة الصناديق" })).toHaveAttribute("href", "/dashboard/finance/cash-registers");
  await expect(ui.confirm).toHaveCount(0);
  await expect(ui.preview).toHaveCount(0);
});

test("CV-09–12 CV-14–19 CV-21 CV-24: stale quote, lost acknowledgement and reconciled cash reversal", async ({ page }, info) => {
  const data = await fixture(page);
  const guardianContext = await page.context().browser()!.newContext();
  try {
    const guardian = await guardianContext.newPage();
    await guardianLogin(guardian, data.guardianPhone);
    const guardianApi = `/api/v1/guardian/subscriptions/receipts/${data.receiptId}`;
    const originalGuardian = await read(guardian, guardianApi);
    expect(originalGuardian.isVoided).toBe(false);
    await guardian.goto("/guardian/receipts");
    const familyCard = guardian.locator(`#receipt-${data.receiptId}`);
    await expect(familyCard).toContainText(originalGuardian.receiptNumber);
    await expect(familyCard.getByText("إيصال معكوس", { exact: true })).toHaveCount(0);
    await openReview(page, data.receiptRoute);
    const ui = fields(page);
    const foreignShift = await openShift(page, "31000000-0000-0000-0000-000000000002", `وردية فرع آخر ${data.suffix}`);
    const wrongShift = await write(page, `${data.voidPath}-quote`, { cashSessionId: foreignShift.id });
    expect(wrongShift.status()).toBe(409);
    await ui.shift.selectOption(data.shift.id);
    await ui.reason.fill("تسوية اختبارية");
    await ui.preview.click();
    await expect(ui.quote).toContainText("1,000");
    await data.addCollection();
    await ui.confirm.click();
    await expect(ui.error).toContainText("تغير الاشتراك أو رصيد الوردية");
    await expect(ui.confirm).toHaveCount(0);
    await expect(ui.reason).toHaveValue("تسوية اختبارية");
    const before = await read(page, data.shiftPath);
    expect(before.balance).toBe("1900.00");
    expect(before.refunds).toHaveLength(0);
    await ui.preview.click();
    await expect(ui.quote).toContainText("1,900");
    const attempts: { body: unknown; key: string }[] = [];
    await page.route(`**${data.voidPath}`, async route => {
      const request = route.request();
      attempts.push({ body: request.postDataJSON(), key: request.headers()["idempotency-key"] });
      const upstream = await route.fetch();
      expect(upstream.ok()).toBe(true);
      if (attempts.length === 1) await route.abort();
      else await route.fulfill({ response: upstream });
    });
    await ui.confirm.click();
    await expect(ui.replay).toBeVisible();
    await expect(ui.shift).toBeDisabled();
    await expect(ui.reason).toBeDisabled();
    await expect(ui.confirm).toHaveCount(0);
    await expect(page.getByRole("status").filter({ hasText: "تم عكس الإيصال" })).toHaveCount(0);
    const acknowledged = page.waitForResponse(r => r.url().endsWith(data.voidPath) && r.ok());
    await ui.replay.click();
    const reversal = await (await acknowledged).json();
    expect(reversal.replay).toBe(true);
    expect(attempts).toHaveLength(2);
    expect(attempts[1]).toEqual(attempts[0]);
    await expect(page.getByRole("status").filter({ hasText: "تم عكس الإيصال" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "تم عكس الإيصال وإلغاء الفترة المرتبطة بنجاح." })).toBeFocused();
    const after = await read(page, data.shiftPath);
    expect(after.balance).toBe("1000.00");
    expect(after.refunds).toHaveLength(1);
    expect(after.receipts).toHaveLength(2);
    const { token } = await read(page, "/api/v1/auth/csrf");
    const changed = await page.request.post(data.voidPath, {
      data: { ...attempts[0].body as object, reason: "سبب مختلف" }, headers: { "Idempotency-Key": attempts[0].key, "X-CSRF-TOKEN": token },
    });
    expect(changed.status()).toBe(409);
    expect((await write(page, data.voidPath, attempts[0].body)).status()).toBe(409);
    expect((await read(page, data.shiftPath)).refunds).toHaveLength(1);
    const sourceLink = page.getByRole("link", { name: "عرض رصيد الوردية" });
    await expect(sourceLink).toHaveAttribute("href", `/dashboard/finance/cash-registers/sessions/${data.shift.id}`);
    await sourceLink.click();
    await expect(page.getByRole("heading", { name: "الاستردادات النقدية المصروفة" })).toBeVisible();
    await page.goBack();
    await expect(page.getByRole("link", { name: "العودة إلى الإيصال", exact: true })).toBeVisible();
    await page.getByRole("link", { name: "العودة إلى الإيصال", exact: true }).click();
    await expect(page.getByText("إيصال معكوس — الفترة المرتبطة ملغاة")).toBeVisible();
    await expect(page.getByRole("link", { name: /مراجعة عكس/ })).toHaveCount(0);
    await page.emulateMedia({ media: "print" });
    await expect(page.getByText("إيصال معكوس — الفترة المرتبطة ملغاة")).toBeVisible();
    await expect(page.getByRole("button", { name: "طباعة الإيصال" })).toBeHidden();
    await page.screenshot({ path: info.outputPath("reversed-print.jpg"), type: "jpeg", quality: 68 });
    const today = cairoDate(new Date());
    const report = async (date: string) => read(page, `/api/v1/reports/financial?from=${date}&to=${date}&search=${encodeURIComponent(data.playerName)}`);
    const reversalReport = await report(today);
    expect(reversalReport.items.filter((item: { collectionType: string }) => item.collectionType === "Void")).toHaveLength(1);
    const paidOn = cairoDate(data.original.paidAtUtc);
    if (today !== paidOn) expect(reversalReport.netAmount).toBe(-data.original.amount);
    const paidReport = await report(paidOn);
    expect(paidReport.items.filter((item: { collectionType: string }) => item.collectionType !== "Void")).toHaveLength(1);
    if (today !== paidOn) expect(paidReport.netAmount).toBe(data.original.amount);
    const periods = await read(page, "/api/v1/subscriptions/periods");
    expect(periods.find((p: { id: string }) => p.id === reversal.subscriptionPeriodId)?.status).toBe("Cancelled");
    const other = after.receipts.find((r: { receiptId: string }) => r.receiptId !== data.receiptId);
    expect((await read(page, `${receipts}/${other.receiptId}`)).isVoided).toBe(false);
    const familyReceipt = await read(guardian, guardianApi);
    expect(familyReceipt).toMatchObject({ isVoided: true, amount: originalGuardian.amount, paidAtUtc: originalGuardian.paidAtUtc, receiptNumber: originalGuardian.receiptNumber });
    expect((await guardian.request.get(`${data.voidPath}-options`)).status()).toBe(403);
    await guardian.reload();
    await expect(familyCard).toContainText(originalGuardian.receiptNumber);
    await expect(familyCard.getByText("إيصال معكوس", { exact: true })).toBeVisible();
    await familyCard.click();
    await expect(guardian.getByText("إيصال معكوس — الفترة المرتبطة ملغاة")).toBeVisible();
  } finally {
    await guardianContext.close();
  }
});

test("CV-13: a malformed acknowledgement cannot confirm a reversal", async ({ page }) => {
  const data = await fixture(page);
  await openReview(page, data.receiptRoute);
  const ui = fields(page);
  await ui.shift.selectOption(data.shift.id);
  await ui.preview.click();
  await expect(ui.quote).toBeVisible();
  await page.route(`**${data.voidPath}`, async route => {
    const upstream = await route.fetch();
    expect(upstream.ok()).toBe(true);
    await route.fulfill({ status: 200, json: { unexpected: true } });
  }, { times: 1 });
  await ui.confirm.click();
  await expect(ui.replay).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: "تم عكس الإيصال" })).toHaveCount(0);
  expect((await read(page, data.shiftPath)).refunds).toHaveLength(1);
  await ui.replay.click();
  await expect(page.getByRole("status").filter({ hasText: "تم عكس الإيصال" })).toBeVisible();
  expect((await read(page, data.shiftPath)).refunds).toHaveLength(1);
});
