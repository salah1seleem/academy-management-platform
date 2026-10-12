import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { CashRegisters, CashSessionDetail } from "./cash-registers";
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
const push = vi.fn();
vi.mock("./use-staff-action", () => ({ useStaffAction: () => canAdd }));
let canAdd = true;
const response = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
const register = { id: "register", name: "صندوق اصطناعي", branch: "الفرع التجريبي", currency: "EGP", canOpen: true, cashiers: [{ membershipId: "member", name: "أمين تجريبي", isActive: true }] };
const data = { cashEnabled: true, items: [register], history: [] };
const options = { isOwner: true, branches: [{ id: "branch", name: "الفرع التجريبي" }], members: [{ id: "member", name: "أمين تجريبي" }] };
beforeEach(() => { canAdd = true; push.mockClear(); sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); global.fetch = vi.fn(async url => response(String(url).endsWith("options") ? options : data)); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
it("shows scoped cash registers and owner setup without inventing income", async () => {
  render(<CashRegisters />); await screen.findByText("صندوق اصطناعي");
  expect(screen.getByText("فتح وردية")).toBeInTheDocument(); expect(screen.getByText("إضافة صندوق فرع")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "إعدادات الأكاديمية" })).toHaveAttribute("href", "/dashboard/settings");
  expect(screen.queryByText("تأكيد التحويل")).not.toBeInTheDocument();
});
it("disabled cash hides open even for an assigned cashier; read-only admin cannot configure", async () => {
  canAdd = false; global.fetch = vi.fn(async url => response(String(url).endsWith("options") ? { ...options, isOwner: false, members: [] } : { ...data, cashEnabled: false }));
  render(<CashRegisters />); await screen.findByText("النقدي متوقف");
  expect(screen.queryByText("فتح وردية")).not.toBeInTheDocument(); expect(screen.queryByText("إضافة صندوق فرع")).not.toBeInTheDocument(); expect(screen.queryByText("صلاحيات أمناء الصندوق")).not.toBeInTheDocument();
});
it("retains precise opening amount and idempotency key after uncertain failure", async () => {
  const keys: string[] = [], bodies: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { keys.push((init.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(JSON.parse(init.body as string)); if (keys.length === 1) throw new Error("انقطع الاتصال"); return response({ id: "session" }); }
    return response(String(url).endsWith("options") ? options : data);
  });
  render(<CashRegisters />); await screen.findByText("فتح وردية"); fireEvent.click(screen.getByText("فتح وردية"));
  const amount = screen.getByLabelText("العهدة الافتتاحية"); fireEvent.change(amount, { target: { value: "12.34" } }); fireEvent.submit(amount.closest("form")!);
  await screen.findByText("انقطع الاتصال"); fireEvent.submit(amount.closest("form")!); await waitFor(() => expect(keys).toHaveLength(2));
  expect(keys[0]).toBe(keys[1]); expect(bodies).toEqual([{ openingAmount: "12.34" }, { openingAmount: "12.34" }]);
});
it("closed reconciliation displays variance without offering a second close", async () => {
  global.fetch = vi.fn(async () => response({ name: "صندوق مغلق", currency: "EGP", balance: "100.00", variance: "-10.00", canClose: true, session: { openingAmount: "100", openedAtUtc: "2026-09-28T06:00:00Z", closedAtUtc: "2026-09-28T14:00:00Z", expectedAmount: "100", countedAmount: "90" }, movements: [] }));
  render(<CashSessionDetail id="session" />); await screen.findByText("فرق المطابقة");
  expect(screen.queryByRole("button", { name: "تثبيت المطابقة وإقفال الوردية" })).not.toBeInTheDocument();
  expect(screen.getByText(/لا توجد حركات للوردية/)).toBeInTheDocument();
});
it("failed authorization does not show stale money or controls", async () => {
  global.fetch = vi.fn(async () => response({}, 404)); render(<CashSessionDetail id="foreign" />); await screen.findByText("تعذر تحميل الصندوق أو أنه خارج صلاحياتك.");
  expect(screen.queryByText("الرصيد الدفتري")).not.toBeInTheDocument(); expect(screen.queryByLabelText("المبلغ الموجود فعليًا")).not.toBeInTheDocument();
});
it("links typed product cash postings without pretending they are subscription receipts", async () => {
  global.fetch = vi.fn(async () => response({ name: "صندوق متجر", currency: "EGP", balance: "280.00", variance: null, canClose: false, session: { openingAmount: "100.00", openedAtUtc: "2026-09-28T06:00:00Z" }, movements: [], productSales: [{ id: "posting", productSaleId: "sale", amount: "180.00", currency: "EGP", quantity: 2, productName: "طقم اصطناعي", createdAtUtc: "2026-09-28T06:00:00Z" }] }));
  render(<CashSessionDetail id="session" />); await screen.findByText("مبيعات المنتجات النقدية"); expect(screen.getByText("طقم اصطناعي")).toBeVisible();
  expect(screen.getByRole("link", { name: "تفاصيل البيع" })).toHaveAttribute("href", "/dashboard/product-sales/sale"); expect(screen.queryByText("إيصالات القبض في الوردية")).not.toBeInTheDocument();
});
it("closing is explicit, optional reason and navigates back only after confirmed write", async () => {
  vi.spyOn(window, "confirm").mockReturnValue(true); const writes: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { writes.push(JSON.parse(init.body as string)); return response({ id: "session" }); }
    return response({ name: "صندوق مفتوح", currency: "EGP", balance: "100.00", variance: null, canClose: true, session: { openingAmount: "100", openedAtUtc: "2026-09-28T06:00:00Z" }, movements: [] });
  });
  render(<CashSessionDetail id="session" />); const amount = await screen.findByLabelText("المبلغ الموجود فعليًا"); fireEvent.change(amount, { target: { value: "90.00" } }); fireEvent.submit(amount.closest("form")!);
  await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/finance/cash-registers")); expect(writes).toEqual([{ countedAmount: "90.00", reason: null }]);
});
