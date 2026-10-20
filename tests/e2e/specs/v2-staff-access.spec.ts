import { expect, test } from "@playwright/test";
import { selectStaffAccess } from "../support/staff-access";

test("Owner sets an explicit branch and read-only module matrix; Admin cannot write or self-grant", async ({ page, browser }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const stamp = Date.now(); const name = `إداري تحقق الصلاحيات ${stamp}`; const email = `scope.e2e.${stamp}@example.test`;
  const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const created = await page.request.post("/api/v1/manage/staff/", { headers: { "X-CSRF-TOKEN": token }, data: { name, email, phone: `010${String(stamp).slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "اختبار اصطناعي" } });
  expect(created.ok()).toBeTruthy(); const member = (await created.json()).id;
  await page.goto("/dashboard/staff");
  await selectStaffAccess(page, member);
  await page.getByRole("radio", { name: "فروع محددة" }).check();
  expect(await page.evaluate(()=>innerWidth)).toBeLessThanOrEqual(page.viewportSize()!.width+1);
  await page.getByRole("group",{name:"نطاق الفروع",exact:true}).getByRole("checkbox").first().check();
  await page.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور", exact: true }).check();
  await page.getByRole("switch",{name:"عرض اللاعبين وأولياء الأمور",exact:true}).check();
  await page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true }).click();
  await page.getByRole("button", { name: "تأكيد الاعتماد", exact: true }).click();
  await expect(page.getByRole("heading", { name: `صلاحيات ${name}`, exact: true })).not.toBeVisible();
  await selectStaffAccess(page, member);
  await expect(page.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور", exact: true })).toBeChecked();
  await expect(page.getByRole("switch", { name: "إضافة اللاعبون وأولياء الأمور", exact: true })).not.toBeChecked();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: info.outputPath("staff-access-mobile.png"), fullPage: true });
  const context = await browser.newContext(); const admin = await context.newPage();
  try {
    await admin.goto(`${test.info().project.use.baseURL}/login`);
    await admin.getByLabel("البريد الإلكتروني").fill(email); await admin.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await admin.getByRole("button", { name: "دخول آمن" }).click(); await admin.waitForURL("**/dashboard");
    expect((await admin.request.get("/api/v1/people/players")).status()).toBe(200);
    const options = await (await admin.request.get("/api/v1/manage/structure/options")).json(); expect(options.branches).toHaveLength(1);
    const csrf = (await (await admin.request.get("/api/v1/auth/csrf")).json()).token;
    const headers = { "X-CSRF-TOKEN": csrf };
    expect((await admin.request.post("/api/v1/people/players", { headers, data: {} })).status()).toBe(403);
    expect((await admin.request.put(`/api/v1/manage/staff/${member}/access`, { headers, data: {} })).status()).toBe(403);
    expect((await admin.request.get("/api/v1/reports/financial")).status()).toBe(403);
  } finally { await context.close(); }
});
