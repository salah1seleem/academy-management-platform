import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { CoachAgreements } from "./coach-agreements";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const draft = { id: "draft", branchId: "branch", sportId: "sport", branch: "الرحاب", sport: "كرة القدم", effectiveFrom: "2026-10-15", variableMethod: "Percent", variableValue: 40, eligibility: "DeliveredCollected", baseCoverage: "GroupOnly", currency: "EGP", baseSalarySnapshot: 3000 };
const initial = { configured: true, employeeId: "employee", monthlyBase: 3000, currency: "EGP", today: "2026-10-15", branches: [{ id: "branch", name: "الرحاب" }], sports: [{ id: "sport", name: "كرة القدم" }], items: [] };
const response = (data: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(data), { status }));

it("approval timestamp follows the trusted academy timezone rather than UTC date or browser timezone", async () => {
  global.fetch = vi.fn(url => String(url).includes("my-permissions") ? response([]) : response({ ...initial, timeZone: "Africa/Cairo", items: [{ ...draft, version: 1, approvedBy: "مالك العرض", approvedAtUtc: "2026-10-14T23:30:00Z" }] })) as typeof fetch;
  render(<CoachAgreements coachId="coach" />);
  const stamp = await screen.findByText(/اعتمد بواسطة مالك العرض/);
  expect(stamp).toHaveTextContent(/15.*10.*2026/); expect(stamp).toHaveTextContent("2:30");
});

it("requires an explicit value including zero and saves an idempotent draft before review", async () => {
  let items: unknown[] = [];
  global.fetch = vi.fn((url, init) => String(url).includes("my-permissions") ? response(["payroll.agreement.manage"]) : String(url).includes("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? (items = [draft], response({ id: "draft" }, 201)) : response({ ...initial, items })) as typeof fetch;
  render(<CoachAgreements coachId="coach" />); fireEvent.click(await screen.findByRole("button", { name: "نسخة اتفاق جديدة" }));
  expect(screen.getByLabelText("النسبة المئوية")).toHaveValue(null); expect(screen.getByLabelText("النسبة المئوية")).toBeRequired();
  fireEvent.change(screen.getByLabelText("فرع الاتفاق"), { target: { value: "branch" } }); fireEvent.change(screen.getByLabelText("رياضة الاتفاق"), { target: { value: "sport" } }); fireEvent.change(screen.getByLabelText("النسبة المئوية"), { target: { value: "40" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ مسودة الاتفاق" }));
  expect(await screen.findByText("تم حفظ مسودة الاتفاق؛ لا تصبح نافذة قبل الاعتماد.")).toBeVisible();
  const saved = vi.mocked(fetch).mock.calls.find(([, options]) => options?.method === "POST");
  expect(JSON.parse(String(saved?.[1]?.body))).toMatchObject({ variableValue: 40, eligibility: "DeliveredCollected", baseCoverage: "GroupOnly", effectiveFrom: initial.today });
  expect(saved?.[1]?.headers).toHaveProperty("Idempotency-Key"); expect(screen.queryByRole("form")).not.toBeInTheDocument();
  expect(screen.getByText(/مسودة غير نافذة/)).toBeVisible();
});
it("approves only after confirmation with the latest scoped version and keeps history visible", async () => {
  const previous = { ...draft, id: "previous", version: 1, variableValue: 30 }; let approved = false;
  global.fetch = vi.fn((url, init) => String(url).includes("my-permissions") ? response(["payroll.agreement.manage"]) : String(url).includes("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? (approved = true, response({ version: 2 })) : response({ ...initial, items: [{ ...draft, ...(approved ? { version: 2 } : {}) }, previous] })) as typeof fetch;
  render(<CoachAgreements coachId="coach" />); fireEvent.click(await screen.findByRole("button", { name: "مراجعة واعتماد" }));
  expect(vi.mocked(fetch).mock.calls.some(([, options]) => options?.method === "POST")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد اعتماد الاتفاق" })); await screen.findByText("تم اعتماد نسخة الاتفاق وحفظ شروطها دون تغيير النسخ السابقة.");
  const saved = vi.mocked(fetch).mock.calls.find(([, options]) => options?.method === "POST"); expect(JSON.parse(String(saved?.[1]?.body))).toEqual({ expectedLatestAgreementId: "previous" });
  expect(screen.getByText(/معتمد — نسخة 1/)).toBeVisible(); expect(screen.getByText(/معتمد — نسخة 2/)).toBeVisible();
});
it("module CRUD alone cannot edit rates or approve agreements", async () => {
  global.fetch = vi.fn(url => String(url).includes("my-permissions") ? response(["payroll.manage", "payroll.pay", "payroll.addition.review"]) : response({ ...initial, items: [draft] })) as typeof fetch;
  render(<CoachAgreements coachId="coach" />); await screen.findByText(/مسودة غير نافذة/);
  expect(screen.queryByRole("button", { name: "نسخة اتفاق جديدة" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "مراجعة واعتماد" })).not.toBeInTheDocument();
});
it("hides financial terms when server denies read and does not offer a fake save", async () => {
  global.fetch = vi.fn(url => String(url).includes("my-permissions") ? response([]) : response({}, 403)) as typeof fetch;
  render(<CoachAgreements coachId="coach" />); await waitFor(() => expect(screen.queryByRole("region", { name: "اتفاقات أجر الخاص" })).not.toBeInTheDocument());
});
