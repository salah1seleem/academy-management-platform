import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/);
}

test("Admin creates, saves and publishes a player's evaluation", async ({ page }) => {
  await staffLogin(page); await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("button", { name: "الفريق والتطوير" }).click(); await page.getByRole("link", { name: "تقييمات اللاعبين" }).click();
  await page.getByRole("link", { name: "تقييم جديد" }).click(); await page.getByRole("combobox", { name: "اللاعب", exact: true }).selectOption({ label: "عمر أحمد محمود — كرة القدم — ناشئين 2016 — مجموعة أ" }); await page.getByRole("button", { name: "إنشاء المسودة" }).click();
  const scoreInputs = page.getByRole("spinbutton"); await scoreInputs.nth(0).fill("82"); await scoreInputs.nth(1).fill("79"); await scoreInputs.nth(2).fill("88"); await page.getByRole("button", { name: "حفظ المسودة" }).click(); await expect(page.getByRole("button", { name: "حفظ المسودة" })).toBeEnabled();
  await page.getByRole("button", { name: "نشر التقييم" }).click(); await page.getByRole("button", { name: "تأكيد النشر" }).click(); await expect(page.getByRole("heading", { name: "تقرير اللاعب" })).toBeVisible(); await expect(page.getByText("التقييم الإجمالي")).toBeVisible();
});

test("Guardian sees published football report and never sees Draft", async ({ page }) => {
  await guardianLogin(page); const child = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" }); await expect(child).not.toContainText("مسودة"); await child.getByRole("link", { name: "عرض الملف" }).click(); await expect(page.getByRole("heading", { name: "تقرير اللاعب" })).toBeVisible(); await page.getByRole("link", { name: /عرض التقرير/ }).first().click();
  await expect(page.getByText("جناح أيمن")).toBeVisible(); await expect(page.getByText("التقييم الإجمالي")).toBeVisible(); await expect(page.getByRole("img", { name: "مخطط محاور أداء كرة القدم" })).toBeVisible(); await expect(page.getByLabel("القيم النصية للمحاور")).toContainText("التمرير"); await expect(page.getByText("142 سم")).toBeVisible(); await expect(page.getByText("38 كجم")).toBeVisible(); await expect(page.getByText("اليمنى")).toBeVisible(); await expect(page.getByRole("heading", { name: "المعايير التفصيلية" })).toBeVisible(); await expect(page.getByText(/ملعب/)).toHaveCount(0);
});

test("Guardian is denied an unrelated evaluation", async ({ page }) => {
  await guardianLogin(page); await page.goto("/guardian/evaluations/72000000-0000-0000-0000-000000000005"); await expect(page.getByText("تعذر تحميل التقرير أو أنه غير متاح لك.")).toBeVisible();
});
