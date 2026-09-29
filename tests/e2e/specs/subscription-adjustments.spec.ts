import { expect, Page, test } from "@playwright/test";

const footballPeriod = "53000000-0000-0000-0000-000000000501";
const swimmingPeriod = "53000000-0000-0000-0000-000000000503";
const mariamPeriod = "53000000-0000-0000-0000-000000000504";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.context().clearCookies(); await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**");
}

async function guardianLogin(page: Page) {
  await page.context().clearCookies(); await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click(); await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click(); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click(); await page.waitForURL("**/guardian**");
}

async function openPeriod(page: Page, id: string) { await page.goto(`/dashboard/subscriptions/periods/${id}`); await expect(page.getByRole("heading", { name: "تفاصيل الاشتراك" })).toBeVisible(); }
async function fillDateReason(page: Page, date: string, reason: string) { await page.getByLabel("التاريخ الفعلي").fill(date); await page.getByLabel("سبب التعديل").fill(reason); await page.getByRole("button", { name: "متابعة" }).click(); }

test.describe.serial("Slice 8B subscription adjustment journeys", () => {
  test("Journey A — admin freezes, guardian sees Frozen, and admin resumes with extended end", async ({ page }) => {
    await staffLogin(page); await openPeriod(page, footballPeriod); await page.getByRole("button", { name: "تجميد" }).click(); await fillDateReason(page, "2026-09-27", "سفر الأسرة"); await page.getByRole("button", { name: "تأكيد التجميد" }).click(); await expect(page.getByText("مجمد", { exact: true })).toBeVisible();
    await guardianLogin(page); await page.goto(`/guardian/subscriptions/periods/${footballPeriod}`); await expect(page.getByText("مجمد", { exact: true })).toBeVisible(); await expect(page.getByText("الاشتراك مجمد.")).toBeVisible();
    await staffLogin(page); await openPeriod(page, footballPeriod); await page.getByRole("button", { name: "استئناف" }).click(); await page.getByLabel("التاريخ الفعلي").fill("2026-09-28"); await page.getByLabel("سبب التعديل").fill("عودة اللاعب"); await page.getByRole("button", { name: "متابعة" }).click(); await expect(page.getByText(/2026-09-05 — 2026-10-05/)).toBeVisible(); await expect(page.getByText("استئناف الاشتراك", { exact: true })).toBeVisible();
  });

  test("Journey B — admin adds five days then deducts two with both audit rows", async ({ page }) => {
    await staffLogin(page); await openPeriod(page, footballPeriod); await page.getByRole("button", { name: "إضافة أيام" }).click(); await page.getByLabel("عدد الأيام").fill("5"); await page.getByLabel("سبب التعديل").fill("تعويض توقف التدريب"); await page.getByRole("button", { name: "متابعة" }).click(); await expect(page.getByText(/2026-09-05 — 2026-10-10/)).toBeVisible();
    await page.getByRole("button", { name: "خصم أيام" }).click(); await page.getByLabel("عدد الأيام").fill("2"); await page.getByLabel("سبب التعديل").fill("تصحيح إداري"); await page.getByRole("button", { name: "متابعة" }).click(); await expect(page.getByRole("dialog")).toContainText("سيتم خصم 2 يوم"); await page.getByRole("button", { name: "تأكيد الخصم" }).click(); await expect(page.getByText(/2026-09-05 — 2026-10-08/)).toBeVisible(); const history = page.locator(".adjustment-history"); await expect(history.getByText("إضافة أيام", { exact: true })).toBeVisible(); await expect(history.getByText("خصم أيام", { exact: true })).toBeVisible();
  });

  test("Journey C — cancellation preserves receipt and owner revenue", async ({ page }) => {
    await staffLogin(page, "owner.nogoom@example.test"); await page.goto("/dashboard/reports/financial"); const before = await page.locator(".report-total strong").textContent(); await openPeriod(page, mariamPeriod); const receipt = page.getByRole("link", { name: "عرض الإيصال المحفوظ" }); await expect(receipt).toBeVisible();
    await page.getByRole("button", { name: "إلغاء الاشتراك" }).click(); await fillDateReason(page, "2026-09-28", "إلغاء إداري للفترة"); await expect(page.getByRole("dialog")).toContainText("لن يتم حذف التحصيل أو الإيصال ولن يتم رد الأموال تلقائيًا"); await page.getByRole("button", { name: "تأكيد الإلغاء" }).click(); await expect(page.getByText("ملغي", { exact: true })).toBeVisible(); await expect(receipt).toBeVisible();
    await page.goto("/dashboard/reports/financial"); await expect(page.locator(".report-total strong")).toHaveText(before ?? "");
  });

  test("Journey D — frozen combined attendance does not consume, resume restores future consumption", async ({ page }) => {
    await staffLogin(page); await openPeriod(page, swimmingPeriod); await expect(page.getByText("12 حصة")).toBeVisible(); await page.getByRole("button", { name: "تجميد" }).click(); await fillDateReason(page, "2026-09-27", "إيقاف مؤقت للسباحة"); await page.getByRole("button", { name: "تأكيد التجميد" }).click(); await expect(page.getByText("مجمد", { exact: true })).toBeVisible();
    await page.goto("/dashboard/attendance/sessions/62000000-0000-0000-0000-000000000005/players"); const player = page.locator("article").filter({ hasText: "عمر أحمد محمود" }); await player.getByRole("button", { name: "حاضر" }).click(); await page.getByRole("button", { name: "حفظ الحضور" }).click(); await expect(page.getByText(/تم تسجيل الحضور دون خصم: الاشتراك مجمد أو غير مؤهل/)).toBeVisible();
    await openPeriod(page, swimmingPeriod); await expect(page.getByText("12 حصة")).toBeVisible(); await page.getByRole("button", { name: "استئناف" }).click(); await page.getByLabel("التاريخ الفعلي").fill("2026-09-28"); await page.getByLabel("سبب التعديل").fill("استئناف السباحة"); await page.getByRole("button", { name: "متابعة" }).click(); await expect(page.getByText("نشط", { exact: true })).toBeVisible(); await expect(page.locator(".adjustment-history").getByText("استئناف الاشتراك", { exact: true })).toBeVisible();
    await page.goto("/dashboard/attendance/sessions"); await page.getByRole("button", { name: "إنشاء حصة" }).click(); const form = page.locator("form.inline-attendance-form"); await form.locator("select[name=group]").selectOption({ label: "سباحة ناشئين — مجموعة أ" }); await form.locator("input[name=date]").fill("2026-09-29"); await form.locator("input[name=start]").fill("20:00"); await form.locator("input[name=end]").fill("21:00"); await form.getByRole("button", { name: "حفظ الحصة" }).click(); await expect(page.getByText("تم إنشاء الحصة.")).toBeVisible(); const session = page.locator("article").filter({ hasText: "2026-09-29" }).filter({ hasText: "سباحة ناشئين — مجموعة أ" }); await session.getByRole("link", { name: "تسجيل الحضور" }).click(); const later = page.locator("article").filter({ hasText: "عمر أحمد محمود" }); await later.getByRole("button", { name: "حاضر" }).click(); await page.getByRole("button", { name: "حفظ الحضور" }).click(); await expect(later).toContainText("الرصيد: 11");
  });
});
