import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { OperatingExpenses } from "./operating-expenses";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const row = { id: "expense-1", branchId: "branch-1", scope: "Group", trainingGroupId: "group-1", category: "Water", periodFrom: "2026-10-01", periodTo: "2026-10-13", amount: 100, currency: "EGP", status: "Draft" };

it("verifies a committed expense after its response is lost without posting again", async () => {
  let status = "Approved";
  const posts = vi.fn(() => { status = "Posted"; throw new TypeError("offline"); });
  global.fetch = vi.fn(async input => {
    const path = String(input);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [{ id: "group-1", branchId: "branch-1", arabicName: "مجموعة اصطناعية" }] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", canApprove: true, canRecord: true });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0 });
    if (path.includes("operating-expenses/?")) return response({ count: 1, items: [{ ...row, status }] });
    if (path.endsWith("/expense-1")) return response({ expense: { ...row, status }, allocations: status === "Posted" ? [{ serviceKind: "Training", trainingGroupId: "group-1", amount: 100 }] : [], allocationTotal: status === "Posted" ? 100 : 0 });
    if (path.endsWith("/csrf")) return response({ token: "test-only" });
    if (path.endsWith("/quote")) return response({ quoteHash: "a".repeat(64), shares: [], total: 100, currency: "EGP", method: "مجموعة مباشرة" });
    if (path.endsWith("/post")) return posts();
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses initialExpenseId={row.id} />);
  fireEvent.click(await screen.findByRole("button", { name: "معاينة توزيع التكلفة" }));
  fireEvent.click(await screen.findByRole("button", { name: "ترحيل المصروف" }));
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "تأكيد الترحيل" }));
  fireEvent.click(await screen.findByRole("button", { name: "التحقق من حالة الترحيل" }));
  expect(await screen.findByRole("heading", { name: "الأنصبة المحفوظة" })).toBeInTheDocument();
  expect(posts).toHaveBeenCalledTimes(1);
});

it("keeps the same quote and key when a lost post can be retried after verification", async () => {
  let status = "Approved";
  let reads = 0;
  const attempts: { headers?: HeadersInit; body?: BodyInit | null }[] = [];
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", canApprove: true, canRecord: true });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0 });
    if (path.includes("operating-expenses/?")) return response({ count: 1, items: [{ ...row, status }] });
    if (path.endsWith("/expense-1")) {
      if (++reads === 2) return response({}, 503);
      return response({ expense: { ...row, status }, allocations: [], allocationTotal: status === "Posted" ? 100 : 0 });
    }
    if (path.endsWith("/csrf")) return response({ token: "test-only" });
    if (path.endsWith("/quote")) return response({ quoteHash: "a".repeat(64), shares: [], total: 100, currency: "EGP", method: "مجموعة مباشرة" });
    if (path.endsWith("/post")) {
      attempts.push({ headers: init?.headers, body: init?.body });
      if (attempts.length === 1) throw new TypeError("offline");
      status = "Posted";
      return response({ id: row.id });
    }
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses initialExpenseId={row.id} />);
  fireEvent.click(await screen.findByRole("button", { name: "معاينة توزيع التكلفة" }));
  const post = () => screen.findByRole("button", { name: "ترحيل المصروف" });
  const confirm = () => within(screen.getByRole("dialog")).getByRole("button", { name: "تأكيد الترحيل" });
  const verify = () => screen.findByRole("button", { name: "التحقق من حالة الترحيل" });
  fireEvent.click(await post());
  fireEvent.click(confirm());
  fireEvent.click(await verify());
  await screen.findByText(/تعذر قراءة حالة المصروف/);
  expect(screen.queryByRole("button", { name: "ترحيل المصروف" })).not.toBeInTheDocument();
  expect(attempts).toHaveLength(1);
  fireEvent.click(await verify());
  fireEvent.click(await post());
  fireEvent.click(confirm());
  await screen.findByRole("heading", { name: "الأنصبة المحفوظة" });
  expect(attempts).toEqual([attempts[0], attempts[0]]);
});

