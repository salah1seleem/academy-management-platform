import { expect, Page, test } from "@playwright/test";

async function staffLogin(page: Page, email = "coach.nogoom@example.test") {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click();
}

async function guardianLogin(page: Page) {
  await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click(); await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click(); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click();
}

test("Coach creates, saves and publishes an assigned player's evaluation", async ({ page }) => {
  await staffLogin(page); await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("button", { name: /التقييمات/ }).click(); await page.getByRole("link", { name: "تقييمات اللاعبين" }).click();
  await page.getByRole("link", { name: "تقييم جديد" }).click(); await page.getByLabel("اللاعب").selectOption({ label: "عمر أحمد محمود — كرة القدم — ناشئين 2016 — مجموعة أ" }); await page.getByRole("button", { name: "إنشاء المسودة" }).click();
  const scoreInputs = page.getByRole("spinbutton"); await scoreInputs.nth(0).fill("82"); await scoreInputs.nth(1).fill("79"); await scoreInputs.nth(2).fill("88"); await page.getByRole("button", { name: "حفظ المسودة" }).click(); await expect(page.getByRole("button", { name: "حفظ المسودة" })).toBeEnabled();
  await page.getByRole("button", { name: "نشر التقييم" }).click(); await page.getByRole("button", { name: "تأكيد النشر" }).click(); await expect(page.getByRole("heading", { name: "تقرير اللاعب" })).toBeVisible(); await expect(page.getByText("التقييم الإجمالي")).toBeVisible();
});

test("Guardian sees published football report and never sees Draft", async ({ page }) => {
  await guardianLogin(page); const child = page.locator("article").filter({ hasText: "عمر أحمد محمود" }).first(); await expect(child.getByRole("heading", { name: "تقرير اللاعب" })).toBeVisible(); await expect(child).not.toContainText("مسودة"); await child.getByRole("link", { name: /عرض التقرير/ }).first().click();
  await expect(page.getByText("جناح أيمن")).toBeVisible(); await expect(page.getByText("التقييم الإجمالي")).toBeVisible(); await expect(page.getByRole("img", { name: "مخطط محاور أداء كرة القدم" })).toBeVisible(); await expect(page.getByLabel("القيم النصية للمحاور")).toContainText("التمرير"); await expect(page.getByText("142 سم")).toBeVisible(); await expect(page.getByText("38 كجم")).toBeVisible(); await expect(page.getByText("اليمنى")).toBeVisible(); await expect(page.getByRole("heading", { name: "المعايير التفصيلية" })).toBeVisible(); await expect(page.getByText(/ملعب/)).toHaveCount(0);
});

test("Coach and guardian are denied unrelated evaluations", async ({ page }) => {
  await staffLogin(page); await page.goto("/dashboard/evaluations/72000000-0000-0000-0000-000000000004/report"); await expect(page.getByText("تعذر تحميل التقرير أو أنه غير متاح لك.")).toBeVisible();
  await page.context().clearCookies(); await guardianLogin(page); await page.goto("/guardian/evaluations/72000000-0000-0000-0000-000000000005"); await expect(page.getByText("تعذر تحميل التقرير أو أنه غير متاح لك.")).toBeVisible();
});
