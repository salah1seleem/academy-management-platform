import { test, expect } from "@playwright/test";

test("Owner previews and publishes a resource-bound group timetable on desktop and mobile without financial effects", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  async function post(path: string, data: unknown) { const r = await page.request.post(`/api/v1/${path}`, { headers: { "X-CSRF-TOKEN": csrf.token }, data }); expect(r.ok(), await r.text()).toBeTruthy(); return r.json(); }
  const options = await page.request.get("/api/v1/manage/structure/options").then(r => r.json()), facilities = await page.request.get("/api/v1/facilities").then(r => r.json());
  const seeded = await page.request.get("/api/v1/manage/groups/58000000-0000-0000-0000-000000000100/details").then(r => r.json()); expect(seeded.sessions).toHaveLength(4); expect(seeded.resourcePlan.seats).toBe(4); expect(seeded.players).toHaveLength(1);
  const space = facilities.spaces.find((s: { id: string }) => s.id === "58000000-0000-0000-0000-000000000002");
  const original = options.groups.find((g: { sportId: string }) => g.sportId === space.sportId), unique = Date.now();
  const group = await post("manage/groups", { arabicName: `جدول حارة اصطناعي ${unique}`, branchId: space.branchId, sportId: space.sportId, ageCategoryId: original.ageCategoryId, capacity: 8 });
  const coach = await post("manage/staff", { name: "مدرب جدول الاختبار", email: `schedule-${unique}@example.test`, phone: "010" + String(unique).slice(-8), temporaryPassword: "Demo-Only-123!", role: "Coach" });
  await post(`manage/groups/${group}/coaches`, { membershipId: coach.id });
  const resource = await post("facilities/resources", { name: `RT-${unique}`, spaceId: space.id, capacity: 8, shared: true, days: [0, 1, 2, 3, 4, 5, 6], opensAt: "08:00", closesAt: "22:00", serviceKinds: ["Training"] });
  await post(`facilities/resources/${resource.id}/approve`, { evidence: "اعتماد اصطناعي لاختبار الجدول فقط، لا مرفق حقيقي", qualifiedResponsibilityConfirmed: true });
  await page.goto(`/dashboard/academy/groups/${group}?tab=schedule`); await page.getByText("إنشاء الحصص / تعديل الجدول", { exact: true }).click();
  await page.getByRole("checkbox", { name: "السبت", exact: true }).check(); await page.getByRole("checkbox", { name: "الاثنين", exact: true }).check();
  const start = new Date(); start.setDate(start.getDate() + 20); const end = new Date(start); end.setDate(end.getDate() + 14);
  const date = (d: Date) => new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(d);
  await page.getByLabel("من تاريخ", { exact: true }).fill(date(start)); await page.getByLabel("إلى تاريخ", { exact: true }).fill(date(end));
  for (const [label, hour] of [["وقت البداية", "5"], ["وقت النهاية", "6"]]) { await page.getByLabel(`${label} — الساعة`, { exact: true }).selectOption(hour); await page.getByLabel(`${label} — الدقيقة`, { exact: true }).selectOption("00"); await page.getByLabel(`${label} — الفترة`, { exact: true }).selectOption("pm"); }
  await page.getByRole("combobox", { name: "الحارة أو منطقة التدريب", exact: true }).selectOption(resource.id); await page.getByRole("combobox", { name: "المدرب المسند", exact: true }).selectOption(coach.id); await page.getByLabel("المقاعد المحجوزة").fill("4");
  await page.getByRole("button", { name: "معاينة الجدول", exact: true }).click(); const preview = page.getByRole("region", { name: "معاينة أثر الجدول" }); await expect(preview).toBeVisible(); await expect(preview).toContainText(`RT-${unique}`); await expect(page.locator("main [role=alert]")).toHaveCount(0);
  const before = await page.request.get(`/api/v1/manage/groups/${group}/details`).then(r => r.json()); expect(before.sessions).toHaveLength(0);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await preview.scrollIntoViewIfNeeded(); await page.screenshot({ path: info.outputPath(`resource-timetable-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
  const saving = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`/groups/${group}/timetable`)); await page.getByRole("button", { name: "حفظ وإنشاء الحصص", exact: true }).click(); const saved = await saving; expect(saved.ok()).toBe(true); const result = await saved.json(); expect(result.created).toBeGreaterThan(0); await page.waitForURL("**/dashboard/academy/groups");
  const after = await page.request.get(`/api/v1/manage/groups/${group}/details`).then(r => r.json()); expect(after.sessions.length).toBe(result.created); expect(after.resourcePlan.resourceId).toBe(resource.id);
  const board = await page.request.get(`/api/v1/facilities/board?date=${after.sessions[0].sessionDate}`).then(r => r.json()); expect(board.bookings.some((b: { trainingGroupId: string; trainingSessionId: string }) => b.trainingGroupId === group && b.trainingSessionId === after.sessions[0].id)).toBe(true);
});
