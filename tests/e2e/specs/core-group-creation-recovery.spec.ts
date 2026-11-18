import { expect, test } from "@playwright/test";
import { creationDraft, creationState, type CreationDraft } from "../support/group-creation";
import { afterDays, ensurePlacementSession, placementEnrollment, placementId as id, placementRead, placementWrite, serviceIdentity, type PlacementPlayer } from "../support/placement";

test.beforeEach(async ({ page }) => {
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
});

test("Invalid group drafts and an unpaid mixed batch roll back group, schedule and placements; other identities cannot write these resources", async ({ page, browser }, info) => {
  const { draft, today } = await creationDraft(page.request, 18, [id(101)]);
  const before = await creationState(page.request, draft.timetable.from);
  const players = await Promise.all([100, 30].map(n => placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(n)}`)));
  expect(players.map((row, index) => placementEnrollment(row, id(index === 0 ? 101 : 31)).trainingGroupId)).toEqual([null, null]);
  const invalid: { body: Partial<CreationDraft>; status: number }[] = [
    { body: { arabicName: " " }, status: 422 },
    { body: { arabicName: "س".repeat(161) }, status: 422 },
    ...[0, -1, 10001].map(capacity => ({ body: { capacity }, status: 422 })),
    { body: { capacity: 1.5 }, status: 400 },
    { body: { coachMembershipId: "ffffffff-ffff-ffff-ffff-ffffffffffff" }, status: 422 },
    { body: { registrationFormat: "Private", capacityReviewed: false }, status: 422 },
    { body: { registrationFormat: "SemiPrivate", capacityReviewed: true }, status: 422 },
    { body: { enrollmentIds: [id(101), id(101)] }, status: 422 },
    { body: { levelId: "ffffffff-ffff-ffff-ffff-ffffffffffff" }, status: 404 },
    ...[
      { from: afterDays(today, -1) },
      { to: afterDays(draft.timetable.from, -1) },
      { to: afterDays(draft.timetable.from, 367) },
      { days: [] },
      { days: [draft.timetable.days[0], draft.timetable.days[0]] },
      { startTime: "14:00:00", endTime: "14:00:00" },
    ].map(timetable => ({ body: { timetable: { ...draft.timetable, ...timetable } }, status: 422 })),
  ];
  for (const scenario of invalid) {
    const response = await placementWrite(page.request, "/api/v1/manage/group-creation/preview", { ...draft, ...scenario.body });
    expect(response.status(), JSON.stringify({ scenario, response: await response.text() })).toBe(scenario.status);
  }
  const searchRoot = `/api/v1/manage/group-creation/candidates?branchId=${draft.branchId}&sportId=${draft.sportId}`;
  expect((await page.request.get(`${searchRoot}&offset=-1`)).status()).toBe(422);
  expect((await page.request.get(`${searchRoot}&search=${encodeURIComponent("س".repeat(101))}`)).status()).toBe(422);
  const mixed = { ...draft, enrollmentIds: [id(101), id(31)] };
  const preview = await placementWrite(page.request, "/api/v1/manage/group-creation/preview", mixed);
  expect(preview.status(), await preview.text()).toBe(200);
  const review = await preview.json();
  expect(review.conflicts.join(" ")).toContain("لا يوجد استحقاق تدريب سار");
  const rejected = await placementWrite(page.request, "/api/v1/manage/group-creation", { ...mixed, fingerprint: review.fingerprint });
  expect(rejected.status()).toBe(409);
  expect(await rejected.text()).toContain("لا يوجد استحقاق تدريب سار");
  expect(await creationState(page.request, draft.timetable.from)).toEqual(before);
  for (const [index, n] of [100, 30].entries()) {
    const current = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(n)}`);
    expect(serviceIdentity(current)).toEqual(serviceIdentity(players[index]));
    expect(placementEnrollment(current, id(index === 0 ? 101 : 31)).trainingGroupId).toBeNull();
  }
  for (const role of ["visitor", "coach", "other-academy"] as const) {
    const context = await browser.newContext({ baseURL: info.project.use.baseURL });
    try {
      if (role !== "visitor") {
        const email = role === "coach" ? "coach.nogoom@example.test" : "owner.future@example.test";
        expect((await placementWrite(context.request, "/api/v1/auth/login", { email, password: "Demo-Only-123!" })).status()).toBe(200);
      }
      const otherBefore = role === "other-academy" ? await placementRead(context.request, "/api/v1/subscriptions/collections") : null;
      for (const suffix of ["/preview", ""]) {
        const response = await placementWrite(context.request, `/api/v1/manage/group-creation${suffix}`, { ...draft, fingerprint: "not-issued-to-this-identity" });
        expect(response.status()).toBe(role === "visitor" ? 401 : role === "coach" ? 403 : 404);
        expect(await response.text()).not.toContain("لاعب إنشاء مجموعة تجريبي");
      }
      if (otherBefore) expect(await placementRead(context.request, "/api/v1/subscriptions/collections")).toEqual(otherBefore);
    } finally { await context.close(); }
  }
  expect(await creationState(page.request, draft.timetable.from)).toEqual(before);
});

test("A changed academy session invalidates the wizard preview without creating a partial group or service", async ({ page }) => {
  const { draft } = await creationDraft(page.request, 19, [id(101)]);
  const before = await creationState(page.request, draft.timetable.from);
  const player = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(100)}`);
  const preview = await placementWrite(page.request, "/api/v1/manage/group-creation/preview", draft);
  expect(preview.status(), await preview.text()).toBe(200);
  const review = await preview.json();
  expect(review.conflicts).toEqual([]);
  const extra = await ensurePlacementSession(page.request, id(1), draft.timetable.from, "15:00", "16:00");
  try {
    const afterSession = await creationState(page.request, draft.timetable.from);
    const rejected = await placementWrite(page.request, "/api/v1/manage/group-creation", { ...draft, fingerprint: review.fingerprint });
    expect(rejected.status()).toBe(409);
    expect(await rejected.text()).toContain("تغير الجدول أو الأهلية منذ المعاينة");
    expect(await creationState(page.request, draft.timetable.from)).toEqual(afterSession);
  } finally {
    const csrf = await placementRead<{ token: string }>(page.request, "/api/v1/auth/csrf");
    expect((await page.request.put(`/api/v1/attendance/sessions/${extra.id}/status`, { headers: { "X-CSRF-TOKEN": csrf.token }, data: { status: "Cancelled" } })).status()).toBe(204);
  }
  const final = await creationState(page.request, draft.timetable.from);
  expect(final.groups).toEqual(before.groups); expect(final.collections).toEqual(before.collections);
  const current = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(100)}`);
  expect(serviceIdentity(current)).toEqual(serviceIdentity(player)); expect(placementEnrollment(current, id(101)).trainingGroupId).toBeNull();
  const refreshed = await placementWrite(page.request, "/api/v1/manage/group-creation/preview", draft);
  expect(refreshed.status()).toBe(200); expect((await refreshed.json()).conflicts).toEqual([]);
});
