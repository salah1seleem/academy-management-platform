import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { GroupWorkspace } from "./group-workspace";
import { GroupSessions } from "./group-sessions";
import { TimeField } from "../components/time-field";
import { WeekdaysField } from "../components/weekdays-field";
import { csrfRequest } from "./dashboard-api";
import SessionsPage from "../app/dashboard/academy/groups/sessions/page";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push, replace: vi.fn() }), usePathname: () => "/dashboard/academy/groups/group", useSearchParams: () => new URLSearchParams("tab=schedule") }));
const response = (value: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(value), { status }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.useRealTimers(); push.mockClear(); });

describe("training timetable UX", () => {
  it("starts Saturday and sends actual weekday values, with visible checked state", () => {
    render(<form><WeekdaysField /></form>);
    expect(screen.getAllByRole("checkbox").map(x => x.getAttribute("value"))).toEqual(["6", "0", "1", "2", "3", "4", "5"]);
    for (const day of ["السبت", "الاثنين", "الأربعاء"]) fireEvent.click(screen.getByRole("checkbox", { name: day }));
    expect(screen.getByRole("checkbox", { name: "السبت" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "الأحد" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "السبت" }).closest("label")).toHaveClass("selected");
    expect(new FormData(screen.getByRole("group").closest("form")!).getAll("days")).toEqual(["6", "1", "3"]);
  });
  it("restores both numeric and string weekdays without shifting them", () => {
    render(<WeekdaysField initialDays={[6, "Monday", "Wednesday"]} />);
    expect(screen.getAllByRole("checkbox").filter(x => (x as HTMLInputElement).checked).map(x => x.getAttribute("value"))).toEqual(["6", "1", "3"]);
  });
  it("converts noon and midnight correctly, preserves minutes and clears optional times", () => {
    render(<form aria-label="الوقت"><TimeField name="time" label="موعد الحصة" defaultValue="17:07:00" /></form>);
    const form = screen.getByRole("form") as HTMLFormElement;
    expect(new FormData(form).get("time")).toBe("17:07");
    fireEvent.change(screen.getByLabelText("موعد الحصة — الساعة"), { target: { value: "12" } });
    expect(new FormData(form).get("time")).toBe("12:07");
    fireEvent.change(screen.getByLabelText("موعد الحصة — الفترة"), { target: { value: "am" } });
    expect(new FormData(form).get("time")).toBe("00:07");
    fireEvent.click(screen.getByRole("button", { name: "مسح الوقت" }));
    expect(new FormData(form).get("time")).toBe("");
    fireEvent.change(screen.getByLabelText("موعد الحصة — الساعة"), { target: { value: "5" } });
    expect(screen.getByLabelText("موعد الحصة — الدقيقة")).toBeRequired();
  });
  it("saves exactly Saturday/Monday/Wednesday, removes explanatory text and returns to groups", async () => {
    const writes: unknown[] = [];
    global.fetch = vi.fn((input, init) => String(input).includes("csrf") ? response({ token: "token" }) : init ? (writes.push(JSON.parse(String(init.body))), response({ created: 6 })) : response({ players: [], schedules: [{ dayOfWeek: "Saturday", startTime: "17:00:00", endTime: "18:30:00", validFrom: "2026-10-03", validTo: "2026-10-17" }], sessions: [] })) as typeof fetch;
    render(<GroupWorkspace id="group" />);
    await screen.findByRole("checkbox", { name: "السبت" });
    fireEvent.click(screen.getByText("إنشاء الحصص / تعديل الجدول"));
    fireEvent.click(screen.getByRole("checkbox", { name: "الاثنين" })); fireEvent.click(screen.getByRole("checkbox", { name: "الأربعاء" }));
    expect(screen.queryByText(/حتى سنة/)).not.toBeInTheDocument();
    fireEvent.submit(screen.getByRole("button", { name: "حفظ وإنشاء الحصص" }).closest("form")!);
    await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/academy/groups"));
    expect(writes).toEqual([{ from: "2026-10-03", to: "2026-10-17", days: [6, 1, 3], startTime: "17:00:00", endTime: "18:30:00" }]);
  });
  it("never claims success or redirects when persistence fails", async () => {
    const saved = vi.fn(); window.addEventListener("academy-save-success", saved);
    global.fetch = vi.fn((input, init) => String(input).includes("csrf") ? response({ token: "token" }) : init ? response({ message: "تعذر الحفظ" }, 422) : response({ players: [], schedules: [{ dayOfWeek: 6, startTime: "17:00:00", endTime: "18:00:00" }], sessions: [] })) as typeof fetch;
    render(<GroupWorkspace id="group" />); await screen.findByRole("checkbox", { name: "السبت" });
    fireEvent.click(screen.getByText("إنشاء الحصص / تعديل الجدول"));
    fireEvent.submit(screen.getByRole("button", { name: "حفظ وإنشاء الحصص" }).closest("form")!);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر الحفظ");
    expect(push).not.toHaveBeenCalled(); expect(saved).not.toHaveBeenCalled(); window.removeEventListener("academy-save-success", saved);
  });
  it("emits an Arabic success notice only after a successful write", async () => {
    const saved = vi.fn(); window.addEventListener("academy-save-success", saved);
    global.fetch = vi.fn(input => response(String(input).includes("csrf") ? { token: "token" } : {})) as typeof fetch;
    await csrfRequest("/api/v1/manage/example", "PUT", {});
    expect(saved.mock.calls[0][0].detail).toBe("تم حفظ التعديلات بنجاح.");
    window.removeEventListener("academy-save-success", saved);
  });
  it("does not treat a read-only registration quote as a saved change", async () => {
    const saved = vi.fn(); window.addEventListener("academy-save-success", saved);
    global.fetch = vi.fn(input => response(String(input).includes("csrf") ? { token: "token" } : {})) as typeof fetch;
    await csrfRequest("/api/v1/people/registration-quote", "POST", {});
    expect(saved).not.toHaveBeenCalled(); window.removeEventListener("academy-save-success", saved);
  });
  it("filters all sessions by guardian and child without losing dropdown options or hanging on clear", async () => {
    const options = { groups: [{ id: "g", name: "براعم" }], coaches: [{ id: "c", name: "مدرب تجريبي" }], players: [{ id: "p", name: "لاعب تجريبي" }], guardians: [{ id: "a", name: "ولي تجريبي" }], branches: [], sports: [] };
    global.fetch = vi.fn(input => response(String(input).includes("options") ? options : [{ id: "s", trainingGroupId: "g", group: "براعم", coaches: ["مدرب تجريبي"], branch: "مدينة نصر", sport: "كرة القدم", sessionDate: "2026-10-03", startTime: "17:00:00", endTime: "18:30:00", playerCount: 2 }])) as typeof fetch;
    render(<GroupSessions initialDate="2026-10-03" />); await screen.findByRole("option", { name: "ولي تجريبي" }); await screen.findByText(/السبت ·/);
    expect(screen.getByLabelText("من تاريخ")).toHaveValue("2026-10-01"); expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-10-03");
    expect(fetch).toHaveBeenCalledWith("/api/v1/manage/sessions?from=2026-10-01&to=2026-10-03", expect.anything());
    fireEvent.change(screen.getByLabelText("ولي الأمر"), { target: { value: "a" } });
    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/manage/sessions?guardianId=a&from=2026-10-01&to=2026-10-03", expect.anything()));
    fireEvent.change(screen.getByLabelText("اللاعب"), { target: { value: "p" } });
    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/manage/sessions?playerId=p&guardianId=a&from=2026-10-01&to=2026-10-03", expect.anything()));
    await waitFor(() => expect(screen.getByRole("link", { name: "حضور اللاعبين" })).toHaveAttribute("href", "/dashboard/attendance/sessions/s/players?playerId=p"));
    expect(within(screen.getByLabelText("المجموعة")).getByRole("option", { name: "براعم" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("من تاريخ"), { target: { value: "2026-09-28" } });
    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/manage/sessions?playerId=p&guardianId=a&from=2026-09-28&to=2026-10-03", expect.anything()));
    fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
    await waitFor(() => expect(screen.getByRole("link", { name: "حضور اللاعبين" })).toHaveAttribute("href", "/dashboard/attendance/sessions/s/players"));
    expect(screen.getByLabelText("من تاريخ")).toHaveValue(""); expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("");
    fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" })); expect(screen.queryByText("جارٍ تحميل البيانات…")).not.toBeInTheDocument();
  });
  it("defaults to the real Cairo calendar day, not UTC or the fixed Demo reference date", async () => {
    vi.useFakeTimers({ toFake: ["Date"] }); vi.setSystemTime(new Date("2026-10-07T22:30:00Z"));
    const page = await SessionsPage({ searchParams: Promise.resolve({ groupId: "group" }) });
    expect(page.props.initialDate).toBe("2026-10-08"); expect(page.props.initialGroupId).toBe("group");
  });
});
