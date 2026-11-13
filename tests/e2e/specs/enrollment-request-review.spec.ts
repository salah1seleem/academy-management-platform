import { randomUUID } from "node:crypto";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

const requests = "/api/v1/guardian/enrollment-requests";
const staffRequests = "/api/v1/enrollment-requests";
const form = "/guardian/enrollment-requests/new";
const list = "/guardian/enrollment-requests";
const football = "32000000-0000-0000-0000-000000000001";
const branch = "31000000-0000-0000-0000-000000000001";
const group = "34000000-0000-0000-0000-000000000001";
const academyTwoRequest = "72000000-0000-0000-0000-000000000001";

async function post(api: APIRequestContext, path: string, data: unknown, headers: Record<string, string> = {}) {
  const { token } = await api.get("/api/v1/auth/csrf").then(response => response.json());
  return api.post(path, { data, headers: { "X-CSRF-TOKEN": token, ...headers } });
}

async function guardian(api: APIRequestContext, phoneNumber = "01000000001") {
  const challenge = await post(api, "/api/v1/auth/guardian/otp/request", { phoneNumber });
  expect(challenge.ok()).toBe(true);
  expect((await post(api, "/api/v1/auth/guardian/otp/verify", {
    phoneNumber, code: "246810", challengeId: (await challenge.json()).challengeId,
  })).ok()).toBe(true);
}

async function staff(api: APIRequestContext, email = "owner.nogoom@example.test") {
  expect((await post(api, "/api/v1/auth/login", { email, password: "Demo-Only-123!" })).ok()).toBe(true);
}

function syntheticChild() {
  return `لاعب اختبار اشتراك ${randomUUID().slice(0, 8)}`;
}

function requestBody(name: string, dob = "2017-01-01") {
  return { requestType: 2, newChildArabicName: name, newChildDateOfBirth: dob, sportId: football, preferredBranchId: branch };
}

async function newRequest(api: APIRequestContext, name: string, extra = {}) {
  const response = await post(api, requests, { ...requestBody(name), ...extra }, { "Idempotency-Key": randomUUID() });
  expect(response.status()).toBe(201);
  return (await response.json()).id as string;
}

async function createGroup(api: APIRequestContext) {
  const response = await post(api, "/api/v1/manage/groups", {
    arabicName: `مجموعة اختبار الاشتراك ${randomUUID().slice(0, 8)}`,
    branchId: branch,
    sportId: football,
    ageCategoryId: "33000000-0000-0000-0000-000000000002",
    capacity: 1,
  });
  expect(response.status()).toBe(201);
  const id = await response.json();
  expect(typeof id).toBe("string");
  return id as string;
}

async function openForm(page: Page) {
  await page.goto(form);
  await page.getByRole("button", { name: /إضافة طفل جديد/ }).click();
}

test("A lost create response replays the saved enrollment request once", async ({ page }, info) => {
  test.setTimeout(80_000);
  await guardian(page.request);
  const name = syntheticChild();
  const receipts = await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json());
  const keys: string[] = [];
  let savedId = "";
  await page.route(`**${requests}`, async route => {
    if (route.request().method() !== "POST") return route.continue();
    const body = route.request().postDataJSON();
    if (body.newChildDateOfBirth === "2099-01-01" || !body.newChildArabicName) return route.continue();
    keys.push(route.request().headers()["idempotency-key"]);
    if (keys.length > 1) return route.continue();
    const response = await route.fetch();
    expect(response.status()).toBe(201);
    savedId = (await response.json()).id;
    await route.abort();
  });
  await openForm(page);
  const send = page.getByRole("button", { name: "إرسال طلب الاشتراك", exact: true });
  await send.click();
  expect(keys).toHaveLength(0);
  await page.getByLabel("اسم الطفل").fill(name);
  await page.getByLabel("تاريخ الميلاد").fill("2099-01-01");
  await page.getByRole("combobox", { name: /^الرياضة/ }).selectOption(football);
  await page.getByLabel("الفرع المفضل").selectOption(branch);
  const future = page.waitForResponse(response => response.url().endsWith(requests) && response.request().method() === "POST");
  await send.click();
  expect((await future).status()).toBe(400);
  await expect(page.getByRole("alert").filter({ hasText: "اسم الطفل وتاريخ ميلاد صحيح مطلوبان." })).toBeVisible();
  await expect(page.getByLabel("تاريخ الميلاد")).toBeEditable();
  await page.getByLabel("تاريخ الميلاد").fill("2017-01-01");
  await page.getByLabel("اسم الطفل").fill("   ");
  const blank = page.waitForResponse(response => response.url().endsWith(requests) && response.request().method() === "POST");
  await send.click();
  expect((await blank).status()).toBe(400);
  await expect(page.getByLabel("اسم الطفل")).toBeEditable();
  await page.getByLabel("اسم الطفل").fill(name);
  await send.click();
  await expect(page.getByText(/تعذر تأكيد/)).toBeVisible();
  await page.screenshot({ path: info.outputPath("create-lost-390.png"), fullPage: true });
  await page.getByRole("button", { name: /إرسال طلب الاشتراك|إعادة إرسال الطلب/ }).click();
  await expect.poll(() => keys.length).toBe(2);
  expect(keys[1]).toBe(keys[0]);
  await expect(page).toHaveURL(`${list}?created=${savedId}`, { timeout: 20_000 });
  const row = page.getByRole("article").filter({ has: page.getByRole("heading", { name }) });
  await expect(row).toHaveCount(1);
  await expect(page.getByRole("article").first()).toContainText(name);
  await expect(row).toContainText("قيد الانتظار");
  await expect(page.getByRole("status").filter({ hasText: "تم إرسال طلب الاشتراك." })).toBeVisible();
  expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(receipts);
  const matches = (await page.request.get(requests).then(r => r.json())).filter((x: { child: string }) => x.child === name);
  expect(matches).toEqual([expect.objectContaining({ id: savedId, status: "Pending", createdSportEnrollmentId: null })]);
});

