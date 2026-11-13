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
    if (init?.method === "POST") { writes.push(init); return response({ id: "sale", status: "Confirmed", amount: quote.amount, currency: "EGP" }); }
    if (u.includes("/quote?")) return response({ ...quote, cashEnabled, sessions });
    if (u.includes("catalog")) return response({ arabicName: "منتج اصطناعي", isActive: true, stockQuantity: 3 });
    if (u.includes("options")) return response({ branches: [{ id: "branch", arabicName: "فرع أول" }, { id: "other", arabicName: "فرع ثان" }] });
    return response([{ id: "player", arabicName: "لاعب اصطناعي" }]);
  });
}
function submitSale() {
  fireEvent.click(screen.getByRole("button", { name: "مراجعة التحصيل والبيع" }));
  fireEvent.click(screen.getByRole("button", { name: "تسجيل البيع" }));
}
async function choose(method = "Cash") {
  await screen.findByText("منتج اصطناعي"); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "branch" } });
  fireEvent.change(screen.getByLabelText("الكمية"), { target: { value: "2" } }); fireEvent.change(screen.getByLabelText("طريقة الدفع"), { target: { value: method } });
  await screen.findByRole("button", { name: "مراجعة التحصيل والبيع" });
}
beforeEach(() => { cashEnabled = true; sessions = quote.sessions; writes = []; setup(); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("requires explicit method and own shift; posts server money as a precise string and locks completed sale", async () => {
  render(<ProductSaleForm id="product" />); await screen.findByText("منتج اصطناعي");
  expect(screen.getByLabelText("طريقة الدفع")).toHaveValue(""); expect(screen.queryByRole("button", { name: "مراجعة التحصيل والبيع" })).not.toBeInTheDocument();
  await choose(); expect(screen.getByRole("button", { name: "مراجعة التحصيل والبيع" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("وردية التحصيل"), { target: { value: "shift" } }); submitSale();
  await screen.findByText("تم التحصيل وخصم المخزون");
  expect(JSON.parse(String(writes[0].body))).toEqual({ productId: "product", branchId: "branch", playerId: null, quantity: 2, paymentMethod: "Cash", expectedAmount: "180.20", cashSessionId: "shift" });
  expect(screen.getByLabelText("الفرع")).toBeDisabled(); expect(screen.getByRole("link", { name: "تفاصيل العملية" })).toHaveAttribute("href", "/dashboard/product-sales/sale");
});
it.each(["disabled", "unassigned"])("prevents collecting when cash is %s", async mode => {
  cashEnabled = mode !== "disabled"; sessions = []; render(<ProductSaleForm id="product" />); await choose();
  expect(screen.getByRole("button", { name: "مراجعة التحصيل والبيع" })).toBeDisabled();
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
  expect(await screen.findByLabelText("وردية التحصيل")).toHaveValue(""); expect(screen.getByRole("button", { name: "مراجعة التحصيل والبيع" })).toBeDisabled();
});
it("online confirmation is explicit and requires reviewed money but no cash shift", async () => {
  cashEnabled = false; render(<ProductSaleForm id="product" />); await choose("Online");
  expect(screen.queryByLabelText("وردية التحصيل")).not.toBeInTheDocument(); submitSale();
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
  render(<ProductSaleForm id="product" />); await choose(); fireEvent.change(screen.getByLabelText("وردية التحصيل"), { target: { value: "shift" } }); submitSale();
  await waitFor(() => expect(writes).toHaveLength(1));
  for (const name of ["الفرع", "الكمية", "طريقة الدفع", "اللاعب — اختياري"]) expect(screen.getByLabelText(name)).toBeDisabled();
  expect(screen.queryByLabelText("وردية التحصيل")).not.toBeInTheDocument();
  done(response({ id: "sale", status: "Confirmed", amount: "180.20", currency: "EGP" })); await screen.findByText("تم التحصيل وخصم المخزون");
});
it("requires an available size/number and sends its exact id to quote and sale",async()=>{
 setup((url)=>{if(url.endsWith('/catalog/product'))return Promise.resolve(response({arabicName:'منتج اصطناعي',isActive:true,stockQuantity:3,usesVariants:true}));if(url.endsWith('/inventory'))return Promise.resolve(response({variants:[{id:'M7',color:'كحلي',size:'M',shirtNumber:7,quantity:3},{id:'M7-white',color:'أبيض',size:'M',shirtNumber:7,quantity:1}]}));return undefined;});
 render(<ProductSaleForm id="product"/>);await screen.findByLabelText('اللون والمقاس والرقم');expect(screen.getByRole('option',{name:'كحلي · M · رقم 7 · المتاح 3'})).toBeVisible();expect(screen.getByRole('option',{name:'أبيض · M · رقم 7 · المتاح 1'})).toBeVisible();expect(screen.queryByRole('button',{name:'مراجعة التحصيل والبيع'})).not.toBeInTheDocument();
 fireEvent.change(screen.getByLabelText('اللون والمقاس والرقم'),{target:{value:'M7'}});await choose('Online');
 await waitFor(()=>expect(vi.mocked(fetch).mock.calls.some(([u])=>String(u).includes('variantId=M7'))).toBe(true));submitSale();await screen.findByText('تم التحصيل وخصم المخزون');expect(JSON.parse(String(writes[0].body)).variantId).toBe('M7');expect(screen.getByLabelText('اللون والمقاس والرقم')).toBeDisabled();
});

it("keeps the same sale locked when its response is lost and reads the exact saved sale", async () => {
  setup((url, init) => {
    if (init?.method === "POST") {
      writes.push(init);
      return Promise.reject(new TypeError("network"));
    }
    if (url.includes("/by-request?")) {
      const key = (writes[0].headers as Record<string, string>)["Idempotency-Key"];
      expect(new URL(url, "http://local.test").searchParams.get("requestKey")).toBe(key);
      return Promise.resolve(response({ id: "sale", status: "Confirmed", amount: "180.20", currency: "EGP" }));
    }
  });
  render(<ProductSaleForm id="product" />);
  await choose("Online");
  submitSale();
  expect(await screen.findByText("نتيجة البيع بانتظار التحقق")).toBeVisible();
  expect(screen.getByLabelText("الكمية")).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "التحقق من تسجيل البيع" }));
  expect(await screen.findByText("تم التحصيل وخصم المخزون")).toBeVisible();
  expect(writes).toHaveLength(1);
});

it("keeps a read-only review until explicit confirmation, and supports cancellation", async () => {
  render(<ProductSaleForm id="product" />);
  await choose("Online");
  expect(screen.queryByRole("link", { name: "فتح الصناديق والورديات" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "مراجعة التحصيل والبيع" }));
  expect(screen.getByRole("dialog")).toHaveTextContent("منتج اصطناعي · فرع أول · الكمية 2");
  expect(screen.getByRole("dialog")).toHaveTextContent("تحويل إلكتروني مستلم");
  fireEvent.click(screen.getByRole("button", { name: "إلغاء" }));
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(screen.getByLabelText("الكمية")).not.toBeDisabled();
  expect(writes).toHaveLength(0);
});

it("requires a new quote and new request after the original price was rejected", async () => {
  setup((url, init) => {
    if (init?.method === "POST" && !writes.length) {
      writes.push(init);
      return Promise.resolve(response({ message: "تغير السعر. حدّث مراجعة المبلغ." }, 409));
    }
  });
  render(<ProductSaleForm id="product" />);
  await choose("Online");
  submitSale();
  expect(await screen.findByRole("alert")).toHaveTextContent("تغير السعر");
  expect(screen.queryByRole("button", { name: "مراجعة التحصيل والبيع" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "تحديث المبلغ والمخزون" }));
  await screen.findByRole("button", { name: "مراجعة التحصيل والبيع" });
  submitSale();
  expect(await screen.findByText("تم التحصيل وخصم المخزون")).toBeVisible();
  expect(writes).toHaveLength(2);
  expect(writes[0].headers).not.toEqual(writes[1].headers);
});

it("does not offer a sale when stock is exhausted", async () => {
  setup(url => url.endsWith("/catalog/product") ? Promise.resolve(response({ arabicName: "منتج اصطناعي", isActive: true, stockQuantity: 0 })) : undefined);
  render(<ProductSaleForm id="product" />);
  expect(await screen.findByText("المخزون المطلوب نافد.")).toBeVisible();
  expect(screen.queryByLabelText("الكمية")).not.toBeInTheDocument();
  expect(writes).toHaveLength(0);
});
