import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AssignedFollowUps } from "./assigned-followups";
import { StaffActionMatrix } from "./staff-access";
const item = { id: "task", version: 4, state: "Assigned", condition: "GroupLowOccupancy", conditionActive: true, overdue: true, dueOn: "2026-10-18", note: "ملاحظة اصطناعية", resolutionReason: null, sourceUrl: "/dashboard/academy/groups/group", source: { name: "مجموعة العرض", branch: "فرع التجربة", sport: "كرة القدم", enrollments: 3, capacity: 20 } };
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("an operational delegate sees draft counts without child source links or publishing controls",async()=>{
  global.fetch=vi.fn(async()=>Response.json({item:{...item,condition:"GroupEvaluationDrafts",source:{...item.source,evaluationDrafts:{draftCount:2,playerCount:1}}},canEdit:true,canOpenSource:false,history:[]})) as typeof fetch;
  render(<AssignedFollowUps id="task"/>);expect(await screen.findByText(/2 مسودة · 1 لاعب فريد/)).toBeVisible();expect(screen.queryByRole("link",{name:/مراجعة تقييم/})).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:/نشر التقييم/})).not.toBeInTheDocument();
});
it("shows only the server inbox with month defaults and clear removes all filters including defaults", async () => {
  const requests: string[] = [];
  global.fetch = vi.fn(async path => { requests.push(String(path)); return Response.json({ from: "2026-10-01", to: "2026-10-17", items: [item], canEdit: true, canOpenSource: false }); }) as typeof fetch;
  render(<AssignedFollowUps/>); expect(await screen.findByRole("link", { name: "فتح المتابعة" })).toHaveAttribute("href", "/dashboard/my-followups/task");
  expect(screen.getByText("إشغال أقل من نصف السعة")).toBeVisible();expect(screen.getByText(/تسجيلات فعالة مسندة: 3، السعة: 20/)).toBeVisible();
  expect(screen.getByLabelText("من تاريخ")).toHaveValue("2026-10-01"); fireEvent.click(screen.getByRole("button", { name: /^مسح$/ }));
  expect(screen.getByLabelText("من تاريخ")).toHaveValue(""); expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("");
  await waitFor(() => expect(requests.at(-1)).toContain("allDates=true&state="));
  expect(screen.queryByRole("button", { name: "إنشاء متابعة" })).not.toBeInTheDocument();
});
it("posts only the delegated fields and version then shows saved history without an assignee selector", async () => {
  let saved = false; let body: Record<string, unknown> | undefined;
  global.fetch = vi.fn(async (path, options) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (options?.method === "POST") { body = JSON.parse(String(options.body)); saved = true; return Response.json({ id: "task" }); }
    return Response.json({ item: { ...item, state: saved ? "Closed" : "Assigned", version: saved ? 5 : 4 }, canEdit: !saved, canOpenSource: false, history: saved ? [{ id: "h", createdAtUtc: "2026-10-17T10:00:00Z", after: JSON.stringify({ State: "Closed", ResolutionReason: "متابعة بشرية فقط" }) }] : [] });
  }) as typeof fetch;
  render(<AssignedFollowUps id="task"/>); await screen.findByRole("button", { name: "حفظ تحديث المتابعة" });
  expect(screen.queryByLabelText("المسؤول", { exact: true })).not.toBeInTheDocument(); expect(screen.queryByRole("link", { name: "فتح المجموعة نفسها" })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("حالة المتابعة"), { target: { value: "Closed" } });
  expect(screen.getByLabelText("سبب إغلاق أو رفض المتابعة")).toBeRequired(); fireEvent.change(screen.getByLabelText("سبب إغلاق أو رفض المتابعة"), { target: { value: "متابعة بشرية فقط" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ تحديث المتابعة" }));
  expect(await screen.findByText("تم تحديث المتابعة وتسجيل الإجراء.")).toBeVisible();
  expect(body).toMatchObject({ version: 4, state: "Closed", resolutionReason: "متابعة بشرية فقط" }); expect(body).not.toHaveProperty("assignedToMembershipId"); expect(body).not.toHaveProperty("trainingGroupId"); expect(body?.requestId).toMatch(/^[a-f0-9-]{36}$/);
  await screen.findByText(/للقراءة فقط أو متابعة مغلقة/); expect(screen.queryByRole("button", { name: "حفظ تحديث المتابعة" })).not.toBeInTheDocument();
});
it("removes stale task data and actions when authorization is revoked on focus", async () => {
  global.fetch = vi.fn().mockResolvedValueOnce(Response.json({ item, canEdit: true, canOpenSource: true, history: [] })).mockResolvedValue(Response.json({}, { status: 404 }));
  render(<AssignedFollowUps id="task"/>); await screen.findByRole("link", { name: "فتح المجموعة نفسها" }); fireEvent(window, new Event("focus"));
  expect(await screen.findByRole("alert")).toHaveTextContent("غير متاحة"); expect(screen.queryByText("مجموعة العرض")).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "حفظ تحديث المتابعة" })).not.toBeInTheDocument();
});
it("the existing matrix permits read/edit for own tasks but not create/delete", () => {
  render(<StaffActionMatrix actions={[{ module: "followups", read: false, edit: false, add: false, delete: false }]} onChange={vi.fn()}/>);
  expect(screen.getByRole("switch", { name: "قراءة المتابعات المسندة" })).toBeEnabled(); expect(screen.getByRole("switch", { name: "تعديل المتابعات المسندة" })).toBeEnabled();
  expect(screen.getByRole("switch", { name: "إضافة المتابعات المسندة" })).toBeDisabled(); expect(screen.getByRole("switch", { name: "حذف المتابعات المسندة" })).toBeDisabled();
});
