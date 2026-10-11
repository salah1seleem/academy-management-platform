import { expect, test } from "@playwright/test";

test("tenant XLSX template and private preview errors are usable at mobile and desktop", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard"); await page.goto("/dashboard/import");
  const template = await page.request.get("/api/v1/people/import/template"); expect(template.status()).toBe(200); expect(template.headers()["content-type"]).toContain("spreadsheetml"); const bytes = await template.body(); expect(bytes.subarray(0, 2).toString()).toBe("PK");
  await page.getByLabel("ملف بيانات الأكاديمية").setInputFiles({ name: "tenant-template.xlsx", mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", buffer: bytes });
  await expect(page.getByRole("heading", { name: "الملف يحتاج تصحيحًا" })).toBeVisible(); await expect(page.getByText(/حدد كود المصدر بحروف لاتينية/)).toBeVisible(); await expect(page.getByText(/الملف يتطلب من 1 إلى 1000/)).toBeVisible();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.evaluate(() => window.scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`import-preview-${width}.png`), animations: "disabled" }); }
});