test("An unavailable proof read blocks a stale staff decision", async ({ page, browser }, info) => {
  test.setTimeout(60_000);
  const family = await browser.newContext({ baseURL: info.project.use.baseURL });
  try {
    await guardian(family.request);
    const id = await newRequest(family.request, syntheticChild(), { subscriptionPlanId: "51000000-0000-0000-0000-000000000001" });
    await staff(page.request);
    const proof = `/api/v1/manage/enrollment-requests/${id}/payment`;
    let finishProofRead!: () => void;
    let proofReadStarted!: () => void;
    const pendingRead = new Promise<void>(resolve => { finishProofRead = resolve; });
    const requestStarted = new Promise<void>(resolve => { proofReadStarted = resolve; });
    await page.route(`**${proof}`, async route => {
      proofReadStarted();
      await pendingRead;
      await route.fulfill({ status: 503, body: "Unavailable" });
    });
    await page.goto(`/dashboard/enrollment-requests/${id}`);
    const start = page.getByRole("button", { name: "بدء المراجعة" });
    try {
      await requestStarted;
      await expect(page.getByRole("heading", { name: "مراجعة طلب الاشتراك" })).toBeVisible();
      await expect(start).toHaveCount(0);
    } finally {
      finishProofRead();
    }
    await expect(page.getByText(/تعذر التحقق من بيانات الدفع/)).toBeVisible();
    await expect(start).toHaveCount(0);
    await page.unroute(`**${proof}`);
    await page.route(`**${proof}`, async route => {
      const response = await route.fetch();
      await route.fulfill({ response, json: { ...await response.json(), status: "Approved" } });
    }, { times: 1 });
    await page.getByRole("button", { name: "التحقق من حالة الطلب" }).click();
    await expect(page.getByText(/تغيّرت حالة الطلب أثناء القراءة/)).toBeVisible();
    await expect(start).toHaveCount(0);
    await page.getByRole("button", { name: "التحقق من حالة الطلب" }).click();
    await expect(start).toBeEnabled();
    await page.getByRole("combobox", { name: /^المجموعة/ }).selectOption(group);
    await expect(page.getByRole("button", { name: "قبول وإنشاء التسجيل" })).toBeDisabled();
    await expect(page.getByText("لم يصل إثبات الدفع بعد.")).toBeVisible();
    const blocked = await post(page.request, `${staffRequests}/${id}/approve`, { trainingGroupId: group });
    expect(blocked.status()).toBe(409);
    expect((await page.request.get(`${proof}`).then(r => r.json())).status).toBe("Pending");
    expect((await page.request.get(`${staffRequests}/${academyTwoRequest}`)).status()).toBe(404);
    expect((await page.request.get(`/api/v1/manage/enrollment-requests/${academyTwoRequest}/payment`)).status()).toBe(404);
  } finally {
    await family.close();
  }
});

