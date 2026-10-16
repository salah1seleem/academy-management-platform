import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DailyCoachRecord, DailyCoaches } from "./daily-staff-attendance";
import { DateFilterDefaults } from "../components/date-filter-defaults";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
const range = { from: "2026-10-01", to: "2026-10-08" };
const coach = { id: "coach-1", name: "مدرب تجريبي", today: range.to, workDate: range.to, status: "NotRecorded", lateMinutes: 0, approvedDeduction: 0, suggestedDeduction: 0 };
const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); push.mockReset(); });

it("keeps an overnight checkout date when saving the selected workday", async () => {
  global.fetch = vi.fn((input, init) => String(input).includes("csrf") ? response({token:"test"}) : String(input).includes("schedule") ? response([]) : init?.method === "PUT" ? response({status:"Present"}) : response({...coach,workDate:"2026-10-07",status:"Present",checkIn:"23:30:00",checkOut:"04:00:00",checkOutDate:"2026-10-08"})) as typeof fetch;
  render(<DateFilterDefaults range={range}><DailyCoachRecord id={coach.id} initialDate="2026-10-07" /></DateFilterDefaults>);
  expect(await screen.findByLabelText("تاريخ الانصراف")).toHaveValue("2026-10-08");
  fireEvent.click(screen.getByRole("button",{name:"حفظ الحضور"})); await waitFor(() => expect(push).toHaveBeenCalled());
  const save=vi.mocked(fetch).mock.calls.find(([,init])=>init?.method==="PUT");
  expect(JSON.parse(String(save?.[1]?.body))).toMatchObject({date:"2026-10-07",checkIn:"23:30:00",checkOut:"04:00:00",checkOutDate:"2026-10-08"});
});

it("defaults to today, searches by coach name and opens only the selected record in a separate route", async () => {
  global.fetch = vi.fn(() => response({ items: [coach, { ...coach, id: "coach-2", name: "مدرب آخر" }] })) as typeof fetch;
  render(<DateFilterDefaults range={range}><DailyCoaches /></DateFilterDefaults>);
  expect(screen.getByLabelText("التاريخ")).toHaveValue(range.to);
  await screen.findByText(coach.name); fireEvent.change(screen.getByLabelText("البحث بالاسم"), { target: { value: coach.name } });
  expect(screen.queryByText("مدرب آخر")).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "تسجيل / تعديل" })).toHaveAttribute("href", "/dashboard/attendance/staff/coach-1?date=2026-10-08");
  expect(screen.queryByRole("button", { name: "حفظ الحضور" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "مسح" }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/manage/staff-attendance/?allDates=true", expect.anything()));
  expect(screen.getByLabelText("التاريخ")).toHaveValue(""); expect(await screen.findByText("مدرب آخر")).toBeVisible();
});

it("records absence without check-in, sends a deduction with reason and returns after success", async () => {
  global.fetch = vi.fn((input, init) => String(input).includes("csrf") ? response({ token: "test" }) : String(input).includes("schedule") ? response([]) : init?.method === "PUT" ? response({ status: "Absent" }) : response(coach)) as typeof fetch;
  render(<DateFilterDefaults range={range}><DailyCoachRecord id={coach.id} /></DateFilterDefaults>);
  await screen.findByText(`حضور ${coach.name}`);
  fireEvent.change(screen.getByLabelText("الحالة"), { target: { value: "Absent" } });
  fireEvent.change(screen.getByLabelText("الخصم المعتمد"), { target: { value: "100" } });
  const reason = screen.getByLabelText("سبب الخصم (مطلوب)"); expect(reason).toBeRequired();
  fireEvent.change(reason, { target: { value: "غياب تجريبي" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/attendance/staff"));
  const save = vi.mocked(fetch).mock.calls.find(([, options]) => options?.method === "PUT");
  expect(JSON.parse(String(save?.[1]?.body))).toEqual({ date: range.to, status: "Absent", checkIn: null, checkOut: null, approvedDeduction: 100, reason: "غياب تجريبي" });
});

it("requires check-in when present, prevents future saves and never navigates after a failed save", async () => {
  global.fetch = vi.fn((input, init) => String(input).includes("csrf") ? response({ token: "test" }) : String(input).includes("schedule") ? response([]) : init?.method === "PUT" ? response({ message: "تعذر الحفظ التجريبي" }, 422) : response(coach)) as typeof fetch;
  render(<DateFilterDefaults range={range}><DailyCoachRecord id={coach.id} /></DateFilterDefaults>);
  await screen.findByText(`حضور ${coach.name}`); fireEvent.change(screen.getByLabelText("الحالة"), { target: { value: "Absent" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ الحضور" }));
  expect(await screen.findByText("تعذر الحفظ التجريبي")).toBeVisible(); expect(push).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText("التاريخ"), { target: { value: "2026-10-09" } });
  expect(await screen.findByText("لا يمكن تسجيل الحضور بتاريخ مستقبلي.")).toBeVisible(); expect(screen.getByRole("button", { name: "حفظ الحضور" })).toBeDisabled();
});

it("requires all three check-in segments when present", async () => {
  global.fetch = vi.fn(input => String(input).includes("schedule") ? response([]) : response({ ...coach, status: "Present" })) as typeof fetch;
  render(<DateFilterDefaults range={range}><DailyCoachRecord id={coach.id} /></DateFilterDefaults>);
  expect(await screen.findByLabelText("وقت الحضور — الساعة")).toBeRequired();
  expect(screen.getByLabelText("وقت الحضور — الدقيقة")).toBeRequired();
  expect(screen.getByLabelText("وقت الحضور — الفترة")).toBeRequired();
});
