import { expect, test } from "@playwright/test";

test("loads the Arabic RTL shell and API liveness endpoint", async ({ page, request }) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "منصة إدارة الأكاديمية" })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "ar");
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.getByRole("note")).toContainText("لا يوجد تسجيل دخول");

  const liveResponse = await request.get("http://127.0.0.1:5080/health/live");
  expect(liveResponse.ok()).toBeTruthy();
});
