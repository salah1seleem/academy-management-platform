import { expect, type Page } from "@playwright/test";

// V4 keeps a single explicit administrator selector; templates are advanced,
// never silently applied to the ordinary action matrix.
export async function selectStaffAccess(page: Page, id: string, advanced = true) {
  await page.getByRole("combobox", { name: "اختر الإداري", exact: true }).selectOption(id);
  const toggle = page.getByRole("checkbox", { name: "عرض القوالب والصلاحيات المتقدمة" });
  if (advanced) await toggle.check();
  await expect(page.locator(".access-editor").getByRole("heading", { name: /^صلاحيات / })).toBeVisible();
  const matrix = page.locator(".access-editor details").filter({has:page.getByText("صلاحيات الوحدات والإجراءات الأخرى", { exact: true })});
  // Toggling the editor mode preserves state; explicitly open the disclosure.
  await matrix.waitFor({state:"attached"});
  if (!(await matrix.evaluate(el=>(el as HTMLDetailsElement).open))) await matrix.locator("summary").click();
}

export async function openRoleTemplates(page: Page) {
  const details = page.locator("details").filter({ has: page.getByText("إدارة متقدمة وسجل الصلاحيات", { exact: true }) });
  if (!(await details.evaluate(el => (el as HTMLDetailsElement).open))) await details.locator("summary").click();
  await details.getByRole("button", { name: "أدوات القوالب السابقة", exact: true }).click();
}