test("Enrollment acceptance is recovered by reading status after a lost decision response", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const family = await browser.newContext({ baseURL: info.project.use.baseURL });
  try {
    await guardian(family.request);
    const name = syntheticChild();
    const id = await newRequest(family.request, name);
    const receipts = () => family.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json()).then(result => result.items);
    const before = await receipts();
    await staff(page.request, "admin.nogoom@example.test");
    const admissionGroup = await createGroup(page.request);
    const endpoint = `${staffRequests}/${id}`;
    await page.goto(`/dashboard/enrollment-requests/${id}`);
    await expect(page.getByRole("link", { name: "العودة إلى طلبات الاشتراك" })).toHaveAttribute("href", "/dashboard/enrollment-requests");
    const accept = page.getByRole("button", { name: "قبول وإنشاء التسجيل" });
    await expect(accept).toBeDisabled();
    const wrong = await post(page.request, `${endpoint}/approve`, { trainingGroupId: "34000000-0000-0000-0000-000000000002" });
    expect(wrong.status()).toBe(422);
    const familyStatus = async () => (await family.request.get(requests).then(r => r.json())).find((r: { id: string }) => r.id === id);
    expect((await familyStatus()).status).toBe("Pending");
    await page.getByRole("button", { name: "بدء المراجعة" }).click();
    await expect(page.getByRole("status").filter({ hasText: "قيد المراجعة" })).toBeVisible();
    expect((await familyStatus()).status).toBe("UnderReview");
    await expect(page.getByRole("button", { name: "بدء المراجعة" })).toHaveCount(0);
    await page.getByRole("combobox", { name: /^المجموعة/ }).selectOption(admissionGroup);
    let writes = 0;
    await page.route(`**${endpoint}/approve`, async route => {
      writes++;
      const response = await route.fetch();
      expect(response.ok()).toBe(true);
      await route.abort();
    });
    await accept.click();
    const dialog = page.getByRole("dialog", { name: "تأكيد قبول طلب الاشتراك" });
    await expect(dialog).toContainText(name);
    await expect(dialog.getByRole("button", { name: "إلغاء", exact: true })).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(accept).toBeFocused();
    expect(writes).toBe(0);
    expect(await receipts()).toEqual(before);
    await accept.click();
    await dialog.getByRole("button", { name: "تأكيد القبول", exact: true }).click();
    await expect(page.getByText("تحقّق من حالة الطلب قبل اتخاذ قرار آخر.")).toBeVisible();
    await expect(accept).toHaveCount(0);
    await page.getByRole("button", { name: "التحقق من حالة الطلب" }).click();
    await expect(page.getByRole("status").filter({ hasText: "مقبول" })).toBeVisible();
    expect(writes).toBe(1);
    const saved = await familyStatus();
    expect(saved).toEqual(expect.objectContaining({ status: "Approved", createdSportEnrollmentId: expect.any(String) }));
    const replay = await post(page.request, `${endpoint}/approve`, { trainingGroupId: admissionGroup });
    expect((await replay.json()).enrollmentId).toBe(saved.createdSportEnrollmentId);
    const waitlisted = await newRequest(family.request, syntheticChild());
    const overCapacity = await post(page.request, `${staffRequests}/${waitlisted}/approve`, { trainingGroupId: admissionGroup });
    expect(overCapacity.status()).toBe(409);
    expect((await overCapacity.json()).message).toContain("المجموعة مكتملة العدد");
    const waiting = (await family.request.get(requests).then(r => r.json())).find((row: { id: string }) => row.id === waitlisted);
    expect(waiting).toEqual(expect.objectContaining({ status: "Pending", createdSportEnrollmentId: null }));
    expect(await receipts()).toEqual(before);
    const familyPage = await family.newPage();
    await familyPage.goto(list);
    const choose = familyPage.getByRole("article").filter({ hasText: name }).getByRole("link", { name: "إكمال الاشتراك / اختيار الباقة" });
    await expect(choose).toHaveAttribute("href", `/guardian/renew/${saved.createdSportEnrollmentId}`);
    await choose.click();
    await expect(familyPage.getByRole("heading", { name: "اختيار باقة الاشتراك" })).toBeVisible();
    await expect(familyPage.getByText(name, { exact: true })).toBeVisible();
    const continueToPayment = familyPage.getByRole("button", { name: "متابعة إلى طلب الدفع" });
    await expect(continueToPayment).toBeDisabled();
    await familyPage.getByRole("radio").first().check();
    await expect(continueToPayment).toBeEnabled();
  } finally {
    await family.close();
  }
});

