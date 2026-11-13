import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { GuardianReceivables, ReceivableDetail, Receivables } from "./receivables";
const push = vi.fn();
vi.mock("next/navigation", async () => {
  const { useSyncExternalStore } = await import("react");
  const subscribe = (listener: () => void) => {
    window.addEventListener("popstate", listener);
    return () => window.removeEventListener("popstate", listener);
  };
  return {
    useRouter: () => ({ push }),
    useSearchParams: () => new URLSearchParams(useSyncExternalStore(subscribe, () => window.location.search)),
  };
});
const ledger = { asOf: "2026-09-28T09:00:00Z", currency: "EGP", count: 1, outstanding: "1740.00", overdue: "1240.00", notYetDue: "500.00", overpaymentCredit: "0.00", items: [{ id: "doc", player: "لاعب اختبار", branch: "فرع اختبار", description: "مطالبة موثقة", currency: "EGP", charge: "540.00", outstanding: "340.00", credits: "0.00", allocated: "200.00", dueDate: "2026-09-20", overdue: true }] };
const credit = { ...ledger.items[0], id: "credit", player: "لاعبة تجريبية", outstanding: "-100.00", overdue: false };
const future = { ...ledger.items[0], id: "future", player: "لاعب تجريبي آخر", outstanding: "500.00", overdue: false };
const snapshot = { ...ledger, outstanding: "840.00", overdue: "340.00", overpaymentCredit: "100.00", count: 3, items: [...ledger.items, credit, future] };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); window.history.replaceState({}, "", "/"); });
beforeEach(() => {
  push.mockReset();
  const replace = window.history.replaceState.bind(window.history);
  vi.spyOn(window.history, "replaceState").mockImplementation((data, unused, url) => {
    replace(data, unused, url);
    window.dispatchEvent(new Event("popstate"));
  });
  sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(url).endsWith("my-access") ? response({ actions: [] }) : String(url).endsWith("my-permissions") ? response(["reports.export"]) : response(snapshot)); });
