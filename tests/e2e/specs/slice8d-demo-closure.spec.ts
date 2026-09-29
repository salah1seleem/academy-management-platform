import { expect, Page, test, TestInfo } from "@playwright/test";

test.use({ viewport: { width: 390, height: 844 } });

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.context().clearCookies();
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard**");
}

async function guardianLogin(page: Page) {
  await page.context().clearCookies();
  await page.goto("/login");
  await page.getByRole("button", { name: "ولي أمر" }).click();
  await page.getByLabel("رقم الهاتف").fill("01000000001");
  await page.getByRole("button", { name: "طلب رمز تجريبي" }).click();
  await page.getByLabel("رمز التحقق").fill("246810");
  await page.getByRole("button", { name: "تحقق ودخول" }).click();
  await page.waitForURL("**/guardian**");
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
  await page.goto("/dashboard/subscriptions/renew/new");
  await page.getByPlaceholder("اسم اللاعب أو الكود أو هاتف ولي الأمر").fill("NG-0003");
  const row = page.locator("article").filter({ hasText: "NG-0003" }).filter({ hasText: "كرة القدم" }).first();
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

  test("Journey B — discounted Admin renewal pays and receipts 810 جنيه", async ({ page }) => {
    await staffLogin(page);
    await createAdminRenewal(page);
    const originalPaymentUrl = page.url();
    const paymentId = originalPaymentUrl.split("/").pop()!;
    const renewalId = await page.evaluate(async id => fetch(`/api/v1/subscriptions/payments/${id}`).then(response => response.json()).then((value: { renewalId: string }) => value.renewalId), paymentId);
    await page.goto(`/dashboard/subscriptions/renewals/${renewalId}`);
    await page.getByRole("button", { name: "تطبيق خصم" }).click();
    await page.getByLabel("نوع الخصم").selectOption({ label: "نسبة مئوية" });
    await page.getByLabel("قيمة الخصم").fill("10");
    await page.getByLabel("سبب الخصم").fill("خصم إخوة تجريبي");
    await page.getByRole("button", { name: "حفظ الخصم" }).click();
    await expect(page.getByText("الإجمالي بعد الخصم", { exact: true }).locator("..")).toContainText(/٨١٠.*جنيه/);
    await page.goto(originalPaymentUrl);
    await page.getByRole("link", { name: "فتح طلب الدفع الحالي" }).click();
    await expect(page.getByText("الإجمالي", { exact: true }).locator("..")).toContainText(/٨١٠.*جنيه/);
    await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
    await page.getByRole("link", { name: "عرض الإيصال" }).click();
    await expect(page.getByText("المبلغ المدفوع", { exact: true }).locator("..")).toContainText(/٨١٠.*جنيه/);
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
