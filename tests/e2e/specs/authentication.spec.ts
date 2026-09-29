import { expect, test } from "@playwright/test";

test("Arabic mobile owner login is tenant-scoped and logout invalidates the session", async ({ page }) => {
  await page.goto("/login");
  await expect(page.locator("html")).toHaveAttribute("lang", "ar");
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.getByRole("heading", { name: "تسجيل الدخول" })).toBeVisible();

  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();

  await expect(page.getByRole("heading", { name: "لوحة المالك", level: 1 })).toBeVisible();

  const forbidden = await page.evaluate(async () => {
    const csrf = await fetch("/api/v1/auth/csrf").then((response) => response.json()) as { token: string };
    return fetch("/api/v1/session/academy", {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf.token },
      body: JSON.stringify({ academyId: "20000000-0000-0000-0000-000000000002" }),
    }).then((response) => response.status);
  });
  expect(forbidden).toBe(403);

  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "تسجيل الخروج" }).click();
  await expect(page.getByRole("heading", { name: "تسجيل الدخول" })).toBeVisible();
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "تسجيل الدخول" })).toBeVisible();
});