it("drills into posted balances and exports the exact displayed asOf", async () => {
  render(<Receivables />); await screen.findByRole("link", { name: "لاعب اختبار" });
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", expect.stringContaining("asOf=2026-09-28T09%3A00%3A00Z"));
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).not.toHaveAttribute("href", expect.stringContaining("branchId="));
  fireEvent.click(screen.getByRole("button", { name: /المتأخر المستحق/ }));
  await waitFor(() => expect(window.location.search).toBe("?state=overdue"));
  expect(screen.getByRole("link", { name: "لاعب اختبار" })).toHaveAttribute("href", "/dashboard/finance/receivables/doc?state=overdue");
  expect(screen.queryByRole("link", { name: credit.player })).toBeNull();
  expect(screen.getByRole("button", { name: /رصيد دائن للأسرة.*100/ })).toBeVisible();
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", expect.stringContaining("state=overdue&asOf=2026-09-28T09%3A00%3A00Z"));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلتر" }));
  expect(await screen.findByRole("link", { name: credit.player })).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.filter(([url]) => String(url).includes("/api/v1/finance/receivables?"))).toHaveLength(1);
});
it("keeps the owner card branch and balance state in rows and export, then clears both", async () => {
  const branchId = "11111111-1111-1111-1111-111111111111";
  window.history.replaceState({}, "", `/dashboard/finance/receivables?state=overdue&branchId=${branchId}`);
  global.fetch = vi.fn(async url => String(url).includes("structure/options") ? response({ branches: [{ id: branchId, arabicName: "فرع اختبار" }] }) : String(url).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(url).endsWith("my-access") ? response({ actions: [] }) : String(url).endsWith("my-permissions") ? response(["reports.export"]) : response(snapshot));
  render(<Receivables />);
  await screen.findByRole("link", { name: "لاعب اختبار" });
  expect(screen.getByRole("heading", { name: "ملخص رصيد الفرع" })).toBeVisible();
  expect(screen.getByRole("button", { name: /الرصيد القائم/ })).toHaveTextContent("840 جنيه");
  expect(fetch).toHaveBeenCalledWith(`/api/v1/finance/receivables?branchId=${branchId}`, expect.anything());
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", expect.stringContaining(`branchId=${branchId}`));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلتر" }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/finance/receivables?", expect.anything()));
  expect(await screen.findByRole("heading", { name: "ملخص رصيد كل الفروع" })).toBeVisible();
});
it.each([
  { ...snapshot, overdue: undefined },
  { ...snapshot, asOf: "invalid" },
  { ...snapshot, items: [{ ...snapshot.items[0], outstanding: "غير متاح" }] },
  { ...snapshot, currency: "" },
  { ...snapshot, items: [{ ...snapshot.items[0], overdue: undefined }] },
  { ...snapshot, items: [{ ...snapshot.items[0], id: "" }] },
])("keeps an incomplete staff ledger out of the totals and recovers", async invalid => {
  const branchId = "11111111-1111-1111-1111-111111111111";
  window.history.replaceState({}, "", `/dashboard/finance/receivables?state=overdue&branchId=${branchId}`);
  const responses = [invalid, snapshot];
  const read = `/api/v1/finance/receivables?branchId=${branchId}`;
  global.fetch = vi.fn(async url => String(url) === read ? response(responses.shift()) : response({}));
  render(<Receivables />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل المطالبات في هذا النطاق");
  expect(screen.queryByRole("group", { name: "أرصدة الفرع" })).toBeNull();
  expect(screen.queryByRole("link", { name: "لاعب اختبار" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByRole("button", { name: /المتأخر المستحق/ })).toHaveTextContent("340 جنيه");
  expect(screen.getByLabelText("حالة الرصيد")).toHaveValue("overdue");
  expect(screen.getByLabelText("الفرع")).toHaveValue(branchId);
});
it("clears the previous branch while the next branch loads or refuses access", async () => {
  const first = "11111111-1111-1111-1111-111111111111";
  const denied = "22222222-2222-2222-2222-222222222222";
  const empty = "33333333-3333-3333-3333-333333333333";
  window.history.replaceState({}, "", `/dashboard/finance/receivables?state=overdue&branchId=${first}`);
  let deny: (value: Response) => void = () => {};
  global.fetch = vi.fn(async input => {
    const url = String(input);
    if (url.includes("structure/options")) return response({ branches: [first, denied, empty].map((id, index) => ({ id, arabicName: `فرع اختبار ${index + 1}` })) });
    if (url.endsWith(`branchId=${denied}`)) return new Promise<Response>(resolve => { deny = resolve; });
    if (url.endsWith(`branchId=${empty}`)) return response({ ...snapshot, outstanding: "0.00", overdue: "0.00", notYetDue: "0.00", overpaymentCredit: "0.00", count: 0, items: [] });
    return response(snapshot);
  });
  render(<Receivables />);
  expect(await screen.findByRole("button", { name: /المتأخر المستحق/ })).toHaveTextContent("340 جنيه");
  await screen.findByRole("option", { name: "فرع اختبار 2" });
  const branch = screen.getByLabelText("الفرع");
  fireEvent.change(branch, { target: { value: denied } });
  expect(screen.queryByText("340 جنيه")).toBeNull();
  deny(response({}, 403));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل المطالبات");
  expect(screen.queryByText("0 جنيه")).toBeNull();
  fireEvent.change(branch, { target: { value: empty } });
  expect(await screen.findByRole("button", { name: /المتأخر المستحق/ })).toHaveTextContent("0 جنيه");
  expect(screen.getByText("لا توجد مطالبات بهذه الحالة في النطاق المحدد.")).toBeVisible();
});
it("read-only Admin has no charge action or movement form", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({ role: "AcademyAdmin" }) : String(url).endsWith("my-access") ? response({ useActionGrants: true, actions: [{ module: "finance", read: true, add: false }] }) : response(ledger));
  render(<Receivables />); await screen.findByRole("link", { name: "لاعب اختبار" }); expect(screen.queryByRole("button", { name: "إضافة مطالبة" })).not.toBeInTheDocument();
});
it("sends a decimal string and a stable identity across an uncertain charge retry", async () => {
  let attempts = 0; const ids: string[] = []; const bodies: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("/me")) return response({ role: "AcademyOwner" }); if (String(url).endsWith("my-access")) return response({});
    if (String(url).includes("/options")) return response({ enrollments: [{ id: "enrollment", player: "لاعب اختبار", sport: "سباحة", branch: "فرع" }] });
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { ids.push((init.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(JSON.parse(init.body as string)); if (++attempts === 1) throw new Error("انقطع الاتصال"); return response({ id: "doc" }, 201); }
    return response(ledger);
  });
  render(<Receivables />); fireEvent.click(await screen.findByRole("button", { name: "إضافة مطالبة" }));
  await screen.findByRole("option", { name: "لاعب اختبار · سباحة · فرع" });
  fireEvent.change(screen.getByLabelText("تسجيل اللاعب"), { target: { value: "enrollment" } }); fireEvent.change(screen.getByLabelText("المبلغ بالجنيه"), { target: { value: "123.45" } }); fireEvent.change(screen.getByLabelText("تاريخ الاستحقاق"), { target: { value: "2026-09-28" } }); fireEvent.change(screen.getByLabelText("الوصف"), { target: { value: "مطالبة معتمدة" } });
  fireEvent.click(screen.getByRole("button", { name: "تثبيت المطالبة" })); await screen.findByText("انقطع الاتصال"); fireEvent.click(screen.getByRole("button", { name: "تثبيت المطالبة" }));
  await waitFor(() => expect(ids).toHaveLength(2)); expect(ids[0]).toBe(ids[1]); expect(bodies[0]).toEqual(bodies[1]); expect(bodies[0]).toMatchObject({ amount: "123.45" });
});
it("loads only the authenticated guardian endpoint and explains true zero balances", async () => {
  global.fetch = vi.fn(async () => response({ ...ledger, outstanding: "0.00", overdue: "0.00", notYetDue: "0.00", overpaymentCredit: "0.00", items: [] }));
  render(<GuardianReceivables />);
  expect(await screen.findByText("لا توجد مطالبات مثبتة للأسرة.")).toBeVisible();
  expect(fetch).toHaveBeenCalledWith("/api/v1/guardian/receivables", { cache: "no-store", signal: expect.any(AbortSignal) });
});

