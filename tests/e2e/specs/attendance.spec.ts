import { expect, Page, test } from "@playwright/test";

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
}

async function openTodayFootballRoster(page: Page) {
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: /الحضور/ }).click();
  await page.getByRole("link", { name: "جلسات التدريب" }).click();
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

test("Coach records own attendance and cannot open an unrelated group", async ({ page }) => {
  await staffLogin(page, "coach.nogoom@example.test");
  await expect(page.getByRole("heading", { name: "جلسات التدريب" })).toBeVisible();
  await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("link", { name: "حضور المدربين" }).click();
  const session = page.locator("article").filter({ hasText: "2026-09-28" }).filter({ hasText: "ناشئين 2016 — مجموعة أ" }); await session.getByRole("link", { name: "تسجيل حضور المدربين" }).click();
  await page.getByRole("button", { name: "حاضر" }).click(); await page.getByRole("button", { name: "حفظ حضور المدربين" }).click(); await expect(page.getByText("تم حفظ حضور الجهاز الفني.")).toBeVisible();
  await page.goto("/dashboard/attendance/sessions/62000000-0000-0000-0000-000000000005/players"); await expect(page.getByText("تعذر تحميل كشف اللاعبين.")).toBeVisible();
});

test("Guardian sees attendance for linked child only", async ({ page }) => {
  await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click(); await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click(); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click();
  const child = page.locator("article").filter({ hasText: "عمر أحمد محمود" }); await expect(child.getByRole("heading", { name: "آخر الحضور" })).toBeVisible(); await expect(child).toContainText("حاضر"); await expect(page.getByText("عمر أحمد حسن")).toHaveCount(0);
});