test("A confirmed rejection is visible to the family and leaves receipts intact", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const owner = await browser.newContext({ baseURL: info.project.use.baseURL, viewport: { width: 390, height: 844 } });
  try {
    await guardian(page.request);
    const name = syntheticChild();
    const id = await newRequest(page.request, name, { newChildDateOfBirth: "1999-01-01" });
    await staff(owner.request);
    const endpoint = `${staffRequests}/${id}`;
    expect((await page.request.get(endpoint)).status()).toBe(403);
    expect((await post(page.request, `${endpoint}/reject`, {})).status()).toBe(403);
    const before = await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json());
    const review = await owner.newPage();
    await review.goto(`/dashboard/enrollment-requests/${id}`);
    await expect(review.getByText(/لا توجد مجموعة مناسبة لعمر اللاعب/)).toBeVisible();
    await expect(review.getByRole("button", { name: "قبول وإنشاء التسجيل" })).toBeDisabled();
    const reason = "المجموعة المناسبة ستتاح الشهر المقبل.";
    await review.getByRole("textbox", { name: /سبب الرفض/ }).fill(reason);
    await review.getByRole("button", { name: "رفض الطلب", exact: true }).click();
    const dialog = review.getByRole("dialog", { name: "تأكيد رفض طلب الاشتراك" });
    await expect(dialog.getByRole("button", { name: "إلغاء", exact: true })).toBeFocused();
    await dialog.getByRole("button", { name: "إلغاء", exact: true }).click();
    expect((await owner.request.get(endpoint).then(r => r.json())).request.status).toBe("Pending");
    await review.getByRole("button", { name: "رفض الطلب", exact: true }).click();
    await review.screenshot({ path: info.outputPath("reject-dialog-390.png"), fullPage: true });
    await dialog.getByRole("button", { name: "تأكيد الرفض" }).click();
    await expect(review.getByRole("status").filter({ hasText: "مرفوض" })).toBeVisible();
    await expect(review.getByText(reason, { exact: true })).toBeVisible();
    await page.goto(`${list}?created=${academyTwoRequest}`);
    const row = page.getByRole("article").filter({ hasText: name });
    await expect(row).toContainText(reason);
    await expect(row).toContainText("مرفوض");
    await expect(row.getByRole("link")).toHaveCount(0);
    await expect(page.getByText("تم إرسال طلب الاشتراك.")).toHaveCount(0);
    expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
  } finally {
    await owner.close();
  }
});

