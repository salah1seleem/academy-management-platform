import { expect, Page, test } from "@playwright/test";

async function staffLogin(page: Page, email: string) { await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**"); }
async function guardianLogin(page: Page) { await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click(); await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click(); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click(); await page.waitForURL("**/guardian**"); }

test.describe("Slice 8A operational reporting journeys", () => {
  test("Journey A — owner dashboard uses stored financial and subscription metrics", async ({ page }) => {
    await staffLogin(page, "owner.nogoom@example.test");
    await expect(page.getByRole("heading", { name: "لوحة المالك" })).toBeVisible();
    for (const label of ["إجمالي التحصيلات", "تحصيلات هذا الشهر", "اشتراكات فعالة", "تنتهي قريبًا", "اشتراكات منتهية"]) await expect(page.getByText(label, { exact: true })).toBeVisible();
    await page.getByRole("link", { name: "فتح التقرير المالي" }).click();
    await expect(page.getByRole("heading", { name: "التقارير المالية" })).toBeVisible();
  });

  test("Journey B — financial filters and real CSV download", async ({ page }) => {
    await staffLogin(page, "admin.nogoom@example.test"); await page.goto("/dashboard/reports/financial");
    await expect(page.getByText("إجمالي التحصيل المؤكد", { exact: true })).toBeVisible();
    await page.getByLabel("من تاريخ").fill("2026-09-01"); await page.getByLabel("إلى تاريخ").fill("2026-09-30"); await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" }); await page.getByRole("button", { name: "تطبيق الفلاتر" }).click();
    await expect(page.getByText(/DEMO-|RC-/).first()).toBeVisible();
    const download = page.waitForEvent("download"); await page.getByRole("link", { name: "تصدير CSV" }).click(); expect((await download).suggestedFilename()).toMatch(/^financial-collections-2026-09\.csv$/);
  });

  test("Journey C — attendance birth-year and group filters export stored rows", async ({ page }) => {
    await staffLogin(page, "admin.nogoom@example.test"); await page.goto("/dashboard/reports/attendance");
    await page.getByLabel("الشهر").fill("9"); await page.getByLabel("السنة").fill("2026"); await page.getByLabel("سنة الميلاد").fill("2019"); await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" }); await page.getByLabel("المجموعة").selectOption({ label: "ناشئين 2016 — مجموعة أ" }); await page.getByRole("button", { name: "تطبيق الفلاتر" }).click();
    await expect(page.getByText(/يوسف خالد سمير/).first()).toBeVisible();
    const download = page.waitForEvent("download"); await page.getByRole("link", { name: "تصدير CSV" }).click(); expect((await download).suggestedFilename()).toBe("attendance-players-2026-09.csv");
  });

  test("Journey D — guardian payment history opens printable Arabic receipt", async ({ page }) => {
    await guardianLogin(page); await page.getByRole("link", { name: "الإيصالات" }).click();
    await expect(page.getByRole("heading", { name: "إيصالات الدفع" })).toBeVisible(); await page.getByRole("link").filter({ hasText: /DEMO-|RC-/ }).first().click();
    await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible(); await expect(page.getByText("أكاديمية النجوم الرياضية")).toBeVisible(); await expect(page.getByRole("button", { name: "طباعة الإيصال" })).toBeVisible();
  });
});
