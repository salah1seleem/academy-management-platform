import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffAttendanceExceptionDetail, StaffAttendanceExceptionInbox } from "./staff-attendance-exceptions";

const navigation = vi.hoisted(() => ({ query: "" }));
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(navigation.query),
}));

const row = { id: "claim", name: "موظف عرض", branch: "فرع العرض", workDate: "2026-10-16", operation: "CheckIn", status: "Pending", claimedAtUtc: "2026-10-16T08:00:00Z", receivedAtUtc: "2026-10-16T08:05:00Z", reason: "تعذر الحصول على موقع دقيق", version: 7 };
const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status }));
afterEach(() => { cleanup(); navigation.query = ""; vi.restoreAllMocks(); });

it("shows the immutable requested period separately from the current source without switching the decision target", async () => {
  global.fetch = vi.fn(() => response({name:row.name,branch:row.branch,timeZone:"Africa/Cairo",canReview:false,request:{...row,operation:"CheckOut",targetPeriod:{id:"original",version:3,startDate:"2026-10-15",startTime:"14:00:00",timeZoneId:"Africa/Cairo"}},currentPeriod:{id:"original",version:4,startDate:"2026-10-15",startTime:"14:30:00"}})) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  expect(await screen.findByRole("complementary",{name:"الفترة المحددة"})).toHaveTextContent("14:00");
  expect(screen.getByText(/الفترة الحالية: 14:30/)).toBeVisible();
  expect(screen.getByText(/القرار يخص هذه الفترة فقط/)).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.every(([,init])=>!init?.method)).toBe(true);
});

it("preserves the queue filter in the URL, detail link and return link", async () => {
  navigation.query = "status=Approved";
  global.fetch = vi.fn(() => response({ total: 1, items: [row] })) as typeof fetch;
  const page = render(<StaffAttendanceExceptionInbox />);
  expect(await screen.findByText(row.name)).toBeVisible();
  expect(screen.getByRole("link", { name: "عرض الطلب" })).toHaveAttribute("href", "/dashboard/attendance/staff/exceptions/claim?status=Approved");
  expect(screen.queryByRole("button", { name: "قبول وتسجيل إداري" })).not.toBeInTheDocument();
  const push = vi.spyOn(window.history, "pushState");
  fireEvent.change(screen.getByRole("combobox", { name: "حالة الطلب" }), { target: { value: "all" } });
  expect(push).toHaveBeenCalledWith(null, "", "/dashboard/attendance/staff/exceptions?status=all");
  navigation.query = "status=all";
  page.rerender(<StaffAttendanceExceptionInbox />);
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/manage/staff-attendance-exceptions/?status=", expect.anything()));
  page.unmount();
  render(<StaffAttendanceExceptionDetail id="claim" />);
  expect(screen.getByRole("link", { name: "العودة إلى الطلبات" })).toHaveAttribute("href", "/dashboard/attendance/staff/exceptions?status=all");
});

it("records the selected decision once, rereads persisted status and does not mutate financial fields", async () => {
  let accepted = false;
  global.fetch = vi.fn((path, init) => String(path).includes("csrf") ? response({ token: "test" }) : init?.method === "POST" ? (accepted = true, response({ id: row.id, status: "Approved" })) : response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: !accepted, request: { ...row, status: accepted ? "Approved" : "Pending" } })) as typeof fetch;
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
    if (init?.method === "POST") return ++posts === 1 ? Promise.reject(new Error("تعذر تأكيد القرار")) : response({ id: row.id, status: "Approved" });
    return response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: posts < 2, request: { ...row, status: posts < 2 ? "Pending" : "Approved" } });
  }) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  fireEvent.click(await screen.findByRole("button", { name: "قبول وتسجيل إداري" }));
  fireEvent.click(await screen.findByRole("button", { name: "تحقق من حالة الطلب" }));
  expect(await screen.findByText(/لا يزال الطلب بانتظار المراجعة/)).toBeVisible();
  expect(posts).toBe(1);
  fireEvent.click(screen.getByRole("button", { name: "إعادة إرسال القرار نفسه" }));
  await screen.findByText("مقبول إداريًا");
  const writes = vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST");
  expect(writes).toHaveLength(2); expect(writes[0][1]?.body).toEqual(writes[1][1]?.body); expect(writes[0][1]?.headers).toEqual(writes[1][1]?.headers);
});

it("reads a committed decision after a lost reply without posting again", async () => {
  let writes = 0;
  global.fetch = vi.fn((path, init) => {
    if (String(path).includes("csrf")) return response({ token: "test" });
    if (init?.method === "POST") {
      writes++;
      return Promise.reject(new Error("Lost response"));
    }
    return response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: writes === 0,
      request: { ...row, status: writes ? "Approved" : "Pending" } });
  }) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  fireEvent.click(await screen.findByRole("button", { name: "قبول وتسجيل إداري" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("لم يتأكد القرار");
  fireEvent.click(screen.getByRole("button", { name: "تحقق من حالة الطلب" }));
  expect(await screen.findByText("تم تحديث حالة الطلب.")).toBeVisible();
  expect(screen.getByText("مقبول إداريًا")).toBeVisible();
  expect(screen.queryByRole("button", { name: "إعادة إرسال القرار نفسه" })).not.toBeInTheDocument();
  expect(writes).toBe(1);
});

it("blocks replay after a definite conflict and clears stale data on a forbidden reload", async () => {
  let attempted = false;
  global.fetch = vi.fn((path, init) => {
    if (String(path).includes("csrf")) return response({ token: "test" });
    if (init?.method === "POST") {
      attempted = true;
      return response({ title: "تغير الطلب؛ أعد تحميل التفاصيل قبل المراجعة." }, 409);
    }
    return attempted ? response({}, 403) : response({ name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: true, request: row });
  }) as typeof fetch;
  render(<StaffAttendanceExceptionDetail id="claim" />);
  fireEvent.click(await screen.findByRole("button", { name: "قبول وتسجيل إداري" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تغير الطلب");
  expect(screen.queryByRole("button", { name: "إعادة إرسال القرار نفسه" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "تحقق من حالة الطلب" }));
  await screen.findByText("هذا الطلب غير متاح لحسابك.");
  expect(screen.queryByText(row.reason)).not.toBeInTheDocument();
});

it("keeps a mismatched record hidden and lets the reviewer retry the read", async () => {
  const detail = { name: row.name, branch: row.branch, timeZone: "Africa/Cairo", canReview: true, request: row };
  global.fetch = vi.fn()
    .mockImplementationOnce(() => response({ ...detail, request: { ...row, id: "other" } }))
    .mockImplementationOnce(() => response(detail));
  render(<StaffAttendanceExceptionDetail id="claim" />);
  const retry = await screen.findByRole("button", { name: "إعادة المحاولة" });
  expect(screen.queryByText(row.reason)).not.toBeInTheDocument();
  fireEvent.click(retry);
  expect(await screen.findByText(row.reason)).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.every(([, init]) => !init?.method)).toBe(true);
});
