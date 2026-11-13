import { expect, test, type APIRequestContext } from "@playwright/test";

const familyFixtureEnrollments = new Set([
  "84000014-0000-0000-0003-000000000001",
  "84000014-0000-0000-0003-000000000002",
]);

async function write(api: APIRequestContext, path: string, data: unknown, method = "POST") {
  const { token } = await api.get("/api/v1/auth/csrf").then(r => r.json());
  return api.fetch(path, { method, headers: { "X-CSRF-TOKEN": token }, data });
}
test("One family transfer is approved atomically and exposes both child receipts", async ({ page, browser }, info) => {
  test.setTimeout(100_000);
  const root = "/api/v1/guardian/subscriptions/family/requests";
  const owner = await browser.newContext({ baseURL: info.project.use.baseURL }); const staff = await owner.newPage();
  expect((await write(owner.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).ok()).toBe(true);
  const prior = await owner.request.get("/api/v1/payment-settings").then(r => r.json());
  let requestId: string | undefined;
  try {
    expect((await write(owner.request, "/api/v1/payment-settings", { instaPayAddress: "family-demo@example.test", walletNumber: null, recipientName: "حساب اصطناعي — لا تحول إليه", isActive: true }, "PUT")).ok()).toBe(true);
    const phoneNumber = "01000000014"; const challenge = await write(page.request, "/api/v1/auth/guardian/otp/request", { phoneNumber }); expect(challenge.ok()).toBe(true);
    expect((await write(page.request, "/api/v1/auth/guardian/otp/verify", { phoneNumber, challengeId: (await challenge.json()).challengeId, code: "246810" })).ok()).toBe(true);
    const before = await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json());
    await page.goto("/guardian/renew-family");
    const options = await page.request.get("/api/v1/guardian/subscriptions/family/options").then(r => r.json());
    const enrollments = options.items.filter((item: { id: string }) => familyFixtureEnrollments.has(item.id));
    expect(enrollments).toHaveLength(2);
    for (const e of enrollments) { await page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true }).check(); await page.getByRole("combobox", { name: `الباقة لـ${e.player} · ${e.sport}`, exact: true }).selectOption("51000000-0000-0000-0000-000000000001"); }
    await expect(page.getByRole("checkbox", { checked: true })).toHaveCount(2);
    await page.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }).click(); await expect(page.getByRole("region", { name: "ملخص تجديد الأسرة" })).toBeVisible(); await page.getByRole("button", { name: "متابعة الدفع للأسرة" }).click(); await expect(page).toHaveURL(/\/guardian\/family-renewals\/[a-f0-9-]+$/); const id = page.url().split("/").at(-1)!;
    requestId = id;
    await expect(page.getByText("family-demo@example.test", { exact: true })).toBeVisible(); expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
    const sendProof = page.getByRole("button", { name: "إرسال إثبات الأسرة", exact: true });
    const proofInput = page.getByLabel("صورة إثبات الأسرة");
    await expect(sendProof).toBeDisabled();
    const corruptPng = Buffer.concat([Buffer.from("89504e470d0a1a0a", "hex"), Buffer.from("invalid image data")]);
    await proofInput.setInputFiles({ name: "corrupt-proof.png", mimeType: "image/png", buffer: corruptPng });
    const corruptUpload = page.waitForResponse(response => response.url().endsWith("/api/v1/assets/upload") && response.request().method() === "POST");
    await sendProof.click();
    expect((await corruptUpload).status()).toBe(400);
    await expect(page.getByRole("status").filter({ hasText: /صورة الإيصال تالفة/ })).toBeVisible();
    const afterCorrupt = await page.request.get(`${root}/${id}`).then(r => r.json());
    expect(afterCorrupt.status).toBe("Pending");
    expect(afterCorrupt.proofUrl).toBeFalsy();
    expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
    const image = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=", "base64");
    await proofInput.setInputFiles({ name: "synthetic-family-proof.png", mimeType: "image/png", buffer: image }); await sendProof.click(); await expect(page.getByText(/وصل إثبات الدفع/)).toBeVisible(); expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
    const pending = await page.request.get(`${root}/${id}`).then(r => r.json());
    const day = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(pending.createdAtUtc));
    const financialReport = async () => {
      const response = await owner.request.get(`/api/v1/reports/financial?from=${day}&to=${day}`);
      expect(response.ok()).toBe(true);
      return response.json();
    };
    const priorFinancial = await financialReport();
    await page.setViewportSize({ width: 390, height: 1000 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath("family-pending-390.png"), fullPage: true, animations: "disabled" });
    const staffPath = `/api/v1/manage/family-renewals/${id}`;
    await staff.route(`**${staffPath}`, route => route.fulfill({ status: 503, body: "Service Unavailable" }));
    await staff.goto("/dashboard/subscriptions/payments?type=Renewal&status=Pending&allDates=true");
    await staff.getByRole("textbox", { name: "رقم الطلب", exact: true }).fill(id);
    await staff.getByRole("button", { name: "بحث", exact: true }).click();
    const row = staff.getByRole("table",{name:"طلبات الدفع",exact:true}).getByRole("row").filter({ has: staff.locator(`a[href^='/dashboard/family-renewals/${id}?']`) });
    await expect(row).toHaveCount(1);
    await row.getByRole("link", { name: "مراجعة" }).click();
    await expect(staff).toHaveURL(url => url.pathname === `/dashboard/family-renewals/${id}`, { timeout: 20_000 });
    await expect(staff.getByRole("heading", { name: "مراجعة تجديد الأسرة" })).toBeVisible({ timeout: 20_000 });
    await expect(staff.locator("main").getByRole("alert")).toContainText("تعذر تحميل طلب الأسرة");
    await staff.unroute(`**${staffPath}`);
    await staff.getByRole("button", { name: "إعادة تحميل الطلب" }).click();
    await expect(staff.getByText(/المجموعة الحالية/)).toHaveCount(2);
    await expect(staff.getByRole("combobox", { name: /^مجموعة / })).toHaveCount(0);
    const proofLink = staff.getByRole("link", { name: "فتح إثبات التحويل الواحد" });
    await expect(proofLink).toBeVisible();
    const contrast = await proofLink.evaluate(element => {
      const channels = (value: string) => (value.match(/[\d.]+/g) ?? []).map(Number);
      const light = (value: number[]) => value.slice(0, 3).map(channel => channel / 255)
        .map(channel => channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4)
        .reduce((sum, channel, index) => sum + [.2126, .7152, .0722][index] * channel, 0);
      const foreground = channels(getComputedStyle(element).color);
      for (let ancestor: Element | null = element; ancestor; ancestor = ancestor.parentElement) {
        const background = channels(getComputedStyle(ancestor).backgroundColor);
        if (background.length === 3 || background[3] === 1) {
          const [darker, lighter] = [light(foreground), light(background)].sort((a, b) => a - b);
          return (lighter + .05) / (darker + .05);
        }
      }
      return 0;
    });
    expect(contrast).toBeGreaterThanOrEqual(4.5);
    const evidence = await staff.request.get((await proofLink.getAttribute("href"))!);
    expect(evidence.ok()).toBe(true);
    expect(await evidence.body()).toEqual(image);
    await staff.setViewportSize({ width: 1440, height: 1100 }); expect(await staff.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await staff.screenshot({ path: info.outputPath("family-staff-1440.png"), fullPage: true, animations: "disabled" });
    const approve = staff.getByRole("button", { name: "قبول وتجديد كل التسجيلات" });
    const dialog = staff.getByRole("dialog", { name: "تأكيد اعتماد تجديد الأسرة" });
    let decisions = 0;
    await staff.route(`**${staffPath}/review`, async route => {
      decisions++;
      const body = route.request().postDataJSON();
      expect(body.accept).toBe(true);
      expect(body.revision).toBe(pending.revision);
      expect(body.placements).toHaveLength(2);
      expect(body.placements.every((line: { groupId: string }) => Boolean(line.groupId))).toBe(true);
      const response = await route.fetch();
      expect(response.ok(), await response.text()).toBe(true);
      await route.abort();
    });
    await approve.click();
    await expect(dialog).toContainText("تسجيلين");
    await expect(dialog.getByRole("button", { name: "إلغاء", exact: true })).toBeFocused();
    for (const width of [320, 390, 1440]) {
      await staff.setViewportSize({ width, height: 900 });
      expect(await staff.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      if (width === 390) await staff.screenshot({ path: info.outputPath("family-confirm-390.jpg"), type: "jpeg", quality: 70 });
    }
    await staff.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    expect(decisions).toBe(0);
    await approve.click();
    await dialog.getByRole("button", { name: "تأكيد الاعتماد" }).click();
    const verify = staff.getByRole("button", { name: "التحقق من حالة الطلب" });
    await expect(verify).toBeVisible();
    await expect(approve).toHaveCount(0);
    await expect(staff.getByRole("button", { name: "رفض الطلب كاملًا" })).toHaveCount(0);
    await verify.click();
    await expect(staff.getByText("تم تجديد الأسرة", { exact: true })).toBeVisible();
    expect(decisions).toBe(1);
    await page.reload();
    await expect(page.getByText("تم تجديد الأسرة", { exact: true })).toBeVisible();
    const detail = await page.request.get(`${root}/${id}`).then(r => r.json()); expect(detail.items).toHaveLength(2); for (const line of detail.items) { expect(line.receiptId).toBeTruthy(); expect((await page.request.get(`/api/v1/guardian/subscriptions/receipts/${line.receiptId}`)).ok()).toBe(true); }
    const after = await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json()); expect(after.items.length).toBe(before.items.length + 2);
    const finalFinancial = await financialReport();
    expect(Math.round((Number(finalFinancial.totalAmount) - Number(priorFinancial.totalAmount)) * 100)).toBe(Math.round(Number(detail.totalAmount) * 100));
    for (const line of detail.items) {
      const receipt = page.locator(`a[href="/guardian/receipts/${line.receiptId}"]`);
      await receipt.click();
      await expect(page.getByRole("article", { name: "إيصال دفع", exact: true })).toBeVisible();
      await page.goBack();
      await expect(page.getByRole("heading", { name: "طلب تجديد الأسرة" })).toBeVisible();
    }
    await expect(page.getByRole("link", { name: "العودة للطلبات", exact: true })).toHaveAttribute("href", "/guardian/family-renewals");
    const back = staff.getByRole("link", { name: "العودة إلى طلبات الدفع", exact: true });
    const returnUrl = new URL((await back.getAttribute("href"))!, staff.url());
    expect(returnUrl.pathname).toBe("/dashboard/subscriptions/payments");
    expect(returnUrl.searchParams.get("type")).toBe("Renewal");
    expect(returnUrl.searchParams.get("status")).toBe("Pending");
    expect(returnUrl.searchParams.get("reference")).toBe(id);
    await staff.reload(); await expect(staff.getByRole("button", { name: "قبول وتجديد كل التسجيلات" })).toHaveCount(0); expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(after); await page.screenshot({ path: info.outputPath("family-approved-390.png"), fullPage: true, animations: "disabled" });
  } finally {
    try {
      if (requestId) {
        const detail = await page.request.get(`${root}/${requestId}`).then(response => response.json());
        if (detail.status === "Pending") {
          expect((await write(page.request, `${root}/${requestId}/cancel`, { revision: detail.revision })).ok()).toBe(true);
        }
      }
    } finally {
      await write(owner.request, "/api/v1/payment-settings", { ...prior, isActive: prior.isActive ?? prior.configured }, "PUT");
      await owner.close();
    }
  }
});

