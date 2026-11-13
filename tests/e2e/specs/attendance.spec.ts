import { expect, Page, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";
import { todaySession } from "../support/attendance";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول" }).click();
  if (email !== "coach.nogoom@example.test") await page.waitForURL("**/dashboard");
}

async function openTodayFootballRoster(page: Page, start = "04:00", end = "04:15") {
  const session = await todaySession(page, "5a000000-0000-0000-0000-000000000130", start, end);
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "عمليات الأكاديمية", exact: true }).click();
  await page.getByRole("navigation", { name: "مساحة الإدارة" }).getByRole("link", { name: "تسجيل حضور اللاعبين", exact: true }).click();
  await page.locator(`a[href^='/dashboard/attendance/sessions/${session}/players?']`).click();
  await expect(page.getByRole("heading", { name: "حضور اللاعبين" })).toBeVisible();
  return `/api/v1/attendance/sessions/${session}/players`;
}

async function saveAttendance(page: Page, state: "Present" | "Absent") {
  const player = page.getByRole("combobox", { name: "حضور لاعب حالات حضور اصطناعي 110", exact: true });
  const save = page.getByRole("button", { name: "حفظ الحضور" });
  await expect(player).toBeVisible();
  if (await player.inputValue() !== state) {
    await player.selectOption(state);
    await expect(save).toBeEnabled();
    await save.click();
    await expect(page.getByText("تم حفظ الحضور بنجاح.", { exact: true })).toBeVisible();
  }
  await expect(player).toHaveValue(state);
  await expect(save).toBeDisabled();
  return player;
}

test("Admin records player attendance and session balance is consumed once", async ({ page }) => {
  await staffLogin(page); const path = await openTodayFootballRoster(page);
  const player = await saveAttendance(page, "Absent");
  const baseline = (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === "5a000000-0000-0000-0000-000000000111").remainingSessions;
  const card = page.getByRole("row").filter({ has: player }).locator('[data-label="رصيد الحصص"]');
  await expect(card).toHaveText(`${baseline}`);
  await saveAttendance(page, "Present");
  await expect(card).toHaveText(`${baseline - 1}`);
  await page.reload();
  await expect(player).toHaveValue("Present");
  await expect(page.getByRole("button", { name: "حفظ الحضور" })).toBeDisabled();
  await expect(card).toHaveText(`${baseline - 1}`);
});

test("Admin correction restores the consumed session", async ({ page }) => {
  await staffLogin(page); const path = await openTodayFootballRoster(page);
  const player = await saveAttendance(page, "Present");
  const consumed = (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === "5a000000-0000-0000-0000-000000000111").remainingSessions;
  await saveAttendance(page, "Absent");
  await expect(page.getByRole("row").filter({ has: player }).locator('[data-label="رصيد الحصص"]')).toHaveText(`${consumed + 1}`); await expect(player).toHaveValue("Absent");
});

test("Coach cannot sign in to the web dashboard", async ({ page }) => {
  await staffLogin(page, "coach.nogoom@example.test");
  await expect(page.locator("main").getByRole("alert")).toContainText("لوحة الأكاديمية متاحة للمالك والإداري.");
  await expect(page).toHaveURL(/\/login$/);
});

test("Guardian sees attendance for linked child only", async ({ page }) => {
  await guardianLogin(page);
  const child = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" }); await child.getByRole("link", { name: "ملف عمر والتقرير", exact: true }).click(); await expect(page.getByRole("heading", { name: "الحضور" })).toBeVisible(); await expect(page.getByText("حاضر", { exact: true }).first()).toBeVisible(); await expect(page.getByText("عمر أحمد حسن")).toHaveCount(0);
});

test("Attendance list preserves its filters when returning from a roster", async ({ page }) => {
  await staffLogin(page);
  const group = "5a000000-0000-0000-0000-000000000130";
  const session = await todaySession(page, group, "04:00", "04:15");
  const data = await page.request.get(`/api/v1/attendance/sessions/${session}/players`).then(response => response.json());
  const date = data.session.sessionDate;
  const query = new URLSearchParams({ groupId: group, from: date, to: date });
  await page.goto(`/dashboard/attendance/sessions?${query}`);
  await page.getByRole("textbox", { name: "البحث بالاسم" }).fill(data.session.group);
  await page.locator(`a[href^='/dashboard/attendance/sessions/${session}/players?']`).click();
  await expect(page.getByRole("heading", { name: "حضور اللاعبين" })).toBeVisible();
  await page.getByRole("link", { name: "العودة إلى الحصص" }).click();
  await expect(page.getByRole("textbox", { name: "البحث بالاسم" })).toHaveValue(data.session.group);
  await expect(page.getByLabel("فلتر المجموعة")).toHaveValue(group);
  await expect(page.getByLabel("من تاريخ")).toHaveValue(date);
  await expect(page.locator(`a[href^='/dashboard/attendance/sessions/${session}/players?']`)).toBeVisible();
});
