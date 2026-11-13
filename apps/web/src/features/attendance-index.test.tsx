import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerAttendance, TrainingSessionsList } from "./attendance";

const base = "/dashboard/attendance/sessions";
const period = { from: "2026-11-08", to: "2026-11-08" };
const first = {
  id: "session-a", trainingGroupId: "a", sessionDate: "2026-11-08", startTime: "16:00:00",
  endTime: "17:00:00", status: "Scheduled", group: "ناشئو السباحة", sport: "السباحة",
  branch: "فرع العرض", playerCount: 3, recorded: 1, excused: 1,
};
const second = { ...first, id: "session-b", trainingGroupId: "b", group: "مجموعة المساء" };
const response = (value: unknown, status = 200) => Promise.resolve(Response.json(value, { status }));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, "", "/");
});

it("keeps other groups selectable and carries the exact filters to the roster", async () => {
  const read = vi.fn<typeof fetch>(() => response([first, second]));
  vi.stubGlobal("fetch", read);
  render(<TrainingSessionsList initial={period} />);
  await screen.findAllByRole("link", { name: "تسجيل الحضور" });
  fireEvent.change(screen.getByLabelText("فلتر المجموعة"), { target: { value: "a" } });
  expect(screen.getByRole("option", { name: second.group })).toBeInTheDocument();
  expect(screen.getAllByRole("link", { name: "تسجيل الحضور" })).toHaveLength(1);
  fireEvent.change(screen.getByRole("textbox", { name: "البحث بالاسم" }), { target: { value: "سباحة" } });
  const href = screen.getByRole("link", { name: "تسجيل الحضور" }).getAttribute("href")!;
  const target = new URL(href, window.location.origin);
  expect(target.pathname).toBe(`${base}/session-a/players`);
  const back = new URL(target.searchParams.get("returnTo")!, window.location.origin);
  expect(Object.fromEntries(back.searchParams)).toEqual({ ...period, groupId: "a", search: "سباحة" });
  expect(read.mock.calls.filter(([path]) => String(path).includes("/attendance/sessions"))).toHaveLength(1);
});

it("separates errors from empty results and re-reads the same period", async () => {
  let fail = false;
  vi.stubGlobal("fetch", vi.fn(() => fail ? response({}, 503) : response([first])));
  render(<TrainingSessionsList initial={period} />);
  await screen.findByRole("link", { name: "تسجيل الحضور" });
  fail = true;
  fireEvent.click(screen.getByRole("button", { name: "تحديث الحصص" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل الحصص");
  expect(screen.queryByRole("link", { name: "تسجيل الحضور" })).not.toBeInTheDocument();
  expect(screen.queryByText(/لا توجد حصص/)).not.toBeInTheDocument();
  fail = false;
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  await screen.findByRole("link", { name: "تسجيل الحضور" });
});

it("rejects reversed dates locally and treats one selected date as one day", async () => {
  const read = vi.fn<typeof fetch>(() => response([]));
  vi.stubGlobal("fetch", read);
  render(<TrainingSessionsList initial={{ from: "2026-11-10", to: "2026-11-08" }} />);
  expect(screen.getByRole("alert")).toHaveTextContent("تاريخ النهاية يجب ألا يسبق تاريخ البداية");
  expect(read.mock.calls.some(([path]) => String(path).includes("/attendance/sessions"))).toBe(false);
  fireEvent.change(screen.getByLabelText("إلى تاريخ"), { target: { value: "" } });
  await waitFor(() => expect(read).toHaveBeenCalledWith("/api/v1/attendance/sessions?from=2026-11-10&to=2026-11-10", expect.anything()));
});

it("limits the roster return link to the attendance list", async () => {
  vi.stubGlobal("fetch", vi.fn(() => response({ session: first, players: [], canManageAttendance: false })));
  render(<PlayerAttendance sessionId={first.id} returnTo="https://outside.example.test/dashboard/attendance/sessions?groupId=a" />);
  expect(await screen.findByRole("link", { name: "العودة إلى الحصص" })).toHaveAttribute("href", base);
});
