import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { EmployeeForm, EmployeeList } from "./employees";
import { EmployeeDeductions, PayrollRequests, PayrollWorkspace } from "./payroll";
import { DateFilterDefaults } from "../components/date-filter-defaults";

const push = vi.fn(), params = new URLSearchParams();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), useSearchParams: () => params }));
const range = { from: "2026-10-01", to: "2026-10-08" };
const employee = { id: "worker", name: "عامل عرض تجريبي", phone: "01022222222", jobTitle: "عامل", branchId: "branch", monthlySalary: 3100, currency: "EGP", contractStart: "2026-10-10", contractEnd: "2026-10-20", isActive: true };
const options = { items: [employee], branches: [{ id: "branch", name: "فرع تجريبي" }], accounts: [] };
const response = (data: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(data), { status }));
const renderPage = (page: React.ReactNode) => render(<DateFilterDefaults range={range}>{page}</DateFilterDefaults>);
afterEach(() => { cleanup(); vi.restoreAllMocks(); push.mockReset(); params.forEach((_, key) => params.delete(key)); });

it("private commission is an included subtotal linked to its source, never added to net salary twice", async () => {
  const salary = { employeeId: "worker", name: employee.name, contractDays: 31, baseDue: 3100, deductions: 20, additions: 100, privateCompensation: 80, coachMembershipId: "coach", netDue: 3180, currency: "EGP", isPaid: false };
  global.fetch = vi.fn(input => String(input).includes("my-permissions") ? response([]) : response({ currency: "EGP",items:[salary] })) as typeof fetch;
  renderPage(<PayrollWorkspace />); const source=await screen.findByRole("link",{name:/محتسبة بالصافي بالفعل/});
  expect(source).toHaveAttribute("href","/dashboard/academy/coaches/coach?tab=accounts"); expect(screen.getByText("منها صافي عمولة الخاص")).toBeVisible();
  expect(screen.getByText("إجمالي المستحق").closest("article")).toHaveTextContent(/3,180/);
  expect(screen.queryByText(/3,260/)).not.toBeInTheDocument();
});

it("module management alone exposes neither salary payment nor addition review", async () => {
  global.fetch = vi.fn(input => String(input).includes("my-permissions") ? response(["payroll.manage"]) : String(input).includes("requests") ? response([{ id: "claim", name: "موظف تجريبي", reasonName: "مباراة", status: "Pending" }]) : String(input).includes("reasons") ? response([]) : response({ currency: "EGP", items: [{ employeeId: "worker", name: employee.name, contractDays: 31, baseDue: 3100, netDue: 3100, deductions: 0, additions: 0, currency: "EGP", isPaid: false }] })) as typeof fetch;
  const view = renderPage(<PayrollWorkspace />); await screen.findByText("لم يُدفع");
  expect(screen.queryByRole("button", { name: "تأكيد الدفع" })).not.toBeInTheDocument(); view.unmount();
  renderPage(<PayrollRequests />); await screen.findByText("مباراة");
  expect(screen.queryByRole("button", { name: "مراجعة" })).not.toBeInTheDocument();
  expect(await screen.findByRole("heading", { name: "أسباب المبالغ الإضافية" })).toBeVisible();
});

it("review-only permission does not imply payroll configuration or salary payment", async () => {
  global.fetch = vi.fn(input => String(input).includes("my-permissions") ? response(["payroll.addition.review"]) : response([{ id: "claim", name: "موظف تجريبي", reasonName: "مباراة", status: "Pending" }])) as typeof fetch;
  renderPage(<PayrollRequests />); expect(await screen.findByRole("button", { name: "مراجعة" })).toBeVisible();
  expect(screen.queryByRole("heading", { name: "أسباب المبالغ الإضافية" })).not.toBeInTheDocument();
});

it("does not replace the selected payroll month with a slower previous response", async () => {
  let finishOld!: (response: Response) => void;
  const salary = { employeeId: employee.id, name: employee.name, monthlySalary: 3100, contractDays: 5, baseDue: 516.67, deductions: 0, additions: 0, netDue: 516.67, currency: "EGP", isPaid: false };
  global.fetch = vi.fn(input => {
    if (String(input).includes("my-permissions")) return response(["payroll.manage", "payroll.pay", "payroll.addition.review"]);
    if (String(input).includes("month=2026-10-01")) return new Promise<Response>(resolve => { finishOld = resolve; });
    return response({ currency: "EGP", items: [salary] });
  }) as typeof fetch;
  renderPage(<PayrollWorkspace />);
  await waitFor(() => expect(finishOld).toBeTypeOf("function"));
  fireEvent.change(screen.getByLabelText("شهر الرواتب"), { target: { value: "2026-09" } });
  await screen.findByText("5 يوم عقد في الشهر");
  await act(async () => { finishOld(new Response(JSON.stringify({ currency: "EGP", items: [{ ...salary, contractDays: 0, baseDue: 0, netDue: 0 }] }))); });
  expect(screen.getByText("5 يوم عقد في الشهر")).toBeVisible();
  expect(screen.queryByText("0 يوم عقد في الشهر")).not.toBeInTheDocument();
});

