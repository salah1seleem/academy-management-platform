import { expect, test } from "@playwright/test";

const sizes = [{ width: 1440, height: 900 }, { width: 1280, height: 800 }, { width: 390, height: 844 }];
const journeys = {
  owner: ["/dashboard", "/dashboard/players", "/dashboard/subscriptions/current", "/dashboard/subscriptions/plans", "/dashboard/reports/financial"],
  admin: ["/dashboard/players", "/dashboard/academy/groups", "/dashboard/attendance/sessions", "/dashboard/evaluations", "/dashboard/content/catalog"],
  coach: ["/dashboard/attendance/sessions", "/dashboard/evaluations"],
};
for (const role of ["owner", "admin", "coach"] as const) for (const size of sizes) {
  test(`sports dashboard ${role} ${size.width}x${size.height}`, async ({ page }, info) => {
    test.setTimeout(90_000);
    await page.setViewportSize(size);
    const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
    await page.goto("/login");
    await page.getByLabel("البريد الإلكتروني").fill(`${role}.nogoom@example.test`);
    await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await page.getByRole("button", { name: "دخول آمن" }).click();
    await expect(page).toHaveURL(/dashboard/);
    for (const path of journeys[role]) {
      await page.goto(path); await expect(page.locator("main h1")).toBeVisible();
      await page.waitForLoadState("networkidle");
      await expect(page.locator("main [role=alert]")).toHaveCount(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
      const image = info.outputPath(`${role}-${size.width}-${path.split("/").filter(Boolean).join("-")}.png`);
      await page.screenshot({ path: image, scale: "css", animations: "disabled" });
      await info.attach(path, { path: image, contentType: "image/png" });
      if (path === "/dashboard") {
        const widgets = info.outputPath(`${role}-${size.width}-widgets.png`);
        await page.locator(".owner-business-grid").screenshot({ path: widgets, scale: "css", animations: "disabled" });
        await info.attach("real-data widgets", { path: widgets, contentType: "image/png" });
      }
    }
    if (role === "coach" || role === "admin") {
      await page.goto("/dashboard/attendance/sessions");
      await page.getByRole("link", { name: "تسجيل الحضور", exact: true }).first().click();
      await expect(page.getByRole("heading", { name: "حضور اللاعبين" })).toBeVisible();
      await page.waitForLoadState("networkidle");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
      const image = info.outputPath(`${role}-${size.width}-roster.png`);
      await page.screenshot({ path: image, scale: "css", animations: "disabled" });
      await info.attach("roster", { path: image, contentType: "image/png" });
    }
    if (role === "coach") {
      await page.goto("/dashboard/attendance/sessions"); await page.waitForLoadState("networkidle");
      await expect(page.getByRole("button", { name: "إنشاء جلسة", exact: true })).toHaveCount(0);
      if (size.width < 1024) await page.getByRole("button", { name: "فتح القائمة" }).click();
      const nav = page.getByRole("navigation", { name: "مساحة المدرب" });
      await expect(nav).toBeVisible();
      await expect(nav.getByRole("button", { name: /الاشتراكات|المحتوى|الأكاديمية/ })).toHaveCount(0);
      await nav.getByRole("button", { name: "التقارير", exact: true }).click();
      await expect(nav.getByRole("link", { name: "تقارير الحضور" })).toBeVisible();
      await expect(nav.getByRole("link", { name: /التقرير المالي|الإيصالات/ })).toHaveCount(0);
    }
    await page.emulateMedia({ reducedMotion: "reduce" });
    expect(await page.locator(".dashboard-content > header").evaluate(element => Number.parseFloat(getComputedStyle(element).animationDuration))).toBeLessThanOrEqual(.001);
    expect(errors).toEqual([]);
  });
}