it("ignores obsolete filter responses and shows read failures apart from an empty list", async () => {
  let resolveOld: ((response: Response) => void) | undefined;
  let filteredReads = 0;
  global.fetch = vi.fn(async input => {
    const path = String(input);
    if (path.includes("structure/options")) return response({ branches: [], groups: [] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", canApprove: true, canRecord: true });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0 });
    if (path.includes("status=Posted")) return ++filteredReads === 1 ? response({}, 503) : response({ count: 0, items: [] });
    if (path.includes("operating-expenses/?")) return new Promise<Response>(resolve => { resolveOld = resolve; });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses />);
  const state = await screen.findByLabelText("الحالة");
  await waitFor(() => expect(resolveOld).toBeDefined());
  fireEvent.change(state, { target: { value: "Posted" } });
  await screen.findByText("تعذر تحميل المصروفات.");
  expect(screen.queryByText("لا توجد مصروفات ضمن الفلاتر الحالية")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة التحميل" }));
  await screen.findByText("لا توجد مصروفات ضمن الفلاتر الحالية");
  resolveOld!(response({ count: 1, items: [row] }));
  await waitFor(() => expect(screen.queryByRole("button", { name: "عرض المصروف" })).not.toBeInTheDocument());
  expect(state).toHaveValue("Posted");
});

it("keeps expense creation, review and posted shares separate from collections", async () => {
  const calls: string[] = []; let status = "Draft";
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input); calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [{ id: "group-1", branchId: "branch-1", arabicName: "مجموعة اصطناعية" }] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path === "/api/v1/finance/operating-expenses/options") return response({ currency: "EGP", categories: [], canApprove:true, canRecord:true });
    if (path.startsWith("/api/v1/finance/operating-expenses/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path === "/api/v1/auth/csrf") return response({ token: "test-only" });
    if (path.startsWith("/api/v1/finance/operating-expenses/?")) return response({ count: 1, items: [{ ...row, status }] });
    if (path.endsWith("/expense-1")) return response({ expense: { ...row, status }, allocations: [], allocationTotal: 0 });
    if (path.endsWith("/approve")) { status = "Approved"; return response({ id: row.id, status }); }
    if (path.endsWith("/quote")) return response({ quoteHash: "a".repeat(64), shares: [{ serviceKind: "Training", groupId: "group-1", weight: 1, amount: 100 }], method: "مجموعة مباشرة", total: 100, currency: "EGP" });
    if (path.endsWith("/post")) { expect(init?.headers).toMatchObject({ "Idempotency-Key": expect.any(String) }); status = "Posted"; return response({ id: row.id, replay: false }); }
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses />);
  await screen.findByRole("button", { name: "تسجيل مصروف" });
  expect(screen.queryByRole("heading", {name:"تسجيل مصروف"})).not.toBeInTheDocument();
  expect(screen.getByText(/ليست رواتب أو تحصيلًا/)).toBeInTheDocument();
  fireEvent.click(await screen.findByRole("button", { name: "عرض المصروف" }));
  fireEvent.click(await screen.findByRole("button", { name: "اعتماد المصروف" }));
  await screen.findByRole("button", { name: "معاينة توزيع التكلفة" });
  fireEvent.click(screen.getByRole("button", { name: "معاينة توزيع التكلفة" }));
  await screen.findByRole("button", { name: "ترحيل المصروف" });
  expect(screen.getByText(/مجموعة اصطناعية —/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "ترحيل المصروف" }));
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "تأكيد الترحيل" }));
  await screen.findByText(/رُحّل المصروف وحُفظ التوزيع/);
  fireEvent.click(screen.getByRole("button", {name:"العودة إلى المصروفات"}));
  expect(calls.some(p => p.includes("/api/v1/reports/financial"))).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&pageSize=25"));
});

it("does not expose expense records when the owner API denies access", async () => {
  global.fetch = vi.fn(async path => String(path).includes("structure/options") ? response({}, 403) : response({}, 403));
  render(<OperatingExpenses />);
  await screen.findByText(/تعذر تحميل الفروع والمجموعات والمرافق/);
  expect(screen.queryByRole("button", { name: "ترحيل المصروف" })).not.toBeInTheDocument();
});

it("keeps owner KPI period, branch and approved status in the expense list", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(async input => {
    const path = String(input); calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", categories: [], canApprove:true, canRecord:true });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path.includes("operating-expenses/?")) return response({ count: 0, items: [] });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses initialFrom="2026-09-01" initialTo="2026-09-28" initialBranchId="branch-1" initialStatus="Approved" />);
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&pageSize=25&from=2026-09-01&to=2026-09-28&branchId=branch-1&status=Approved"));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&pageSize=25"));
});

