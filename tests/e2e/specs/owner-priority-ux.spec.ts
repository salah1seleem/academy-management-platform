import { expect, test } from "@playwright/test";

test("Owner opens stored operational signals and private family feedback", async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  await expect(page.getByRole("heading", { name: "مركز القرارات الذكية" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /^المدربون ·/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: "تقرير الأرباح والخسائر" })).toBeVisible();
  await page.getByRole("link", { name: "كل القرارات", exact: true }).click();
  await expect(page.getByRole("heading", { name: "المتابعات واتخاذ القرار" })).toBeVisible();
  await expect(page.getByRole("link",{name:"إنشاء متابعة",exact:true})).toBeVisible();
  await page.screenshot({ path: info.outputPath("owner-operations.png"), fullPage: true, animations: "disabled" });
  // The compact Home no longer duplicates the assignment form; its canonical
  // contextual route must still preserve this exact group, not the whole list.
  await page.goto("/dashboard/academy/coaches/new?groupId=34000000-0000-0000-0000-000000000003");
  await expect(page).toHaveURL(/\/dashboard\/academy\/coaches\/new\?groupId=/);
  await expect(page.getByRole("combobox", { name: "المجموعة" }).locator("option")).toHaveCount(1);
  await expect(page.getByRole("combobox", { name: "المجموعة" })).toHaveValue("34000000-0000-0000-0000-000000000003");
  await page.goto("/dashboard");
  await page.getByRole("link",{name:"كل المدربين",exact:true}).click();
  await expect(page.getByRole("heading",{name:"المدربون",exact:true})).toBeVisible();
  const menu=page.getByRole("button",{name:"فتح القائمة"}); if(await menu.isVisible())await menu.click();
  await page.getByRole("navigation",{name:"مساحة الإدارة"}).getByRole("link",{name:"الشكاوى والاقتراحات",exact:true}).click();
  await expect(page.getByRole("heading", { name: "ملاحظات أولياء الأمور" })).toBeVisible();
  await page.getByRole("combobox", { name: "النوع" }).selectOption("Rating");
  await expect(page.getByRole("combobox", { name: "النوع" })).toHaveValue("Rating");
});
