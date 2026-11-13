import { expect, test } from "@playwright/test";

test("Owner reconciles payroll approval, recovered payments and stale balances with cash and staff history", async ({ page, browser }, info) => {
  test.setTimeout(120_000);
  const ownerEmail = "owner.football@example.test";
  const password = "Demo-Only-123!";
  async function login(email: string) {
    await page.goto("/login");
    await page.getByLabel("البريد الإلكتروني").fill(email);
    await page.getByLabel("كلمة المرور").fill(password);
    await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
    await page.waitForURL("**/dashboard");
  }
  async function logout() {
    const menu = page.getByRole("button", { name: "فتح القائمة" });
    if (await menu.isVisible()) await menu.click();
    await page.getByRole("button", { name: "تسجيل الخروج", exact: true }).click();
    await page.waitForURL("**/login");
  }
  await login(ownerEmail);

  async function read(path: string) {
    const response = await page.request.get(path);
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  }
  const csrf = (await read("/api/v1/auth/csrf")).token;
  const stamp = Date.now().toString();
  const phone = `0109${stamp.slice(-7)}`;
  const name = "مدرب كشف صرف اصطناعي";
  async function post(path: string, data: unknown) {
    const response = await page.request.post(path, { headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": crypto.randomUUID() }, data });
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  }
  const coach = (await post("/api/v1/manage/staff", { name, email: `statement.${stamp}@example.test`, phone, temporaryPassword: "Demo-Only-123!", role: "Coach", jobTitle: "مدرب عرض" })).id;
  const options = await read("/api/v1/manage/employees");
  const [branch, otherBranch] = options.branches;
  expect(otherBranch?.id).toBeTruthy();
  const adminEmail = `statement.admin.${stamp}@example.test`;
  const admin = (await post("/api/v1/manage/staff", {
    name: "إداري اختبار الرواتب", email: adminEmail, phone: `0108${stamp.slice(-7)}`,
    temporaryPassword: password, role: "AcademyAdmin", jobTitle: "إداري عرض",
  })).id;
  const accessPath = `/api/v1/manage/staff/${admin}/access`;
  const access = await read(accessPath);
  const grant = await page.request.put(accessPath, {
    headers: { "X-CSRF-TOKEN": csrf },
    data: {
      allBranches: false, branches: [branch.id], allSports: true, sports: [],
      actions: access.modules.map((module: string) => ({ module, read: true, add: module === "payroll", edit: false, delete: false })),
      sensitivePermissions: ["payroll.pay"],
    },
  });
  expect(grant.ok(), await grant.text()).toBe(true);
  const employee = (await post("/api/v1/manage/employees", {
    name, phone, jobTitle: "مدرب", branchId: branch.id, monthlySalary: 3600, contractStart: "2000-01-01", contractEnd: null, membershipId: coach, photoAssetId: null, isActive: true,
  })).id;
  const { currentMonth: month, today } = await read(`/api/v1/manage/employees/${employee}/salary-versions`);
  expect(today).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  const yesterday = new Date(Date.parse(today) - 86400000).toISOString().slice(0, 10);
  const range = `from=${today}&to=${today}&branchId=${branch.id}`;
  const root = `/api/v1/manage/payroll/${employee}/statement`;
  const list = `/dashboard/payroll?employeeId=${employee}&month=${month.slice(0, 7)}`;
  const getStatement = () => read(`${root}?month=${month}`);
  const getAccount = () => read(`/api/v1/manage/payroll/${employee}/account?asOf=${today}`);
  const getOperating = (query = range) => read(`/api/v1/reports/operating-statement?${query}`);
  const getCollections = () => read(`/api/v1/reports/financial?${range}`);
  const otherRange = `from=${today}&to=${today}&branchId=${otherBranch.id}`;
  const yesterdayRange = `from=${yesterday}&to=${yesterday}&branchId=${branch.id}`;
  const [baseline, otherBefore, yesterdayBefore, collectionsBefore] = await Promise.all([getOperating(), getOperating(otherRange), getOperating(yesterdayRange), getCollections()]);
  const salaries = (report: { expenses: { key: string; amount: number }[] }) => report.expenses.find(line => line.key === "salary")?.amount;
  const originalSalaries = salaries(baseline)!;
  expect(originalSalaries).toBeDefined();
  async function expectCash(paid: number, remaining: number) {
    const result = await getOperating();
    expect(salaries(result)).toBe(originalSalaries + paid);
    expect(result.totalExpenses).toBeCloseTo(baseline.totalExpenses + paid, 2);
    expect(result.surplus).toBeCloseTo(baseline.surplus - paid, 2);
    expect(result.totalIncome).toBe(baseline.totalIncome);
    expect(result.approvedPayrollPayable).toBe(baseline.approvedPayrollPayable + remaining);
    expect(await getCollections()).toEqual(collectionsBefore);
  }

  await page.goto(list);
  await page.getByRole("link", { name: "الكشف والتفاصيل" }).click();
  await expect(page.getByRole("heading", { name: "كشف المستحقات والصرف", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "عودة للرواتب" })).toHaveAttribute("href", list);
  const reviewApproval = page.getByRole("button", { name: "مراجعة اعتماد الكشف" });
  const confirmApproval = page.getByRole("button", { name: "تأكيد اعتماد الكشف" });
  await reviewApproval.click();
  await expect(page.getByRole("dialog")).toContainText("الاعتماد ليس صرفًا");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(reviewApproval).toBeFocused();
  expect((await getStatement()).statement).toBeNull();

  let approvalWrites = 0;
  await page.route(`**${root}/approve`, async route => {
    approvalWrites++;
    expect((await route.fetch()).status()).toBe(201);
    await route.abort("failed");
  }, { times: 1 });
  await reviewApproval.click();
  await confirmApproval.click();
  await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد الحفظ" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("heading", { name: "محاولة اعتماد بانتظار التحقق" })).toBeVisible();
  await expect(reviewApproval).toHaveCount(0);
  await page.getByRole("button", { name: "التحقق من اعتماد الكشف" }).click();
  await expect(page.getByText("تم اعتماد الكشف دون تسجيل صرف.")).toBeVisible();
  expect(approvalWrites).toBe(1);
  expect((await getStatement()).row).toMatchObject({ netDue: 3600, paidAmount: 0, remainingAmount: 3600 });
  await expectCash(0, 3600);

  const amount = page.getByLabel("مبلغ الصرف");
  const reviewPayment = page.getByRole("button", { name: "مراجعة الصرف" });
  const confirmPayment = page.getByRole("button", { name: "تأكيد تسجيل الصرف" });
  async function preview(value: string, reference = "سند اصطناعي") {
    await amount.fill(value);
    await page.getByLabel("مرجع الصرف (اختياري)").fill(reference);
    await reviewPayment.click();
    await expect(page.getByRole("dialog")).toContainText("لا ينفذ التطبيق تحويلًا بنكيًا");
  }
  for (const invalid of ["0", "-5", "100.001", "3600.01"]) {
    await amount.fill(invalid);
    await reviewPayment.click();
    await expect(page.getByRole("alert").filter({ hasText: "أدخل مبلغًا موجبًا" })).toBeVisible();
    await expect(page.getByRole("dialog")).toHaveCount(0);
  }
  expect((await getStatement()).payments).toHaveLength(0);
  const overpay = await page.request.post(`${root}/disbursements`, {
    headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": crypto.randomUUID() },
    data: { month, amount: 3600.01, expectedPaid: 0, expectedRemaining: 3600 },
  });
  expect(overpay.status()).toBe(409);

  const writes: { key: string; body: string | null }[] = [];
  await page.route(`**${root}/disbursements`, async route => {
    writes.push({ key: route.request().headers()["idempotency-key"], body: route.request().postData() });
    expect((await route.fetch()).status()).toBe(201);
    await route.abort("failed");
  }, { times: 1 });
  await preview("2500");
  await page.getByRole("dialog").screenshot({ path: info.outputPath("payroll-confirm-390.png"), animations: "disabled" });
  await confirmPayment.click();
  await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد الحفظ" })).toBeVisible();
  await expect(amount).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole("heading", { name: "محاولة صرف بانتظار التحقق" })).toBeVisible();
  await expect(amount).toHaveCount(0);
  await expect(page.getByRole("button", { name: "إعادة إرسال الطلب نفسه" })).toHaveCount(0);
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
    await page.screenshot({ path: info.outputPath(`payroll-pending-reload-${width}.png`), animations: "disabled" });
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route(`**${root}?month=*`, route => route.fulfill({ status: 503, json: {} }), { times: 1 });
  await page.getByRole("button", { name: "التحقق من تسجيل الصرف" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "حُفظ الطلب، وتعذر تحديث الكشف" })).toBeVisible();
  await expect(amount).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole("heading", { name: "محاولة صرف بانتظار التحقق" })).toBeVisible();
  await expect(amount).toHaveCount(0);
  await page.getByRole("button", { name: "التحقق من تسجيل الصرف" }).click();
  await expect(page.getByText("تم تسجيل الصرف وتحديث المتبقي.")).toBeVisible();
  let statement = await getStatement();
  expect(statement.row).toMatchObject({ netDue: 3600, paidAmount: 2500, remainingAmount: 1100, isPaid: false });
  expect(statement.payments).toHaveLength(1);
  const paidDate = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "long", timeZone: statement.timeZone }).format(new Date(statement.payments[0].createdAtUtc));
  await expect(page.getByText(`صرف مباشر · ${paidDate}`)).toBeVisible();
  expect((await getAccount()).items.find((item: { month: string }) => item.month === month)).toMatchObject({ netDue: 3600, paid: 2500, remaining: 1100 });
  await expectCash(2500, 1100);

  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect(page.getByRole("heading", { name: "كشف المستحقات والصرف", exact: true })).toBeInViewport();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
    await page.screenshot({ path: info.outputPath(`payroll-statement-${width}.png`), animations: "disabled" });
  }

  const otp = await page.request.post("/api/v1/mobile/auth/otp/request", { data: { phoneNumber: phone } });
  expect(otp.status()).toBe(202);
  const verify = await page.request.post("/api/v1/mobile/auth/otp/verify", { data: { phoneNumber: phone, challengeId: (await otp.json()).challengeId, code: "246810", deviceName: "كشف صرف اصطناعي" } });
  expect(verify.status()).toBe(200);
  const token = (await verify.json()).accessToken;
  const own = await page.request.get("/api/v1/staff/payroll/statements", { headers: { Authorization: `Bearer ${token}` } });
  expect(own.status()).toBe(200);
  const mine = (await own.json()).items[0];
  expect(mine).toMatchObject({ netDue: 3600, paid: 2500, remaining: 1100 });
  expect(mine).not.toHaveProperty("note");
  expect(mine).not.toHaveProperty("sources");
  expect(mine.payments[0]).not.toHaveProperty("reference");

  await page.route(`**${root}/disbursements`, route => route.abort("failed"), { times: 1 });
  await preview("1100");
  await confirmPayment.click();
  await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد الحفظ" })).toBeVisible();
  await page.reload();
  await page.getByRole("button", { name: "التحقق من تسجيل الصرف" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "لا توجد نتيجة مؤكدة" })).toBeVisible();
  const anotherSession = await browser.newContext({ storageState: await page.context().storageState(), baseURL: info.project.use.baseURL });
  try {
    const anotherCsrf = (await (await anotherSession.request.get("/api/v1/auth/csrf")).json()).token;
    const later = await anotherSession.request.post(`${root}/disbursements`, {
      headers: { "X-CSRF-TOKEN": anotherCsrf, "Idempotency-Key": crypto.randomUUID() },
      data: { month, amount: 100, expectedPaid: 2500, expectedRemaining: 1100 },
    });
    expect(later.status()).toBe(201);
  } finally { await anotherSession.close(); }
  const stale = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`${root}/disbursements`));
  await page.getByRole("button", { name: "إعادة إرسال الطلب نفسه" }).click();
  expect((await stale).status()).toBe(409);
  await expect(amount).toHaveAttribute("max", "1000");
  await expect(page.getByText("تم تسجيل الصرف وتحديث المتبقي.")).toHaveCount(0);

  const remember = (request: { headers(): Record<string, string>; postData(): string | null }) => writes.push({ key: request.headers()["idempotency-key"], body: request.postData() });
  await page.route(`**${root}/disbursements`, async route => { remember(route.request()); await route.abort("failed"); }, { times: 1 });
  await preview("1000", "مرجع اصطناعي للمحاولة المؤجلة");
  await confirmPayment.click();
  await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد الحفظ" })).toBeVisible();
  await page.getByRole("link", { name: "عودة للرواتب" }).click();
  await page.getByRole("link", { name: "الكشف والتفاصيل" }).click();
  await expect(page.getByRole("heading", { name: "محاولة صرف بانتظار التحقق" })).toBeVisible();
  await expect(amount).toHaveCount(0);
  await expect(page.getByText("مرجع اصطناعي للمحاولة المؤجلة", { exact: true })).toHaveCount(0);
  const statementUrl = page.url();
  const ownerScope = (await getStatement()).recoveryScope;
  await logout();
  await login(adminEmail);
  await page.goto(statementUrl);
  await expect(amount).toHaveAttribute("max", "1000");
  expect((await getStatement()).recoveryScope).not.toBe(ownerScope);
  await expect(page.getByRole("heading", { name: "محاولة صرف بانتظار التحقق" })).toHaveCount(0);
  await expect(page.getByText("مرجع اصطناعي للمحاولة المؤجلة", { exact: true })).toHaveCount(0);
  await logout();
  await login(ownerEmail);
  await page.goto(statementUrl);
  await expect(page.getByRole("heading", { name: "محاولة صرف بانتظار التحقق" })).toBeVisible();
  await page.getByRole("button", { name: "التحقق من تسجيل الصرف" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "لا توجد نتيجة مؤكدة" })).toBeVisible();
  await page.route(`**${root}/disbursements`, async route => {
    remember(route.request());
    const response = await route.fetch();
    expect(response.status()).toBe(201);
    await route.fulfill({ response });
  }, { times: 1 });
  await page.route(`**${root}?month=*`, route => route.fulfill({ status: 503, json: {} }), { times: 1 });
  await page.getByRole("button", { name: "إعادة إرسال الطلب نفسه" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "حُفظ الطلب، وتعذر تحديث الكشف" })).toBeVisible();
  await expect(amount).toHaveCount(0);
  await page.getByRole("button", { name: "إعادة قراءة الكشف" }).click();
  await expect(page.getByText("تم تسجيل الصرف وتحديث المتبقي.")).toBeVisible();
  expect(writes).toHaveLength(3);
  expect(writes[1]).toEqual(writes[2]);
  await expect(page.getByText("مرجع اصطناعي للمحاولة المؤجلة", { exact: true })).toBeVisible();
  expect(writes[0].key).not.toBe(writes[1].key);

  statement = await getStatement();
  expect(statement.row).toMatchObject({ netDue: 3600, paidAmount: 3600, remainingAmount: 0, isPaid: true });
  expect(statement.payments.map((payment: { amount: number }) => payment.amount)).toEqual([1000, 100, 2500]);
  await expectCash(3600, 0);
  expect(await getOperating(otherRange)).toEqual(otherBefore);
  expect(await getOperating(yesterdayRange)).toEqual(yesterdayBefore);
  expect((await getAccount()).items.find((item: { month: string }) => item.month === month)).toMatchObject({ netDue: 3600, paid: 3600, remaining: 0 });
  await expect(page.getByRole("form", { name: "تسجيل صرف", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "عودة للرواتب" }).click();
  await expect(page.getByRole("row").filter({ hasText: name })).toContainText("تم الدفع");
  await expect(page.getByText("إجمالي المستحق").locator("..")).toContainText("لم يُدفع: 0 جنيه");
});
