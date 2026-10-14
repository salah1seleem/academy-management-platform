import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { GuardianReceivables, ReceivableDetail, Receivables } from "./receivables";
const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
const ledger = { asOf: "2026-09-28T09:00:00Z", currency: "EGP", count: 1, outstanding: "1740.00", overdue: "1240.00", notYetDue: "500.00", overpaymentCredit: "0.00", items: [{ id: "doc", player: "لاعب اختبار", branch: "فرع اختبار", description: "مطالبة موثقة", currency: "EGP", charge: "540.00", outstanding: "340.00", credits: "0.00", allocated: "200.00", dueDate: "2026-09-20", overdue: true }] };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });
beforeEach(() => { push.mockReset(); sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(url).endsWith("my-access") ? response({ actions: [] }) : response(ledger)); });
it("drills into posted balances and exports the exact displayed asOf", async () => {
  render(<Receivables />); await screen.findByRole("link", { name: "التفاصيل والحركات" });
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", expect.stringContaining("asOf=2026-09-28T09%3A00%3A00Z"));
  fireEvent.click(screen.getByRole("button", { name: /المتأخر المستحق/ }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/finance/receivables?state=overdue", expect.anything()));
  await screen.findByRole("link", { name: "التفاصيل والحركات" }); fireEvent.click(screen.getByRole("button", { name: "مسح الفلتر" }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/finance/receivables?state=", expect.anything()));
});
it("keeps the owner card branch and balance state in rows and export, then clears both", async () => {
  const branchId = "11111111-1111-1111-1111-111111111111";
  window.history.replaceState({}, "", `/dashboard/finance/receivables?state=overdue&branchId=${branchId}`);
  global.fetch = vi.fn(async url => String(url).includes("structure/options") ? response({ branches: [{ id: branchId, arabicName: "فرع اختبار" }] }) : String(url).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(url).endsWith("my-access") ? response({ actions: [] }) : response(ledger));
  render(<Receivables />);
  await screen.findByRole("link", { name: "التفاصيل والحركات" });
  expect(fetch).toHaveBeenCalledWith(`/api/v1/finance/receivables?state=overdue&branchId=${branchId}`, expect.anything());
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", expect.stringContaining(`branchId=${branchId}`));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلتر" }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/finance/receivables?state=", expect.anything()));
});
it("read-only Admin has no charge action or movement form", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({ role: "AcademyAdmin" }) : String(url).endsWith("my-access") ? response({ useActionGrants: true, actions: [{ module: "finance", read: true, add: false }] }) : response(ledger));
  render(<Receivables />); await screen.findByRole("link", { name: "التفاصيل والحركات" }); expect(screen.queryByRole("button", { name: "إضافة مطالبة" })).not.toBeInTheDocument();
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
it("loads only the authenticated guardian endpoint and hides true zero balances", async () => {
  global.fetch = vi.fn(async () => response({ ...ledger, outstanding: "0.00", overpaymentCredit: "0.00", items: [] }));
  render(<GuardianReceivables />); await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/guardian/receivables", { cache: "no-store" })); expect(screen.queryByText("أرصدة الأسرة")).not.toBeInTheDocument();
});
it("keeps posted documents read-only and offers corrections, not history edits", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(url).endsWith("my-access") ? response({}) : String(url).endsWith("collections") ? response([]) : response({ document: { id: "doc", description: "مطالبة موثقة", amount: "540.00", currency: "EGP", dueDate: "2026-09-20" }, movements: [] }));
  render(<ReceivableDetail id="doc" />); await screen.findByRole("heading", { name: "إضافة حركة موثقة" }); expect(screen.getByText(/مستند مثبت لا يُعدل ولا يُحذف/)).toBeVisible(); expect(screen.queryByRole("button", { name: /حذف/ })).not.toBeInTheDocument();
});
