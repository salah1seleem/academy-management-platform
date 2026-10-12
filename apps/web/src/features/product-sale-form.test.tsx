import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ProductSaleForm } from "./product-sale-form";
const response = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
const quote = { unitPrice: "90.10", amount: "180.20", currency: "EGP", stockQuantity: 3, cashEnabled: true, sessions: [{ id: "shift", name: "صندوق فرعي" }] };
let cashEnabled = true, sessions = quote.sessions;
let writes: RequestInit[];
function setup(override?: (url: string, init?: RequestInit) => Promise<Response> | undefined) {
  global.fetch = vi.fn(async (url, init) => {
    const u = String(url); const custom = override?.(u, init); if (custom) return custom;
    if (u.endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { writes.push(init); return response({ id: "sale", amount: quote.amount, currency: "EGP" }); }
    if (u.includes("/quote?")) return response({ ...quote, cashEnabled, sessions });
    if (u.includes("catalog")) return response({ arabicName: "منتج اصطناعي", isActive: true, stockQuantity: 3 });
    if (u.includes("options")) return response({ branches: [{ id: "branch", arabicName: "فرع أول" }, { id: "other", arabicName: "فرع ثان" }] });
    return response([]);
  });
}
async function choose(method = "Cash") {
  await screen.findByText("منتج اصطناعي"); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "branch" } });
  fireEvent.change(screen.getByLabelText("الكمية"), { target: { value: "2" } }); fireEvent.change(screen.getByLabelText("طريقة الدفع"), { target: { value: method } });
  await screen.findByRole("button", { name: "تأكيد التحصيل والبيع" });
}
beforeEach(() => { cashEnabled = true; sessions = quote.sessions; writes = []; vi.spyOn(window, "confirm").mockReturnValue(true); setup(); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("requires explicit method and own shift; posts server money as a precise string and locks completed sale", async () => {
  render(<ProductSaleForm id="product" />); await screen.findByText("منتج اصطناعي");
  expect(screen.getByLabelText("طريقة الدفع")).toHaveValue(""); expect(screen.queryByRole("button", { name: "تأكيد التحصيل والبيع" })).not.toBeInTheDocument();
  await choose(); expect(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("وردية التحصيل"), { target: { value: "shift" } }); fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" }));
  await screen.findByText("تم التحصيل وخصم المخزون");
  expect(JSON.parse(String(writes[0].body))).toEqual({ productId: "product", branchId: "branch", playerId: null, quantity: 2, paymentMethod: "Cash", expectedAmount: "180.20", cashSessionId: "shift" });
  expect(screen.getByLabelText("الفرع")).toBeDisabled(); expect(screen.getByRole("link", { name: "تفاصيل العملية" })).toHaveAttribute("href", "/dashboard/product-sales/sale");
});
it.each(["disabled", "unassigned"])("prevents collecting when cash is %s", async mode => {
  cashEnabled = mode !== "disabled"; sessions = []; render(<ProductSaleForm id="product" />); await choose();
  expect(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" })).toBeDisabled();
  expect(await screen.findByText(mode === "disabled" ? "النقدي غير مفعل لهذه الأكاديمية." : "لا توجد وردية مفتوحة مخولة في فرع البيع وعملة المنتج.")).toBeVisible();
});
it("refresh discards shift and ignores responses belonging to an old branch", async () => {
  let resolveOld: (r: Response) => void = () => {};
  setup(url => url.includes("/quote?") && url.includes("branchId=branch") ? new Promise<Response>(resolve => { resolveOld = resolve; }) : undefined);
  render(<ProductSaleForm id="product" />); await screen.findByText("منتج اصطناعي"); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "branch" } }); fireEvent.change(screen.getByLabelText("طريقة الدفع"), { target: { value: "Cash" } });
  fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "other" } }); await screen.findByLabelText("وردية التحصيل");
  resolveOld(response({ ...quote, sessions: [{ id: "stale", name: "صندوق قديم لا يظهر" }] }));
  await waitFor(() => expect(screen.queryByText("صندوق قديم لا يظهر")).not.toBeInTheDocument());
  fireEvent.change(screen.getByLabelText("وردية التحصيل"), { target: { value: "shift" } });
  fireEvent.click(screen.getByRole("button", { name: "تحديث المبلغ والورديات" }));
  expect(await screen.findByLabelText("وردية التحصيل")).toHaveValue(""); expect(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" })).toBeDisabled();
});
it("online confirmation is explicit and requires reviewed money but no cash shift", async () => {
  cashEnabled = false; render(<ProductSaleForm id="product" />); await choose("Online");
  expect(screen.queryByLabelText("وردية التحصيل")).not.toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" }));
  await screen.findByText("تم التحصيل وخصم المخزون"); expect(JSON.parse(String(writes[0].body))).toMatchObject({ paymentMethod: "Online", expectedAmount: "180.20", cashSessionId: null });
});
it("uses Arabic connection errors and allows a fresh quote without technical leakage", async () => {
  setup(url => url.includes("/quote?") ? Promise.reject(new Error("Failed to fetch")) : undefined);
  render(<ProductSaleForm id="product" />); await screen.findByText("منتج اصطناعي"); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "branch" } }); fireEvent.change(screen.getByLabelText("طريقة الدفع"), { target: { value: "Cash" } });
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل المراجعة"); expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument();
  setup(); fireEvent.click(screen.getByRole("button", { name: "تحديث المبلغ والورديات" })); await screen.findByLabelText("وردية التحصيل");
});
it("locks all context controls while submitting and never changes the pending amount", async () => {
  let done: (r: Response) => void = () => {};
  setup((url, init) => init?.method === "POST" ? (writes.push(init), new Promise<Response>(resolve => { done = resolve; })) : undefined);
  render(<ProductSaleForm id="product" />); await choose(); fireEvent.change(screen.getByLabelText("وردية التحصيل"), { target: { value: "shift" } }); fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" }));
  await waitFor(() => expect(writes).toHaveLength(1));
  for (const name of ["الفرع", "الكمية", "طريقة الدفع", "اللاعب — اختياري", "وردية التحصيل"]) expect(screen.getByLabelText(name)).toBeDisabled();
  done(response({ id: "sale", amount: "180.20", currency: "EGP" })); await screen.findByText("تم التحصيل وخصم المخزون");
});
