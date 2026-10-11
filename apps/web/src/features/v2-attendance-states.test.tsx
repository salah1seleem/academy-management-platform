import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerAttendance, StaffAttendance } from "./attendance";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const row = { sportEnrollmentId: "e", playerId: "p", playerCode: "DEMO-1", arabicName: "لاعب اصطناعي", attendanceStatus: "NotRecorded", version: 41, sessionConsumed: false };
const session = { id: "s", sessionDate: "2026-10-11", startTime: "17:00", endTime: "18:00", status: "Scheduled", group: "سباحة تجريبية", sport: "سباحة", branch: "فرع تجريبي" };
function mockPlayer(conflict = false, reloadFails = false) {
  let saved = false;
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("/csrf")) return { ok: true, json: async () => ({ token: "test" }) };
    if (init?.method === "PUT") { saved = true; return { ok: !conflict, json: async () => conflict ? ({ message: "تغير السجل؛ راجع الكشف قبل الحفظ." }) : ({ items: [] }) }; }
    return { ok: !(saved && reloadFails), json: async () => ({ canManageAttendance: true, lastSynchronizedAtUtc: "2026-10-11T09:00:00Z", session, players: [{ ...row, attendanceStatus: saved ? "Late" : row.attendanceStatus, version: saved ? 42 : 41 }] }) };
  }) as unknown as typeof fetch;
}
it("sends the loaded version, supports five states and reloads the confirmed snapshot", async () => {
  mockPlayer(); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  expect(screen.getByText(/آخر مزامنة مؤكدة/)).toBeVisible();
  for (const name of ["حاضر", "متأخر", "غائب", "معتذر", "لم يُسجل"]) expect(screen.getByRole("button", { name })).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "متأخر" })); fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  await screen.findByText("تم حفظ الحضور بنجاح.");
  const write = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "PUT"); expect(JSON.parse(String(write?.[1]?.body)).items).toEqual([{ sportEnrollmentId: "e", status: 4, expectedVersion: 41 }]);
  expect(screen.getByRole("button", { name: "متأخر" })).toHaveAttribute("aria-pressed", "true");
});
it("keeps a conflicting draft without reporting successful synchronization", async () => {
  mockPlayer(true); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  fireEvent.click(screen.getByRole("button", { name: "معتذر" })); fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  await screen.findByText("تغير السجل؛ راجع الكشف قبل الحفظ."); expect(screen.queryByText("تم حفظ الحضور بنجاح.")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "معتذر" })).toHaveAttribute("aria-pressed", "true");
});
it("does not claim a refreshed snapshot when the post-save reload fails", async () => {
  mockPlayer(false, true); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" })); await screen.findByText(/تعذر تحديث الكشف/); expect(screen.queryByText("تم حفظ الحضور بنجاح.")).not.toBeInTheDocument();
});
for (const manage of [false, true]) it(`staff attendance uses server permissions (${manage}) and row versions`, async () => {
  global.fetch = vi.fn(async (url, init) => ({ ok: true, json: async () => String(url).endsWith("/my-permissions") ? manage ? ["attendance.manage"] : ["attendance.read"] : String(url).endsWith("/csrf") ? { token: "test" } : init?.method === "PUT" ? {} : [{ academyMembershipId: "c", name: "مدرب اصطناعي", role: "Coach", attendanceStatus: "Late", version: 73 }] })) as unknown as typeof fetch;
  render(<StaffAttendance sessionId="s" />); await screen.findByText("مدرب اصطناعي");
  if (!manage) { expect(screen.queryByRole("button", { name: "حفظ حضور المدربين" })).not.toBeInTheDocument(); expect(screen.getByText("متأخر")).toBeVisible(); return; }
  fireEvent.click(screen.getByRole("button", { name: "معتذر" })); fireEvent.click(screen.getByRole("button", { name: "حفظ حضور المدربين" }));
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "PUT")).toBe(true)); await screen.findByText("تم حفظ حضور الجهاز الفني.");
  const write = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "PUT"); expect(JSON.parse(String(write?.[1]?.body)).items).toEqual([{ academyMembershipId: "c", status: 5, expectedVersion: 73 }]);
});
