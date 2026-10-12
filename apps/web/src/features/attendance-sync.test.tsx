import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerAttendance, StaffAttendance } from "./attendance";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const row = { sportEnrollmentId: "e", playerId: "p", arabicName: "لاعب مسودة اصطناعي", playerCode: "SYNC-TEST", attendanceStatus: "NotRecorded", version: 0 };
const session = { id: "s", sessionDate: "2026-10-12", startTime: "17:00", endTime: "18:00", status: "Scheduled", group: "مجموعة تجريبية", sport: "سباحة", branch: "اختبار" };
function setup() {
  let server = { ...row }, scope = "account-academy-session", fail = true, count = 0, manage = true;
  const writes: { items: { status: number; expectedVersion: number }[]; draftLease: string }[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("/csrf")) return { ok: true, status: 200, json: async () => ({ token: "csrf" }) };
    if (init?.method === "PUT") {
      writes.push(JSON.parse(String(init.body))); count++;
      if (fail) throw new TypeError("offline");
      server = { ...server, attendanceStatus: "Present", version: 9 };
      return { ok: true, status: 200, json: async () => ({}) };
    }
    return { ok: true, status: 200, json: async () => ({ session, canManageAttendance: manage, players: [server], draftScope: scope, draftLease: "short-lease", draftExpiresAtUtc: "2100-01-01T00:00:00Z" }) };
  }) as unknown as typeof fetch;
  return { writes, get count() { return count; }, online: () => { fail = false; }, remote: (status: string, version: number) => { server = { ...server, attendanceStatus: status, version }; }, switchAccount: () => { scope = "another-account"; }, revoke: () => { manage = false; } };
}
async function pending() {
  render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  fireEvent.click(screen.getByRole("button", { name: "حاضر" })); fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  await screen.findByText("بانتظار المزامنة");
  expect(screen.getByRole("button", { name: "حاضر" })).toBeDisabled();
  expect(screen.queryByText("تم حفظ الحضور بنجاح.")).not.toBeInTheDocument();
}
const retry = () => fireEvent.click(screen.getByRole("button", { name: "مراجعة وإعادة المزامنة" }));
it("keeps a volatile draft and retries only after explicit action and server refresh", async () => {
  const api = setup(); await pending(); api.online(); window.dispatchEvent(new Event("online")); expect(api.count).toBe(1);
  retry(); await screen.findByText("تم حفظ الحضور بنجاح."); expect(api.count).toBe(2);
  expect(api.writes[1]).toEqual({ draftLease: "short-lease", items: [{ sportEnrollmentId: "e", status: 2, expectedVersion: 0 }] });
  expect(screen.queryByText("بانتظار المزامنة")).not.toBeInTheDocument();
});
it("confirms a lost successful response without replaying a write or charging a session twice", async () => {
  const api = setup(); await pending(); api.remote("Present", 9); retry();
  await screen.findByText(/الحضور محفوظ بالفعل دون إعادة الإرسال/); expect(api.count).toBe(1);
});
for (const keep of [false, true]) it(`resolves a concurrent correction explicitly: keep local ${keep}`, async () => {
  const api = setup(); await pending(); api.remote("Excused", 8); api.online(); retry();
  const conflict = await screen.findByRole("region", { name: `تعارض حضور ${row.arabicName}` });
  expect(api.count).toBe(1); expect(screen.getByRole("button", { name: "مراجعة وإعادة المزامنة" })).toBeDisabled();
  fireEvent.click(within(conflict).getByRole("button", { name: keep ? "الاحتفاظ باختياري بعد المراجعة" : "اعتماد حالة الخادم" }));
  retry(); await screen.findByText(keep ? "تم حفظ الحضور بنجاح." : "لا توجد تغييرات جديدة للحفظ.");
  expect(api.count).toBe(keep ? 2 : 1); if (keep) expect(api.writes[1].items[0].expectedVersion).toBe(8);
});
for (const scenario of ["account", "permission", "expiry"] as const) it(`discards the draft and hides the old roster on ${scenario}`, async () => {
  const api = setup(); let now = 1000; vi.spyOn(Date, "now").mockImplementation(() => now);
  await pending();
  if (scenario === "account") api.switchAccount(); if (scenario === "permission") api.revoke(); if (scenario === "expiry") now += 16 * 60_000;
  retry(); await screen.findByText(/لا يمكن إرسال هذه المسودة/);
  expect(screen.queryByText(row.arabicName)).not.toBeInTheDocument(); expect(api.count).toBe(1);
});
it("discard reloads remote status and does not retain a queue on remount", async () => {
  const api = setup(); await pending(); api.remote("Excused", 8);
  fireEvent.click(screen.getByRole("button", { name: "تجاهل المسودة وفتح كشف محدث" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "معتذر" })).toHaveAttribute("aria-pressed", "true"));
  cleanup(); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName); expect(screen.queryByText("بانتظار المزامنة")).not.toBeInTheDocument(); expect(api.count).toBe(1);
});
it("scrubs the roster and draft when navigation retains the page in browser history", async () => {
  const api = setup(); await pending();
  fireEvent(window, new Event("pagehide")); await screen.findByText(/لا يمكن إرسال هذه المسودة/);
  expect(screen.queryByText(row.arabicName)).not.toBeInTheDocument(); expect(screen.queryByText("بانتظار المزامنة")).not.toBeInTheDocument(); expect(api.count).toBe(1);
});
it("staff drafts use the same refresh-before-retry protection", async () => {
  let fail = true, writes = 0;
  global.fetch = vi.fn(async (url, init) => {
    if (init?.method === "PUT") { writes++; if (fail) throw new TypeError("offline"); }
    return { ok: true, status: 200, json: async () => String(url).endsWith("/csrf") ? { token: "c" } : String(url).endsWith("/my-permissions") ? ["attendance.manage"] : String(url).endsWith("/correction-context") ? { canManageAttendance: true, draftScope: "scope", draftLease: "lease" } : [{ academyMembershipId: "coach", name: "مدرب مسودة اصطناعي", attendanceStatus: writes > 1 ? "Present" : "NotRecorded", version: writes > 1 ? 10 : 0 }] };
  }) as unknown as typeof fetch;
  render(<StaffAttendance sessionId="s" />); await screen.findByText("مدرب مسودة اصطناعي");
  fireEvent.click(screen.getByRole("button", { name: "حاضر" })); fireEvent.click(screen.getByRole("button", { name: "حفظ حضور المدربين" }));
  await screen.findByText("بانتظار المزامنة"); fail = false; retry(); await screen.findByText("تم حفظ حضور الجهاز الفني."); expect(writes).toBe(2);
});
