import { expect, Page, test } from "@playwright/test";

async function guardianLogin(page: Page) {
  await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click(); await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click(); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click();
}
async function openOmar(page: Page) { const card = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" }); await card.getByRole("link", { name: "عرض الملف" }).click(); }

test.describe("Slice 7 Guardian content journeys", () => {
  test("Journey A — linked sports catalog is deduplicated and catalog-only", async ({ page }) => {
    await guardianLogin(page); const catalog = page.locator("section.catalog-section"); await expect(catalog.getByRole("heading", { name: "كرة القدم", exact: true })).toHaveCount(1); await expect(catalog.getByText("كرة تدريب", { exact: true })).toBeVisible(); await expect(catalog.getByRole("heading", { name: "السباحة", exact: true })).toHaveCount(1); await expect(catalog.getByText("نظارة سباحة", { exact: true })).toBeVisible(); await expect(catalog.getByText(/شراء|أضف للسلة|اطلب الآن/)).toHaveCount(0);
  });

  test("Journey B — nutrition is an informational library with honest status", async ({ page }) => {
    await guardianLogin(page); await openOmar(page); await page.getByRole("link", { name: /التغذية والصحة/ }).click(); await expect(page.getByRole("tab", { name: "الإفطار" })).toHaveAttribute("aria-selected", "true"); await page.getByRole("link", { name: /شكشوكة/ }).click(); await expect(page.getByRole("heading", { name: "شكشوكة" })).toBeVisible(); for (const fact of ["السعرات الحرارية", "البروتين", "الكربوهيدرات", "الدهون"]) await expect(page.getByText(fact, { exact: true })).toBeVisible(); await expect(page.getByText(/الحصة:/)).toBeVisible(); await expect(page.getByText("بيانات تجريبية غير مراجعة")).toBeVisible(); const details = page.locator("article.meal-details"); for (const forbidden of ["السعر", "التقييم", "الكمية", "السلة", "الطلب", "الدفع"]) await expect(details.getByText(forbidden, { exact: true })).toHaveCount(0);
  });

  test("Journey C — Guardian sees published medical content only", async ({ page }) => {
    await guardianLogin(page); await openOmar(page); await page.getByRole("link", { name: /الإصابات والاستشارات الطبية/ }).click(); await expect(page.getByText("متابعة إجهاد بسيط بعد التدريب")).toBeVisible(); await expect(page.getByText("مسودة داخلية غير منشورة")).toHaveCount(0); await expect(page.getByText(/ملاحظة داخلية/)).toHaveCount(0); const denied = await page.request.get("/api/v1/guardian/children/36000000-0000-0000-0000-000000000003/medical"); expect(denied.status()).toBe(404);
  });

  test("Journey D — child gallery contains published project media only", async ({ page }) => {
    await guardianLogin(page); await openOmar(page); await page.getByRole("link", { name: /الصور والفيديوهات/ }).click(); await expect(page.getByText("لحظة من التدريب — رسم تجريبي")).toBeVisible(); await expect(page.getByText("إنجاز تدريبي — رسم تجريبي")).toBeVisible(); await expect(page.getByText("عنصر غير منشور")).toHaveCount(0); await expect(page.getByText("وسائط الأكاديمية الثانية")).toHaveCount(0);
  });
});
