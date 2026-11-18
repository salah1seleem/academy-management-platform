import { expect, test } from "@playwright/test";
import { afterDays, assertPlacementContrast, assertPlacementViewport, ensurePlacementSession, placementEnrollment, placementId as id, placementRead, placementWrite, serviceIdentity, setPlacementAppearance, type PlacementPlayer } from "../support/placement";
import { formatDateAr } from "../../../apps/web/src/lib/formatters";

test.beforeEach(async ({ page }) => { expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200); });
const weekdays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];
async function prepare(api: Parameters<typeof placementWrite>[0]) {
  const { today } = await placementRead<{ today: string }>(api, "/api/v1/academy/calendar");
  const date = afterDays(today, 8); await ensurePlacementSession(api, id(191), date, "11:00", "12:00");
  return { today, date };
}
async function state(api: Parameters<typeof placementWrite>[0]) {
  const [player, money, history] = await Promise.all([
    placementRead<PlacementPlayer>(api, `/api/v1/people/players/${id(194)}`), placementRead(api, "/api/v1/subscriptions/collections"),
    placementRead<{ id: string }[]>(api, `/api/v1/attendance/sessions?groupId=${id(191)}&from=2026-11-15&to=2026-11-15`),
  ]);
  expect(placementEnrollment(player, id(195)).trainingGroupId).toBeNull();
  return { service: serviceIdentity(player), money, history: history.filter(s => s.id === id(193)) };
}

test("Owner sees explained eligible alternatives, exercises every preference and keeps the selected child through explicit placement preview", async ({ page }, info) => {
  const { today, date } = await prepare(page.request), before = await state(page.request);
  const group = await placementRead<{ coaches: { id: string; name: string }[] }>(page.request, `/api/v1/manage/groups/${id(191)}/details`);
  expect(group.coaches).toHaveLength(1); const coach = group.coaches[0];
  await page.goto(`/dashboard/players/${id(194)}?tab=training`);
  const panel = page.locator(".placement-recommendations"); await panel.locator("summary").click();
  const dayName = weekdays[new Date(`${date}T12:00:00Z`).getUTCDay()];
  await panel.getByRole("checkbox", { name: dayName, exact: true }).check();
  for (const [label, value] of [["الساعة", "11"], ["الدقيقة", "00"], ["الفترة", "am"]]) await panel.getByRole("combobox", { name: `وقت مفضل اختياري — ${label}`, exact: true }).selectOption(value);
  await panel.getByLabel("مدرب مفضل اختياري", { exact: true }).selectOption(coach.id);
  const choices = page.getByRole("region", { name: "بدائل التسكين" });
  const query = async () => {
    const response = page.waitForResponse(r => r.url().endsWith(`/enrollments/${id(195)}/placement-recommendations`) && r.request().method() === "POST");
    await panel.getByRole("button", { name: "عرض البدائل المؤهلة" }).click();
    const result = await response; expect(result.status(), await result.text()).toBe(200); const data = await result.json();
    expect(data).toMatchObject({ asOfDate: today, waitingDays: Math.max(0, (Date.parse(`${today}T12:00:00Z`) - Date.parse("2026-09-28T12:00:00Z")) / 86400000) });
    expect(data.choices).toEqual([expect.objectContaining({ groupId: id(191), rank: 1, currentPlayers: 0, capacity: 2, nextDate: date, startTime: "11:00:00" })]);
    await expect(choices.getByRole("link", { name: "فتح المجموعة ومراجعة التسكين" })).toHaveCount(1);
    await expect(choices).toContainText("مطابقة للفرع والرياضة والعمر والمستوى المراجع");
    await expect(choices).toContainText("0 لاعب من 2"); await expect(choices).toContainText(formatDateAr(date));
    return result.request().postDataJSON();
  };
  expect(await query()).toMatchObject({ days: [new Date(`${date}T12:00:00Z`).getUTCDay()], startTime: "11:00", preferredCoachId: coach.id });
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    for (const mode of ["dark", "light"] as const) {
      await setPlacementAppearance(page, mode); await page.screenshot({ path: info.outputPath(`recommendations-${mode}-${width}.png`), fullPage: true, animations: "disabled" });
      await panel.screenshot({ path: info.outputPath(`recommendations-panel-${mode}-${width}.png`), animations: "disabled" });
      await assertPlacementViewport(page, width); await assertPlacementContrast(choices, "p, h4, li, a");
    }
  }
  await panel.getByRole("checkbox", { name: dayName, exact: true }).uncheck(); await expect(choices).toHaveCount(0);
  await panel.getByRole("checkbox", { name: dayName, exact: true }).check(); await query();
  await panel.getByLabel("مدرب مفضل اختياري", { exact: true }).selectOption(""); await expect(choices).toHaveCount(0);
  await panel.getByLabel("مدرب مفضل اختياري", { exact: true }).selectOption(coach.id); await query();
  await panel.getByRole("combobox", { name: "وقت مفضل اختياري — الساعة", exact: true }).selectOption("10"); await expect(choices).toHaveCount(0);
  await panel.getByRole("combobox", { name: "وقت مفضل اختياري — الساعة", exact: true }).selectOption("11"); await query();
  await panel.getByRole("button", { name: "مسح الوقت" }).click(); await expect(choices).toHaveCount(0);
  expect(await query()).toMatchObject({ startTime: null, preferredCoachId: coach.id });
  expect(await state(page.request)).toEqual(before);
  await choices.getByRole("link").click(); await expect(page).toHaveURL(new RegExp(`candidateId=${id(195)}`));
  const placement = page.getByRole("region", { name: "التسكين الجماعي", exact: true });
  await expect(placement.getByRole("checkbox")).toHaveCount(1); await expect(placement.getByRole("checkbox")).toBeChecked();
  await expect(placement.getByRole("button", { name: "تأكيد تسكين الدفعة" })).toHaveCount(0);
  await placement.getByRole("button", { name: "معاينة التسكين" }).click(); await expect(placement.getByRole("button", { name: "تأكيد تسكين الدفعة" })).toBeEnabled();
  expect(await state(page.request)).toEqual(before);
  await page.goBack(); await expect(page).toHaveURL(new RegExp(`/dashboard/players/${id(194)}\\?tab=training$`));
  await page.goto(`/dashboard/players/${id(30)}?tab=training`); const unpaid = page.locator(".placement-recommendations"); await unpaid.locator("summary").click();
  await unpaid.getByRole("button", { name: "عرض البدائل المؤهلة" }).click();
  await expect(page.getByRole("region", { name: "بدائل التسكين" })).toContainText("لا توجد مجموعة تستوفي الشروط الحالية");
  await expect(page.getByRole("region", { name: "بدائل التسكين" }).getByRole("link")).toHaveCount(0);
  expect(await state(page.request)).toEqual(before);
});