it("shows employees without accounts and opens an editable fixed contract", async () => {
  global.fetch = vi.fn(input => String(input).includes("my-permissions") ? response(["payroll.manage", "payroll.pay", "payroll.addition.review"]) : response(options)) as typeof fetch;
  renderPage(<EmployeeList />); expect(await screen.findByText("بدون حساب دخول")).toBeVisible();
  fireEvent.click(await screen.findByRole("button",{name:"المزيد من الإجراءات"}));
  expect(screen.getByRole("menuitem", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/staff/employees/worker/edit");
  expect(screen.getByRole("menuitem", { name: "المستحقات والصرف" })).toHaveAttribute("href", "/dashboard/staff/employees/worker?tab=payroll&month=2026-10");
  expect(screen.queryByRole("link", { name: "إضافة موظف" })).not.toBeInTheDocument();
});

it("employee edits cannot overwrite salary and link to the separately authorized dated source",async()=>{
  global.fetch=vi.fn(input=>String(input).endsWith("/worker")?response({...employee,hasContractHistory:true}):response(options)) as typeof fetch;
  renderPage(<EmployeeForm id="worker"/>);await screen.findByLabelText("الاسم");
  expect(screen.getByLabelText("الراتب الشهري")).toHaveAttribute("readonly");expect(screen.getByLabelText("الراتب الشهري")).toHaveValue(3100);
  expect(screen.getByRole("link",{name:"تغيير الراتب من السجل المؤرخ"})).toHaveAttribute("href","/dashboard/staff/employees/worker/salary");
  expect(screen.getByLabelText("تاريخ الانضمام")).toHaveAttribute("readonly");expect(screen.getByLabelText("تاريخ انتهاء العقد")).toHaveAttribute("readonly");expect(screen.getByRole("link",{name:"تغيير مدة العقد من السجل المؤرخ"})).toHaveAttribute("href","/dashboard/staff/employees/worker/contract");
});

it("creates a fixed contract without credentials or photo and returns only after success", async () => {
  global.fetch = vi.fn((input, init) => String(input).includes("csrf") ? response({ token: "demo" }) : init?.method === "POST" ? response({ id: "worker" }, 201) : response(options)) as typeof fetch;
  renderPage(<EmployeeForm />); await screen.findByLabelText("الاسم");
  fireEvent.change(screen.getByLabelText("الاسم"), { target: { value: employee.name } });
  fireEvent.change(screen.getByLabelText("رقم الهاتف"), { target: { value: employee.phone } });
  fireEvent.change(screen.getByLabelText("الوظيفة"), { target: { value: employee.jobTitle } });
  fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "branch" } });
  fireEvent.change(screen.getByLabelText("الراتب الشهري"), { target: { value: "3100" } });
  fireEvent.click(screen.getByLabelText("محدد المدة")); fireEvent.change(screen.getByLabelText("تاريخ انتهاء العقد"), { target: { value: "2026-10-20" } });
  expect(screen.getByLabelText("الصورة (اختياري)")).not.toBeRequired();
  fireEvent.click(screen.getByRole("button", { name: "إنشاء الموظف" }));
  await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/staff/employees"));
  const save = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "POST");
  expect(JSON.parse(String(save?.[1]?.body))).toMatchObject({ monthlySalary: 3100, contractEnd: "2026-10-20", membershipId: null, photoAssetId: null });
});

it("salary payment retries preserve the operation key and never mark a failed operation paid", async () => {
  const salary = { employeeId: employee.id, name: employee.name, monthlySalary: 3100, contractDays: 11, baseDue: 1100, deductions: 100, additions: 50, netDue: 1050, currency: "EGP", isPaid: false };
  global.fetch = vi.fn((input, init) => String(input).includes("my-permissions") ? response(["payroll.manage", "payroll.pay", "payroll.addition.review"]) : String(input).includes("csrf") ? response({ token: "demo" }) : init?.method === "POST" ? response({ message: "اتصال تجريبي غير مكتمل" }, 503) : response({ currency: "EGP", totalBase: 1100, totalDeductions: 100, totalAdditions: 50, totalNet: 1050, unpaid: 1050, items: [salary] })) as typeof fetch;
  vi.spyOn(window, "confirm").mockReturnValue(true); renderPage(<PayrollWorkspace />);
  await screen.findByRole("button", { name: "تأكيد الدفع" }); fireEvent.click(screen.getByRole("button", { name: "تأكيد الدفع" })); await screen.findByText("اتصال تجريبي غير مكتمل");
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الدفع" }));
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([, o]) => o?.method === "POST")).toHaveLength(2));
  const saves = vi.mocked(fetch).mock.calls.filter(([, o]) => o?.method === "POST"); expect(saves[0][1]?.headers).toEqual(saves[1][1]?.headers); expect(screen.queryByText("تم الدفع")).not.toBeInTheDocument();
  expect(JSON.parse(String(saves[0][1]?.body))).toEqual({ month: "2026-10-01", expectedNetDue: 1050 });
});

