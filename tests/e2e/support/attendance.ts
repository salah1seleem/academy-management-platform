import { expect, Page } from "@playwright/test";

export function cairoDateRange() {
  const to = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  return { from: `${to.slice(0, 8)}01`, to };
}

// Create/reuse only this opt-in synthetic group's dated rehearsal. Never move a
// historical session or freeze the application's real Cairo attendance clock.
export async function todaySession(page: Page, groupId: string, start: string, end: string) {
  const { to: today } = cairoDateRange();
  const list = await page.request.get(`/api/v1/attendance/sessions?from=${today}&to=${today}&groupId=${groupId}`);
  expect(list.ok(), await list.text()).toBe(true);
  const existing = (await list.json()).find((s: { startTime: string }) => s.startTime === `${start}:00`);
  if (existing) return existing.id as string;
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  const response = await page.request.post("/api/v1/attendance/sessions", {
    headers: { "X-CSRF-TOKEN": csrf.token },
    data: { trainingGroupId: groupId, sessionDate: today, startTime: `${start}:00`, endTime: `${end}:00`, notes: "حصة اختبار اصطناعي لليوم الفعلي — لا بيانات عملاء" },
  });
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()).id as string;
}
