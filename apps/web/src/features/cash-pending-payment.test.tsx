import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CashPendingPayment } from "./cash-pending-payment";

const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const options = { amount: "1234.56", currency: "EGP", sessions: [{ id: "shift", name: "صندوق التدريب", branch: "الفرع الاصطناعي" }] };
beforeEach(() => { vi.spyOn(window, "confirm").mockReturnValue(true); global.fetch = vi.fn(async () => response(options)); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("requires an authorized open shift and submits the exact server amount", async () => {
  const saved = vi.fn().mockResolvedValue(undefined); let payload: unknown;
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { payload = JSON.parse(String(init.body)); return response({ receiptId: "receipt" }); }
    return response(options);
  });
  render(<CashPendingPayment id="payment" onCollected={saved} />);
  const button = await screen.findByRole("button", { name: "تأكيد التحصيل النقدي وإصدار الإيصال" }); expect(button).toBeDisabled();
  fireEvent.change(screen.getByRole("combobox", { name: "وردية التحصيل" }), { target: { value: "shift" } }); fireEvent.click(button);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(payload).toEqual({ paymentMethod: "Cash", cashSessionId: "shift", expectedAmount: "1234.56" });
});
it("empty options offer setup without a cash confirmation", async () => {
  global.fetch = vi.fn(async () => response({ ...options, sessions: [] })); render(<CashPendingPayment id="payment" onCollected={vi.fn()} />);
  await screen.findByText("لا توجد وردية مخولة مفتوحة في فرع اللاعب وبعملته.");
  expect(screen.getByRole("button", { name: "تأكيد التحصيل النقدي وإصدار الإيصال" })).toBeDisabled();
  expect(screen.getByRole("link", { name: "فتح الصناديق والورديات" })).toHaveAttribute("href", "/dashboard/finance/cash-registers");
});
it("a denied cashier sees the server error, not money or a usable shift", async () => {
  global.fetch = vi.fn(async () => response({ title: "يلزم تفويض التحصيل." }, 403)); render(<CashPendingPayment id="payment" onCollected={vi.fn()} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("يلزم تفويض التحصيل."); expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
});
it("uncertain failure retains identical retry details and never claims success", async () => {
  const saved = vi.fn(); const bodies: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { bodies.push(JSON.parse(String(init.body))); if (bodies.length === 1) throw new Error("انقطع الاتصال"); return response({ receiptId: "receipt" }); }
    return response(options);
  });
  render(<CashPendingPayment id="payment" onCollected={saved} />); await screen.findByRole("combobox");
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "shift" } }); fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل النقدي وإصدار الإيصال" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("انقطع الاتصال"); expect(saved).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل النقدي وإصدار الإيصال" })); await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(bodies).toHaveLength(2); expect(bodies[0]).toEqual(bodies[1]);
});
it("refresh clears the old shift and requests the authoritative amount again", async () => {
  let loads = 0; global.fetch = vi.fn(async () => response({ ...options, amount: ++loads === 1 ? "1234.56" : "1000.00" }));
  render(<CashPendingPayment id="payment" onCollected={vi.fn()} />); await screen.findByRole("combobox"); fireEvent.change(screen.getByRole("combobox"), { target: { value: "shift" } });
  fireEvent.click(screen.getByRole("button", { name: "تحديث المبلغ والورديات" })); await screen.findByRole("combobox");
  expect(screen.getByRole("combobox")).toHaveValue(""); expect(loads).toBe(2);
});