it("refreshes changed salary after a rejected stale confirmation without marking it paid", async () => {
  let netDue = 1100;
  global.fetch = vi.fn((input, init) => String(input).includes("my-permissions") ? response(["payroll.manage", "payroll.pay", "payroll.addition.review"]) : String(input).includes("csrf") ? response({ token: "demo" }) : init?.method === "POST" ? (netDue = 1000, response({ message: "تغيرت المستحقات. راجع الصافي المحدّث." }, 409)) : response({ currency: "EGP", items: [{ employeeId: employee.id, name: employee.name, contractDays: 11, baseDue: 1100, deductions: 1100 - netDue, additions: 0, netDue, currency: "EGP", isPaid: false }] })) as typeof fetch;
  vi.spyOn(window, "confirm").mockReturnValue(true); renderPage(<PayrollWorkspace />);
  fireEvent.click(await screen.findByRole("button", { name: "تأكيد الدفع" }));
  await screen.findByText("تغيرت المستحقات. راجع الصافي المحدّث.");
  expect(screen.getByText("إجمالي المستحق").closest("article")).toHaveTextContent("1,000");
  expect(screen.queryByText("تم الدفع")).not.toBeInTheDocument();
});

it("salary cards follow the selected employee and name filter instead of hidden staff totals", async () => {
  params.set("employeeId", employee.id);
  const salary = { employeeId: employee.id, name: employee.name, contractDays: 11, baseDue: 1100, deductions: 100, additions: 50, netDue: 1050, currency: "EGP", isPaid: false };
  const other = { ...salary, employeeId: "other", name: "موظف آخر", baseDue: 8000, netDue: 7950 };
  global.fetch = vi.fn(input => String(input).includes("my-permissions") ? response([]) : response({ currency: "EGP", totalBase: 9100, totalDeductions: 200, totalAdditions: 100, totalNet: 9000, unpaid: 9000, items: [salary, other] })) as typeof fetch;
  renderPage(<PayrollWorkspace />); await screen.findByRole("table", { name: "المستحقات" });
  expect(screen.getByText("إجمالي المستحق").closest("article")).toHaveTextContent("1,050");
  expect(screen.queryByText("موظف آخر")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("البحث بالاسم"), { target: { value: "غير موجود" } });
  expect(screen.getByText("إجمالي المستحق").closest("article")).toHaveTextContent("0 جنيه");
});

it("employee deductions include attendance deductions and save the selected employee", async () => {
  params.set("employeeId", "worker");
  const rows = [{ id: "daily", name: employee.name, amount: 100, currency: "EGP", dueDate: range.to, reason: "غياب تجريبي" }];
  global.fetch = vi.fn((input, init) => String(input).includes("my-permissions") ? response(["payroll.manage", "payroll.pay", "payroll.addition.review"]) : String(input).includes("csrf") ? response({ token: "demo" }) : init?.method === "POST" ? response({ id: "saved" }, 201) : String(input).includes("employees") ? response(options) : response(rows)) as typeof fetch;
  renderPage(<EmployeeDeductions />); await screen.findByText("غياب تجريبي");
  fireEvent.change(screen.getByLabelText("المبلغ"), { target: { value: "50" } }); fireEvent.change(screen.getByLabelText("سبب الخصم"), { target: { value: "خصم اختبار" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ الخصم" }));
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([, x]) => x?.method === "POST")).toBe(true));
  const save = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "POST"); expect(JSON.parse(String(save?.[1]?.body))).toMatchObject({ employeeId: "worker", amount: 50, reason: "خصم اختبار" });
});

it("reviews a selected request in a dialog; rejection requires neither date nor note", async () => {
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value: function(this: HTMLDialogElement) { this.setAttribute("open", ""); } });
  Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value: function(this: HTMLDialogElement) { this.removeAttribute("open"); } });
  const request = { id: "claim", employeeId: "worker", name: employee.name, amount: 100, currency: "EGP", reasonName: "ساعة إضافية", status: "Pending" };
  global.fetch = vi.fn((input, init) => String(input).includes("my-permissions") ? response(["payroll.manage", "payroll.pay", "payroll.addition.review"]) : String(input).includes("csrf") ? response({ token: "demo" }) : init?.method === "POST" ? response({ status: "Rejected" }) : String(input).includes("reasons") ? response([]) : response([request])) as typeof fetch;
  renderPage(<PayrollRequests />); fireEvent.click(await screen.findByRole("button", { name: "مراجعة" }));
  expect(screen.getByRole("dialog")).toBeVisible(); expect(screen.getByLabelText("تاريخ الاستحقاق")).toBeRequired();
  fireEvent.change(screen.getByLabelText("القرار"), { target: { value: "reject" } }); expect(screen.getByLabelText("تاريخ الاستحقاق")).not.toBeRequired();
  fireEvent.click(screen.getByRole("button", { name: "حفظ القرار" })); await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  const save = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "POST"); expect(JSON.parse(String(save?.[1]?.body))).toMatchObject({ accept: false, dueDate: null, note: "" });
});