test("Family rejection and cancellation remain outside collections and receipts", async ({ page, browser }, info) => {
  test.setTimeout(80_000);
  const family = "/api/v1/guardian/subscriptions/family";
  const owner = await browser.newContext({ baseURL: info.project.use.baseURL });
  const staff = await owner.newPage();
  expect((await write(owner.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).ok()).toBe(true);
  const prior = await owner.request.get("/api/v1/payment-settings").then(response => response.json());
  const requestIds: string[] = [];
  try {
    expect((await write(owner.request, "/api/v1/payment-settings", {
      instaPayAddress: "family-demo@example.test", walletNumber: null, recipientName: "حساب اصطناعي", isActive: true,
    }, "PUT")).ok()).toBe(true);
    const phoneNumber = "01000000014";
    const challenge = await write(page.request, "/api/v1/auth/guardian/otp/request", { phoneNumber });
    expect(challenge.ok()).toBe(true);
    const verified = await write(page.request, "/api/v1/auth/guardian/otp/verify", { phoneNumber, challengeId: (await challenge.json()).challengeId, code: "246810" });
    expect(verified.ok()).toBe(true);
    const receipts = async () => (await page.request.get("/api/v1/guardian/subscriptions/receipts")).json();
    const beforeReceipts = await receipts();
    const options = await page.request.get(`${family}/options`).then(response => response.json());
    const lines = options.items.filter((item: { id: string }) => familyFixtureEnrollments.has(item.id))
      .map((item: { id: string }) => ({ sportEnrollmentId: item.id, subscriptionPlanId: "51000000-0000-0000-0000-000000000001" }));
    expect(lines).toHaveLength(2);
    async function newRequest() {
      const quote = await write(page.request, `${family}/quote`, { lines });
      expect(quote.ok(), await quote.text()).toBe(true);
      const created = await write(page.request, `${family}/requests`, { lines, quoteHash: (await quote.json()).quoteHash, idempotencyKey: crypto.randomUUID() });
      expect(created.ok(), await created.text()).toBe(true);
      const request = await created.json();
      requestIds.push(request.id);
      return request;
    }
    const reject = await newRequest();
    const date = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(reject.createdAtUtc));
    const report = async () => {
      const response = await owner.request.get(`/api/v1/reports/financial?from=${date}&to=${date}`);
      expect(response.ok()).toBe(true);
      return response.json();
    };
    const beforeReport = await report();
    await staff.goto(`/dashboard/family-renewals/${reject.id}`);
    await expect(staff.getByRole("button", { name: "قبول وتجديد كل التسجيلات" })).toBeDisabled();
    const reason = "بيانات التحويل بحاجة إلى مراجعة مع الإدارة";
    await staff.getByLabel(/ملاحظة أو سبب الرفض/).fill(reason);
    await staff.getByRole("button", { name: "رفض الطلب كاملًا" }).click();
    const review = staff.getByRole("dialog", { name: "تأكيد رفض طلب الأسرة" });
    await expect(review).toContainText(reason);
    const beforeReject = await page.request.get(`${family}/requests/${reject.id}`).then(response => response.json());
    expect(beforeReject.status).toBe("Pending");
    await review.getByRole("button", { name: "تأكيد رفض الطلب" }).click();
    await expect(staff.getByText("لم يُقبل الطلب", { exact: true })).toBeVisible();
    await page.goto(`/guardian/family-renewals/${reject.id}`);
    await expect(page.getByText(reason, { exact: true })).toBeVisible();
    expect(await receipts()).toEqual(beforeReceipts);

    const cancel = await newRequest();
    await page.goto(`/guardian/family-renewals/${cancel.id}`);
    const cancelTrigger = page.getByRole("button", { name: "إلغاء طلب التجديد" });
    await cancelTrigger.click();
    const keep = page.getByRole("button", { name: "الاحتفاظ بالطلب" });
    await expect(keep).toBeFocused();
    await keep.click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    let cancellations = 0;
    await page.route(`**${family}/requests/${cancel.id}/cancel`, async route => {
      cancellations++;
      const response = await route.fetch();
      expect(response.ok(), await response.text()).toBe(true);
      await route.abort();
    });
    await cancelTrigger.click();
    await page.getByRole("button", { name: "تأكيد إلغاء الطلب" }).click();
    await page.getByRole("button", { name: "التحقق من حالة الطلب" }).click();
    await expect(page.getByText("ملغي", { exact: true })).toBeVisible();
    expect(cancellations).toBe(1);
    expect(await receipts()).toEqual(beforeReceipts);
    expect(await report()).toEqual(beforeReport);
  } finally {
    try {
      for (const id of requestIds) {
        const path = `${family}/requests/${id}`;
        const detail = await page.request.get(path).then(response => response.json());
        if (detail.status === "Pending") {
          expect((await write(page.request, `${path}/cancel`, { revision: detail.revision })).ok()).toBe(true);
        }
      }
    } finally {
      await write(owner.request, "/api/v1/payment-settings", { ...prior, isActive: prior.isActive ?? prior.configured }, "PUT");
      await owner.close();
    }
  }
});
