import { expect, Page, test, TestInfo } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { formatMoneyAr } from "../../../apps/web/src/lib/formatters";

test.use({ viewport: { width: 390, height: 844 } });

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.context().clearCookies();
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard**");
}

async function assertMobileArabicSurface(page: Page) {
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.locator("body").evaluate(element => getComputedStyle(element).fontFamily)).toContain("Cairo");
  await expect(page.locator("body")).not.toContainText(/Pending|Confirmed|Failed|Cancelled|Duration|Sessions|Combined|EGP/);
}

async function attachScreen(page: Page, testInfo: TestInfo, name: string) {
  await testInfo.attach(name, { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
}

async function createAdminRenewal(page: Page, testInfo?: TestInfo) {
  // Independent synthetic enrollment: a failed prior run must not leave this
  // journey blocked behind another test's pending renewal for NG-0003.
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001";
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  async function write(path: string, data: unknown) {
    const r = await page.request.post(path, { headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() }, data });
    expect(r.ok(), await r.text()).toBe(true); return r.json();
  }
  const groupId = await write("/api/v1/manage/groups", { arabicName: `تجديد اصطناعي ${Date.now()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  const guardianPhone = `010${Date.now().toString().slice(-8)}`;
  const quote = await write("/api/v1/people/registration-quote", { branchId, planId, guardianPhone });
  const saved = await write("/api/v1/people/registrations", { playerArabicName: "طفل تجديد اصطناعي", dateOfBirth: "2017-01-01", guardianName: "ولي تجديد اصطناعي", guardianPhone, branchId, sportId, groupId, planId, paymentMethod: "Online", expectedAmount: quote.finalAmount });
  const detail = await page.request.get(`/api/v1/people/players/${saved.playerId}`).then(r => r.json());
  await page.goto("/dashboard/subscriptions/renew/new");
  await page.getByPlaceholder("اسم اللاعب أو الكود أو هاتف ولي الأمر").fill(detail.playerCode);
  const row = page.locator("article").filter({ hasText: detail.playerCode }).filter({ hasText: "كرة القدم" });
  await expect(row).toBeVisible();
  if (testInfo) await attachScreen(page, testInfo, "admin-renewal-selection-390");
  await row.getByRole("button", { name: "اختيار" }).click();
  await page.getByText("اشتراك شهري", { exact: true }).click();
  await expect(page.getByText("بداية الفترة المتوقعة")).toBeVisible();
  await assertMobileArabicSurface(page);
  if (testInfo) await attachScreen(page, testInfo, "admin-renewal-review-390");
  await page.getByRole("button", { name: "إنشاء طلب التجديد والدفع" }).click();
  await page.waitForURL("**/dashboard/subscriptions/payments/**");
}

test.describe.serial("Slice 8D demo closure journeys", () => {
  test("Journey A — Admin renewal completes through receipt and new period", async ({ page }, testInfo) => {
    await staffLogin(page);
    await createAdminRenewal(page, testInfo);
    await expect(page.getByText("قيد الانتظار", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
    await expect(page.getByText("مؤكد", { exact: true })).toBeVisible();
    await attachScreen(page, testInfo, "admin-payment-success-390");
    await page.getByRole("link", { name: "عرض الإيصال" }).click();
    await expect(page.getByRole("article").getByRole("heading", { name: "إيصال دفع" })).toBeVisible();
    await assertMobileArabicSurface(page);
    await attachScreen(page, testInfo, "admin-receipt-390");
  });

  test("Journey B — discounted Admin renewal pays and receipts the exact ten-percent discounted amount", async ({ page }) => {
    await staffLogin(page);
    await createAdminRenewal(page);
    const originalPaymentUrl = page.url();
    const paymentId = originalPaymentUrl.split("/").pop()!;
    const payment = await page.request.get(`/api/v1/subscriptions/payments/${paymentId}`).then(r => r.json());
    const renewalId = payment.renewalId;
    const finalAmount = formatMoneyAr(payment.originalAmount - Math.round(payment.originalAmount * 10) / 100, payment.currency);
    await page.goto(`/dashboard/subscriptions/renewals/${renewalId}`);
    await page.getByRole("button", { name: /^(تطبيق خصم|تعديل الخصم)$/ }).click();
    await page.getByLabel("نوع الخصم").selectOption({ label: "نسبة مئوية" });
    await page.getByLabel("قيمة الخصم").fill("10");
    await page.getByLabel("سبب الخصم").fill("خصم إخوة تجريبي");
    await page.getByRole("button", { name: "حفظ الخصم" }).click();
    await expect(page.getByText("الإجمالي بعد الخصم", { exact: true }).locator("..")).toContainText(finalAmount);
    await page.goto(originalPaymentUrl);
    await page.getByRole("link", { name: "فتح طلب الدفع الحالي" }).click();
    await expect(page.getByText("الإجمالي", { exact: true }).locator("..")).toContainText(finalAmount);
    await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
    await page.getByRole("link", { name: "عرض الإيصال" }).click();
    await expect(page.getByText("المبلغ المدفوع", { exact: true }).locator("..")).toContainText(finalAmount);
  });

  test("Journey C — Guardian checkout, failure, retry and success remain Arabic", async ({ page }, testInfo) => {
    await guardianLogin(page);
    const child = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" });
    await child.locator(".subscription-mini p").filter({ hasText: "كرة القدم" }).getByRole("link").click();
    await page.getByText("اشتراك شهري", { exact: true }).click();
    await page.getByRole("button", { name: /الدفع الإلكتروني — ادفع الآن/ }).click();
    await page.waitForURL("**/guardian/checkout/**");
    await assertMobileArabicSurface(page);
    await attachScreen(page, testInfo, "guardian-checkout-390");
    await page.getByRole("button", { name: "محاكاة فشل الدفع" }).click();
    await expect(page.getByText("فشلت عملية الدفع ولم يتم تجديد الاشتراك.")).toBeVisible();
    await page.getByRole("button", { name: "إعادة محاولة الدفع" }).click();
    await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
    await expect(page.getByText("تم الدفع وتجديد الاشتراك بنجاح")).toBeVisible();
    await assertMobileArabicSurface(page);
    await attachScreen(page, testInfo, "guardian-payment-success-390");
  });

  test("Journey D — root routing and PWA entry are auth-aware", async ({ page }) => {
    await page.context().clearCookies();
    await page.goto("/");
    await page.waitForURL("**/login");
    await expect(page.locator("body")).not.toContainText(/Foundation|Slice 1/);
    await guardianLogin(page);
    await page.goto("/");
    await page.waitForURL("**/guardian**");
    const manifest = await page.evaluate(async () => fetch("/manifest.webmanifest").then(response => response.json()));
    expect(manifest.start_url).toBe("/login");
    expect(manifest.display).toBe("standalone");
  });
});
