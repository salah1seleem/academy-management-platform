import { expect, test } from "@playwright/test";

for (const width of [360, 390, 430, 768, 1440]) {
  test(`V2 staff workspaces preserve routes and record context at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/login");
    await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
    await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await page.getByRole("button", { name: "دخول آمن" }).click();
    await page.waitForURL("**/dashboard");
    const menu = page.getByRole("button", { name: "فتح القائمة" });
    if (await menu.isVisible()) await menu.click();
    const nav = page.getByRole("navigation", { name: "مساحة الإدارة" });
    await expect(nav.getByRole("link", { name: "الأعضاء والاشتراكات", exact: true })).toBeVisible();
    await expect(nav.getByRole("button", { name: "التشغيل اليومي", exact: true })).toBeVisible();
    await expect(nav.getByRole("button", { name: "الإعدادات والمساعدة", exact: true })).toBeVisible();
    await nav.getByRole("link", { name: "الأعضاء والاشتراكات", exact: true }).click();
    await expect(page.getByRole("heading", { name: "قائمة اللاعبين", exact: true })).toBeVisible();
    await expect(page.getByLabel("صفوف الصفحة")).toHaveValue("25");
    const player = page.getByRole("link", { name: "عرض", exact: true }).first();
    await player.click();
    const tabs = page.getByRole("tablist", { name: "أقسام ملف اللاعب" });
    await expect(tabs.getByRole("tab")).toHaveCount(6);
    await expect(tabs.getByRole("tab", { name: "المباريات", exact: true })).toBeVisible();
    await tabs.getByRole("tab", { name: "الاشتراكات والمالية" }).click();
    await expect(page).toHaveURL(/tab=subscriptions/);
    await expect(page.getByRole("tabpanel")).toContainText("التسجيلات والاشتراكات");
    await page.reload();
    await expect(tabs.getByRole("tab", { name: "الاشتراكات والمالية" })).toHaveAttribute("aria-selected", "true");
    await tabs.getByRole("tab", { name: "التدريب والحضور" }).click();
    await expect(page).toHaveURL(/tab=training/);
    await page.goto("/dashboard/academy/groups/34000000-0000-0000-0000-000000000001?tab=coaches");
    await expect(page.getByRole("tabpanel")).toContainText("مدربو المجموعة");
    await expect(page.getByRole("tabpanel").getByRole("link", { name: "ملف المدرب" }).first()).toBeVisible();
    await page.getByRole("tab", { name: "الأداء", exact: true }).click();
    await expect(page.getByRole("link", { name: "تقييمات لاعبي المجموعة" })).toHaveAttribute("href", /groupId=34000000/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
    await page.screenshot({ path: info.outputPath(`v2-group-${width}.png`), fullPage: true });
  });
}
