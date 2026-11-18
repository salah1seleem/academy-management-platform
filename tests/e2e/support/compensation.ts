import { expect, type APIRequestContext } from "@playwright/test";
import { afterDays, ensurePlacementSession, placementEnrollment, placementId as id, placementRead, placementWrite, serviceIdentity, type PlacementPlayer } from "./placement";

export type CompensationBooking = { id: string; trainingSessionId: string; state: string; version: number; units: number; consumed: number; startTime: string };
export type CompensationGrant = { id: string; sourceSessionId: string; units: number; allocated: number; consumed: number; validFrom: string; validTo: string; bookings: CompensationBooking[] };
export type CompensationOptions = { today: string; sources: { id: string; date: string }[]; targets: { id: string; date: string }[] };

export async function prepareCompensation(api: APIRequestContext) {
  const { today } = await placementRead<{ today: string }>(api, "/api/v1/academy/calendar");
  const date = afterDays(today, 7);
  const target = await ensurePlacementSession(api, id(90), date, "19:00", "20:00");
  const options = await placementRead<CompensationOptions>(api, `/api/v1/people/enrollments/${id(81)}/compensation/options`);
  expect(options.today).toBe(today);
  expect(options.targets.some(row => row.id === target.id)).toBe(true);
  expect(options.targets.some(row => row.id === id(91))).toBe(false);
  return { today, date, target, validTo: afterDays(today, 30) };
}

export async function compensationLedger(api: APIRequestContext) {
  const [player, collections, grants, historical] = await Promise.all([
    placementRead<PlacementPlayer>(api, `/api/v1/people/players/${id(80)}`),
    placementRead(api, "/api/v1/subscriptions/collections"),
    placementRead<CompensationGrant[]>(api, `/api/v1/people/enrollments/${id(81)}/compensations`),
    placementRead<{ id: string }[]>(api, `/api/v1/attendance/sessions?groupId=${id(90)}&from=2026-11-15&to=2026-11-15`),
  ]);
  expect(placementEnrollment(player, id(81)).trainingGroupId).toBe(id(70));
  return { service: serviceIdentity(player), collections, grants, historical: historical.filter(row => row.id === id(91)) };
}

export async function signInCompensationGuardian(api: APIRequestContext) {
  const challenge = await placementWrite(api, "/api/v1/auth/guardian/otp/request", { phoneNumber: "01000000001" });
  expect(challenge.status()).toBe(202);
  expect((await placementWrite(api, "/api/v1/auth/guardian/otp/verify", { phoneNumber: "01000000001", code: "246810", challengeId: (await challenge.json()).challengeId })).status()).toBe(200);
}
