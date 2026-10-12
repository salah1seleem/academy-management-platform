import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { todaySession } from "../support/attendance";
import { formatDateAr } from "../../../apps/web/src/lib/formatters";

const footballPeriod = "53000000-0000-0000-0000-000000000501";
const swimmingPeriod = "53000000-0000-0000-0000-000000000503";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.context().clearCookies(); await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**");
}

async function openPeriod(page: Page, id: string) { await page.goto(`/dashboard/subscriptions/periods/${id}`); await expect(page.getByRole("heading", { name: "تفاصيل الاشتراك" })).toBeVisible(); }
async function fillDateReason(page: Page, date: string, reason: string) { await page.getByLabel("التاريخ الفعلي").fill(date); await page.getByLabel("سبب التعديل").fill(reason); await page.getByRole("button", { name: "متابعة" }).click(); }
const shifted = (date: string, days: number) => new Date(Date.parse(`${date}T00:00:00Z`) + days * 86400000).toISOString().slice(0, 10);
const period = (page: Page, id: string) => page.request.get(`/api/v1/subscriptions/periods/${id}`).then(r => r.json());
async function expectEnd(page: Page, id: string, end: string) {
  await expect.poll(async () => (await period(page, id)).endDate).toBe(end);
  await expect(page.locator(".period-summary")).toContainText(formatDateAr(end));
}
async function write(page: Page, path: string, data: unknown) {
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  const response = await page.request.post(path, { headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() }, data });
  expect(response.ok(), await response.text()).toBe(true); return response.json();
}
async function cancellablePeriod(page: Page) {
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", planId = "51000000-0000-0000-0000-000000000001";
  const groupId = await write(page, "/api/v1/manage/groups", { arabicName: `إلغاء اشتراك اصطناعي ${Date.now()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  const guardianPhone = `010${Date.now().toString().slice(-8)}`;
  const quote = await write(page, "/api/v1/people/registration-quote", { planId, branchId, guardianPhone });
  const saved = await write(page, "/api/v1/people/registrations", { playerArabicName: "طفل إلغاء اصطناعي", dateOfBirth: "2017-01-01", guardianName: "ولي إلغاء اصطناعي", guardianPhone, branchId, sportId, groupId, planId, paymentMethod: "Online", expectedAmount: quote.finalAmount });
  const player = await page.request.get(`/api/v1/people/players/${saved.playerId}`).then(r => r.json());
  return player.enrollments[0].subscription.id as string;
}
async function attendance(page: Page, session: string, state: string) {
  await page.goto(`/dashboard/attendance/sessions/${session}/players`);
  const player = page.getByRole("group", { name: "حضور عمر أحمد محمود", exact: true });
  await player.getByRole("button", { name: state, exact: true }).click();
  await page.getByRole("button", { name: "حفظ الحضور" }).click();
  await expect(page.getByText("تم حفظ الحضور بنجاح.")).toBeVisible();
  await expect(page.getByRole("button", { name: "حفظ الحضور" })).toBeEnabled();
  return player;
}

test.describe.serial("Slice 8B subscription adjustment journeys", () => {
  test("Journey A — admin freezes, guardian sees Frozen, and admin resumes with extended end", async ({ page }) => {
    await staffLogin(page); const before = await period(page, footballPeriod); await openPeriod(page, footballPeriod); await page.getByRole("button", { name: "تجميد" }).click(); await fillDateReason(page, "2026-09-27", "سفر الأسرة"); await page.getByRole("button", { name: "تأكيد التجميد" }).click(); await expect(page.getByText("مجمد", { exact: true })).toBeVisible();
    await guardianLogin(page); await page.goto(`/guardian/subscriptions/periods/${footballPeriod}`); await expect(page.getByText("مجمد", { exact: true })).toBeVisible(); await expect(page.getByText("الاشتراك مجمد.").first()).toBeVisible();
    await staffLogin(page); await openPeriod(page, footballPeriod); await page.getByRole("button", { name: "استئناف" }).click(); await page.getByLabel("التاريخ الفعلي").fill("2026-09-28"); await page.getByLabel("سبب التعديل").fill("عودة اللاعب"); await page.getByRole("button", { name: "متابعة" }).click(); await expectEnd(page, footballPeriod, shifted(before.endDate, 1)); await expect(page.locator(".adjustment-history").getByText("استئناف الاشتراك", { exact: true }).first()).toBeVisible();
  });

  test("Journey B — admin adds five days then deducts two with both audit rows", async ({ page }) => {
    await staffLogin(page); const before = await period(page, footballPeriod); await openPeriod(page, footballPeriod); await page.getByRole("button", { name: "إضافة أيام" }).click(); await page.getByLabel("عدد الأيام").fill("5"); await page.getByLabel("سبب التعديل").fill("تعويض توقف التدريب"); await page.getByRole("button", { name: "متابعة" }).click(); await expectEnd(page, footballPeriod, shifted(before.endDate, 5));
    await page.getByRole("button", { name: "خصم أيام" }).click(); await page.getByLabel("عدد الأيام").fill("2"); await page.getByLabel("سبب التعديل").fill("تصحيح إداري"); await page.getByRole("button", { name: "متابعة" }).click(); await expect(page.getByRole("dialog")).toContainText("سيتم خصم 2 يوم"); await page.getByRole("button", { name: "تأكيد الخصم" }).click(); await expectEnd(page, footballPeriod, shifted(before.endDate, 3)); const history = page.locator(".adjustment-history"); await expect(history.getByText("إضافة أيام", { exact: true }).first()).toBeVisible(); await expect(history.getByText("خصم أيام", { exact: true }).first()).toBeVisible();
  });

  test("Journey C — cancellation preserves receipt and owner revenue", async ({ page }) => {
    await staffLogin(page, "owner.nogoom@example.test"); const mariamPeriod = await cancellablePeriod(page); const paid = await period(page, mariamPeriod); expect(paid.receiptId).toBeTruthy(); await page.goto("/dashboard/reports/financial"); const before = await page.locator(".report-total strong").textContent(); await openPeriod(page, mariamPeriod); const receipt = page.getByRole("link", { name: "عرض الإيصال المحفوظ" }); await expect(receipt).toBeVisible();
    await page.getByRole("button", { name: "إلغاء الاشتراك" }).click(); await fillDateReason(page, "2026-09-28", "إلغاء إداري للفترة"); await expect(page.getByRole("dialog")).toContainText("لن يتم حذف التحصيل أو الإيصال ولن يتم رد الأموال تلقائيًا"); await page.getByRole("button", { name: "تأكيد الإلغاء" }).click(); await expect(page.getByText("ملغي", { exact: true })).toBeVisible(); await expect(receipt).toBeVisible(); const cancelled = await period(page, mariamPeriod); expect(cancelled.receiptId).toBe(paid.receiptId); expect(cancelled.priceSnapshot).toBe(paid.priceSnapshot);
    await page.goto("/dashboard/reports/financial"); await expect(page.locator(".report-total strong")).toHaveText(before ?? "");
  });

  test("Journey D — frozen combined attendance does not consume, resume restores future consumption", async ({ page }) => {
    await staffLogin(page);
    const group = "34000000-0000-0000-0000-000000000002";
    const frozenSession = await todaySession(page, group, "01:30", "01:45"), resumedSession = await todaySession(page, group, "01:50", "02:05");
    await attendance(page, frozenSession, "غائب"); await attendance(page, resumedSession, "غائب");
    const before = await period(page, swimmingPeriod);
    await openPeriod(page, swimmingPeriod); await page.getByRole("button", { name: "تجميد", exact: true }).click(); await fillDateReason(page, "2026-09-27", "إيقاف مؤقت للسباحة"); await page.getByRole("button", { name: "تأكيد التجميد" }).click(); await expect(page.getByText("مجمد", { exact: true })).toBeVisible();
    await attendance(page, frozenSession, "حاضر"); expect((await period(page, swimmingPeriod)).remainingSessions).toBe(before.remainingSessions);
    await openPeriod(page, swimmingPeriod); await page.getByRole("button", { name: "استئناف", exact: true }).click(); await fillDateReason(page, "2026-09-28", "استئناف السباحة"); await expect(page.getByText("فعال", { exact: true })).toBeVisible(); await expectEnd(page, swimmingPeriod, shifted(before.endDate, 1));
    await attendance(page, resumedSession, "حاضر"); expect((await period(page, swimmingPeriod)).remainingSessions).toBe(before.remainingSessions - 1);
  });
});
