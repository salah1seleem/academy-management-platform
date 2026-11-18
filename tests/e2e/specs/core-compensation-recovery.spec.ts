import { expect, test } from "@playwright/test";
import { compensationLedger, prepareCompensation } from "../support/compensation";
import { afterDays, placementId as id, placementRead, placementWrite } from "../support/placement";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Compensation and alternatives reject invalid dates, causes, preferences and untrusted identities without grants or money", async ({ page, browser }, info) => {
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
  const { today, validTo, target } = await prepareCompensation(page.request);
  const before = await compensationLedger(page.request);
  const root = `/api/v1/people/enrollments/${id(81)}/compensation`;
  const body = { sourceSessionId: id(71), cause: "CancelledSession", unit: "Session", validFrom: today, validTo };
  for (const change of [{ validFrom: afterDays(today, -1) }, { validTo: afterDays(today, -1) }, { validTo: afterDays(today, 91) }]) {
    expect((await placementWrite(page.request, `${root}/preview`, { ...body, ...change })).status()).toBe(422);
  }
  for (const change of [{ cause: "Unrecognized" }, { unit: "Unrecognized" }]) expect((await placementWrite(page.request, `${root}/preview`, { ...body, ...change })).status()).toBe(400);
  expect((await placementWrite(page.request, `${root}/preview`, { ...body, sourceSessionId: "ffffffff-ffff-ffff-ffff-ffffffffffff" })).status()).toBe(404);
  for (const change of [{ cause: "CoachAbsent" }, { cause: "ApprovedAbsence" }, { sourceSessionId: target.id }]) {
    const response = await placementWrite(page.request, `${root}/preview`, { ...body, ...change });
    expect(response.status()).toBe(200); const review = await response.json(); expect(review.conflicts.length).toBeGreaterThan(0);
    expect((await placementWrite(page.request, root, { ...body, ...change, fingerprint: review.fingerprint })).status()).toBe(409);
  }
  const normal = await placementWrite(page.request, `${root}/preview`, body);
  expect(normal.status()).toBe(200); expect(await normal.json()).toMatchObject({ units: 1, conflicts: [] });
  const minutes = await placementWrite(page.request, `${root}/preview`, { ...body, unit: "Minute" });
  expect(minutes.status()).toBe(200); expect(await minutes.json()).toMatchObject({ units: 60, conflicts: [] });
  expect((await page.request.post(`${root}/preview`, { data: body })).status()).toBe(400);
  const preferences = `/api/v1/people/enrollments/${id(195)}/placement-recommendations`;
  for (const input of [{ days: [-1] }, { days: [7] }, { days: [1, 1] }, { days: [0, 1, 2, 3, 4, 5, 6, 0] }, { preferredCoachId: "ffffffff-ffff-ffff-ffff-ffffffffffff" }]) {
    expect((await placementWrite(page.request, preferences, input)).status()).toBe(422);
  }
  expect((await placementWrite(page.request, preferences, { startTime: "25:61:00" })).status()).toBe(400);
  for (const role of ["visitor", "coach", "other-academy"] as const) {
    const context = await browser.newContext({ baseURL: info.project.use.baseURL });
    try {
      let headers: Record<string, string> | undefined;
      if (role === "coach") {
        const token = await mobileDemoToken(context.request, "01000000020", "رفض تعويض وتسـكين من هوية مدرب اصطناعي");
        headers = { Authorization: `Bearer ${token}`, "Idempotency-Key": crypto.randomUUID() };
        expect((await context.request.get("/api/v1/coach/groups", { headers })).status()).toBe(200);
      } else if (role === "other-academy") expect((await placementWrite(context.request, "/api/v1/auth/login", { email: "owner.future@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
      const status = role === "visitor" ? 401 : role === "coach" ? 403 : 404;
      for (const path of [`${root}/options`, `/api/v1/people/enrollments/${id(195)}/placement-preferences`]) {
        const response = await context.request.get(path, { headers }); expect(response.status()).toBe(status); expect(await response.text()).not.toContain("لاعب تعويض سباحة تجريبي");
      }
      for (const [path, data] of [[`${root}/preview`, body], [root, { ...body, fingerprint: "untrusted" }], [preferences, {}]] as const) {
        const response = headers ? await context.request.post(path, { headers, data }) : await placementWrite(context.request, path, data);
        expect(response.status()).toBe(status); expect(await response.text()).not.toContain("لاعب تعويض سباحة تجريبي");
      }
    } finally { await context.close(); }
  }
  expect(await compensationLedger(page.request)).toEqual(before);
  const player = await placementRead<{ enrollments: { id: string; trainingGroupId: string | null }[] }>(page.request, `/api/v1/people/players/${id(194)}`);
  expect(player.enrollments.find(e => e.id === id(195))?.trainingGroupId).toBeNull();
});
