import { expect, test } from "@playwright/test";
import { selectStaffAccess } from "../support/staff-access";

test("Owner applies a branch/sport scoped template and Admin scope stays live on revocation", async ({ page, browser }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const stamp = Date.now(); const name = `إداري قالب اصطناعي ${stamp}`;
  const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const created = await page.request.post("/api/v1/manage/staff/", { headers: { "X-CSRF-TOKEN": token }, data: { name, email: `v3.template.${stamp}@example.test`, phone: `011${String(stamp).slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "تجربة قالب فقط" } });
  expect(created.status()).toBe(200); const member = (await created.json()).id;
  await page.goto("/dashboard/staff");
  await selectStaffAccess(page, member);
  await page.getByRole("combobox", { name: "قالب الصلاحيات" }).selectOption("branch-operator");
  await expect(page.getByRole("radio", { name: "فروع محددة" })).toBeChecked();
  await expect(page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true })).toBeDisabled();
  await page.getByRole("group",{name:"نطاق الفروع",exact:true}).getByRole("checkbox").first().check();
  await page.getByRole("radio", { name: "رياضات محددة", exact: true }).check();
  await expect(page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true })).toBeDisabled();
  const sportOptions = page.getByRole("group", { name: "نطاق الرياضات", exact: true }).getByRole("checkbox");
  await sportOptions.first().check();
  await expect(page.getByRole("switch", { name: "قراءة الموظفون والرواتب", exact: true })).not.toBeChecked();
  await expect(page.getByRole("switch", { name: "حذف اللاعبون وأولياء الأمور", exact: true })).not.toBeChecked();
  await page.getByText(/التغييرات المقترحة/).click();
  await expect(page.getByRole("region", { name: "معاينة أثر الصلاحيات" })).toContainText("منح إضافة الحضور");
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`staff-template-${width}.png`), fullPage: true, animations: "disabled" });
  }
  await page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true }).click();
  await page.getByRole("button", { name: "تأكيد الاعتماد", exact: true }).click();
  await expect(page.locator(".access-editor")).toHaveCount(0);
  await selectStaffAccess(page, member);
  await expect(page.getByRole("switch", { name: "إضافة الحضور", exact: true })).toBeChecked();
  await expect(page.getByRole("switch", { name: "قراءة الموظفون والرواتب", exact: true })).not.toBeChecked();
  const saved = await (await page.request.get(`/api/v1/manage/staff/${member}/access`)).json();
  expect(saved.allBranches).toBe(false); expect(saved.selectedBranches).toHaveLength(1);
  expect(saved.allSports).toBe(false); expect(saved.selectedSports).toHaveLength(1);
  expect(JSON.parse(saved.history[0].after).TemplateKey).toBe("branch-operator");
  const context = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const admin = await context.newPage();
  try {
    await admin.goto("/login");
    await admin.getByLabel("البريد الإلكتروني").fill(`v3.template.${stamp}@example.test`);
    await admin.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await admin.getByRole("button", { name: "دخول آمن" }).click(); await admin.waitForURL("**/dashboard");
    const options = await (await admin.request.get("/api/v1/manage/structure/options")).json();
    expect(options.sports.map((s: { id: string }) => s.id)).toEqual(saved.selectedSports);
    expect(options.branches.map((b: { id: string }) => b.id)).toEqual(saved.selectedBranches);
    expect((await admin.request.get("/api/v1/reports/owner-summary")).status()).toBe(403);
    const otherSport = saved.sports.find((s: { id: string }) => s.id !== saved.selectedSports[0]);
    expect(otherSport).toBeTruthy();
    const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
    const changed = await page.request.put(`/api/v1/manage/staff/${member}/access`, { headers: { "X-CSRF-TOKEN": token }, data: { allBranches: false, branches: saved.selectedBranches, allSports: false, sports: [otherSport.id], actions: saved.actions } });
    expect(changed.status()).toBe(204);
    const after = await (await admin.request.get("/api/v1/manage/structure/options")).json();
    expect(after.sports.map((s: { id: string }) => s.id)).toEqual([otherSport.id]);
    expect(after.sports.map((s: { id: string }) => s.id)).not.toContain(saved.selectedSports[0]);
  } finally { await context.close(); }
});