test("Preference and recommendation failures recover in Arabic without stale choices or an implicit placement", async ({ page }, info) => {
  await prepare(page.request); const before = await state(page.request); let phase = "network", mutation = 0;
  const crashes: string[] = []; page.on("pageerror", error => crashes.push(error.message));
  page.on("request", r => { if (r.method() === "POST" && /\/(placement|compensation|booking)\/?$/.test(r.url())) mutation++; });
  await page.route(`**/api/v1/people/enrollments/${id(195)}/placement-preferences`, async route => {
    if (phase === "network") await route.abort("failed");
    else if (phase === "json") await route.fulfill({ status: 200, contentType: "application/json", body: "{invalid-json" });
    else if (phase === "shape") await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ coaches: [{}] }) });
    else await route.continue();
  });
  await page.setViewportSize({ width: 390, height: 900 }); await page.goto(`/dashboard/players/${id(194)}?tab=training`);
  const panel = page.locator(".placement-recommendations"); await panel.locator("summary").click();
  for (const next of ["json", "shape", "healthy"]) {
    await expect(panel.getByRole("alert")).toHaveText("تعذر تحميل تفضيلات التسكين. أعد المحاولة.");
    await expect(panel.getByRole("button", { name: "عرض البدائل المؤهلة" })).toHaveCount(0);
    await expect(panel).not.toContainText(/Failed to fetch|invalid-json/);
    if (phase === "network") await page.screenshot({ path: info.outputPath("preferences-failure-390.png"), fullPage: true, animations: "disabled" });
    phase = next; await panel.getByRole("button", { name: "إعادة تحميل تفضيلات التسكين" }).click();
  }
  await expect(panel.getByRole("button", { name: "عرض البدائل المؤهلة" })).toBeEnabled();
  let responseMode = "healthy";
  await page.route(`**/api/v1/people/enrollments/${id(195)}/placement-recommendations`, async route => {
    if (responseMode === "service") await route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "Synthetic upstream error" }) });
    else if (responseMode === "json") await route.fulfill({ status: 200, contentType: "application/json", body: "{invalid-json" });
    else if (responseMode === "shape") await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ choices: [{}] }) });
    else await route.continue();
  });
  const choices = panel.getByRole("region", { name: "بدائل التسكين" });
  await panel.getByRole("button", { name: "عرض البدائل المؤهلة" }).click(); await expect(choices.getByRole("link")).toHaveCount(1);
  for (const mode of ["service", "json", "shape"]) {
    responseMode = mode; await panel.getByRole("button", { name: "عرض البدائل المؤهلة" }).click();
    await expect(panel.getByRole("alert")).toContainText("تعذر"); await expect(choices).toHaveCount(0);
    await expect(panel).not.toContainText(/Synthetic upstream|invalid-json|Failed to fetch/);
  }
  responseMode = "healthy"; await panel.getByRole("button", { name: "عرض البدائل المؤهلة" }).click(); await expect(choices.getByRole("link")).toHaveCount(1);
  await page.setViewportSize({ width: 1440, height: 900 }); await assertPlacementViewport(page, 1440);
  await page.screenshot({ path: info.outputPath("preferences-recovered-1440.png"), fullPage: true, animations: "disabled" });
  expect(mutation).toBe(0); expect(crashes).toEqual([]); expect(await state(page.request)).toEqual(before);
});
