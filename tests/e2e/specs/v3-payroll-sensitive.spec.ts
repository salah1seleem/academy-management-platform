import { expect, test, type Page } from "@playwright/test";

test("Owner independently delegates review and payment; employee requests only a reason and the scoped payroll settles once", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  async function login(p: Page, email: string) {
    await p.goto("/login"); await p.getByLabel("البريد الإلكتروني").fill(email); await p.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await p.getByRole("button", { name: "دخول آمن" }).click(); await p.waitForURL("**/dashboard");
  }
  await login(page, "owner.nogoom@example.test");
  const stamp = Date.now(); const headers = { "X-CSRF-TOKEN": (await (await page.request.get("/api/v1/auth/csrf")).json()).token };
  async function provision(tag: string, offset: number) {
    const email = `payroll.${tag}.${stamp}@example.test`, name = `موظف تفويض ${tag} ${stamp}`, phone = `010${String(stamp + offset).slice(-8)}`;
    const created = await page.request.post("/api/v1/manage/staff", { headers, data: { email, name, phone, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin" } }); expect(created.ok()).toBeTruthy();
    const id = (await created.json()).id; const access = await (await page.request.get(`/api/v1/manage/staff/${id}/access`)).json(); const branch = access.branches[0].id;
    const actions = access.modules.map((module: string) => ({ module, read: module === "payroll", add: module === "payroll", edit: module === "payroll", delete: false }));
    expect((await page.request.put(`/api/v1/manage/staff/${id}/access`, { headers, data: { allBranches: false, branches: [branch], allSports: true, sports: [], actions, sensitivePermissions: [] } })).status()).toBe(204);
    return { id, email, name, phone, branch };
  }
  const reviewer = await provision("reviewer", 1), employee = await provision("requester", 2);
  const created = await page.request.post("/api/v1/manage/employees", { headers, data: { name: employee.name, phone: employee.phone, jobTitle: "عامل اصطناعي", branchId: employee.branch, monthlySalary: 3100, contractStart: "2026-10-01", contractEnd: null, membershipId: employee.id, photoAssetId: null, isActive: true } }); expect(created.status()).toBe(201); const employeeId = (await created.json()).id;
  const reason = await page.request.post("/api/v1/manage/payroll/reasons", { headers, data: { name: `حضور مباراة اصطناعية ${stamp}` } }); expect(reason.status()).toBe(201); const reasonId = (await reason.json()).id;
  const reviewerContext = await browser.newContext({ baseURL: info.project.use.baseURL }); const requesterContext = await browser.newContext({ baseURL: info.project.use.baseURL });
  const review = await reviewerContext.newPage(), requester = await requesterContext.newPage();
  try {
    await login(requester, employee.email); const requesterHeaders = { "X-CSRF-TOKEN": (await (await requester.request.get("/api/v1/auth/csrf")).json()).token, "Idempotency-Key": `v3-request-${stamp}` };
    const requested = await requester.request.post("/api/v1/staff/payroll/requests", { headers: requesterHeaders, data: { reasonId } }); expect(requested.status()).toBe(201); const claim = (await requested.json()).id;
    await login(review, reviewer.email); await review.goto("/dashboard/payroll/requests"); await expect(review.getByText(employee.name, { exact: true })).toBeVisible();
    await expect(review.getByRole("button", { name: "مراجعة", exact: true })).toHaveCount(0);
    async function openAccess() {
      await page.goto("/dashboard/staff"); await page.locator("article").filter({ hasText: reviewer.name }).getByRole("button", { name: "الصلاحيات", exact: true }).click();
      await expect(page.getByRole("switch", { name: "تسجيل صرف الرواتب", exact: true })).toBeVisible();
    }
    async function saveAccess() {
      await page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true }).click();
      const saved = page.waitForResponse(r => r.url().endsWith(`/staff/${reviewer.id}/access`) && r.request().method() === "PUT");
      await page.getByRole("button", { name: "تأكيد الاعتماد", exact: true }).click(); expect((await saved).status()).toBe(204);
    }
    await openAccess(); await expect(page.getByRole("switch", { name: "تسجيل صرف الرواتب", exact: true })).not.toBeChecked();
    await page.getByRole("switch", { name: "اعتماد أو رفض المبالغ الإضافية", exact: true }).check();
    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 900 }); await page.getByRole("group", { name: "تفويضات الرواتب الحساسة — موافقة مستقلة" }).scrollIntoViewIfNeeded();
      await page.evaluate(() => window.scrollBy(0, -96));
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      await page.screenshot({ path: info.outputPath(`payroll-grants-${width}.png`), animations: "disabled" });
    }
    await saveAccess(); await review.reload();
    await review.getByRole("row").filter({ hasText: employee.name }).getByRole("button", { name: "مراجعة", exact: true }).click();
    await review.getByLabel("المبلغ المعتمد").fill("150"); await review.getByLabel("تاريخ الاستحقاق").fill("2026-10-08"); await review.getByRole("button", { name: "حفظ القرار" }).click(); await expect(review.getByRole("dialog")).toHaveCount(0);
    const own = await (await requester.request.get("/api/v1/staff/payroll")).json(); expect(own.requests.find((r: { id: string }) => r.id === claim)).toMatchObject({ status: "Approved", amount: 150 });
    await review.goto(`/dashboard/payroll?month=2026-10&employeeId=${employeeId}`); await expect(review.getByText("لم يُدفع", { exact: true })).toBeVisible(); await expect(review.getByRole("button", { name: "تأكيد الدفع", exact: true })).toHaveCount(0);
    await openAccess(); await page.getByRole("switch", { name: "اعتماد أو رفض المبالغ الإضافية", exact: true }).uncheck(); await page.getByRole("switch", { name: "تسجيل صرف الرواتب", exact: true }).check(); await saveAccess();
    await review.reload(); review.once("dialog", dialog => dialog.accept()); await review.getByRole("button", { name: "تأكيد الدفع", exact: true }).click(); await expect(review.getByText("تم الدفع", { exact: true })).toBeVisible();
    const report = await (await review.request.get("/api/v1/manage/payroll?month=2026-10-01")).json(); expect(report.items.find((r: { employeeId: string }) => r.employeeId === employeeId)).toMatchObject({ baseDue: 3100, additions: 150, netDue: 3250, isPaid: true });
    await openAccess(); await page.getByRole("switch", { name: "تسجيل صرف الرواتب", exact: true }).uncheck(); await saveAccess();
    const permissions = await (await review.request.get("/api/v1/my-permissions")).json(); expect(permissions).not.toContain("payroll.pay"); expect(permissions).not.toContain("payroll.addition.review");
  } finally { await reviewerContext.close(); await requesterContext.close(); }
});
