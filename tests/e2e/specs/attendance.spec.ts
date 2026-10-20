import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { todaySession } from "../support/attendance";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  if (email !== "coach.nogoom@example.test") await page.waitForURL("**/dashboard");
}

async function openTodayFootballRoster(page: Page) {
  const session = await todaySession(page, "5a000000-0000-0000-0000-000000000130", "04:00", "04:15");
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "عمليات الأكاديمية", exact: true }).click();
  await page.getByRole("navigation", { name: "مساحة الإدارة" }).getByRole("link", { name: "تسجيل حضور اللاعبين", exact: true }).click();
  await page.locator(`a[href='/dashboard/attendance/sessions/${session}/players']`).click();
  await expect(page.getByRole("heading", { name: "حضور اللاعبين" })).toBeVisible();
  return `/api/v1/attendance/sessions/${session}/players`;
}

test("Admin records player attendance and session balance is consumed once", async ({ page }) => {
  await staffLogin(page); const path = await openTodayFootballRoster(page);
  const player = page.getByRole("group", { name: "حضور لاعب حالات حضور اصطناعي 110", exact: true });
  const save = page.getByRole("button", { name: "حفظ الحضور" });
  await player.getByRole("button", { name: "غائب" }).click(); await expect(player.getByRole("button", { name: "غائب" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(page.getByText(/تم حفظ الحضور بنجاح\.|لا توجد تغييرات جديدة للحفظ\./)).toBeVisible(); await expect(save).toBeEnabled();
  const baseline = (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === "5a000000-0000-0000-0000-000000000111").remainingSessions;
  const card = page.locator(".attendance-roster article").filter({ has: player });
  await expect(card).toContainText(`الرصيد: ${baseline}`);
  await player.getByRole("button", { name: "حاضر" }).click(); await expect(player.getByRole("button", { name: "حاضر" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(save).toBeEnabled();
  await expect(card).toContainText(`الرصيد: ${baseline - 1}`);
  await page.reload(); const reloaded = page.getByRole("group", { name: "حضور لاعب حالات حضور اصطناعي 110", exact: true }); await reloaded.getByRole("button", { name: "حاضر", exact: true }).click(); await expect(reloaded.getByRole("button", { name: "حاضر", exact: true })).toHaveAttribute("aria-pressed", "true"); const reloadSave = page.getByRole("button", { name: "حفظ الحضور" }); await reloadSave.click(); await expect(reloadSave).toBeEnabled(); await expect(page.locator(".attendance-roster article").filter({ has: reloaded })).toContainText(`الرصيد: ${baseline - 1}`);
});

test("Admin correction restores the consumed session", async ({ page }) => {
  await staffLogin(page); const path = await openTodayFootballRoster(page); const player = page.getByRole("group", { name: "حضور لاعب حالات حضور اصطناعي 110", exact: true });
  const save = page.getByRole("button", { name: "حفظ الحضور" });
  await player.getByRole("button", { name: "حاضر" }).click(); await expect(player.getByRole("button", { name: "حاضر" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(page.getByText(/تم حفظ الحضور بنجاح\.|لا توجد تغييرات جديدة للحفظ\./)).toBeVisible(); await expect(save).toBeEnabled();
  const consumed = (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === "5a000000-0000-0000-0000-000000000111").remainingSessions;
  await player.getByRole("button", { name: "غائب" }).click(); await expect(player.getByRole("button", { name: "غائب" })).toHaveAttribute("aria-pressed", "true"); await save.click(); await expect(save).toBeEnabled();
  await expect(page.locator(".attendance-roster article").filter({ has: player })).toContainText(`الرصيد: ${consumed + 1}`); await expect(player.getByRole("button", { name: "غائب" })).toHaveAttribute("aria-pressed", "true");
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
