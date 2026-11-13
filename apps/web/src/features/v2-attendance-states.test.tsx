import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerAttendance, StaffAttendance } from "./attendance";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const row = { sportEnrollmentId: "e", playerId: "p", playerCode: "DEMO-1", arabicName: "لاعب اصطناعي", attendanceStatus: "NotRecorded", version: 41, sessionConsumed: false };
const session = { id: "s", sessionDate: "2026-10-11", startTime: "17:00", endTime: "18:00", status: "Scheduled", group: "سباحة تجريبية", sport: "سباحة", branch: "فرع تجريبي" };
function mockPlayer(conflict = false, reloadFails = false, persisted = true) {
  let saved = false;
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("/csrf")) return { ok: true, json: async () => ({ token: "test" }) };
    if (init?.method === "PUT") { saved = true; return { ok: !conflict, json: async () => conflict ? ({ message: "تغير السجل؛ راجع الكشف قبل الحفظ." }) : ({ items: [] }) }; }
    return { ok: !(saved && reloadFails), json: async () => ({ canManageAttendance: true, draftLease: "lease", draftScope: "scope", lastSynchronizedAtUtc: "2026-10-11T09:00:00Z", session, players: [{ ...row, attendanceStatus: saved && persisted ? "Late" : row.attendanceStatus, version: saved && persisted ? 42 : 41 }] }) };
  }) as unknown as typeof fetch;
}
it("sends the loaded version, supports five states and reloads the confirmed snapshot", async () => {
  mockPlayer(); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  expect(screen.getByRole("button", { name: "حفظ الحضور" })).toBeDisabled();
  for (const name of ["حاضر", "متأخر", "غائب", "معتذر", "لم يُسجل"]) expect(screen.getByRole("option", { name })).toBeInTheDocument();
  fireEvent.change(screen.getByRole("combobox",{name:/^حضور /}),{target:{value:"Late"}}); fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  await screen.findByText("تم حفظ الحضور بنجاح.");
  const write = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "PUT"); expect(JSON.parse(String(write?.[1]?.body)).items).toEqual([{ sportEnrollmentId: "e", status: 4, expectedVersion: 41 }]);
  expect(screen.getByRole("combobox",{name:/^حضور /})).toHaveValue("Late");
});
it("keeps a conflicting draft without reporting successful synchronization", async () => {
  mockPlayer(true); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  fireEvent.change(screen.getByRole("combobox",{name:/^حضور /}),{target:{value:"Excused"}}); fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  await screen.findByText("تغير السجل؛ راجع الكشف قبل الحفظ."); expect(screen.queryByText("تم حفظ الحضور بنجاح.")).not.toBeInTheDocument();
  expect(screen.getByRole("combobox",{name:/^حضور /})).toHaveValue("Excused");
});
it("does not claim a refreshed snapshot when the post-save reload fails", async () => {
  mockPlayer(false, true); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  fireEvent.change(screen.getByRole("combobox",{name:/^حضور /}),{target:{value:"Late"}});
  fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" })); await screen.findByText(/تعذر تحديث الكشف/); expect(screen.queryByText("تم حفظ الحضور بنجاح.")).not.toBeInTheDocument();
});
it("keeps the selected state when a successful write is missing from the readback", async () => {
  mockPlayer(false, false, false); render(<PlayerAttendance sessionId="s" />); await screen.findByText(row.arabicName);
  fireEvent.change(screen.getByRole("combobox", { name: /^حضور / }), { target: { value: "Late" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  expect(await screen.findByText("لم تظهر كل التغييرات في الكشف المحدث.")).toBeVisible();
  expect(screen.queryByText("تم حفظ الحضور بنجاح.")).not.toBeInTheDocument();
  expect(screen.getByRole("combobox", { name: /^حضور / })).toHaveValue("Late");
});
it("only bulk-marks unrecorded matches and preserves a decision made before filtering", async () => {
  const players = [
    { ...row, sportEnrollmentId: "first", playerCode: "GROUP-1", arabicName: "لاعب النتيجة الأول" },
    { ...row, sportEnrollmentId: "second", playerCode: "GROUP-2", arabicName: "لاعب النتيجة الثاني", attendanceStatus: "Excused" },
    { ...row, sportEnrollmentId: "third", playerCode: "OUT-3", arabicName: "لاعب خارج البحث" },
    { ...row, sportEnrollmentId: "fourth", playerCode: "OUT-4", arabicName: "لاعب لم يظهر" },
  ];
  global.fetch = vi.fn(async () => Response.json({ canManageAttendance: true, session, players }));
  render(<PlayerAttendance sessionId="s" />);
  const state = (name: string) => screen.getByRole("combobox", { name: `حضور ${name}` });
  await screen.findByText(players[0].arabicName);
  fireEvent.change(state(players[2].arabicName), { target: { value: "Absent" } });
  const search = screen.getByRole("textbox", { name: "بحث بالاسم أو كود اللاعب" });
  fireEvent.change(search, { target: { value: "group" } });
  fireEvent.click(screen.getByRole("button", { name: /اعتبار غير المسجلين في النتائج حاضرين \(1\)/ }));
  fireEvent.change(search, { target: { value: "" } });
  expect(state(players[0].arabicName)).toHaveValue("Present");
  expect(state(players[1].arabicName)).toHaveValue("Excused");
  expect(state(players[2].arabicName)).toHaveValue("Absent");
  expect(state(players[3].arabicName)).toHaveValue("NotRecorded");
  expect(screen.getByText("تغييرات قيد المراجعة (2)")).toBeVisible();
});
for (const manage of [false, true]) it(`staff attendance uses server permissions (${manage}) and row versions`, async () => {
  let saved = false;
  global.fetch = vi.fn(async (url, init) => {
    if (init?.method === "PUT") saved = true;
    return { ok: true, json: async () => String(url).endsWith("/sessions/s") ? session : String(url).endsWith("/correction-context") ? {canManageAttendance:manage,canRequestCorrection:false} : String(url).endsWith("/my-permissions") ? manage ? ["attendance.manage"] : ["attendance.read"] : String(url).endsWith("/csrf") ? { token: "test" } : init?.method === "PUT" ? {} : [{ academyMembershipId: "c", name: "مدرب اصطناعي", role: "Coach", attendanceStatus: saved ? "Excused" : "Late", version: saved ? 74 : 73 }] };
  }) as unknown as typeof fetch;
  render(<StaffAttendance sessionId="s" />); await screen.findByText("مدرب اصطناعي");
  if (!manage) { expect(screen.queryByRole("button", { name: "حفظ حضور المدربين" })).not.toBeInTheDocument(); expect(screen.getByText("متأخر")).toBeVisible(); return; }
  fireEvent.change(screen.getByRole("combobox",{name:/^حضور /}),{target:{value:"Excused"}}); fireEvent.click(screen.getByRole("button", { name: "حفظ حضور المدربين" }));
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "PUT")).toBe(true)); await screen.findByText("تم حفظ حضور الجهاز الفني.");
  const write = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "PUT"); expect(JSON.parse(String(write?.[1]?.body)).items).toEqual([{ academyMembershipId: "c", status: 5, expectedVersion: 73 }]);
});
