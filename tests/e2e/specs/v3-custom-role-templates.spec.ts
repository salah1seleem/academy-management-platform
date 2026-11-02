import { expect, test } from "@playwright/test";
import { selectStaffAccess, openRoleTemplates } from "../support/staff-access";

test("Owner copies a template, reviews a scoped application and explicitly reapplies updates without live escalation", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const stamp = Date.now(); const name = `إداري قالب مخصص ${stamp}`; const roleName = `منسق متابعة ${stamp}`; const email = `custom.role.${stamp}@example.test`;
  const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const created = await page.request.post("/api/v1/manage/staff/", { headers: { "X-CSRF-TOKEN": token }, data: { name, email, phone: `010${String(stamp).slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "قالب اصطناعي" } });
  expect(created.ok()).toBeTruthy(); const member = (await created.json()).id;
  await page.goto("/dashboard/staff"); await openRoleTemplates(page);
  await page.getByRole("button", { name: "إنشاء قالب مخصص" }).click(); await page.getByLabel("اسم القالب").fill(roleName); await page.getByLabel("وصف مختصر").fill("قراءة تشغيلية اصطناعية، دون رواتب أو تحصيل");
  await page.getByRole("combobox", { name: "نسخ صلاحيات من" }).selectOption("read-only");
  await page.locator("summary").filter({ hasText: "التشغيل والتدريب" }).click();
  await expect(page.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور", exact: true })).toBeChecked();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await expect.poll(() => page.locator(".access-table-scroll").first().evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`custom-template-${width}.png`), fullPage: true, animations: "disabled" });
  }
  await page.getByRole("button", { name: "مراجعة وحفظ القالب" }).click(); await page.getByRole("button", { name: "تأكيد حفظ القالب" }).click();
  await expect(page.getByRole("heading", { name: roleName, exact: true })).toBeVisible();
  const roles = await (await page.request.get("/api/v1/manage/staff/role-templates")).json(); const savedRole = roles.items.find((r: { template: { name: string } }) => r.template.name === roleName);
  expect(savedRole.template.revision).toBe(1);
  await page.getByRole("button", { name: "عودة للموظفين" }).click();
  await selectStaffAccess(page, member);
  await page.getByRole("combobox", { name: "قالب الصلاحيات" }).selectOption(savedRole.template.key);
  await page.getByRole("switch",{name:"عرض اللاعبين وأولياء الأمور",exact:true}).check();
  await page.getByRole("radio", { name: "فروع محددة", exact: true }).check(); await page.getByRole("group", { name: "نطاق الفروع", exact: true }).getByRole("checkbox").first().check();
  await page.getByRole("radio", { name: "رياضات محددة", exact: true }).check(); await page.getByRole("group", { name: "نطاق الرياضات", exact: true }).getByRole("checkbox").first().check();
  await page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true }).click();
  const firstApply = page.waitForResponse(r => r.url().endsWith(`/manage/staff/${member}/access`) && r.request().method() === "PUT");
  await page.getByRole("button", { name: "تأكيد الاعتماد", exact: true }).click(); expect((await firstApply).status()).toBe(204);
  await expect(page.getByRole("combobox", { name: "اختر الإداري", exact: true })).toBeVisible(); await expect(page.getByRole("heading", { name: `صلاحيات ${name}`, exact: true })).toHaveCount(0);
  const applied = await (await page.request.get(`/api/v1/manage/staff/${member}/access`)).json(); expect(applied.source.appliedRevision).toBe(1); expect(applied.source.customized).toBe(false);
  const context = await browser.newContext({ baseURL: test.info().project.use.baseURL }); const admin = await context.newPage();
  try {
    await admin.goto("/login"); await admin.getByLabel("البريد الإلكتروني").fill(email); await admin.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await admin.getByRole("button", { name: "دخول آمن" }).click(); await admin.waitForURL("**/dashboard");
    expect((await admin.request.get("/api/v1/people/players")).status()).toBe(200);
    expect((await admin.request.get("/api/v1/manage/staff/role-templates")).status()).toBe(403);
    await openRoleTemplates(page);
    await page.locator("article").filter({ hasText: roleName }).getByRole("button", { name: "عرض وتعديل" }).click();
    await expect(page.getByRole("region", { name: "معاينة تغيير القالب" })).toContainText(name);
    await page.locator("summary").filter({ hasText: "التشغيل والتدريب" }).click();
    await page.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور", exact: true }).uncheck();
    await page.getByRole("button", { name: "مراجعة وحفظ القالب" }).click(); await page.getByRole("button", { name: "تأكيد حفظ القالب" }).click();
    await expect(page.getByRole("heading", { name: roleName, exact: true })).toBeVisible();
    expect((await admin.request.get("/api/v1/people/players")).status()).toBe(200); // no implicit live mutation
    await page.getByRole("button", { name: "عودة للموظفين" }).click(); await selectStaffAccess(page, member);
    await expect(page.getByText(/تغير القالب أو أوقف؛ المنح الحالية لم تتغير/)).toBeVisible();
    await page.getByRole("combobox", { name: "قالب الصلاحيات" }).selectOption(savedRole.template.key);
    await page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true }).click();
    const secondApply = page.waitForResponse(r => r.url().endsWith(`/manage/staff/${member}/access`) && r.request().method() === "PUT");
    await page.getByRole("button", { name: "تأكيد الاعتماد", exact: true }).click(); expect((await secondApply).status()).toBe(204);
    await expect(page.getByRole("combobox", { name: "اختر الإداري", exact: true })).toBeVisible(); await expect(page.getByRole("heading", { name: `صلاحيات ${name}`, exact: true })).toHaveCount(0);
    expect((await admin.request.get("/api/v1/people/players")).status()).toBe(403); // explicit version 2 applies to same session
    const after = await (await page.request.get(`/api/v1/manage/staff/${member}/access`)).json(); expect(after.source.appliedRevision).toBe(2); expect(after.selectedSports).toEqual(applied.selectedSports); expect(after.selectedBranches).toEqual(applied.selectedBranches);
    await openRoleTemplates(page); await page.locator("article").filter({ hasText: roleName }).getByRole("button", { name: "عرض وتعديل" }).click();
    await page.getByRole("checkbox", { name: "متاح للاختيار الجديد" }).uncheck(); await page.getByRole("button", { name: "مراجعة وحفظ القالب" }).click(); await page.getByRole("button", { name: "تأكيد حفظ القالب" }).click();
    await expect(page.locator("article").filter({ hasText: roleName })).toContainText("مؤرشف");
    expect((await admin.request.get("/api/v1/attendance/sessions?allDates=true")).status()).toBe(200);
  } finally { await context.close(); }
});
