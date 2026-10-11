import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { resolve } from "node:path";

async function login(page: Page, role: "admin" | "owner") {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(`${role}.nogoom@example.test`);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
}

test("Admin has no financial dashboard or report without an explicit grant", async ({ page }) => {
  await login(page, "admin");
  await expect(page.getByRole("heading", { name: "الرئيسية" })).toBeVisible();
  await expect(page.getByText("تحصيل اليوم", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("navigation", { name: "الإجراءات التشغيلية السريعة" }).getByRole("link", { name: "التقرير المالي" })).toHaveCount(0);
  expect((await page.request.get("/api/v1/reports/financial")).status()).toBe(403);
  expect((await page.request.get("/api/v1/reports/owner-summary")).status()).toBe(403);
});

test("Owner payment-priority card opens the matching request type with month-to-date filters", async ({ page }) => {
  await login(page, "owner");
  await page.locator(".requests-card").getByRole("link", { name: /منتجات/ }).click();
  await expect(page.getByRole("heading", { name: "طلبات الدفع" })).toBeVisible();
  await expect(page.getByLabel("نوع الطلب")).toHaveValue("Product");
  await expect(page.getByLabel("من تاريخ")).toHaveValue("2026-09-01");
  await expect(page.getByLabel("إلى تاريخ")).toHaveValue("2026-09-28");
  await expect(page.locator("main").getByRole("alert")).toHaveCount(0);
});

test("A real news image stays private in draft, then becomes visible to a tenant guardian on publish", async ({ page }) => {
  await login(page, "owner");
  await page.goto("/dashboard/news");
  const title = `خبر اختبار مراجعة ${Date.now()}`;
  await page.getByLabel("عنوان الخبر").fill(title);
  await page.getByLabel("تفاصيل الخبر").fill("خبر مصطنع لا يتضمن بيانات شخصية.");
  await page.getByLabel("صورة الخبر").setInputFiles(resolve(__dirname, "../../../apps/mobile/assets/demo/nutrition/breakfast-1.jpg"));
  await page.getByRole("button", { name: "حفظ كمسودة" }).click();
  const item = page.locator("article").filter({ hasText: title });
  await expect(item).toContainText("مسودة");
  const draft = (await page.request.get("/api/v1/content/news/").then(r => r.json())).find((x: { title: string }) => x.title === title);
  await guardianLogin(page);
  expect(await page.request.get(draft.imageUrl).then(r => r.status())).toBe(404);
  expect(await page.request.get("/api/v1/news").then(r => r.text())).not.toContain(title);
  await page.context().clearCookies(); await login(page, "owner"); await page.goto("/dashboard/news");
  await page.locator("article").filter({ hasText: title }).getByRole("button", { name: "تعديل" }).click();
  await page.getByRole("button", { name: "نشر", exact: true }).click();
  await expect(page.locator("article").filter({ hasText: title })).toContainText("منشور");
  await guardianLogin(page);
  expect(await page.request.get("/api/v1/news").then(r => r.text())).toContain(title);
  const image = await page.request.get(draft.imageUrl);
  expect(image.status()).toBe(200);
  expect(image.headers()["cache-control"]).toContain("no-store");
});
