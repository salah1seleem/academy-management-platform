import { expect, type APIRequestContext, type Page } from "@playwright/test";
import { afterDays, placementId, placementRead } from "./placement";

export const creationDayLabel = (day: number) => ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"][day];

export type CreationDraft = {
  arabicName: string; branchId: string; sportId: string; ageCategoryId: string;
  levelId: string; capacity: number; registrationFormat: string; capacityReviewed: boolean;
  coachMembershipId: string;
  timetable: { from: string; to: string; days: number[]; startTime: string; endTime: string };
  enrollmentIds: string[];
};
type Option = { id: string; arabicName: string };
export type CreationGroup = { id: string; arabicName: string; playerCount: number; coaches: string[] };

export async function creationDraft(api: APIRequestContext, daysAhead: number, enrollmentIds: string[] = []) {
  const [calendar, options, source] = await Promise.all([
    placementRead<{ today: string }>(api, "/api/v1/academy/calendar"),
    placementRead<{ coaches: Option[]; categories: Option[] }>(api, "/api/v1/manage/structure/options"),
    placementRead<{ group: { branchId: string; sportId: string; trainingLevelId: string }; coaches: { id: string; name: string }[] }>(api, `/api/v1/manage/groups/${placementId(1)}/details`),
  ]);
  expect(source.coaches.length).toBeGreaterThan(0);
  const coach = options.coaches.find(row => row.id === source.coaches[0].id);
  expect(coach).toBeTruthy();
  const category = "33000000-0000-0000-0000-000000000002";
  expect(options.categories.some(row => row.id === category)).toBe(true);
  const date = afterDays(calendar.today, daysAhead);
  const draft: CreationDraft = {
    arabicName: "مجموعة إنشاء متكاملة تجريبية", branchId: source.group.branchId,
    sportId: source.group.sportId, ageCategoryId: category, levelId: source.group.trainingLevelId,
    capacity: 2, registrationFormat: "Group", capacityReviewed: false, coachMembershipId: coach!.id,
    timetable: { from: date, to: date, days: [new Date(`${date}T12:00:00Z`).getUTCDay()], startTime: "13:00:00", endTime: "14:00:00" },
    enrollmentIds,
  };
  return { draft, today: calendar.today, coach: coach! };
}

export async function fillCreationIdentity(page: Page, draft: CreationDraft) {
  await page.goto("/dashboard/academy/groups/new");
  await page.getByLabel("اسم المجموعة", { exact: true }).fill(draft.arabicName);
  await page.getByLabel("السعة", { exact: true }).fill(String(draft.capacity));
  for (const [label, value] of [["الفرع", draft.branchId], ["الرياضة", draft.sportId], ["الفئة العمرية", draft.ageCategoryId], ["مستوى التدريب", draft.levelId]] as const) {
    await page.getByRole("combobox", { name: label, exact: true }).selectOption(value);
  }
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  await expect(page.getByRole("heading", { name: "متى وأين ومع من؟" })).toBeVisible();
}

export async function fillCreationSchedule(page: Page, draft: CreationDraft) {
  await page.getByRole("combobox", { name: "المدرب", exact: true }).selectOption(draft.coachMembershipId);
  for (const day of draft.timetable.days) await page.getByRole("checkbox", { name: creationDayLabel(day), exact: true }).check();
  await page.getByLabel("من تاريخ", { exact: true }).fill(draft.timetable.from);
  await page.getByLabel("إلى تاريخ", { exact: true }).fill(draft.timetable.to);
  for (const [label, time] of [["وقت البداية", draft.timetable.startTime], ["وقت النهاية", draft.timetable.endTime]] as const) {
    const [hour, minute] = time.split(":").map(Number);
    await page.getByRole("combobox", { name: `${label} — الساعة`, exact: true }).selectOption(String(hour % 12 || 12));
    await page.getByRole("combobox", { name: `${label} — الدقيقة`, exact: true }).selectOption(String(minute).padStart(2, "0"));
    await page.getByRole("combobox", { name: `${label} — الفترة`, exact: true }).selectOption(hour >= 12 ? "pm" : "am");
  }
}

export async function creationState(api: APIRequestContext, date: string) {
  const [groups, sessions, collections] = await Promise.all([
    placementRead<CreationGroup[]>(api, "/api/v1/manage/structure/groups"),
    placementRead<{ id: string; trainingGroupId: string; sessionDate: string; startTime: string; endTime: string }[]>(api, `/api/v1/attendance/sessions?from=${date}&to=${date}`),
    placementRead(api, "/api/v1/subscriptions/collections"),
  ]);
  return { groups: groups.sort((a, b) => a.id.localeCompare(b.id)), sessions: sessions.sort((a, b) => a.id.localeCompare(b.id)), collections };
}

export async function assertCreationMetricWords(page: Page) {
  expect(await page.evaluate(async () => {
    const faces = await Promise.all([400, 700].map(weight => document.fonts.load(`${weight} 14px AcademyArabic`, "المجموعة المختارون 123")));
    return faces.every(rows => rows.some(face => face.family === "AcademyArabic" && face.status === "loaded"));
  })).toBe(true);
  const words = await page.locator(".group-creation .timetable-preview-counts p").evaluateAll(metrics => metrics.flatMap(metric => {
    const result: { word: string; lines: number }[] = [];
    const walker = document.createTreeWalker(metric, NodeFilter.SHOW_TEXT);
    for (let text = walker.nextNode(); text; text = walker.nextNode()) {
      for (const match of (text.textContent ?? "").matchAll(/\S+/gu)) {
        const range = document.createRange();
        range.setStart(text, match.index!); range.setEnd(text, match.index! + match[0].length);
        const baselines = new Set(Array.from(range.getClientRects()).filter(rect => rect.width && rect.height).map(rect => Math.round(rect.top)));
        result.push({ word: match[0], lines: baselines.size });
      }
    }
    return result;
  }));
  expect(words.length).toBeGreaterThan(0);
  for (const word of words) expect(word.lines, JSON.stringify(word)).toBe(1);
}
