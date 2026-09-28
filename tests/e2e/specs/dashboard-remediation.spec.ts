import { expect, test } from "@playwright/test";

test("Demo Admin uses accordion, searches, opens, edits and changes a branch status", async ({ page }) => {
  const suffix = Date.now().toString().slice(-6);
  const originalName = `فرع تجربة ${suffix}`;
  const updatedName = `${originalName} محدث`;

  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("admin.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: /الأكاديمية/ }).click();
  await expect(page.getByRole("button", { name: /الأكاديمية/ })).toHaveAttribute("aria-expanded", "true");
  await page.getByRole("link", { name: "الفروع" }).click();
  await expect(page.getByRole("heading", { name: "الفروع" })).toBeVisible();
  await page.getByRole("link", { name: "إضافة فرع" }).click();
  await page.getByLabel("الاسم بالعربية").fill(originalName);
  await page.getByLabel("العنوان المختصر").fill("عنوان تجريبي آمن");
  await page.getByRole("button", { name: "حفظ" }).click();

  await page.getByRole("textbox", { name: "بحث الفروع" }).fill(originalName);
  const row = page.locator("article").filter({ hasText: originalName });
  await expect(row).toBeVisible();
  await row.getByRole("link", { name: "عرض" }).click();
  await expect(page.getByRole("heading", { name: originalName })).toBeVisible();
  await page.getByRole("link", { name: "تعديل" }).click();
  await page.getByLabel("الاسم بالعربية").fill(updatedName);
  await page.getByRole("button", { name: "حفظ التعديلات" }).click();

  await page.getByRole("textbox", { name: "بحث الفروع" }).fill(updatedName);
  const updatedRow = page.locator("article").filter({ hasText: updatedName });
  await expect(updatedRow).toBeVisible();
  await updatedRow.getByRole("button", { name: "إيقاف" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "إيقاف" }).click();
  await expect(updatedRow).toContainText("متوقف");
  await updatedRow.getByRole("button", { name: "تفعيل" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "تفعيل" }).click();
  await expect(updatedRow).toContainText("فعال");
});
