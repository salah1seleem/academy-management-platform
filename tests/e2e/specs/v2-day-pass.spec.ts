import { test, expect } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Guardian sees a linked-child paid day pass without training attendance", async ({ page }, info) => {
  await guardianLogin(page);
  await page.goto("/guardian/day-pass");
  await expect(page.getByRole("heading", { name: "الزيارة اليومية" })).toBeVisible();
  const options = await page.request.get("/api/v1/guardian/day-pass/options");
  expect(options.ok()).toBe(true);
  const data = await options.json();
  expect(data.bookings.length).toBeGreaterThan(0);
  expect(data.children.length).toBeGreaterThan(0);
  await page.getByRole("combobox", { name: "الطفل" }).selectOption({ index: 1 });
  await page.getByRole("combobox", { name: "الموعد" }).selectOption({ index: 1 });
  await page.getByRole("combobox", { name: "باقة الزيارة" }).selectOption({ index: 1 });
  await page.getByRole("button", { name: "مراجعة السعر والموعد" }).click();
  await expect(page.getByText("الدفع تحويل يدوي؛ لا نؤكد استلام المبلغ بمجرد رفع صورة.")).toBeVisible();
  await page.setViewportSize({ width: 390, height: 900 });
  await page.screenshot({ path: info.outputPath("day-pass-390.png"), fullPage: true, animations: "disabled" });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});
