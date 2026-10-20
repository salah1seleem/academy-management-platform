import { expect, test } from "@playwright/test";

const sizes = [{ width: 1440, height: 900 }, { width: 1280, height: 800 }, { width: 390, height: 844 }];
const journeys = {
  owner: ["/dashboard", "/dashboard/players", "/dashboard/subscriptions/current", "/dashboard/subscriptions/plans", "/dashboard/reports/financial"],
  admin: ["/dashboard/players", "/dashboard/academy/groups", "/dashboard/attendance/sessions", "/dashboard/evaluations", "/dashboard/content/catalog"],
};
for (const role of ["owner", "admin"] as const) for (const size of sizes) {
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
      if (path === "/dashboard" && role === "owner") {
        // Comparing scrollWidth with an already-inflated mobile innerWidth
        // misses RTL min-content overflow and displaced tap coordinates.
        expect(await page.evaluate(()=>innerWidth)).toBeLessThanOrEqual(size.width+1);
        const widgets = info.outputPath(`${role}-${size.width}-widgets.png`);
        await page.getByRole("region", { name: "مؤشرات الأكاديمية", exact: true }).screenshot({ path: widgets, scale: "css", animations: "disabled" });
        await info.attach("real-data widgets", { path: widgets, contentType: "image/png" });
      }
    }
    if (role === "admin") {
      await page.goto("/dashboard/attendance/sessions");
      await page.getByRole("link", { name: "تسجيل الحضور", exact: true }).first().click();
      await expect(page.getByRole("heading", { name: "حضور اللاعبين" })).toBeVisible();
      await page.waitForLoadState("networkidle");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
      const image = info.outputPath(`${role}-${size.width}-roster.png`);
      await page.screenshot({ path: image, scale: "css", animations: "disabled" });
      await info.attach("roster", { path: image, contentType: "image/png" });
    }
    await page.emulateMedia({ reducedMotion: "reduce" });
    expect(await page.locator("main header").first().evaluate(element => Number.parseFloat(getComputedStyle(element).animationDuration))).toBeLessThanOrEqual(.001);
    expect(errors).toEqual([]);
  });
}
