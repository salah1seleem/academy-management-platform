import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { CoachPrivateServices } from "./coach-private-services";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status }));
const row = { id: "delivery", documentId: "document", player: "طفل اصطناعي", branch: "الرحاب", sport: "كرة القدم", serviceDate: "2026-10-01", ordinal: 1, sessionCount: 8, variableMethod: "Percent", variableValue: 40, earned: 50, contingent: 50, currency: "EGP" };
const options = { packages: [{ id: "package", documentId: "document", player: "طفل اصطناعي", plan: "8 حصص", sessionCount: 8, delivered: 1 }], documents: [{ id: "document", player: "طفل جديد", plan: "8 حصص", sessionCount: 8 }], sessions: [{ id: "session", group: "خاص", sessionDate: "2026-10-01", startTime: "10:00:00" }] };
it("shows earned and contingent separately without claiming payroll posting and links the existing source", async () => {
  global.fetch = vi.fn(() => response({ canReview: false, items: [row] })) as typeof fetch;
  render(<CoachPrivateServices coachId="coach" />); await screen.findByText(/طفل اصطناعي/);
  expect(screen.getByText("عمولة مستوفية للشروط — غير مرحّلة")).toBeVisible(); expect(screen.getByText("معلقة على التحصيل — ليست مستحقًا")).toBeVisible();
  expect(screen.getByRole("link", { name: "مصدر التحصيل" })).toHaveAttribute("href", "/dashboard/finance/receivables/document");
  expect(screen.queryByRole("button", { name: "مراجعة واعتماد التنفيذ" })).not.toBeInTheDocument();
});
it("requires confirmation before persisting an existing invoice allocation and preserves the source count", async () => {
  global.fetch = vi.fn((url, init) => String(url).includes("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? response({ id: "package" }, 201) : String(url).endsWith("options") ? response(options) : response({ canReview: true, items: [] })) as typeof fetch;
  render(<CoachPrivateServices coachId="coach" />); fireEvent.click(await screen.findByText("مراجعة تنفيذ حصة خاصة")); await screen.findByLabelText("المطالبة الموجودة");
  fireEvent.change(screen.getByLabelText("المطالبة الموجودة"), { target: { value: "document" } }); fireEvent.submit(screen.getByRole("form", { name: "ربط مطالبة الخاص" }));
  expect(vi.mocked(fetch).mock.calls.some(([, o]) => o?.method === "POST")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد مراجعة الخدمة" })); await screen.findByRole("status");
  const request = vi.mocked(fetch).mock.calls.find(([, o]) => o?.method === "POST"); expect(JSON.parse(String(request?.[1]?.body))).toEqual({ documentId: "document", sessionCount: 8 });
  expect(String(request?.[0])).toContain("private-services/packages");
});
it("explicitly reviews session and ordinal, never accepts coach rate or amount from this form", async () => {
  global.fetch = vi.fn((url, init) => String(url).includes("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? response({}, 201) : String(url).endsWith("options") ? response(options) : response({ canReview: true, items: [] })) as typeof fetch;
  render(<CoachPrivateServices coachId="coach" />); fireEvent.click(await screen.findByText("مراجعة تنفيذ حصة خاصة")); await screen.findByLabelText("باقة الخدمة");
  fireEvent.change(screen.getByLabelText("باقة الخدمة"), { target: { value: "package" } }); fireEvent.change(screen.getByLabelText("الحصة المنفذة"), { target: { value: "session" } }); fireEvent.change(screen.getByLabelText("رقم الحصة في الباقة"), { target: { value: "2" } });
  fireEvent.submit(screen.getByRole("form", { name: "اعتماد تنفيذ الخاص" })); expect(screen.getByRole("dialog")).toHaveTextContent("الحضور وحده ليس الإثبات");
  fireEvent.click(screen.getByRole("button", { name: "تأكيد مراجعة الخدمة" })); await screen.findByRole("status");
  const request = vi.mocked(fetch).mock.calls.find(([, o]) => o?.method === "POST"); expect(JSON.parse(String(request?.[1]?.body))).toEqual({ trainingSessionId: "session", ordinal: 2 });
});
it("server denial hides private financial content", async () => {
  global.fetch = vi.fn(() => response({}, 403)) as typeof fetch; render(<CoachPrivateServices coachId="coach" />);
  await waitFor(() => expect(screen.queryByRole("region", { name: "تنفيذ الخاص وعمولاته" })).not.toBeInTheDocument());
});
it("a later conflicting attendance marks the assessment incomplete instead of showing a fake zero commission", async () => {
  global.fetch = vi.fn(() => response({ canReview: false, items: [{ ...row, earned: null, contingent: null, requiresReview: true }] })) as typeof fetch;
  render(<CoachPrivateServices coachId="coach" />); expect(await screen.findByRole("alert")).toHaveTextContent("التقييم غير مكتمل");
  expect(screen.getByText("تعارض يتطلب مراجعة — العمولة غير محسوبة")).toBeVisible();
  expect(screen.queryByText(/مستوفية: 0/)).not.toBeInTheDocument();
});
