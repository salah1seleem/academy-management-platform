import { expect, test, type APIRequestContext } from "@playwright/test";
import {
  afterDays, ensurePlacementSession, placementEnrollment, placementId as id,
  placementRead, placementWrite, preparePlacementSessions, serviceIdentity, type PlacementPlayer,
} from "../support/placement";

test.beforeEach(async ({ page }) => {
  const response = await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" });
  expect(response.status()).toBe(200);
});

test("Dated placement setup reuses exact sessions and recovers a lost create acknowledgement without changing history or money", async ({ page }) => {
  const historical = await placementRead(page.request, `/api/v1/attendance/sessions/${id(4)}`);
  const money = await placementRead(page.request, "/api/v1/subscriptions/collections");
  const first = await preparePlacementSessions(page.request);
  const repeated = await preparePlacementSessions(page.request);
  expect(repeated).toEqual(first);
  expect(first.date > first.today).toBe(true);
  let creates = 0;
  const lostAcknowledgement = {
    get: page.request.get.bind(page.request),
    post: async (...args: Parameters<APIRequestContext["post"]>) => {
      creates++;
      const result = await page.request.post(...args);
      expect(result.status(), await result.text()).toBe(201);
      throw new TypeError("Synthetic lost acknowledgement after confirmed server write");
    },
  };
  const date = afterDays(first.date, 2);
  const recovered = await ensurePlacementSession(lostAcknowledgement, id(50), date, "17:00", "18:00");
  const reread = await ensurePlacementSession(page.request, id(50), date, "17:00", "18:00");
  expect(reread.id).toBe(recovered.id);
  expect(creates).toBe(1);
  expect(await placementRead(page.request, `/api/v1/attendance/sessions/${id(4)}`)).toEqual(historical);
  expect(await placementRead(page.request, "/api/v1/subscriptions/collections")).toEqual(money);
});

test("An unpaid mixed batch, invalid selections and a second academy cannot change placements or collections", async ({ page, browser }, info) => {
  await preparePlacementSessions(page.request);
  const root = `/api/v1/manage/groups/${id(1)}/placement`;
  const money = await placementRead(page.request, "/api/v1/subscriptions/collections");
  const players = await Promise.all([10, 30].map(n => placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(n)}`)));
  expect(players.map((row, index) => placementEnrollment(row, id(index === 0 ? 11 : 31)).trainingGroupId)).toEqual([null, null]);
  for (const enrollmentIds of [[], [id(11), id(11)]]) {
    expect((await placementWrite(page.request, `${root}/preview`, { enrollmentIds })).status()).toBe(422);
  }
  expect((await placementWrite(page.request, `${root}/preview`, { enrollmentIds: ["ffffffff-ffff-ffff-ffff-ffffffffffff"] })).status()).toBe(404);
  const preview = await placementWrite(page.request, `${root}/preview`, { enrollmentIds: [id(11), id(31)] });
  expect(preview.status()).toBe(200);
  const review = await preview.json();
  expect(review.players.find((row: { id: string }) => row.id === id(31)).reasons.join(" ")).toContain("لا يوجد استحقاق تدريب سار");
  const rejected = await placementWrite(page.request, root, { enrollmentIds: [id(11), id(31)], fingerprint: review.fingerprint });
  expect(rejected.status()).toBe(409);
  const other = await browser.newContext({ baseURL: info.project.use.baseURL });
  try {
    expect((await placementWrite(other.request, "/api/v1/auth/login", { email: "owner.future@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
    const otherMoney = await placementRead(other.request, "/api/v1/subscriptions/collections");
    const foreign = await placementWrite(other.request, `${root}/preview`, { enrollmentIds: [id(11)] });
    expect(foreign.status()).toBe(404);
    expect(await foreign.text()).not.toContain("لاعب تسكين");
    expect(await placementRead(other.request, "/api/v1/subscriptions/collections")).toEqual(otherMoney);
  } finally {
    await other.close();
  }
  for (const [index, n] of [10, 30].entries()) {
    const after = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(n)}`);
    expect(serviceIdentity(after)).toEqual(serviceIdentity(players[index]));
    expect(placementEnrollment(after, id(index === 0 ? 11 : 31)).trainingGroupId).toBeNull();
  }
  expect(await placementRead(page.request, "/api/v1/subscriptions/collections")).toEqual(money);
});

test("A changed session invalidates the old preview and one confirmed placement replays the same revision without a second service or receipt", async ({ page }) => {
  const sessions = await preparePlacementSessions(page.request);
  const root = `/api/v1/manage/groups/${id(1)}/placement`;
  const money = await placementRead(page.request, "/api/v1/subscriptions/collections");
  const before = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(20)}`);
  expect(placementEnrollment(before, id(21)).trainingGroupId).toBeNull();
  const initial = await placementWrite(page.request, `${root}/preview`, { enrollmentIds: [id(21)] });
  expect(initial.status()).toBe(200);
  const stale = await initial.json();
  const extra = await ensurePlacementSession(page.request, id(1), afterDays(sessions.date, 1), "15:00", "16:00");
  try {
    const rejected = await placementWrite(page.request, root, { enrollmentIds: [id(21)], fingerprint: stale.fingerprint });
    expect(rejected.status()).toBe(409);
    expect(await rejected.text()).toContain("تغيرت البيانات بعد المعاينة");
    expect(placementEnrollment(await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(20)}`), id(21)).trainingGroupId).toBeNull();
  } finally {
    const csrf = await placementRead<{ token: string }>(page.request, "/api/v1/auth/csrf");
    expect((await page.request.put(`/api/v1/attendance/sessions/${extra.id}/status`, { headers: { "X-CSRF-TOKEN": csrf.token }, data: { status: "Cancelled" } })).status()).toBe(204);
  }
  const preview = await placementWrite(page.request, `${root}/preview`, { enrollmentIds: [id(21)] });
  expect(preview.status()).toBe(200);
  const body = { enrollmentIds: [id(21)], fingerprint: (await preview.json()).fingerprint };
  const key = crypto.randomUUID();
  const result = await placementWrite(page.request, root, body, key);
  expect(result.status(), await result.text()).toBe(200);
  const confirmed = await result.json();
  expect(confirmed.placed).toBe(1);
  const replay = await placementWrite(page.request, root, body, key);
  expect(replay.status()).toBe(200);
  expect(await replay.json()).toMatchObject({ id: confirmed.id, placed: 1, replayed: true });
  const record = await placementRead<{ assignedEnrollmentIds: string[] }>(page.request, `/api/v1/manage/groups/${id(1)}/placement-revisions/${confirmed.id}`);
  expect(record.assignedEnrollmentIds).toEqual([id(21)]);
  const after = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(20)}`);
  expect(placementEnrollment(after, id(21)).trainingGroupId).toBe(id(1));
  expect(serviceIdentity(after)).toEqual(serviceIdentity(before));
  expect(await placementRead(page.request, "/api/v1/subscriptions/collections")).toEqual(money);
});
