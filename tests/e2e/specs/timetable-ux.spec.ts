import { expect, test } from "@playwright/test";

for (const journey of [
  { title: "desktop Saturday/Monday/Wednesday", width: 1440, days: ["السبت", "الاثنين", "الأربعاء"], values: ["Saturday", "Monday", "Wednesday"], numeric: [6, 1, 3] },
  { title: "mobile Sunday/Tuesday/Thursday", width: 393, days: ["الأحد", "الثلاثاء", "الخميس"], values: ["Sunday", "Tuesday", "Thursday"], numeric: [0, 2, 4] },
]) {
  test(`Admin timetable: ${journey.title}, exact persistence, time and list handoff`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: journey.width, height: 900 });
    await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("admin.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
    const options = await page.request.get("/api/v1/manage/structure/options").then(r => r.json());
    const original = options.groups[0];
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const groupResponse = await page.request.post("/api/v1/manage/groups", { headers: { "X-CSRF-TOKEN": csrf.token }, data: { arabicName: `مجموعة اختبار الواجهة ${journey.width} ${Date.now()}`, branchId: original.branchId, sportId: original.sportId, ageCategoryId: original.ageCategoryId } });
    expect(groupResponse.ok()).toBeTruthy(); const id = await groupResponse.json() as string;
    await page.goto(`/dashboard/academy/groups/${id}?tab=schedule`); await page.getByText("إنشاء الحصص / تعديل الجدول", { exact: true }).click();
    await expect(page.getByRole("checkbox").first()).toHaveAccessibleName("السبت");
    for (const day of journey.days) { await page.getByRole("checkbox", { name: day, exact: true }).click(); await expect(page.getByRole("checkbox", { name: day, exact: true })).toBeChecked(); }
    const dayTiles = await page.locator(".weekday-choice").evaluateAll(labels => labels.map(label => ({ height: label.getBoundingClientRect().height, color: getComputedStyle(label).color, background: getComputedStyle(label).backgroundColor, selected: label.classList.contains("selected"), checkboxOpacity: getComputedStyle(label.querySelector("input")!).opacity })));
    expect(dayTiles.every(tile => tile.height >= 40 && tile.height <= 60 && tile.checkboxOpacity === "0")).toBeTruthy();
    expect(dayTiles.filter(tile => tile.selected).every(tile => tile.color === "rgb(255, 255, 255)" && tile.background !== dayTiles.find(other => !other.selected)!.background)).toBeTruthy();
    expect(await page.locator(".weekday-choice.selected input").evaluateAll(inputs => inputs.map(x => (x as HTMLInputElement).value))).toEqual(journey.numeric.sort((a, b) => (a === 6 ? -1 : a) - (b === 6 ? -1 : b)).map(String));
    await page.getByLabel("من تاريخ", { exact: true }).fill("2026-10-03"); await page.getByLabel("إلى تاريخ", { exact: true }).fill("2026-10-17");
    for (const [label, hour, minute] of [["وقت البداية", "5", "07"], ["وقت النهاية", "6", "30"]]) {
      await page.getByLabel(`${label} — الساعة`, { exact: true }).selectOption(hour); await page.getByLabel(`${label} — الدقيقة`, { exact: true }).selectOption(minute); await page.getByLabel(`${label} — الفترة`, { exact: true }).selectOption("pm");
    }
    await expect(page.getByText(/حتى سنة/)).toHaveCount(0);
    await page.getByRole("button", { name: "معاينة الجدول", exact: true }).click(); await expect(page.getByRole("region", { name: "معاينة أثر الجدول" })).toBeVisible();
    const save = page.getByRole("button", { name: "حفظ وإنشاء الحصص", exact: true }); await save.hover();
    const colors = await save.evaluate(button => ({ text: getComputedStyle(button).color, background: getComputedStyle(button).backgroundColor }));
    expect(colors.text).toBe("rgb(255, 255, 255)"); expect(colors.background).not.toBe(colors.text);
    await page.screenshot({ path: testInfo.outputPath(`timetable-${journey.width}.png`), fullPage: true });
    await save.click(); await page.waitForURL("**/dashboard/academy/groups"); await expect(page.locator(".save-notice")).toContainText("تم حفظ الجدول وإنشاء");
    await page.goto(`/dashboard/academy/groups/${id}`); await page.getByRole("tab", { name: "الجدول", exact: true }).click(); await page.getByText("إنشاء الحصص / تعديل الجدول", { exact: true }).click();
    for (const day of journey.days) await expect(page.getByRole("checkbox", { name: day, exact: true })).toBeChecked();
    const details = await page.request.get(`/api/v1/manage/groups/${id}/details`).then(r => r.json());
    expect(details.schedules.map((x: { dayOfWeek: string }) => x.dayOfWeek).sort()).toEqual([...journey.values].sort());
    for (const s of details.sessions) { expect(journey.numeric).toContain(new Date(`${s.sessionDate}T12:00:00Z`).getUTCDay()); expect(s.startTime).toBe("17:07:00"); expect(s.endTime).toBe("18:30:00"); }
    await page.getByRole("link", { name: "عرض حصص المجموعة" }).click(); await expect(page.getByRole("heading", { name: "الحصص", exact: true })).toBeVisible(); await expect(page.getByLabel("المجموعة", { exact: true })).toHaveValue(id);
    const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
    await expect(page.getByLabel("من تاريخ", { exact: true })).toHaveValue(`${today.slice(0, 8)}01`); await expect(page.getByLabel("إلى تاريخ", { exact: true })).toHaveValue(today);
    await page.getByLabel("من تاريخ", { exact: true }).fill("2026-10-03"); await page.getByLabel("إلى تاريخ", { exact: true }).fill("2026-10-17");
    await expect(page.locator("main").getByRole("alert")).toHaveCount(0); await expect(page.getByRole("table", { name: "حصص المجموعات" })).toBeVisible();
    await page.getByRole("button", { name: "مسح الفلاتر" }).click(); await expect(page.getByLabel("المجموعة", { exact: true })).toHaveValue(""); await expect(page.getByLabel("ولي الأمر", { exact: true })).toBeVisible();
    await expect(page.getByLabel("من تاريخ", { exact: true })).toHaveValue(""); await expect(page.getByLabel("إلى تاريخ", { exact: true })).toHaveValue("");
    await page.screenshot({ path: testInfo.outputPath(`sessions-${journey.width}.png`), fullPage: true });
  });
}