it("recovers a malformed family ledger without showing false zero balances", async () => {
  global.fetch = vi.fn().mockResolvedValueOnce(response({ ...snapshot, overdue: undefined })).mockResolvedValueOnce(response(snapshot));
  render(<GuardianReceivables />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل أرصدة الأسرة");
  expect(screen.queryByRole("region", { name: "ملخص الرصيد" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByRole("region", { name: "ملخص الرصيد" })).toHaveTextContent("غير مستحق بعد500 جنيه");
});
it("keeps posted documents read-only and offers corrections, not history edits", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(url).endsWith("my-access") ? response({}) : String(url).endsWith("collections") ? response([]) : response({ document: { id: "doc", description: "مطالبة موثقة", amount: "540.00", currency: "EGP", dueDate: "2026-09-20" }, movements: [] }));
  render(<ReceivableDetail id="doc" />); await screen.findByRole("heading", { name: "إضافة حركة موثقة" }); expect(screen.getByText(/مستند مثبت لا يُعدل ولا يُحذف/)).toBeVisible(); expect(screen.queryByRole("button", { name: /حذف/ })).not.toBeInTheDocument();
});
it("distinguishes original charge from current credit and hides the prior document while another loads", async () => {
  const source = { asOf: "2026-10-22T06:00:00Z", document: { id: "source", description: "مستند مصحح", amount: "200.00", currency: "EGP", dueDate: "2026-10-01" }, balance: { id: "source", outstanding: "-100.00", allocated: "200.00", credits: "100.00", currency: "EGP" }, movements: [] };
  let finish: (r: Response) => void = () => {};
  global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({role:"AcademyOwner"}) : String(url).endsWith("my-access") ? response({}) : String(url).endsWith("collections") ? response([]) : String(url).endsWith("/target") ? new Promise<Response>(resolve => {finish=resolve;}) : response(source));
  const view = render(<ReceivableDetail id="source" />);
  await screen.findByRole("heading", {name:"رصيد لصالح العميل"});
  expect(screen.getByText(/أصل المطالبة: 200/)).toBeVisible();
  expect(screen.getByText(/تحصيل مخصص: 200.*إشعار دائن: 100/)).toBeVisible();
  expect(screen.getByText(/رصيد حتى/)).toBeVisible();
  view.rerender(<ReceivableDetail id="target" />);
  expect(screen.queryByText("مستند مصحح")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", {name:"تثبيت الحركة"})).not.toBeInTheDocument();
  finish(response({...source,document:{...source.document,id:"foreign"}}));
  await screen.findByText("لم تتأكد هوية مستند المستحقات. أعد فتحه من القائمة.");
  expect(screen.queryByText("مستند مصحح")).not.toBeInTheDocument();
});

it("keeps a verified document and branch link when payment options fail, then distinguishes an empty retry", async () => {
  const branchId = "11111111-1111-1111-1111-111111111111";
  window.history.replaceState({}, "", `/dashboard/finance/receivables/doc?state=overdue&branchId=${branchId}`);
  let reads = 0;
  global.fetch = vi.fn(async url => {
    if (String(url).endsWith("/me")) return response({ role: "AcademyOwner" });
    if (String(url).endsWith("/my-access")) return response({});
    if (String(url).endsWith("/collections")) return ++reads === 1 ? response({}, 503) : response([]);
    return response({
      document: { id: "doc", description: "رسوم تدريب تجريبية", currency: "EGP", amount: "540.00", dueDate: "2026-09-20" },
      movements: [],
      creditApplications: [{ id: "application", sourceDocumentId: "credit", targetDocumentId: "doc", amount: "60.00", reason: "تسوية موثقة", createdAtUtc: ledger.asOf }],
    });
  });
  render(<ReceivableDetail id="doc" />);
  await screen.findByRole("heading", { name: "رسوم تدريب تجريبية" });
  expect(reads).toBe(0);
  expect(screen.getByRole("link", { name: "العودة إلى المطالبات" })).toHaveAttribute("href", `/dashboard/finance/receivables?state=overdue&branchId=${branchId}`);
  expect(screen.getByText("تسوية برصيد دائن")).toBeVisible();
  expect(screen.queryByText("لم تُسجل حركات على المطالبة.")).toBeNull();
  fireEvent.change(await screen.findByLabelText("نوع الحركة"), { target: { value: "PaymentAllocation" } });
  await screen.findByText("تعذر تحميل التحصيلات المتاحة.");
  expect(screen.getByRole("heading", { name: "رسوم تدريب تجريبية" })).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل التحصيلات" }));
  await screen.findByText("لا يوجد تحصيل مؤكد متاح للتخصيص.");
  expect(screen.getByRole("button", { name: "تثبيت الحركة" })).toBeDisabled();
});

it("displays credit as a positive amount owed to the family", async () => {
  global.fetch = vi.fn(async () => response({ ...snapshot, outstanding: "0.00", overdue: "0.00", items: [credit] }));
  render(<GuardianReceivables />);
  expect(await screen.findByText("رصيد دائن للأسرة")).toBeVisible();
  expect(screen.getByText(/رصيد دائن 100 جنيه/)).toBeVisible();
  expect(screen.queryByText(/-100 جنيه/)).toBeNull();
});

it("clears an enrollment selection when search changes and ignores a superseded response", async () => {
  let finishOld: (value: Response) => void = () => {};
  const person = { id: "registration", player: "لاعب خيالي", sport: "سباحة", branch: "فرع تجريبي" };
  global.fetch = vi.fn(async input => {
    const url = String(input);
    if (url.endsWith("/me")) return response({ role: "AcademyOwner" });
    if (url.endsWith("/my-access")) return response({});
    if (url.includes("/receivables/options?")) {
      if (url.endsWith("search=old")) return new Promise<Response>(resolve => { finishOld = resolve; });
      return response({ enrollments: url.endsWith("search=new") ? [] : [person] });
    }
    return response(snapshot);
  });
  render(<Receivables />);
  fireEvent.click(await screen.findByRole("button", { name: "إضافة مطالبة" }));
  await screen.findByRole("option", { name: "لاعب خيالي · سباحة · فرع تجريبي" });
  fireEvent.change(screen.getByLabelText("تسجيل اللاعب"), { target: { value: person.id } });
  expect(screen.getByRole("button", { name: "تثبيت المطالبة" })).toBeEnabled();
  const search = screen.getByLabelText("البحث عن اللاعب");
  fireEvent.change(search, { target: { value: "old" } });
  expect(screen.getByRole("button", { name: "تثبيت المطالبة" })).toBeDisabled();
  fireEvent.change(search, { target: { value: "new" } });
  await screen.findByText("لا توجد تسجيلات مطابقة.");
  finishOld(response({ enrollments: [person] }));
  await waitFor(() => expect(screen.getByLabelText("تسجيل اللاعب")).toBeDisabled());
  expect(screen.queryByRole("option", { name: /لاعب خيالي/ })).toBeNull();
});
