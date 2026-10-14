import { expect, test } from "@playwright/test";

test("Owner applies a scoped template, reviews the diff and reopens the saved grants", async ({ page }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const stamp = Date.now(); const name = `إداري قالب اصطناعي ${stamp}`;
  const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const created = await page.request.post("/api/v1/manage/staff/", { headers: { "X-CSRF-TOKEN": token }, data: { name, email: `v3.template.${stamp}@example.test`, phone: `011${String(stamp).slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "تجربة قالب فقط" } });
  expect(created.status()).toBe(200); const member = (await created.json()).id;
  await page.goto("/dashboard/staff");
  await page.locator("article").filter({ hasText: name }).getByRole("button", { name: "الصلاحيات", exact: true }).click();
  await page.getByRole("combobox", { name: "قالب الصلاحيات" }).selectOption("branch-operator");
  await expect(page.getByRole("radio", { name: "فروع محددة" })).toBeChecked();
  await expect(page.getByRole("button", { name: "اعتماد الصلاحيات", exact: true })).toBeDisabled();
  await page.locator(".access-branch-options input").first().check();
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
  await page.locator("article").filter({ hasText: name }).getByRole("button", { name: "الصلاحيات", exact: true }).click();
  await expect(page.getByRole("switch", { name: "إضافة الحضور", exact: true })).toBeChecked();
  await expect(page.getByRole("switch", { name: "قراءة الموظفون والرواتب", exact: true })).not.toBeChecked();
  const saved = await (await page.request.get(`/api/v1/manage/staff/${member}/access`)).json();
  expect(saved.allBranches).toBe(false); expect(saved.selectedBranches).toHaveLength(1);
  expect(JSON.parse(saved.history[0].after).TemplateKey).toBe("branch-operator");
});
