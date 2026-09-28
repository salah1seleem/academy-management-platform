import { expect, Page, test } from "@playwright/test";

async function guardianLogin(page: Page) {
  await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click();
  await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click();
  await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click();
  await expect(page.getByRole("heading", { name: "الأبناء المرتبطون" })).toBeVisible();
}

async function startFootballRenewal(page: Page) {
  const child = page.locator("article").filter({ hasText: "عمر أحمد محمود" });
  const row = child.locator(".sport-subscription").filter({ hasText: "كرة القدم" });
  await row.getByRole("link", { name: "تجديد الاشتراك" }).click();
  await page.getByText("اشتراك شهري", { exact: true }).click();
  await page.getByRole("button", { name: /الدفع الإلكتروني — ادفع الآن/ }).click();
  await expect(page.getByText("بيئة دفع تجريبية — لا توجد أموال حقيقية")).toBeVisible();
}

test("Guardian completes a successful online test renewal and sees receipt", async ({ page }) => {
  await guardianLogin(page); await startFootballRenewal(page);
  await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click();
  await expect(page.getByText("تم الدفع وتجديد الاشتراك بنجاح")).toBeVisible();
  await page.getByRole("link", { name: "عرض الإيصال" }).click();
  await expect(page.getByText("إيصال تحصيل", { exact: true })).toBeVisible(); await expect(page.getByText("900 EGP")).toBeVisible();
});

test("Guardian failed payment does not report a renewal", async ({ page }) => {
  await guardianLogin(page); await startFootballRenewal(page);
  await page.getByRole("button", { name: "محاكاة فشل الدفع" }).click();
  await expect(page.getByText("فشلت عملية الدفع ولم يتم تجديد الاشتراك.")).toBeVisible();
  await expect(page.getByRole("link", { name: "عرض الإيصال" })).toHaveCount(0);
});

test("Admin sees confirmed and failed payments and confirmed collection", async ({ page }) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("admin.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("button", { name: /الاشتراكات/ }).click();
  await page.getByRole("link", { name: "طلبات الدفع" }).click(); await expect(page.getByRole("heading", { name: "طلبات الدفع" })).toBeVisible();
  await expect(page.getByText(/Confirmed/).first()).toBeVisible(); await expect(page.getByText(/Failed/).first()).toBeVisible();
  await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("link", { name: "التحصيلات" }).click();
  await expect(page.getByText(/إجمالي التحصيل المؤكد/)).toBeVisible(); await expect(page.getByText(/DEMO-|RC-/).first()).toBeVisible();
});