it("opens the all-time unpaid owner KPI without silently applying a month filter", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(async input => {
    const path = String(input); calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", categories: [], canApprove:true, canRecord:true });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path.includes("operating-expenses/?")) return response({ count: 0, items: [] });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses initialBranchId="branch-1" initialUnpaid />);
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&pageSize=25&branchId=branch-1&unpaid=true"));
  expect(screen.getByLabelText("السداد")).toHaveValue("unpaid");
});

it("uses payment dates and preserves the operating statement drilldown filters", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(async input => {
    const path = String(input);
    calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [], sports: [{ id: "sport-1", arabicName: "سباحة" }] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", categories: [], canApprove: true, canRecord: true, today: "2026-11-12" });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0 });
    if (path.includes("operating-expenses/?")) return response({ count: 1, items: [{ ...row, status: "Posted", paidOn: "2026-11-09" }] });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses
    initialFrom="2026-11-09" initialTo="2026-11-09" initialBranchId="branch-1" initialSportId="sport-1"
    initialCategory="Water" initialStatus="Posted" initialPaymentDates initialPaid
  />);
  expect(await screen.findByLabelText("من تاريخ السداد")).toHaveValue("2026-11-09");
  const request = calls.find(path => path.includes("operating-expenses/?"));
  expect(Object.fromEntries(new URL(request!, "http://localhost").searchParams)).toMatchObject({
    from: "2026-11-09", to: "2026-11-09", branchId: "branch-1", sportId: "sport-1", category: "Water", status: "Posted", dateBasis: "payment", paid: "true",
  });
  expect(screen.getByText("الرياضة: سباحة")).toBeInTheDocument();
  expect(await screen.findByText("سُدد في 2026-11-09")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "تسجيل مصروف" }));
  expect(screen.getByLabelText("من فترة الاستحقاق")).toHaveValue("2026-11-01");
  expect(screen.getByLabelText("إلى فترة الاستحقاق")).toHaveValue("2026-11-12");
  fireEvent.click(screen.getByRole("button", { name: "العودة إلى المصروفات" }));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&pageSize=25"));
  expect(screen.getByLabelText("من تاريخ")).toHaveValue("");
});

it("offers branch and branch-scoped sport shared costs without a misleading group target", async () => {
  const created: unknown[] = [];
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [], sports: [{ id: "sport-1", arabicName: "سباحة" }] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", categories: [], canApprove:true, canRecord:true, today: "2026-11-12" });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path.includes("operating-expenses/?")) return response({ count: 0, items: [] });
    if (path === "/api/v1/auth/csrf") return response({ token: "demo-token" });
    if (path === "/api/v1/finance/operating-expenses/" && init?.method === "POST") { created.push(JSON.parse(String(init.body))); return response({ id: "expense-1" }); }
    if (path.endsWith("/expense-1")) return response({ expense: { ...row, status: "Draft" } });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses />);
  fireEvent.click(await screen.findByRole("button", {name:"تسجيل مصروف"}));
  await screen.findByRole("heading", { name: "تسجيل مصروف" });
  fireEvent.change(screen.getAllByLabelText("الفرع")[0], { target: { value: "branch-1" } });
  fireEvent.change(screen.getByLabelText("نطاق التكلفة"), { target: { value: "Branch" } });
  const form = screen.getByRole("button", { name: "حفظ مسودة المصروف" }).closest("form")!;
  expect(within(form).queryByLabelText("المجموعة")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText(/المبلغ/), { target: { value: "100" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ مسودة المصروف" }));
  await waitFor(() => expect(created).toHaveLength(1));
  expect(created[0]).toMatchObject({ branchId: "branch-1", scope: "Branch", targetId: "branch-1" });
  fireEvent.click(await screen.findByRole("button",{name:"العودة إلى المصروفات"}));fireEvent.click(await screen.findByRole("button",{name:"تسجيل مصروف"}));
  fireEvent.change(screen.getByLabelText("نطاق التكلفة"), { target: { value: "Sport" } });
  expect(screen.getByLabelText("الرياضة")).toContainHTML("سباحة");
});