test("One enrollment proof creates one matching receipt and collection after approval", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const family = await browser.newContext({ baseURL: info.project.use.baseURL });
  try {
    await guardian(family.request, "01000000014");
    await staff(page.request);
    const admissionGroup = await createGroup(page.request);
    const name = syntheticChild();
    const id = await newRequest(family.request, name, { subscriptionPlanId: "51000000-0000-0000-0000-000000000001" });
    const endpoint = `${staffRequests}/${id}`;
    const proofPath = `${requests}/${id}/payment`;
    const saved = await family.request.get(proofPath).then(r => r.json());
    expect(saved.amount).toBeGreaterThan(0);
    const receipts = () => family.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json());
    const before = await receipts();
    const enrollment = (await family.request.get(requests).then(r => r.json())).find((row: { id: string }) => row.id === id);
    const day = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(enrollment.createdAtUtc));
    const report = async () => {
      const response = await page.request.get(`/api/v1/reports/financial?from=${day}&to=${day}`);
      expect(response.ok()).toBe(true);
      return response.json();
    };
    const beforeReport = await report();
    const { token } = await family.request.get("/api/v1/auth/csrf").then(r => r.json());
    const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=", "base64");
    const upload = await family.request.post("/api/v1/assets/upload", {
      headers: { "X-CSRF-TOKEN": token }, multipart: { purpose: "Proof", file: { name: "synthetic-enrollment.png", mimeType: "image/png", buffer: png } },
    });
    expect(upload.status()).toBe(201);
    const assetId = (await upload.json()).id;
    const attach = await post(family.request, `${requests}/${id}/proof`, { assetId });
    expect(attach.ok()).toBe(true);
    expect(await receipts()).toEqual(before);
    expect((await report()).totalAmount).toBe(beforeReport.totalAmount);

    const stale = await page.context().newPage();
    const route = `/dashboard/enrollment-requests/${id}`;
    await stale.goto(route);
    await expect(stale.getByRole("button", { name: "رفض الطلب", exact: true })).toBeEnabled();
    await page.goto(route);
    const proof = page.getByRole("link", { name: "عرض إثبات الدفع" });
    await expect(proof).toHaveAttribute("href", `/api/v1/assets/${assetId}`);
    expect((await page.request.get(`/api/v1/assets/${assetId}`)).ok()).toBe(true);
    await page.getByRole("combobox", { name: /^المجموعة/ }).selectOption(admissionGroup);
    await page.getByRole("button", { name: "قبول وإنشاء التسجيل" }).click();
    const dialog = page.getByRole("dialog", { name: "تأكيد قبول طلب الاشتراك" });
    await expect(dialog).toContainText("تُفعّل الباقة");
    await page.screenshot({ path: info.outputPath("paid-approval-390.png"), fullPage: true });
    await dialog.getByRole("button", { name: "تأكيد القبول", exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: /^مقبول$/ })).toBeVisible();
    await expect(page.getByText("المبلغ المحصّل", { exact: false })).toBeVisible();
    await expect(page.getByRole("link", { name: "عرض الإيصال" })).toBeVisible();
    const payment = await family.request.get(proofPath).then(r => r.json());
    expect(payment.status).toBe("Approved");
    expect(payment.paymentRequestId).toBeTruthy();
    expect(payment.amount).toBe(saved.amount);
    const finalReceipts = await receipts();
    expect(finalReceipts.total).toBe(before.total + 1);
    const issued = finalReceipts.items.filter((entry: { player: string }) => entry.player === name);
    expect(issued).toEqual([expect.objectContaining({ amount: saved.amount, currency: saved.currency })]);
    expect(Math.round(((await report()).totalAmount - beforeReport.totalAmount) * 100)).toBe(Math.round(saved.amount * 100));

    await stale.getByRole("button", { name: "رفض الطلب", exact: true }).click();
    await stale.getByRole("button", { name: "تأكيد الرفض", exact: true }).click();
    await expect(stale.getByText(/تحقّق من حالة الطلب قبل اتخاذ قرار آخر/)).toBeVisible();
    await stale.getByRole("button", { name: "التحقق من حالة الطلب" }).click();
    await expect(stale.getByRole("status").filter({ hasText: "مقبول" })).toBeVisible();
    expect((await post(page.request, `${endpoint}/approve`, { trainingGroupId: admissionGroup, expectedProofAssetId: assetId })).ok()).toBe(true);
    expect(await receipts()).toEqual(finalReceipts);
    expect(Math.round(((await report()).totalAmount - beforeReport.totalAmount) * 100)).toBe(Math.round(saved.amount * 100));

    const familyPage = await family.newPage();
    await familyPage.goto(list);
    const row = familyPage.getByRole("article").filter({ hasText: name });
    await expect(row.getByRole("link", { name: "عرض الإيصالات" })).toHaveAttribute("href", "/guardian/receipts");
    await expect(row.getByRole("link", { name: /اختيار الباقة/ })).toHaveCount(0);
    await row.getByRole("link", { name: "عرض الإيصالات" }).click();
    await expect(familyPage).toHaveURL("/guardian/receipts");
    await expect(familyPage.getByText(name, { exact: true }).first()).toBeVisible();
  } finally {
    await family.close();
  }
});

test("Enrollment options and family history can be retried without writing", async ({ page }, info) => {
  await guardian(page.request);
  const optionPath = "**/api/v1/guardian/enrollment-request-options";
  let writes = 0;
  await page.route(`**${requests}`, route => {
    if (route.request().method() === "POST") writes++;
    return route.continue();
  });
  await page.route(optionPath, route => route.fulfill({ status: 503 }));
  await page.goto(form);
  await page.getByRole("button", { name: "إضافة رياضة لطفل موجود" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "تعذر تحميل الأطفال والرياضات والفروع." })).toBeVisible();
  await page.unroute(optionPath);
  await page.getByRole("button", { name: "إعادة تحميل الاختيارات" }).click();
  const child = page.getByRole("combobox", { name: /^الطفل/ });
  await expect(child).toBeVisible();
  const children = await page.request.get("/api/v1/guardian/enrollment-request-options").then(r => r.json());
  expect(await child.locator("option").count()).toBe(children.children.length + 1);
  await page.getByRole("button", { name: "تغيير نوع الطلب" }).click();
  await page.getByRole("button", { name: "إضافة طفل جديد" }).click();
  await expect(page.getByLabel("اسم الطفل")).toBeVisible();
  await page.route(`**${requests}`, route => route.fulfill({ status: 503 }));
  await page.getByRole("link", { name: "العودة إلى الطلبات" }).click();
  await expect(page).toHaveURL(list, { timeout: 20_000 });
  await expect(page.getByRole("alert").filter({ hasText: "تعذر تحميل طلبات الاشتراك." })).toBeVisible();
  await page.unroute(`**${requests}`);
  await page.getByRole("button", { name: "إعادة تحميل الطلبات" }).click();
  await expect(page.getByRole("article").first()).toBeVisible();
  expect(writes).toBe(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: info.outputPath("family-list-390.png"), fullPage: true });
});
