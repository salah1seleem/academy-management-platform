import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { cairoDateRange } from "../support/attendance";

async function staffLogin(page: Page, email: string) { await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**"); }

test.describe("Slice 8A operational reporting journeys", () => {
  test("Journey A — owner dashboard uses stored financial and subscription metrics", async ({ page }) => {
    await staffLogin(page, "owner.nogoom@example.test");
    await expect(page.getByRole("heading", { name: "الرئيسية" })).toBeVisible();
    const metrics = page.locator(".owner-command-grid");
    for (const label of ["تحصيل اليوم", "تحصيل الشهر / الفترة"]) await expect(metrics.getByText(label, { exact: true })).toBeVisible();
    await expect(metrics.getByRole("link", { name: /فعالة/ })).toBeVisible();
    await expect(metrics.getByRole("link", { name: /تنتهي قريبًا/ })).toBeVisible();
    await expect(metrics.getByText("منتهية", { exact: true })).toBeVisible();
    await expect(page.locator(".requests-card").getByRole("link", { name: /طلبات الدفع/ })).toBeVisible();
    await expect(page.getByLabel("من تاريخ")).toHaveValue(cairoDateRange().from);
    await page.getByRole("navigation", { name: "الإجراءات التشغيلية السريعة" }).getByRole("link", { name: /^التقرير المالي/ }).click();
    await expect(page.getByRole("heading", { name: "التقارير المالية" })).toBeVisible();
  });

  test("Journey B — financial filters and real CSV download", async ({ page }) => {
    await staffLogin(page, "owner.nogoom@example.test"); await page.goto("/dashboard/reports/financial");
    await expect(page.getByText("إجمالي التحصيل المؤكد", { exact: true })).toBeVisible();
    await page.getByLabel("من تاريخ").fill("2026-09-01"); await page.getByLabel("إلى تاريخ").fill("2026-09-30"); await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" }); await page.getByRole("button", { name: "تطبيق الفلاتر" }).click();
    await expect(page.getByText(/DEMO-|RC-/).first()).toBeVisible();
    const download = page.waitForEvent("download"); await page.getByRole("link", { name: "تصدير CSV" }).click(); expect((await download).suggestedFilename()).toMatch(/^financial-collections-2026-09\.csv$/);
  });

  test("Journey C — attendance birth-year and group filters export stored rows", async ({ page }) => {
    await staffLogin(page, "owner.nogoom@example.test"); await page.goto("/dashboard/reports/attendance");
    await page.getByLabel("من تاريخ").fill("2026-09-01"); await page.getByLabel("إلى تاريخ").fill("2026-09-30"); await page.getByLabel("سنة الميلاد").fill("2019"); await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" }); await page.getByLabel("المجموعة").selectOption({ label: "ناشئين 2016 — مجموعة أ" }); await page.getByRole("button", { name: "تطبيق الفلاتر" }).click();
    await expect(page.getByText(/يوسف خالد سمير/).first()).toBeVisible();
    const download = page.waitForEvent("download"); await page.getByRole("link", { name: "تصدير CSV" }).click(); expect((await download).suggestedFilename()).toBe("attendance-players-2026-09.csv");
  });

  test("Journey D — guardian payment history opens printable Arabic receipt", async ({ page }) => {
    await guardianLogin(page); await page.getByRole("link", { name: "الإيصالات" }).click();
    await expect(page.getByRole("heading", { name: "إيصالات الدفع" })).toBeVisible(); await page.getByRole("link").filter({ hasText: /DEMO-|RC-/ }).first().click();
    await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible(); await expect(page.getByText("أكاديمية النجوم الرياضية")).toBeVisible(); await expect(page.getByRole("button", { name: "طباعة الإيصال" })).toBeVisible();
  });
});
