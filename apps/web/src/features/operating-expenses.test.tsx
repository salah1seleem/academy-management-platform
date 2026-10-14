import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { OperatingExpenses } from "./operating-expenses";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const row = { id: "expense-1", branchId: "branch-1", scope: "Group", trainingGroupId: "group-1", category: "Water", periodFrom: "2026-10-01", periodTo: "2026-10-13", amount: 100, currency: "EGP", status: "Draft" };

it("keeps expense creation, review and posted shares separate from collections", async () => {
  const calls: string[] = []; let status = "Draft";
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input); calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [{ id: "group-1", branchId: "branch-1", arabicName: "مجموعة اصطناعية" }] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path === "/api/v1/finance/operating-expenses/options") return response({ currency: "EGP", categories: [] });
    if (path.startsWith("/api/v1/finance/operating-expenses/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path === "/api/v1/auth/csrf") return response({ token: "test-only" });
    if (path.startsWith("/api/v1/finance/operating-expenses/?")) return response({ count: 1, items: [{ ...row, status }] });
    if (path.endsWith("/approve")) { status = "Approved"; return response({ id: row.id, status }); }
    if (path.endsWith("/quote")) return response({ quoteHash: "a".repeat(64), shares: [{ serviceKind: "Training", groupId: "group-1", weight: 1, amount: 100 }], method: "مجموعة مباشرة", total: 100, currency: "EGP" });
    if (path.endsWith("/post")) { expect(init?.headers).toMatchObject({ "Idempotency-Key": expect.any(String) }); status = "Posted"; return response({ id: row.id, replay: false }); }
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses />);
  await screen.findByRole("heading", { name: "تسجيل مصروف" });
  expect(screen.getByText(/ليست رواتب أو تحصيلًا/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "عرض وتوزيع" }));
  fireEvent.click(screen.getByRole("button", { name: "اعتماد المصروف" }));
  await screen.findByRole("button", { name: "معاينة توزيع التكلفة" });
  fireEvent.click(screen.getByRole("button", { name: "معاينة توزيع التكلفة" }));
  await screen.findByRole("button", { name: "ترحيل التكلفة مرة واحدة" });
  expect(screen.getByText(/التدريب · مجموعة اصطناعية —/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "ترحيل التكلفة مرة واحدة" }));
  await screen.findByText(/تم ترحيل المصروف وأنصبته المحفوظة مرة واحدة/);
  expect(calls.some(p => p.includes("/api/v1/reports/financial"))).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1"));
});

it("does not expose expense records when the owner API denies access", async () => {
  global.fetch = vi.fn(async path => String(path).includes("structure/options") ? response({}, 403) : response({}, 403));
  render(<OperatingExpenses />);
  await screen.findByText(/تعذر تحميل الفروع والمجموعات والمرافق/);
  expect(screen.queryByRole("button", { name: "ترحيل التكلفة مرة واحدة" })).not.toBeInTheDocument();
});

it("keeps owner KPI period, branch and approved status in the expense list", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(async input => {
    const path = String(input); calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", categories: [] });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path.includes("operating-expenses/?")) return response({ count: 0, items: [] });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses initialFrom="2026-09-01" initialTo="2026-09-28" initialBranchId="branch-1" initialStatus="Approved" />);
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&from=2026-09-01&to=2026-09-28&branchId=branch-1&status=Approved"));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1"));
});

it("opens the all-time unpaid owner KPI without silently applying a month filter", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(async input => {
    const path = String(input); calls.push(path);
    if (path.includes("structure/options")) return response({ branches: [{ id: "branch-1", arabicName: "فرع اصطناعي" }], groups: [] });
    if (path === "/api/v1/facilities") return response({ resources: [] });
    if (path.endsWith("/options")) return response({ currency: "EGP", categories: [] });
    if (path.includes("/allocations?")) return response({ count: 0, items: [], total: 0, truncated: false });
    if (path.includes("operating-expenses/?")) return response({ count: 0, items: [] });
    throw new Error(`Unexpected request: ${path}`);
  });
  render(<OperatingExpenses initialBranchId="branch-1" initialUnpaid />);
  await waitFor(() => expect(calls).toContain("/api/v1/finance/operating-expenses/?page=1&branchId=branch-1&unpaid=true"));
  expect(screen.getByLabelText("السداد")).toHaveValue("unpaid");
});
