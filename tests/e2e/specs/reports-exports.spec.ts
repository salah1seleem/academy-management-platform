import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { cairoDateRange } from "../support/attendance";

async function staffLogin(page: Page, email: string) { await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**"); }

test.describe("Slice 8A operational reporting journeys", () => {
  test("Journey A — owner dashboard uses stored financial and subscription metrics", async ({ page }) => {
    test.setTimeout(60_000);
    await staffLogin(page, "owner.nogoom@example.test");
    await expect(page.getByRole("region", { name: "ملخص الأكاديمية", exact: true })).toBeVisible();
    for (const label of ["التحصيل اليومي", "التحصيل الشهري"]) await expect(page.getByRole("heading", { name: label, exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: /اشتراكات فعالة/ })).toBeVisible();
    await expect(page.getByRole("link", { name: /تنتهي قريبًا/ })).toBeVisible();
    await expect(page.getByRole("link", { name: /اشتراكات منتهية/ })).toBeVisible();
    const requests=page.locator("section").filter({has:page.getByRole("heading",{name:"طلبات الدفع",exact:true})});
    await expect(requests.getByRole("link",{name:"عرض الكل",exact:true})).toHaveAttribute("href",/subscriptions\/payments/);
    const calendar=await (await page.request.get("/api/v1/academy/calendar")).json();
    await expect(page.getByLabel("من تاريخ")).toHaveValue(calendar.trailingMonthFrom);
    const totalLink=page.getByRole("region",{name:"ملخص الأكاديمية",exact:true}).getByRole("link").first();
    await totalLink.scrollIntoViewIfNeeded();
    expect(await page.evaluate(()=>innerWidth)).toBeLessThanOrEqual(page.viewportSize()!.width+1);
    await totalLink.click();
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
    const stored=await (await page.request.get("/api/v1/reports/attendance?subject=players&allDates=true&birthYear=2019")).json();
    const record=stored.items.find((r:{player:string})=>r.player==="يوسف خالد سمير"); expect(record).toBeTruthy();
    const month=record.date.slice(0,7);
    await page.getByLabel("من تاريخ").fill(`${month}-01`); await page.getByLabel("إلى تاريخ").fill(record.date); await page.getByLabel("سنة الميلاد").fill("2019"); await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" }); await page.getByLabel("المجموعة").selectOption({ label: "ناشئين 2016 — مجموعة أ" }); await page.getByRole("button", { name: "تطبيق الفلاتر" }).click();
    await expect(page.getByText(/يوسف خالد سمير/).first()).toBeVisible();
    const download = page.waitForEvent("download"); await page.getByRole("link", { name: "تصدير CSV" }).click(); expect((await download).suggestedFilename()).toBe(`attendance-players-${month}.csv`);
  });

  test("Journey D — guardian payment history opens printable Arabic receipt", async ({ page }) => {
    await guardianLogin(page); await page.getByRole("link", { name: "الإيصالات" }).click();
    await expect(page.getByRole("heading", { name: "إيصالات الدفع" })).toBeVisible(); await page.getByRole("link").filter({ hasText: /DEMO-|RC-/ }).first().click();
    await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible(); await expect(page.getByText("أكاديمية النجوم الرياضية")).toBeVisible(); await expect(page.getByRole("button", { name: "طباعة الإيصال" })).toBeVisible();
  });
});
