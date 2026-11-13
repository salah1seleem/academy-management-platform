import { expect, test, type Page } from "@playwright/test";
import { writeFile } from "node:fs/promises";

const api = "/api/v1/finance/operating-expenses";
const screen = "/dashboard/finance/operating-expenses";
const password = "Demo-Only-123!";
const owner = process.env.ACADEMY_EXPENSE_QA_OWNER_EMAIL ?? "owner.nogoom@example.test";
const cents = (value: number | string) => Math.round(Number(value) * 100);
const dayBefore = (day: string, days = 1) => new Date(Date.parse(`${day}T12:00:00Z`) - days * 86_400_000).toISOString().slice(0, 10);

async function signIn(page: Page, email = owner) {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill(password);
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click({ timeout: 12_000 });
  await expect(page).toHaveURL(/\/dashboard$/);
}

async function read(page: Page, path: string) {
  const response = await page.request.get(path);
  expect(response.status(), path).toBe(200);
  return response.json();
}

test("Owner reconciles a paid expense from the form to the operating report after lost responses", async ({ page, browser }, info) => {
  test.setTimeout(100_000);
  page.setDefaultTimeout(12_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page);
  const { today } = await read(page, "/api/v1/academy/calendar");
  const { branches } = await read(page, "/api/v1/manage/structure/options");
  const [branch, other] = branches;
  expect(other).toBeDefined();
  const yesterday = dayBefore(today);
  const serviceDay = dayBefore(today, 45);
  const scope = new URLSearchParams({ from: today, to: today, branchId: branch.id });
  const statement = (filters = scope) => read(page, `/api/v1/reports/operating-statement?${filters}`);
  const collection = () => read(page, `/api/v1/reports/financial?${scope}`);
  const otherScope = new URLSearchParams({ ...Object.fromEntries(scope), branchId: other.id });
  const yesterdayScope = new URLSearchParams({ ...Object.fromEntries(scope), from: yesterday, to: yesterday });
  const allScope = new URLSearchParams({ from: today, to: today });
  const overdue = async () => (await read(page, `/api/v1/reports/owner-cockpit?branchId=${branch.id}`)).overdue;
  const before = await statement();
  const beforeAll = await statement(allScope);
  const beforeOther = await statement(otherScope);
  const beforeYesterday = await statement(yesterdayScope);
  const beforeCash = await collection();
  const beforeOverdue = await overdue();
  const totals = (value: { totalIncome: number; totalExpenses: number; surplus: number }) => [value.totalIncome, value.totalExpenses, value.surplus].map(cents);

  await page.goto(`${screen}?allDates=true&branchId=${branch.id}`);
  const open = page.getByRole("button", { name: "تسجيل مصروف", exact: true });
  const form = page.locator("form.form-shell");
  const submit = form.getByRole("button", { name: "حفظ مسودة المصروف" });
  const alert = (text: string) => page.getByRole("alert").filter({ hasText: text });
  let draftPosts = 0;
  page.on("request", request => {
    if (request.method() === "POST" && new URL(request.url()).pathname === `${api}/`) draftPosts++;
  });
  await open.click();
  await submit.click();
  await expect(alert("اختر الفرع")).toBeVisible();
  const formBranch = form.getByRole("combobox", { name: "الفرع", exact: true });
  await formBranch.selectOption(branch.id);
  await submit.click();
  await expect(alert("اختر نطاق التكلفة والهدف")).toBeVisible();
  await page.getByRole("button", { name: "العودة إلى المصروفات" }).click();
  await expect(open).toBeFocused();
  await expect(page.getByRole("combobox", { name: "الفرع", exact: true })).toHaveValue(branch.id);
  await open.click();
  await expect(formBranch).toHaveValue("");
  await formBranch.selectOption(branch.id);
  await form.getByRole("combobox", { name: "نطاق التكلفة", exact: true }).selectOption("Branch");
  await expect(form.getByRole("combobox", { name: "المجموعة", exact: true })).toHaveCount(0);
  await form.getByRole("combobox", { name: "الفئة", exact: true }).selectOption("Maintenance");
  await form.getByLabel("من فترة الاستحقاق").fill(today);
  await form.getByLabel("إلى فترة الاستحقاق").fill(serviceDay);
  await submit.click();
  await expect(alert("حدد فترة استحقاق صحيحة")).toBeVisible();
  await form.getByLabel("من فترة الاستحقاق").fill(serviceDay);
  for (const invalid of ["0", "-10", "137.651"]) {
    await form.getByLabel(/المبلغ/).fill(invalid);
    await submit.click();
    await expect(alert("أدخل مبلغًا موجبًا")).toBeVisible();
  }
  expect(draftPosts).toBe(0);
  await form.getByLabel(/المبلغ/).fill("137.65");
  await form.getByLabel(/تاريخ السداد/).fill(today);
  const note = "صيانة تجريبية · تحقق من المصروف المدفوع";
  await form.getByLabel(/ملاحظة/).fill(note);

  const requests: { key: string; body: string }[] = [];
  let id = "";
  let failedDetailReads = 0;
  await page.route(new RegExp(`${api}/$`), async route => {
    const request = route.request();
    requests.push({ key: request.headers()["idempotency-key"], body: request.postData()! });
    const result = await route.fetch();
    expect(result.ok()).toBe(true);
    const saved = await result.json();
    if (requests.length === 1) {
      id = saved.id;
      await route.abort();
    } else {
      expect(saved).toMatchObject({ id, replay: true });
      await route.fulfill({ response: result });
    }
  });
  await page.route(new RegExp(`${api}/[0-9a-f-]{36}$`), async route => {
    if (route.request().url().endsWith(`/${id}`) && failedDetailReads++ === 0) await route.fulfill({ status: 503 });
    else await route.continue();
  });
  await submit.click();
  const verifyDraft = form.getByRole("button", { name: "التحقق من حفظ المسودة" });
  await expect(verifyDraft).toBeEnabled();
  await expect(form.getByLabel(/المبلغ/)).toBeDisabled();
  await verifyDraft.click();
  await expect(page.getByText("تعذر تحميل تفاصيل المصروف.", { exact: true })).toBeVisible();
  await expect(page.getByText(/حُفظ المصروف مسودة/)).toBeVisible();
  await page.getByRole("button", { name: "إعادة التحميل", exact: true }).click();
  const detail = page.getByRole("region", { name: "مراجعة المصروف" });
  await expect(detail).toContainText(branch.arabicName);
  await expect(detail).toContainText("137.65");
  await expect(detail).toContainText(note);
  expect(requests).toEqual([requests[0], requests[0]]);
  expect(totals(await statement())).toEqual(totals(before));

  let approvals = 0;
  await page.route(`**${api}/${id}/approve`, async route => {
    approvals++;
    expect((await route.fetch()).ok()).toBe(true);
    await route.abort();
  });
  await detail.getByRole("button", { name: "اعتماد المصروف" }).click();
  await page.getByRole("button", { name: "التحقق من حالة الاعتماد" }).click();
  await expect(detail).toContainText("معتمد للمراجعة");
  expect(approvals).toBe(1);
  expect(totals(await statement())).toEqual(totals(before));

  const preview = detail.getByRole("button", { name: "معاينة توزيع التكلفة" });
  const post = detail.getByRole("button", { name: "ترحيل المصروف" });
  const dialog = page.getByRole("dialog");
  const confirm = dialog.getByRole("button", { name: "تأكيد الترحيل" });
  const postRequests: { key: string; body: string }[] = [];
  await page.route(`**${api}/${id}/post`, async route => {
    const request = route.request();
    postRequests.push({ key: request.headers()["idempotency-key"], body: request.postData()! });
    if (postRequests.length === 1) {
      const result = await route.fetch({ postData: { quoteHash: "0".repeat(64) } });
      expect(result.status()).toBe(409);
      await route.fulfill({ response: result });
    } else {
      expect((await route.fetch()).ok()).toBe(true);
      await route.abort();
    }
  });
  await preview.click();
  await expect(detail.getByText(/إجمالي التوزيع/)).toContainText("137.65");
  await post.click();
  await expect(dialog).toContainText(branch.arabicName);
  await expect(dialog).toContainText("137.65");
  await expect(dialog.getByRole("button", { name: "إلغاء", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(post).toBeFocused();
  expect(postRequests).toHaveLength(0);
  await post.click();
  await page.screenshot({ path: info.outputPath("expense-confirmation-390.png") });
  await confirm.click();
  await expect(alert("افتح معاينة جديدة")).toBeVisible();
  expect(totals(await statement())).toEqual(totals(before));

  await preview.click();
  await post.click();
  await confirm.click();
  await page.getByRole("button", { name: "التحقق من حالة الترحيل" }).click();
  await expect(detail.getByRole("heading", { name: "الأنصبة المحفوظة" })).toBeVisible();
  await expect(detail.getByText(/الإجمالي المرحّل/)).toContainText("137.65");
  expect(postRequests).toHaveLength(2);
  expect(postRequests[0].key).not.toBe(postRequests[1].key);
  const stored = await read(page, `${api}/${id}`);
  expect(stored.expense.status).toBe("Posted");
  expect(cents(stored.allocationTotal)).toBe(13765);
  expect(stored.allocations.reduce((sum: number, row: { amount: number }) => sum + cents(row.amount), 0)).toBe(13765);
  if (stored.allocations.some((row: { trainingGroupId: string | null }) => !row.trainingGroupId)) {
    await expect(detail.getByText(/غير موزع على المجموعات/)).toBeVisible();
  }
  const serviceLabel = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "long", timeZone: "UTC" }).format(new Date(`${serviceDay}T12:00:00Z`));
  await expect(detail.locator("dd").filter({ hasText: serviceLabel })).toHaveText(serviceLabel);

  const after = await statement();
  expect(totals(after)).toEqual([cents(before.totalIncome), cents(before.totalExpenses) + 13765, cents(before.surplus) - 13765]);
  expect(totals(await statement(otherScope))).toEqual(totals(beforeOther));
  expect(totals(await statement(yesterdayScope))).toEqual(totals(beforeYesterday));
  expect((await collection()).netAmount).toBe(beforeCash.netAmount);
  const afterOverdue = await overdue();
  expect([afterOverdue.amount, afterOverdue.players]).toEqual([beforeOverdue.amount, beforeOverdue.players]);
  const afterAll = await statement(allScope);
  expect(cents(afterAll.surplus) - cents(beforeAll.surplus)).toBe(-13765);

  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`posted-expense-${width}.png`), fullPage: true });
  }
  const line = after.expenses.find((entry: { key: string }) => entry.key === "Maintenance");
  await page.goto(`/dashboard/finance/operating-statement?${scope}`);
  const source = page.getByRole("link", { name: /الصيانة والتشغيل/ });
  await expect(source).toHaveAttribute("href", new RegExp(`branchId=${branch.id}`));
  await source.click();
  await expect(page.getByLabel("من تاريخ السداد")).toHaveValue(today);
  await expect(page.getByRole("combobox", { name: "الحالة", exact: true })).toHaveValue("Posted");
  expect(line.sourceUrl).toContain("dateBasis=payment");
  await expect(page.getByRole("table", { name: "المصروفات التشغيلية" })).toContainText("137.65");

  await page.goto("/dashboard");
  const homeReport = page.getByRole("region", { name: "تقرير الأرباح والخسائر" });
  await homeReport.getByRole("button", { name: "هذا اليوم", exact: true }).click();
  const formatted = new Intl.NumberFormat("ar-EG-u-nu-latn", { maximumFractionDigits: 2 });
  const balance = homeReport.getByText(afterAll.surplus < 0 ? "العجز التشغيلي" : "الفائض التشغيلي", { exact: true }).locator("..");
  await expect(balance).toContainText(formatted.format(afterAll.surplus));
  await expect(homeReport.getByRole("link", { name: /الصيانة والتشغيل/ })).toBeVisible();
  await balance.scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath("owner-operating-report.png") });

  const foreign = await browser.newPage({ baseURL: new URL(page.url()).origin });
  try {
    await signIn(foreign, "owner.future@example.test");
    expect((await foreign.request.get(`${api}/${id}`)).status()).toBe(404);
    await foreign.goto(`${screen}?expenseId=${id}`);
    await expect(foreign.getByText("هذا المصروف غير متاح لهذا الحساب.", { exact: true })).toBeVisible();
    await expect(foreign.getByRole("region", { name: "مراجعة المصروف" })).toHaveCount(0);
  } finally {
    await foreign.close();
  }
  await writeFile(info.outputPath("expense-totals.json"), JSON.stringify({ branch: branch.arabicName, today, before: totals(before), after: totals(after), beforeAll: totals(beforeAll), afterAll: totals(afterAll), afterOther: totals(beforeOther), afterYesterday: totals(beforeYesterday), collections: beforeCash.netAmount, overdue: [afterOverdue.amount, afterOverdue.players] }, null, 2));
});
