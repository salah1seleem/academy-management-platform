import { expect, test } from "@playwright/test";
import { selectStaffAccess } from "../support/staff-access";

test("Owner creates and activates a scoped context; the same admin explicitly switches without combining grants", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const stamp = Date.now(); const name = `إداري سياقات ${stamp}`; const email = `contexts.${stamp}@example.test`; const contextName = `متابعة السباحة ${stamp}`;
  const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const created = await page.request.post("/api/v1/manage/staff/", { headers: { "X-CSRF-TOKEN": token }, data: { name, email, phone: `010${String(stamp).slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin" } }); expect(created.ok()).toBeTruthy(); const primary = (await created.json()).id;
  const settings = await (await page.request.get(`/api/v1/manage/staff/${primary}/access`)).json();
  const branchA = "31000000-0000-0000-0000-000000000001"; const branchB = "31000000-0000-0000-0000-000000000002";
  const football = "32000000-0000-0000-0000-000000000001"; const swimming = "32000000-0000-0000-0000-000000000002";
  const branchName = settings.branches.find((b: { id: string }) => b.id === branchB).name;
  const sportName = settings.sports.find((s: { id: string }) => s.id === swimming).name;
  expect((await page.request.put(`/api/v1/manage/staff/${primary}/access`, { headers: { "X-CSRF-TOKEN": token }, data: { allBranches: false, branches: [branchA], allSports: false, sports: [football], actions: settings.modules.map((module: string) => ({ module, read: true, add: module !== "followups", edit: true, delete: false })) } })).status()).toBe(204);
  await page.goto("/dashboard/staff"); await selectStaffAccess(page, primary); await page.getByText("إدارة متقدمة وسجل الصلاحيات", { exact: true }).click(); await page.getByRole("button", { name: "نطاقات عمل إضافية لهذا الإداري", exact: true }).click();
  await page.getByLabel("اسم السياق الجديد").fill(contextName); await page.getByRole("button", { name: "إنشاء وتحديد الصلاحيات" }).click();
  await expect(page.getByText("هذا سياق تشغيل إضافي", { exact: false })).toBeVisible();
  await page.getByRole("combobox", { name: "قالب الصلاحيات" }).selectOption("read-only");
  await page.getByRole("group", { name: "نطاق الفروع", exact: true }).getByRole("checkbox", { name: branchName, exact: true }).check();
  await page.getByRole("radio", { name: "رياضات محددة", exact: true }).check(); await page.getByRole("group", { name: "نطاق الرياضات", exact: true }).getByRole("checkbox", { name: sportName, exact: true }).check();
  await page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true }).click(); await page.getByRole("button", { name: "تأكيد الاعتماد", exact: true }).click();
  await page.getByRole("button", { name: "الاسم والتفعيل", exact: true }).click(); await page.getByLabel("سياق فعال").check(); await page.getByRole("button", { name: "مراجعة الحفظ", exact: true }).click();
  const activated = page.waitForResponse(r => r.url().endsWith("/context") && r.request().method() === "PUT"); await page.getByRole("button", { name: "تأكيد حالة السياق", exact: true }).click(); expect((await activated).status()).toBe(204);
  await expect(page.getByRole("heading", { name: contextName, exact: true })).toBeVisible();
  const contexts = await (await page.request.get(`/api/v1/manage/staff/${primary}/contexts`)).json(); const added = contexts.items.find((c: { name: string }) => c.name === contextName); expect(added.isActive).toBe(true);
  const browserContext = await browser.newContext({ baseURL: test.info().project.use.baseURL }); const admin = await browserContext.newPage();
  try {
    await admin.goto("/login"); await admin.getByLabel("البريد الإلكتروني").fill(email); await admin.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await admin.getByRole("button", { name: "دخول آمن" }).click(); await admin.waitForURL("**/dashboard/context");
    expect((await admin.request.get("/api/v1/me")).status()).toBe(403); expect((await admin.request.get("/api/v1/people/players")).status()).toBe(403);
    await expect(admin.getByRole("button", { name: "فتح مساحة العمل" })).toBeDisabled();
    await admin.getByRole("radio", { name: new RegExp(contextName) }).check();
    for (const width of [390, 1440]) {
      await admin.setViewportSize({ width, height: 900 }); await expect.poll(() => admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      await admin.screenshot({ path: info.outputPath(`context-selection-${width}.png`), fullPage: true, animations: "disabled" });
    }
    await admin.getByRole("button", { name: "فتح مساحة العمل" }).click(); await admin.waitForURL("**/dashboard");
    const current = await (await admin.request.get("/api/v1/me")).json(); expect(current.membershipId).toBe(added.id); expect(current.contextName).toBe(contextName);
    const weakOptions = await (await admin.request.get("/api/v1/manage/structure/options")).json(); expect(weakOptions.branches.map((b: { id: string }) => b.id)).toEqual([branchB]); expect(weakOptions.sports.map((s: { id: string }) => s.id)).toEqual([swimming]);
    await admin.goto("/dashboard/players"); await expect(admin.getByRole("heading", { name: "قائمة اللاعبين", exact: true })).toBeVisible();
    const csrf = (await (await admin.request.get("/api/v1/auth/csrf")).json()).token;
    expect((await admin.request.post("/api/v1/manage/groups", { headers: { "X-CSRF-TOKEN": csrf }, data: { arabicName: "كتابة ممنوعة", branchId: branchB, sportId: swimming, ageCategoryId: weakOptions.categories[0].id } })).status()).toBe(403);
    await admin.goto("/dashboard/context"); await admin.getByRole("radio", { name: /الإدارة الأساسية/ }).check(); await admin.getByRole("button", { name: "فتح مساحة العمل" }).click(); await admin.waitForURL("**/dashboard");
    const rootOptions = await (await admin.request.get("/api/v1/manage/structure/options")).json(); expect(rootOptions.branches.map((b: { id: string }) => b.id)).toEqual([branchA]); expect(rootOptions.sports.map((s: { id: string }) => s.id)).toEqual([football]);
    const latestToken = (await (await admin.request.get("/api/v1/auth/csrf")).json()).token;
    expect((await admin.request.post("/api/v1/manage/groups", { headers: { "X-CSRF-TOKEN": latestToken }, data: { arabicName: `إنشاء بالسياق القوي ${stamp}`, branchId: branchA, sportId: football, ageCategoryId: rootOptions.categories[0].id } })).status()).toBe(201);
    expect((await admin.request.get("/api/v1/manage/staff")).status()).toBe(403);
  } finally { await browserContext.close(); }
});
