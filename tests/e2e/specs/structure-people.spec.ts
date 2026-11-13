import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Explicitly authorized branch Admin registers a paid synthetic player from mobile", async ({ page }) => {
  const suffix = Date.now().toString().slice(-6);
  const playerName = `يوسف اختبار ${suffix}`;
  const guardianPhone = `0101${suffix}`.padEnd(11, "7");
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await page.waitForURL("**/dashboard");
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true); return response.status() === 204 ? null : response.json();
  }
  const email = `registration.${crypto.randomUUID()}@example.test`;
  // Own capacity: repeated rehearsals must not fill the shared demo group.
  const groupName = `تسجيل إداري اصطناعي ${crypto.randomUUID()}`;
  const groupId = await write("/api/v1/manage/groups", { arabicName: groupName, branchId: "31000000-0000-0000-0000-000000000001", sportId: "32000000-0000-0000-0000-000000000001", ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 });
  expect(typeof groupId).toBe("string");
  const member = await write("/api/v1/manage/staff", { name: "إداري تسجيل اصطناعي", email, phone: `011${Date.now().toString().slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "تسجيل" });
  const access = await page.request.get(`/api/v1/manage/staff/${member.id}/access`).then(r => r.json());
  await write(`/api/v1/manage/staff/${member.id}/access`, { allBranches: false, branches: ["31000000-0000-0000-0000-000000000001"], workflowActions: ["workflow.players.read", "workflow.players.create", "workflow.payments.collect"], actions: access.modules.map((module: string) => ({ module, read: ["players", "subscriptions", "payments", "finance"].includes(module), add: ["players", "subscriptions", "payments", "finance"].includes(module), edit: module === "payments", delete: false })) }, "PUT");
  await page.context().clearCookies(); await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await page.getByRole("navigation", { name: "إجراءات سريعة" }).getByRole("link", { name: "اشتراك جديد", exact: true }).click();
  await expect(page.getByRole("heading", { name: "تسجيل لاعب جديد" })).toBeVisible();
  await page.getByLabel("اسم اللاعب بالعربية").fill(playerName);
  await page.getByLabel("تاريخ الميلاد").fill("2016-05-10");
  await page.getByLabel("اسم ولي الأمر").fill(`وليد اختبار ${suffix}`);
  await page.getByLabel("رقم الهاتف").fill("1012345678");
  await page.getByLabel("الفرع").selectOption({ label: "فرع مدينة نصر" });
  await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" });
  await page.getByLabel("الفئة العمرية").selectOption({ label: "تحت 10 سنوات" });
  await page.getByLabel("المجموعة").selectOption({ label: groupName });
  await page.getByLabel("الباقة", { exact: true }).selectOption("51000000-0000-0000-0000-000000000001");
  const badQuote = page.waitForResponse(response => response.url().endsWith("/people/registration-quote") && response.request().method() === "POST");
  await page.getByRole("button", { name: "حساب المبلغ المستحق" }).click();
  expect((await badQuote).status()).toBe(422);
  await expect(page.getByRole("alert").filter({ hasText: "اكتب رقم موبايل ولي الأمر" })).toBeVisible();
  await expect(page.getByRole("button", { name: "تسجيل وتأكيد التحصيل" })).toBeDisabled();
  await page.getByLabel("رقم الهاتف").fill(guardianPhone);
  await page.getByRole("button", { name: "حساب المبلغ المستحق" }).click();
  await expect(page.getByText(/المبلغ المطلوب تحصيله/)).toBeVisible();
  await page.getByLabel("طريقة التحصيل").selectOption("Online");
  page.once("dialog", async dialog => {
    expect(dialog.type()).toBe("confirm");
    expect(dialog.message()).toContain("بالتحويل الإلكتروني");
    expect(dialog.message()).toContain("وإصدار الإيصال");
    await dialog.accept();
  });
  const registration = page.waitForResponse(response => response.url().endsWith("/people/registrations") && response.request().method() === "POST");
  await page.getByRole("button", { name: "تسجيل وتأكيد التحصيل" }).click();
  const registered = await registration;
  expect(registered.status()).toBe(201);
  const saved = await registered.json();
  expect(saved).toMatchObject({ subscriptionCreated: true, replay: false, periodId: expect.any(String), receiptId: expect.any(String) });
  const payload = registered.request().postDataJSON();
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  const headers = { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": (await registered.request().headerValue("Idempotency-Key"))! };
  const replay = await page.request.post("/api/v1/people/registrations", { headers, data: payload });
  expect(replay.status()).toBe(200);
  expect(await replay.json()).toMatchObject({ playerId: saved.playerId, periodId: saved.periodId, receiptId: saved.receiptId, replay: true });
  const tampered = await page.request.post("/api/v1/people/registrations", { headers, data: { ...payload, expectedAmount: 1 } });
  expect(tampered.status()).toBe(409);
  await page.waitForURL("**/dashboard/players");
  await expect(page.getByRole("heading", { name: "قائمة اللاعبين" })).toBeVisible();
  await page.getByLabel("بحث اللاعبين").fill(playerName);
  await expect(page.getByText(playerName, { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "تسجيل الخروج" }).click();
  await guardianLogin(page, guardianPhone);
  await page.goto(`/guardian/receipts/${saved.receiptId}`);
  await expect(page.getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible();
  await expect(page.getByRole("article", { name: "إيصال دفع" }).getByRole("heading", { name: playerName })).toBeVisible();
});

test("Demo Guardian sees linked siblings and no unrelated child", async ({ page }) => {
  await guardianLogin(page);
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "عمر أحمد محمود" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "مريم أحمد محمود" })).toBeVisible();
  await expect(page.getByText("عمر أحمد حسن", { exact: true })).toHaveCount(0);
});
