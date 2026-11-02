import { expect, type Page } from "@playwright/test";

export async function prepareImportUpload(page: Page) {
  const file = page.getByLabel("ملف بيانات الأكاديمية", { exact: true });
  const next = page.getByRole("button", { name: "استيراد ملف جديد", exact: true });
  await expect(file.or(next)).toBeVisible();
  if (await next.isVisible()) await next.click();
  await expect(file).toBeVisible();
}
