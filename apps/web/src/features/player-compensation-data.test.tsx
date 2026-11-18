import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerCompensation } from "./player-compensation";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const grant = { id: "g", sport: "السباحة", cause: "CancelledSession", unit: "Minute", units: 60, consumed: 0, allocated: 0, validFrom: "2026-11-18", validTo: "2026-12-18", sourceDate: "2026-09-28", sourceSessionId: "s", bookings: [] };
const options = { today: "2026-11-18", sources: [{ id: "s", group: "أصل اختباري", date: "2026-09-28" }], targets: [{ id: "t", group: "موعد اختباري", date: "2026-11-25", start: "11:00", end: "11:30" }] };
const booking = { id: "b", trainingSessionId: "t", group: "موعد اختباري", date: "2026-11-25", state: "Reserved", version: 1, units: 30, consumed: 0 };
const review = { fingerprint: "review", units: 30, available: 60, expectedPlayers: 1, capacity: 2, conflicts: [], target: options.targets[0] };

it.each([
  { name: "missing bookings", path: "grants", data: [{ ...grant, bookings: null }] },
  { name: "missing display field", path: "grants", data: [{ ...grant, sport: "" }] },
  { name: "fractional booking units", path: "grants", data: [{ ...grant, allocated: 30, bookings: [{ ...booking, units: 1.5 }] }] },
  { name: "missing booking date", path: "grants", data: [{ ...grant, allocated: 30, bookings: [{ ...booking, date: "" }] }] },
  { name: "negative credit", path: "grants", data: [{ ...grant, units: -1 }] },
  { name: "overallocated credit", path: "grants", data: [{ ...grant, allocated: 61 }] },
  { name: "invalid consumption", path: "grants", data: [{ ...grant, consumed: 1 }] },
  { name: "unknown booking state", path: "grants", data: [{ ...grant, bookings: [{ id: "b", trainingSessionId: "t", group: "موعد اختباري", date: "2026-11-25", state: "Unknown", version: 1, units: 30, consumed: 0 }] }] },
  { name: "invalid sources", path: "options", data: { ...options, sources: {} } },
  { name: "invalid time", path: "options", data: { ...options, targets: [{ ...options.targets[0], start: "25:61" }] } },
  { name: "impossible date", path: "options", data: { ...options, today: "2026-02-30" } },
])("recovers $name with no partial controls or write", async failure => {
  let broken = true;
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    expect(init?.method ?? "GET").toBe("GET");
    const kind = String(input).endsWith("/options") ? "options" : "grants";
    return Response.json(broken && kind === failure.path ? failure.data : kind === "options" ? options : [grant]);
  });
  vi.stubGlobal("fetch", fetcher);
  render(<PlayerCompensation enrollmentId="e" />);
  fireEvent.click(screen.getByText("التعويضات والحصص البديلة"));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل التعويضات والمواعيد. أعد المحاولة.");
  expect(screen.queryByRole("button", { name: "معاينة حجز التعويض" })).not.toBeInTheDocument();
  expect(screen.queryByRole("region", { name: "التعويضات المعتمدة" })).not.toBeInTheDocument();
  broken = false;
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل التعويضات" }));
  expect(await screen.findByRole("combobox", { name: "الحصة التعويضية" })).toBeEnabled();
  expect(screen.getByRole("region", { name: "التعويضات المعتمدة" })).toHaveTextContent("60 دقيقة");
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it.each([
  { name: "zero units", data: { ...review, units: 0 } },
  { name: "fractional units", data: { ...review, units: 1.5 } },
  { name: "text units", data: { ...review, units: "bad" } },
  { name: "negative available", data: { ...review, available: -1 } },
  { name: "negative roster", data: { ...review, expectedPlayers: -1 } },
  { name: "text capacity", data: { ...review, capacity: "2" } },
  { name: "malformed target", data: { ...review, target: { ...review.target, date: "not-a-date" } } },
  { name: "malformed target time", data: { ...review, target: { ...review.target, end: "25:00" } } },
  { name: "malformed conflicts", data: { ...review, conflicts: [{}] } },
])("rejects $name before enabling a compensation confirmation", async failure => {
  let broken = true;
  const writes: string[] = [];
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input);
    if (path.endsWith("/csrf")) return Response.json({ token: "test-csrf" });
    if (init?.method === "POST") { writes.push(path); return Response.json(broken ? failure.data : review); }
    return Response.json(path.endsWith("/options") ? options : [grant]);
  }));
  render(<PlayerCompensation enrollmentId="e" />);
  fireEvent.click(screen.getByText("التعويضات والحصص البديلة"));
  fireEvent.change(await screen.findByRole("combobox", { name: "الحصة التعويضية" }), { target: { value: "t" } });
  fireEvent.click(screen.getByRole("button", { name: "معاينة حجز التعويض" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر عرض المعاينة. أعد المحاولة.");
  expect(screen.queryByRole("region", { name: "معاينة التعويض" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "تأكيد حجز التعويض" })).not.toBeInTheDocument();
  broken = false;
  fireEvent.click(screen.getByRole("button", { name: "معاينة حجز التعويض" }));
  expect(await screen.findByRole("region", { name: "معاينة التعويض" })).toHaveTextContent("30 دقيقة");
  expect(writes).toEqual(["/api/v1/people/compensations/g/booking/preview", "/api/v1/people/compensations/g/booking/preview"]);
});
