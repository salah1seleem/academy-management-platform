import { expect, test } from "@playwright/test";

async function owner(page: import("@playwright/test").Page) {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
}

async function command(page: import("@playwright/test").Page, path: string, data: unknown, key?: string) {
  const csrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  const result = await page.request.post(path, { data, headers: { "X-CSRF-TOKEN": csrf.token, ...(key ? { "Idempotency-Key": key } : {}) } });
  expect(result.ok(), await result.text()).toBeTruthy();
  return result.json();
}

test("Staff UX: employee without account, fixed contract, deduction, net and paid month", async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 1000 }); await owner(page);
  const name = `عامل اختبار الواجهة ${Date.now()}`;
  await page.goto("/dashboard/staff/employees/new"); await page.getByLabel("الاسم", { exact: true }).fill(name); await page.getByLabel("رقم الهاتف").fill(`0102${String(Date.now()).slice(-7)}`); await page.getByLabel("الوظيفة").fill("عامل تجهيز الملاعب");
  const employeeOptionsResponse = await page.request.get("/api/v1/manage/employees/"); expect(employeeOptionsResponse.ok()).toBeTruthy(); const employeeOptions = await employeeOptionsResponse.json(); await page.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(employeeOptions.branches[0].id); await page.getByLabel("الراتب الشهري").fill("3100"); await page.getByLabel("محدد المدة").check(); await page.getByLabel("تاريخ الانضمام").fill("2026-09-16"); await page.getByLabel("تاريخ انتهاء العقد").fill("2026-09-20");
  await page.getByRole("button", { name: "إنشاء الموظف" }).click(); await page.waitForURL("**/dashboard/staff/employees"); await page.getByLabel("البحث بالاسم أو الهاتف").fill(name); const row = page.getByRole("row").filter({ hasText: name }); await expect(row).toContainText("بدون حساب دخول");
  await row.getByRole("link", { name: "المستحقات" }).click(); await page.getByLabel("شهر الرواتب").fill("2026-09"); const salary = page.getByRole("row").filter({ hasText: name }); await expect(salary).toContainText("5 يوم عقد"); await expect(salary).toContainText("516.67");
  await salary.getByRole("link").click(); await expect(page.getByRole("combobox", { name: "الموظف", exact: true })).not.toHaveValue(""); await page.getByLabel("المبلغ", { exact: true }).fill("100"); await page.getByLabel("تاريخ الاستحقاق", { exact: true }).fill("2026-09-20"); await page.getByLabel("سبب الخصم").fill("خصم اصطناعي لتجربة الواجهة"); await page.getByRole("button", { name: "حفظ الخصم" }).click(); await expect(page.getByRole("table", { name: "الخصومات" })).toContainText("خصم اصطناعي");
  const selected = await page.getByLabel("فلتر الموظف").inputValue(); await page.goto(`/dashboard/payroll?employeeId=${selected}&month=2026-09`); await expect(page.getByRole("row").filter({ hasText: name })).toContainText("416.67");
  const tiles = await page.locator(".payroll-stats > *").all(); expect(tiles).toHaveLength(4);
  const boxes = await Promise.all(tiles.map(tile => tile.boundingBox()));
  expect(boxes.every(box => box && Math.abs(box.y - boxes[0]!.y) < 2)).toBeTruthy();
  await page.screenshot({ path: info.outputPath("payroll-before-payment.png"), fullPage: true, animations: "disabled" });
  page.once("dialog", dialog => dialog.accept()); await page.getByRole("button", { name: "تأكيد الدفع" }).click(); await expect(page.getByRole("row").filter({ hasText: name })).toContainText("تم الدفع");
});

