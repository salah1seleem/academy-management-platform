import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffAttendanceExceptionDetail, StaffAttendanceExceptionInbox } from "./staff-attendance-exceptions";

const row = { id: "claim", name: "موظف عرض", branch: "فرع العرض", workDate: "2026-10-16", operation: "CheckIn", status: "Pending", claimedAtUtc: "2026-10-16T08:00:00Z", receivedAtUtc: "2026-10-16T08:05:00Z", reason: "تعذر الحصول على موقع دقيق", version: 7 };
const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("shows the immutable requested period separately from the current source without switching the decision target", async () => {
  global.fetch = vi.fn(() => response({name:row.name,branch:row.branch,timeZone:"Africa/Cairo",canReview:false,request:{...row,operation:"CheckOut",targetPeriod:{id:"original",version:3,startDate:"2026-10-15",startTime:"14:00:00",timeZoneId:"Africa/Cairo"}},currentPeriod:{id:"original",version:4,startDate:"2026-10-15",startTime:"14:30:00"}})) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  expect(await screen.findByRole("complementary",{name:"الفترة المحددة"})).toHaveTextContent("14:00");
  expect(screen.getByText(/الفترة الحالية: 14:30/)).toBeVisible();
  expect(screen.getByText(/القرار يخص هذه الفترة فقط/)).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.every(([,init])=>!init?.method)).toBe(true);
});

it("lists pending source records, clears to all states and links to a separate detail", async () => {
  global.fetch = vi.fn(() => response({ total: 1, items: [row] })) as typeof fetch;
  render(<StaffAttendanceExceptionInbox />);
  expect(await screen.findByText(row.name)).toBeVisible();
  expect(screen.getByRole("link", { name: "عرض الطلب" })).toHaveAttribute("href", "/dashboard/attendance/staff/exceptions/claim");
  expect(screen.queryByRole("button", { name: "قبول وتسجيل إداري" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلتر" }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/manage/staff-attendance-exceptions/?status=", expect.anything()));
});

it("records the selected decision once, rereads persisted status and does not mutate financial fields", async () => {
  let accepted = false;
  global.fetch = vi.fn((path, init) => String(path).includes("csrf") ? response({ token: "test" }) : init?.method === "POST" ? (accepted = true, response({ status: "Approved" })) : response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: !accepted, request: { ...row, status: accepted ? "Approved" : "Pending" } })) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  fireEvent.click(await screen.findByRole("button", { name: "قبول وتسجيل إداري" }));
  expect(await screen.findByText("تم قبول الطلب وتسجيل الحضور إداريًا.")).toBeVisible();
  expect(screen.getByText("مقبول إداريًا")).toBeVisible();
  const writes = vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST");
  expect(writes).toHaveLength(1); expect(JSON.parse(String(writes[0][1]?.body))).toEqual({ approve: true, note: null, version: 7 });
  expect((writes[0][1]?.headers as Record<string, string>)["Idempotency-Key"]).toBeTruthy();
});

it("validates rejection, hides self/readonly controls and never writes on open", async () => {
  let canReview = true;
  global.fetch = vi.fn(() => response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview, request: row })) as typeof fetch;
  const page = render(<StaffAttendanceExceptionDetail id="claim" />);
  fireEvent.click(await screen.findByRole("button", { name: "رفض الطلب" }));
  expect(await screen.findByText("اكتب سبب الرفض من خمسة أحرف على الأقل.")).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.every(([, init]) => !init?.method)).toBe(true);
  canReview = false; page.unmount(); render(<StaffAttendanceExceptionDetail id="claim" />);
  expect(await screen.findByText(/لا يمكنك مراجعة هذا الطلب بنفسك/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "قبول وتسجيل إداري" })).not.toBeInTheDocument();
});

it("a lost reply retains the exact decision and idempotency key for retry", async () => {
  let posts = 0;
  global.fetch = vi.fn((path, init) => {
    if (String(path).includes("csrf")) return response({ token: "test" });
    if (init?.method === "POST") return ++posts === 1 ? Promise.reject(new Error("تعذر تأكيد القرار")) : response({ status: "Approved" });
    return response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: posts < 2, request: { ...row, status: posts < 2 ? "Pending" : "Approved" } });
  }) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  fireEvent.click(await screen.findByRole("button", { name: "قبول وتسجيل إداري" }));
  fireEvent.click(await screen.findByRole("button", { name: "إعادة تأكيد القرار" }));
  await screen.findByText("مقبول إداريًا");
  const writes = vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST");
  expect(writes).toHaveLength(2); expect(writes[0][1]?.body).toEqual(writes[1][1]?.body); expect(writes[0][1]?.headers).toEqual(writes[1][1]?.headers);
});
