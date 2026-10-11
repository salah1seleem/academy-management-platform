import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
}

async function openTodayFootballRoster(page: Page) {
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: /الحضور/ }).click();
  await page.getByRole("navigation", { name: "مساحة الإدارة" }).getByRole("link", { name: "حضور اللاعبين" }).click();
  const session = page.locator("article").filter({ hasText: "2026-09-28" }).filter({ hasText: "ناشئين 2016 — مجموعة أ" });
  await session.getByRole("link", { name: "تسجيل الحضور" }).click();
  await expect(page.getByRole("heading", { name: "حضور اللاعبين" })).toBeVisible();
}

test("Admin records player attendance and session balance is consumed once", async ({ page }) => {
  await staffLogin(page); await openTodayFootballRoster(page);
  const player = page.locator("article").filter({ hasText: "عمر أحمد حسن" });
  const save = page.getByRole("button", { name: "حفظ الحضور" });
  await player.getByRole("button", { name: "غائب" }).click(); await expect(player.getByRole("button", { name: "غائب" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(page.getByText("تم حفظ الحضور بنجاح.")).toBeVisible(); await expect(save).toBeEnabled();
  await expect(player).toContainText("الرصيد: 5");
  await player.getByRole("button", { name: "حاضر" }).click(); await expect(player.getByRole("button", { name: "حاضر" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(save).toBeEnabled();
  await expect(player).toContainText("الرصيد: 4");
  await page.reload(); const reloaded = page.locator("article").filter({ hasText: "عمر أحمد حسن" }); await reloaded.getByRole("button", { name: "حاضر" }).click(); await expect(reloaded.getByRole("button", { name: "حاضر" })).toHaveAttribute("aria-pressed", "true"); const reloadSave = page.getByRole("button", { name: "حفظ الحضور" }); await reloadSave.click(); await expect(reloadSave).toBeEnabled(); await expect(reloaded).toContainText("الرصيد: 4");
});

test("Admin correction restores the consumed session", async ({ page }) => {
  await staffLogin(page); await openTodayFootballRoster(page); const player = page.locator("article").filter({ hasText: "عمر أحمد حسن" });
  const save = page.getByRole("button", { name: "حفظ الحضور" });
  await player.getByRole("button", { name: "حاضر" }).click(); await expect(player.getByRole("button", { name: "حاضر" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(page.getByText("تم حفظ الحضور بنجاح.")).toBeVisible(); await expect(save).toBeEnabled();
  await player.getByRole("button", { name: "غائب" }).click(); await expect(player.getByRole("button", { name: "غائب" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(save).toBeEnabled();
  await expect(player).toContainText("الرصيد: 5"); await expect(player.getByRole("button", { name: "غائب" })).toHaveAttribute("aria-pressed", "true");
});

test("Coach cannot sign in to the web dashboard", async ({ page }) => {
  await staffLogin(page, "coach.nogoom@example.test");
  await expect(page.locator("main").getByRole("alert")).toContainText("بيانات الدخول غير صحيحة");
  await expect(page).toHaveURL(/\/login$/);
});

test("Guardian sees attendance for linked child only", async ({ page }) => {
  await guardianLogin(page);
  const child = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" }); await child.getByRole("link", { name: "عرض الملف" }).click(); await expect(page.getByRole("heading", { name: "الحضور" })).toBeVisible(); await expect(page.getByText("حاضر", { exact: true }).first()).toBeVisible(); await expect(page.getByText("عمر أحمد حسن")).toHaveCount(0);
});
