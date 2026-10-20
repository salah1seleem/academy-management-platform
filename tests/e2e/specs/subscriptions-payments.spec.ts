import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { formatMoneyAr } from "../../../apps/web/src/lib/formatters";

async function startFootballRenewal(page: Page) {
  const child = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" });
  const row = child.locator(".subscription-mini p").filter({ hasText: "كرة القدم" });
  await row.getByRole("link").click();
  await page.getByText("اشتراك شهري", { exact: true }).click();
  await page.getByRole("button", { name: /الدفع الإلكتروني — ادفع الآن/ }).click();
  await expect(page.getByText("بيئة دفع تجريبية — لا توجد أموال حقيقية")).toBeVisible();
}

test("Guardian completes a successful online test renewal and sees receipt", async ({ page }) => {
  await guardianLogin(page); await startFootballRenewal(page);
  const paymentId = page.url().split("/").at(-1);
  const payment = await page.request.get(`/api/v1/guardian/subscriptions/payments/${paymentId}`).then(r => r.json());
  expect(payment.amount).toBe(payment.finalAmount);
  expect(payment.finalAmount).toBe(payment.originalAmount - payment.discountAmount);
  expect(payment.finalAmount).toBeGreaterThan(0);
  await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
  await expect(page.getByText("تم الدفع وتجديد الاشتراك بنجاح")).toBeVisible();
  await page.getByRole("link", { name: "عرض الإيصال" }).click();
  await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible();
  await expect(page.locator(".printable-receipt dl > div").filter({ has: page.getByText("المبلغ المدفوع", { exact: true }) }).locator("dd")).toHaveText(formatMoneyAr(payment.finalAmount, payment.currency));
});

test("Guardian failed payment does not report a renewal", async ({ page }) => {
  await guardianLogin(page); await startFootballRenewal(page);
  await page.getByRole("button", { name: "محاكاة فشل الدفع" }).click();
  await expect(page.getByText("فشلت عملية الدفع ولم يتم تجديد الاشتراك.")).toBeVisible();
  await expect(page.getByRole("link", { name: "عرض الإيصال" })).toHaveCount(0);
});

test("Guardian renews for another player without gaining profile access", async ({ page }) => {
  await guardianLogin(page);
  await page.getByRole("link", { name: "تجديد اشتراك لغيره" }).click();
  await page.getByLabel("كود التجديد").fill("RNW-DEMO-NG-0003-7K9M");
  await page.getByRole("button", { name: "تحقق من الكود" }).click();
  await expect(page.getByRole("heading", { name: "تأكيد المستفيد" })).toBeVisible();
  await expect(page.getByText("عمر أحمد حسن")).toBeVisible();
  await expect(page.getByText("كرة القدم")).toBeVisible();
  await page.getByText("اشتراك شهري", { exact: true }).click();
  await page.getByRole("button", { name: "الدفع الإلكتروني — ادفع الآن" }).click();
  await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
  await expect(page.getByText("تم الدفع وتجديد الاشتراك بنجاح")).toBeVisible();
  await page.getByRole("link", { name: "عرض الإيصال" }).click();
  await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible();
  await expect(page.getByText("عمر أحمد حسن")).toBeVisible();
  await page.goto("/guardian");
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toBeVisible();
  await expect(page.getByText("عمر أحمد حسن")).toHaveCount(0);
});

test("Owner sees approved and rejected payment requests and confirmed collection", async ({ page }) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "المتابعات والطلبات" }).click();
  await page.getByRole("navigation", { name: "مساحة الإدارة" }).getByRole("link", { name: "طلبات الدفع", exact: true }).click(); await expect(page.getByRole("heading", { name: "طلبات الدفع" })).toBeVisible();
  await page.getByRole("button", { name: "مسح", exact: true }).click(); await expect(page.getByLabel("من تاريخ")).toHaveValue(""); await expect(page.getByLabel("إلى تاريخ")).toHaveValue("");
  await page.getByRole("combobox", { name: "الحالة", exact: true }).selectOption("Approved"); await expect(page.locator(".payment-queue-row").filter({ hasText: "مقبول" }).first()).toBeVisible(); await page.getByRole("combobox", { name: "الحالة", exact: true }).selectOption("Rejected"); await expect(page.locator(".payment-queue-row").filter({ hasText: "مرفوض" }).first()).toBeVisible();
  await page.goto("/dashboard/subscriptions/collections");
  await expect(page.getByText(/إجمالي التحصيل المؤكد/)).toBeVisible(); await expect(page.getByText(/DEMO-|RC-/).first()).toBeVisible();
});
