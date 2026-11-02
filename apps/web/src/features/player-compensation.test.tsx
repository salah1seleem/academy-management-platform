import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { CompensationCards, PlayerCompensation, type Compensation } from "./player-compensation";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const item: Compensation = { id: "g", sport: "السباحة", cause: "CancelledSession", unit: "Minute", units: 60, consumed: 0, allocated: 60, validFrom: "2026-10-11", validTo: "2026-11-10", sourceDate: "2026-09-28", bookings: [{ id: "b", group: "موعد تعويضي", date: "2026-11-01", units: 60, consumed: 0, state: "Reserved", version: 3 }] };
function setup(conflicts: string[] = [], fail = false) {
  const writes: { path: string; body: unknown; headers: Headers }[] = []; global.fetch = vi.fn(async (input, init) => {
    const path = String(input); if (path.endsWith("csrf")) return Response.json({ token: "csrf" });
    if (!init) return Response.json(path.endsWith("options") ? { today: "2026-10-11", sources: [{ id: "s", group: "المجموعة الأصلية", date: "2026-09-28" }], targets: [{ id: "t", group: "البديلة", date: "2026-11-01", start: "10:00", end: "11:00" }] } : [item]);
    writes.push({ path, body: JSON.parse(String(init.body)), headers: new Headers(init.headers) });
    return path.endsWith("preview") ? Response.json({ fingerprint: "reviewed", units: 1, conflicts, expectedPlayers: 1, capacity: 2 }) : fail ? Response.json({ title: "تغيرت الحصة؛ أعد المعاينة." }, { status: 409 }) : Response.json({ id: "saved" });
  }) as typeof fetch; return writes;
}
async function open() { fireEvent.click(screen.getByText("التعويضات والحصص البديلة")); await screen.findByLabelText("الحصة الأصلية"); }
it("requires review then posts a scoped grant with dates, units, fingerprint and request key", async () => {
  const writes = setup(), saved = vi.fn(); render(<PlayerCompensation enrollmentId="e" onSaved={saved} />); await open(); fireEvent.change(screen.getByLabelText("الحصة الأصلية"), { target: { value: "s" } }); expect(screen.getByLabelText("صالح من")).toHaveValue("2026-10-11"); expect(screen.getByLabelText("صالح إلى")).toHaveValue("2026-11-10");
  fireEvent.click(screen.getByText("معاينة استحقاق التعويض")); fireEvent.click(await screen.findByText("تأكيد استحقاق التعويض")); await waitFor(() => expect(saved).toHaveBeenCalledOnce()); expect(writes[1].body).toEqual({ sourceSessionId: "s", cause: "CancelledSession", unit: "Session", validFrom: "2026-10-11", validTo: "2026-11-10", fingerprint: "reviewed" }); expect(writes[1].headers.get("Idempotency-Key")).toBeTruthy();
});
it("blocks an ineligible booking and discards review when its date changes", async () => {
  setup(["لا يوجد مقعد"]); render(<PlayerCompensation enrollmentId="e" />); await open(); fireEvent.change(screen.getByLabelText("الحصة التعويضية"), { target: { value: "t" } }); fireEvent.click(screen.getByText("معاينة حجز التعويض")); expect(await screen.findByText("تأكيد حجز التعويض")).toBeDisabled(); fireEvent.change(screen.getByLabelText("الحصة التعويضية"), { target: { value: "" } }); expect(screen.queryByText("تأكيد حجز التعويض")).not.toBeInTheDocument();
});
it("requires fresh review after stale credit or calendar rejection", async () => {
  setup([], true); render(<PlayerCompensation enrollmentId="e" />); await open(); fireEvent.change(screen.getByLabelText("الحصة التعويضية"), { target: { value: "t" } }); fireEvent.click(screen.getByText("معاينة حجز التعويض")); fireEvent.click(await screen.findByText("تأكيد حجز التعويض")); await screen.findByText("تغيرت الحصة؛ أعد المعاينة."); expect(screen.queryByText("تأكيد حجز التعويض")).not.toBeInTheDocument();
});
it("shows the Guardian source, validity, unbooked units and actual reserved session without mutation controls", () => { render(<CompensationCards items={[item]} />); expect(screen.getByLabelText("التعويضات المعتمدة")).toHaveTextContent("60 دقيقة"); expect(screen.getByText(/استُخدم 0 · غير محجوز 0/)).toBeVisible(); expect(screen.getByText(/موعد تعويضي/)).toBeVisible(); expect(screen.queryByRole("button")).not.toBeInTheDocument(); });