test("Attendance UX: today's default, clear returns records, named coach page and absent report", async ({ page }, info) => {
  await page.setViewportSize({ width: 393, height: 900 }); await owner(page);
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  await page.goto("/dashboard/attendance/sessions"); await expect(page.getByLabel("من تاريخ", { exact: true })).toHaveValue(today); await expect(page.getByLabel("إلى تاريخ", { exact: true })).toHaveValue(today); await page.getByRole("button", { name: "مسح", exact: true }).click(); await expect(page.getByLabel("من تاريخ", { exact: true })).toHaveValue(""); await expect(page.getByRole("table", { name: "حضور اللاعبين" })).toBeVisible();
  await expect(page.getByRole("table", { name: "حضور اللاعبين" })).not.toContainText(/[٠-٩]/);
  const coachesResponse = await page.request.get(`/api/v1/manage/staff-attendance/?date=${today}`); expect(coachesResponse.ok()).toBeTruthy(); const coaches = await coachesResponse.json(); const coachName = coaches.items[0].name;
  await page.goto("/dashboard/attendance/staff"); await expect(page.getByLabel("التاريخ", { exact: true })).toHaveValue(today); await page.getByLabel("البحث بالاسم").fill(coachName); await expect(page.getByRole("row").filter({ hasText: coachName })).toBeVisible(); await page.getByRole("link", { name: "تسجيل / تعديل" }).first().click(); await expect(page).toHaveURL(/attendance\/staff\/[a-f0-9-]+/); await expect(page.getByRole("heading", { name: `حضور ${coachName}`, exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "الحالة", exact: true }).selectOption("Absent"); await page.getByLabel("الخصم المعتمد").fill("50"); await page.getByLabel("سبب الخصم (مطلوب)").fill("غياب تجريبي"); await page.getByRole("button", { name: "حفظ الحضور" }).click(); await page.waitForURL("**/dashboard/attendance/staff"); await expect(page.getByRole("table", { name: "حضور المدربين اليومي" })).toContainText("غائب");
  const notice = page.locator(".save-notice"); await expect(notice).toBeVisible(); const noticeBox = await notice.boundingBox(); expect(noticeBox?.x).toBeGreaterThanOrEqual(0); expect(noticeBox!.x + noticeBox!.width).toBeLessThanOrEqual(393);
  await page.getByRole("button", { name: "مسح", exact: true }).click(); await expect(page.getByLabel("التاريخ")).toHaveValue(""); await expect(page.getByRole("table", { name: "حضور المدربين اليومي" })).toBeVisible(); await page.screenshot({ path: info.outputPath("daily-coaches-mobile.png"), fullPage: true, animations: "disabled" });
});

test("Additional amounts UX: configure a reason, accept one request and reject another", async ({ page, browser, baseURL }, info) => {
  await page.setViewportSize({ width: 1440, height: 1000 }); await owner(page);
  const reasonName = `ساعة إضافية واجهة ${Date.now()}`;
  const rejectedReasonName = `حضور مباراة واجهة ${Date.now()}`;
  await page.goto("/dashboard/payroll/requests");
  await page.getByLabel("سبب جديد").fill(reasonName);
  await page.getByRole("button", { name: "إضافة السبب", exact: true }).click();
  await expect(page.getByText(reasonName, { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "إضافة السبب", exact: true })).toBeEnabled();
  await page.getByLabel("سبب جديد").fill(rejectedReasonName);
  await page.getByRole("button", { name: "إضافة السبب", exact: true }).click();
  await expect(page.getByText(rejectedReasonName, { exact: false })).toBeVisible();
  const reasons = await (await page.request.get("/api/v1/manage/payroll/reasons")).json();
  const reason = reasons.find((r: { name: string }) => r.name === reasonName); expect(reason).toBeTruthy();
  const rejectedReason = reasons.find((r: { name: string }) => r.name === rejectedReasonName); expect(rejectedReason).toBeTruthy();
  const employeeOptions = await (await page.request.get("/api/v1/manage/employees/")).json();
  // This linked account is a synthetic LegacyRegression fixture, not review data.
  const account = employeeOptions.accounts.find((a: { name: string }) => a.name === "منى السيد");
  if (account) await command(page, "/api/v1/manage/employees/", { name: "إداري اختبار المبالغ الإضافية", phone: `0102${String(Date.now()).slice(-7)}`, jobTitle: "إداري تجريبي", branchId: employeeOptions.branches[0].id, monthlySalary: 3100, contractStart: "2026-09-01", contractEnd: null, membershipId: account.id, photoAssetId: null });
  const staffContext = await browser.newContext({ baseURL });
  try {
    const staffPage = await staffContext.newPage(); await staffPage.goto("/login");
    await staffPage.getByLabel("البريد الإلكتروني").fill("admin.nogoom@example.test"); await staffPage.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await staffPage.getByRole("button", { name: "دخول آمن" }).click(); await staffPage.waitForURL("**/dashboard");
    const own = await (await staffPage.request.get("/api/v1/staff/payroll/")).json(); expect(own.configured).toBe(true);
    const before = await (await page.request.get("/api/v1/manage/payroll/?month=2026-10-01")).json();
    const beforeAddition = before.items.find((e: { employeeId: string }) => e.employeeId === own.employee.id).additions;
    const rejectedRequest = await command(staffPage, "/api/v1/staff/payroll/requests", { reasonId: rejectedReason.id }, `rejected-${Date.now()}`);
    const approvedRequest = await command(staffPage, "/api/v1/staff/payroll/requests", { reasonId: reason.id }, `approved-${Date.now()}`);
    await page.reload();
    const approved = page.getByRole("row").filter({ hasText: reasonName }).filter({ has: page.getByRole("button", { name: "مراجعة", exact: true }) }).first();
    await approved.getByRole("button", { name: "مراجعة", exact: true }).click();
    const dialog = page.getByRole("dialog"); await expect(dialog).toBeVisible();
    await dialog.getByLabel("المبلغ المعتمد").fill("150"); await dialog.getByLabel("تاريخ الاستحقاق").fill("2026-10-08"); await dialog.getByRole("button", { name: "حفظ القرار" }).click();
    await expect(page.getByRole("row").filter({ hasText: reasonName }).first()).toContainText("مقبول");
    const rejected = page.getByRole("row").filter({ hasText: rejectedReasonName }).filter({ has: page.getByRole("button", { name: "مراجعة", exact: true }) }).first();
    await rejected.getByRole("button", { name: "مراجعة", exact: true }).click();
    await dialog.getByLabel("القرار").selectOption("reject"); await expect(dialog.getByLabel("تاريخ الاستحقاق")).toBeDisabled();
    await dialog.getByRole("button", { name: "حفظ القرار" }).click(); await expect(page.getByRole("row").filter({ hasText: rejectedReasonName }).first()).toContainText("مرفوض");
    const after = await (await page.request.get("/api/v1/manage/payroll/?month=2026-10-01")).json();
    expect(after.items.find((e: { employeeId: string }) => e.employeeId === own.employee.id).additions).toBe(beforeAddition + 150);
    const ownAfter = await (await staffPage.request.get("/api/v1/staff/payroll/")).json();
    expect(ownAfter.requests.find((r: { id: string }) => r.id === approvedRequest.id).amount).toBe(150);
    expect(ownAfter.requests.find((r: { id: string }) => r.id === rejectedRequest.id).status).toBe("Rejected");
    await page.screenshot({ path: info.outputPath("additional-requests-reviewed.png"), fullPage: true, animations: "disabled" });
  } finally { await staffContext.close(); }
});
