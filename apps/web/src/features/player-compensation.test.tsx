import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { CompensationCards, PlayerCompensation, type Compensation } from "./player-compensation";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const grant: Compensation = {
  id: "g", sport: "السباحة", cause: "CancelledSession", unit: "Session", units: 1, consumed: 0,
  allocated: 0, validFrom: "2026-10-11", validTo: "2026-11-10",
  sourceDate: "2026-09-28", sourceSessionId: "s", bookings: [],
};
const reserved: Compensation = {
  ...grant, allocated: 1,
  bookings: [{ id: "b", trainingSessionId: "t", group: "موعد تعويضي", date: "2026-11-01", units: 1, consumed: 0, state: "Reserved", version: 3 }],
};

function setup({ conflicts = [] as string[], failure = "", current = grant, saved = reserved } = {}) {
  const writes: { path: string; body: unknown; headers: Headers }[] = [];
  let written = false;
  vi.stubGlobal("fetch", vi.fn(async (input, init) => {
    const path = String(input);
    if (path.endsWith("csrf")) return Response.json({ token: "csrf" });
    if (init?.method !== "POST") return Response.json(path.endsWith("options") ? {
      today: "2026-10-11", sources: [{ id: "s", group: "المجموعة الأصلية", date: "2026-09-28" }],
      targets: [{ id: "t", group: "البديلة", date: "2026-11-01", start: "10:00", end: "11:00" }],
    } : [written ? saved : current]);
    writes.push({ path, body: JSON.parse(String(init.body)), headers: new Headers(init.headers) });
    if (path.endsWith("preview")) return Response.json({ fingerprint: "reviewed", units: 1, conflicts, expectedPlayers: 1, capacity: 2 });
    if (failure === "stale") return Response.json({ title: "تغيرت الحصة؛ أعد المعاينة." }, { status: 409 });
    written = true;
    if (failure === "lost") throw new TypeError("connection reset");
    return Response.json({ id: path.endsWith("/compensation") ? "g" : "b" });
  }));
  return writes;
}
async function open() {
  fireEvent.click(screen.getByText("التعويضات والحصص البديلة"));
  await screen.findByLabelText("الحصة الأصلية");
}
async function previewBooking() {
  fireEvent.change(screen.getByLabelText("الحصة التعويضية"), { target: { value: "t" } });
  fireEvent.click(screen.getByText("معاينة حجز التعويض"));
  return screen.findByText("تأكيد حجز التعويض");
}
it("requires review and confirms the exact saved grant before reporting success", async () => {
  const writes = setup({ saved: grant }), onSaved = vi.fn();
  render(<PlayerCompensation enrollmentId="e" onSaved={onSaved} />);
  await open();
  fireEvent.change(screen.getByLabelText("الحصة الأصلية"), { target: { value: "s" } });
  expect(screen.getByLabelText("صالح من")).toHaveValue("2026-10-11");
  expect(screen.getByLabelText("صالح إلى")).toHaveValue("2026-11-10");
  fireEvent.click(screen.getByText("معاينة استحقاق التعويض"));
  fireEvent.click(await screen.findByText("تأكيد استحقاق التعويض"));
  await waitFor(() => expect(onSaved).toHaveBeenCalledOnce());
  expect(writes[1].body).toEqual({ sourceSessionId: "s", cause: "CancelledSession", unit: "Session", validFrom: "2026-10-11", validTo: "2026-11-10", fingerprint: "reviewed" });
  expect(writes[1].headers.get("Idempotency-Key")).toBeTruthy();
});
it("blocks an ineligible booking and discards review when its date changes", async () => {
  setup({ conflicts: ["لا يوجد مقعد"] });
  render(<PlayerCompensation enrollmentId="e" />);
  await open();
  expect(await previewBooking()).toBeDisabled();
  fireEvent.change(screen.getByLabelText("الحصة التعويضية"), { target: { value: "" } });
  expect(screen.queryByText("تأكيد حجز التعويض")).not.toBeInTheDocument();
});
it("requires fresh review after stale credit or calendar rejection", async () => {
  setup({ failure: "stale" });
  render(<PlayerCompensation enrollmentId="e" />);
  await open();
  fireEvent.click(await previewBooking());
  await screen.findByText("تغيرت الحصة؛ أعد المعاينة.");
  expect(screen.queryByText("تأكيد حجز التعويض")).not.toBeInTheDocument();
});
it("recovers a lost response by reading the booked session without sending the booking again", async () => {
  const writes = setup({ failure: "lost" });
  render(<PlayerCompensation enrollmentId="e" />);
  await open();
  fireEvent.click(await previewBooking());
  const verify = await screen.findByText("تحقّق من سجل التعويض");
  expect(screen.queryByText(/تم حجز الحصة التعويضية/)).not.toBeInTheDocument();
  fireEvent.click(verify);
  await screen.findByText(/تم حجز الحصة التعويضية/);
  expect(writes.filter(write => write.path.endsWith("/booking"))).toHaveLength(1);
});
it("withholds success when the receipt refers to a different booking", async () => {
  setup({ saved: { ...reserved, bookings: [{ ...reserved.bookings[0], id: "other" }] } });
  const onSaved = vi.fn();
  render(<PlayerCompensation enrollmentId="e" onSaved={onSaved} />);
  await open();
  fireEvent.click(await previewBooking());
  await screen.findByText("تحقّق من سجل التعويض");
  expect(onSaved).not.toHaveBeenCalled();
});
it("confirms release after reading the same booking as cancelled", async () => {
  const writes = setup({ current: reserved, saved: { ...grant, bookings: [{ ...reserved.bookings[0], state: "Cancelled" }] } });
  render(<PlayerCompensation enrollmentId="e" />);
  await open();
  fireEvent.click(screen.getByText("تحرير حجز التعويض"));
  fireEvent.click(screen.getByText("تأكيد تحرير الحجز"));
  await screen.findByText("تم تحرير الحجز مع حفظ سجله.");
  expect(await screen.findByText(/حجز محرر/)).toBeVisible();
  expect(writes[0].body).toEqual({ version: 3 });
});
it("shows the Guardian source, validity, remaining units and reserved session", () => {
  render(<CompensationCards items={[reserved]} />);
  expect(screen.getByLabelText("التعويضات المعتمدة")).toHaveTextContent("حصة واحدة");
  expect(screen.getByText("غير المحجوز 0 حصص")).toBeVisible();
  expect(screen.getByText(/موعد تعويضي/)).toBeVisible();
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
});
