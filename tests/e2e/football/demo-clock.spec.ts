import { expect, test } from "@playwright/test";
import { endedFootballSlot } from "../support/ended-football-session";

test("Football fixture uses Cairo midnight rather than UTC epoch modulo", () => {
  expect(endedFootballSlot(new Date("2026-10-15T21:38:09Z"))).toEqual({ sessionDate: "2026-10-16", startTime: "00:38:03", endTime: "00:38:04" });
  expect(endedFootballSlot(new Date("2026-10-15T20:59:59Z"))).toEqual({ sessionDate: "2026-10-15", startTime: "23:59:53", endTime: "23:59:54" });
  expect(endedFootballSlot(new Date("2026-10-15T21:00:01Z"))).toBeNull();
  expect(endedFootballSlot(new Date("2026-10-15T21:00:03Z"))).toEqual({ sessionDate: "2026-10-16", startTime: "00:00:00", endTime: "00:00:01" });
});
