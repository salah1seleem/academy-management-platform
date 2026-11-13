import { webcrypto } from "node:crypto";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { PayrollAdvances } from "./payroll-advances";

const advanceId = "10000000-0000-4000-8000-000000000001";
const scope = "a".repeat(64);
const root = "/api/v1/manage/payroll/worker/advances";
const advance = { id: advanceId, amount: 750, applied: 0, remaining: 750, createdAtUtc: "2026-10-15T10:00:00Z" };
const statement = { month: "2026-10-01", netDue: 3000, remaining: 3000 };
const initial = { employeeId: "worker", employeeName: "موظف سلفة اصطناعي", recoveryScope: scope, currency: "EGP", timeZone: "Africa/Cairo", canManage: true, canRecord: true, advances: [advance], statements: [statement], applications: [] };
const reply = (value: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(value), { status }));

beforeEach(() => { vi.stubGlobal("crypto", webcrypto); sessionStorage.clear(); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); sessionStorage.clear(); });

function backend(handle: (url: string, options?: RequestInit) => Promise<Response>) {
  const fetch = vi.fn((url: string, options?: RequestInit) => url.includes("/csrf") ? reply({ token: "csrf" }) : handle(url, options));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

const button = (name: string) => screen.getByRole("button", { name });
const confirm = () => fireEvent.click(button("تأكيد الحفظ"));
const recorded = () => screen.findByText("تم تسجيل السلفة المصروفة دون خصم تلقائي.");
const allocated = () => screen.findByText("تم تخصيص السلفة دون صرف جديد أو خصم إضافي.");

async function record(amount: string) {
  fireEvent.change(await screen.findByLabelText("مبلغ السلفة"), { target: { value: amount } });
  fireEvent.click(button("مراجعة السلفة"));
}

async function allocate(amount: string, month = statement.month) {
  fireEvent.change(await screen.findByLabelText("السلفة"), { target: { value: advanceId } });
  fireEvent.change(screen.getByLabelText("كشف الشهر"), { target: { value: month } });
  fireEvent.change(screen.getByLabelText("مبلغ التخصيص"), { target: { value: amount } });
  fireEvent.click(button("مراجعة التخصيص"));
}

it.each([
  { ...statement, kind: "Opening", openingAsOf: "2026-11-01", message: "الافتتاح الموثق", link: "/dashboard/payroll/worker/opening" },
  { ...statement, kind: "MonthlySalary", message: "ليس صرفًا جديدًا أو خصمًا", link: "/dashboard/payroll/worker/statement?month=2026-10-01" },
])("sends the reviewed balances and links an allocation to its $kind source", async item => {
  const updated = { ...initial, statements: [item], advances: [{ ...advance, applied: 500, remaining: 250 }] };
  let saved = false;
  const fetch = backend((_, options) => options?.method === "POST" ? (saved = true, reply({ id: "allocation" }, 201)) : reply(saved ? {
    ...updated, applications: [{ id: "allocation", advanceId, month: item.month, amount: 500, kind: item.kind }],
  } : { ...initial, statements: [item] }));
  render(<PayrollAdvances id="worker" />);
  await allocate("500", item.month);
  expect(screen.getByRole("dialog")).toHaveTextContent(item.message);
  confirm();
  await allocated();
  const post = fetch.mock.calls.find(([, options]) => options?.method === "POST");
  expect(post?.[0]).toBe(`${root}/${advanceId}/applications`);
  expect(JSON.parse(String(post?.[1]?.body))).toEqual({ month: item.month, amount: 500, expectedAdvanceRemaining: 750, expectedStatementRemaining: 3000, ...(item.kind === "Opening" ? { opening: true } : {}) });
  expect(screen.getByRole("link", { name: /ليس صرفًا جديدًا/ })).toHaveAttribute("href", item.link);
});

it("requires review, accepts an empty reference and uses distinct keys for two independently confirmed deliveries", async () => {
  const rows = [advance];
  const fetch = backend((_, options) => {
    if (options?.method !== "POST") return reply({ ...initial, advances: rows });
    const id = `new-${rows.length}`;
    rows.unshift({ ...advance, id, amount: 500, remaining: 500 });
    return reply({ id }, 201);
  });
  render(<PayrollAdvances id="worker" />);
  await record("500");
  expect(screen.getByRole("dialog")).toHaveTextContent("لا ينفذ النظام تحويلًا بنكيًا");
  expect(fetch.mock.calls.some(([, options]) => options?.method === "POST")).toBe(false);
  fireEvent.click(button("إلغاء"));
  expect(button("مراجعة السلفة")).toBeInTheDocument();
  expect(screen.getByLabelText("مرجع السلفة (اختياري)")).not.toBeRequired();
  for (let count = 0; count < 2; count++) { await record("500"); confirm(); await recorded(); }
  const requests = fetch.mock.calls.filter(([, options]) => options?.method === "POST");
  expect(requests).toHaveLength(2);
  expect(requests[0]?.[1]?.body).toBe(requests[1]?.[1]?.body);
  expect(JSON.parse(String(requests[0]?.[1]?.body))).toEqual({ amount: 500, reference: "" });
  expect(requests[0]?.[1]?.headers).not.toEqual(requests[1]?.[1]?.headers);
  expect(screen.getByRole("option", { name: /سلفة 2/ })).toBeInTheDocument();
  expect(screen.getByRole("option", { name: /سلفة 3/ })).toBeInTheDocument();
});

it("loads the same request after remount and looks up the saved record without posting again", async () => {
  let saved = false;
  const fetch = backend((url, options) => {
    if (url.includes("/by-request")) return reply({ id: "saved" });
    if (options?.method === "POST") { saved = true; return Promise.reject(new Error("disconnected")); }
    return reply({ ...initial, advances: saved ? [{ ...advance, id: "saved" }] : initial.advances });
  });
  const view = render(<PayrollAdvances id="worker" />);
  await record("400");
  confirm();
  await screen.findByText(/تعذر تأكيد الحفظ/);
  view.unmount();
  render(<PayrollAdvances id="worker" />);
  fireEvent.click(await screen.findByRole("button", { name: "التحقق من تسجيل السلفة" }));
  await recorded();
  const post = fetch.mock.calls.find(([, options]) => options?.method === "POST");
  const key = (post?.[1]?.headers as Record<string, string>)["Idempotency-Key"];
  expect(fetch.mock.calls.filter(([, options]) => options?.method === "POST")).toHaveLength(1);
  expect(fetch.mock.calls.find(([url]) => url.includes("/by-request"))?.[0]).toContain(`kind=record&requestKey=${key}`);
  expect(sessionStorage.length).toBe(0);
});

it("recovers a saved write after a failed balance read, without sending again", async () => {
  let reads = 0;
  const fetch = backend((_, options) => {
    if (options?.method === "POST") return reply({ id: "saved" }, 201);
    reads++;
    if (reads === 2) return reply({}, 503);
    return reply(reads === 1 ? initial : { ...initial, advances: [{ ...advance, id: "saved" }] });
  });
  render(<PayrollAdvances id="worker" />);
  await record("400");
  confirm();
  await screen.findByText(/حُفظ الطلب وتعذر تحديث الأرصدة/);
  expect(screen.queryByRole("form")).not.toBeInTheDocument();
  fireEvent.click(button("إعادة قراءة الأرصدة"));
  await recorded();
  expect(fetch.mock.calls.filter(([, options]) => options?.method === "POST")).toHaveLength(1);
});

it("keeps an unresolved attempt in its original scope and sends the same key on an explicit retry", async () => {
  let currentScope = scope;
  let postCount = 0;
  const fetch = backend((url, options) => {
    if (url.includes("/by-request")) return reply({}, 404);
    if (options?.method === "POST") return ++postCount === 1 ? Promise.reject(new Error("disconnected")) : reply({ id: "saved" }, 201);
    return reply({ ...initial, recoveryScope: currentScope, advances: postCount > 1 ? [{ ...advance, id: "saved" }] : initial.advances });
  });
  const view = render(<PayrollAdvances id="worker" />);
  await record("400");
  confirm();
  await screen.findByText(/تعذر تأكيد الحفظ/);
  view.unmount();
  currentScope = "b".repeat(64);
  const other = render(<PayrollAdvances id="worker" />);
  await screen.findByRole("button", { name: "مراجعة السلفة" });
  expect(screen.queryByRole("button", { name: "التحقق من تسجيل السلفة" })).not.toBeInTheDocument();
  other.unmount();
  currentScope = scope;
  render(<PayrollAdvances id="worker" />);
  fireEvent.click(await screen.findByRole("button", { name: "التحقق من تسجيل السلفة" }));
  fireEvent.click(await screen.findByRole("button", { name: "إعادة إرسال المحاولة نفسها" }));
  await recorded();
  const posts = fetch.mock.calls.filter(([, options]) => options?.method === "POST");
  expect(posts).toHaveLength(2);
  expect(posts[0]?.[1]?.headers).toEqual(posts[1]?.[1]?.headers);
});

it("re-reads stale balances and excludes exhausted sources and settled statements", async () => {
  let applied = false;
  backend((_, options) => options?.method === "POST" ? (applied = true, reply({ title: "تغير رصيد السلفة" }, 409)) : reply(applied ? {
    ...initial, advances: [{ ...advance, applied: 750, remaining: 0 }], statements: [{ ...statement, remaining: 0 }],
  } : initial));
  render(<PayrollAdvances id="worker" />);
  await allocate("100");
  confirm();
  expect(await screen.findByText("تغير رصيد السلفة")).toBeVisible();
  await screen.findByRole("button", { name: "مراجعة السلفة" });
  expect(screen.queryByRole("combobox", { name: "السلفة" })).not.toBeInTheDocument();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
});

it("allows a read-only history after a transient failure and hides amounts on denial", async () => {
  let reads = 0;
  backend(() => ++reads === 1 ? reply({}, 503) : reply({ ...initial, canManage: false, canRecord: false }));
  const view = render(<PayrollAdvances id="worker" />);
  fireEvent.click(await screen.findByRole("button", { name: "إعادة قراءة السلف" }));
  await screen.findByText("السلف المسجلة");
  expect(screen.queryByRole("form")).not.toBeInTheDocument();
  view.unmount();
  backend(() => reply({}, 403));
  render(<PayrollAdvances id="worker" />);
  expect(await screen.findByRole("alert")).toHaveTextContent("سلف هذا الموظف غير متاحة لهذا الحساب.");
  expect(screen.queryByText("السلف المسجلة")).not.toBeInTheDocument();
});

it("uses the academy day for advances created before midnight UTC", async () => {
  backend(() => reply({ ...initial, advances: [{ ...advance, createdAtUtc: "2026-10-15T22:30:00Z" }] }));
  render(<PayrollAdvances id="worker" />);
  expect(await screen.findByText("سلفة 1 · 16 أكتوبر 2026")).toBeVisible();
  expect(screen.getByRole("option", { name: /سلفة 1 · 16 أكتوبر 2026/ })).toBeInTheDocument();
  expect(screen.getByRole("option", { name: /1 أكتوبر 2026 · متبق/ })).toBeInTheDocument();
});
