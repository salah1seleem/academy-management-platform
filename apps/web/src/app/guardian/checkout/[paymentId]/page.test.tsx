import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import Page from "./page";
import { pendingFinancialIntent } from "../../../../lib/pending-financial-intent";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useParams: () => ({ paymentId: "pay-1" }), useRouter: () => ({ push }) }));
vi.mock("../../../../lib/pending-financial-intent", () => ({
  pendingFinancialIntent: vi.fn(async () => "stable-retry-key"),
  clearFinancialIntent: vi.fn(),
}));

const payment = {
  id: "pay-1", provider: "InternalTest", simulationAvailable: true, providerReference: "ITP-1",
  amount: 900, currency: "EGP", status: "Pending", player: "لاعب تجريبي",
  sport: "كرة القدم", plan: "اشتراك شهري", isCurrent: true,
};
const json = (data: unknown, status = 200) => Response.json(data, { status });

beforeEach(() => { vi.clearAllMocks(); });
afterEach(cleanup);

describe("guardian payment checkout", () => {
  it("shows the test warning and discount alongside the exact total", async () => {
    global.fetch = vi.fn(async () => json({
      ...payment, amount: 810, originalAmount: 900,
      discountType: "Percentage", discountValue: 10, discountAmount: 90,
    }));
    render(<Page />);
    expect(await screen.findByText("بيئة دفع تجريبية — لا توجد أموال حقيقية")).toBeVisible();
    expect(screen.getByRole("button", { name: "محاكاة نجاح الدفع" })).toBeVisible();
    expect(screen.getByText("الخصم").parentElement).toHaveTextContent(/10%.*90/);
    expect(screen.getByText("الإجمالي بعد الخصم").parentElement).toHaveTextContent(/810/);
  });

  it("checks the server after a lost simulation response without sending it again", async () => {
    let posted = false;
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return json({ token: "csrf" });
      if (init?.method === "POST") { posted = true; throw new TypeError("connection lost"); }
      return json(posted ? { ...payment, status: "Confirmed", receiptId: "receipt-1" } : payment);
    });
    render(<Page />);
    fireEvent.click(await screen.findByRole("button", { name: "محاكاة نجاح الدفع" }));
    const check = await screen.findByRole("button", { name: "تحقّق من حالة الدفع" });
    expect(screen.queryByText("تم الدفع وتجديد الاشتراك بنجاح")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /محاكاة/ })).not.toBeInTheDocument();
    fireEvent.click(check);
    expect(await screen.findByRole("link", { name: "عرض الإيصال" })).toHaveAttribute("href", "/guardian/receipts/receipt-1");
    expect(vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(1);
  });

  it("reads a confirmed result and receipt after a successful simulation", async () => {
    let posted = false;
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return json({ token: "csrf" });
      if (init?.method === "POST") { posted = true; return json({ accepted: true }); }
      return json(posted ? { ...payment, status: "Confirmed", receiptId: "receipt-1" } : payment);
    });
    render(<Page />);
    fireEvent.click(await screen.findByRole("button", { name: "محاكاة نجاح الدفع" }));
    expect(await screen.findByRole("link", { name: "عرض الإيصال" })).toBeVisible();
  });

  it("shows manual transfer details, then waits for a saved proof status", async () => {
    let submitted = false;
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return json({ token: "csrf" });
      if (String(path).endsWith("/payment-settings")) return json({ configured: true, instaPayAddress: "demo@example.test" });
      if (String(path).endsWith("/assets/upload")) return json({ id: "asset-1" }, 201);
      if (String(path).endsWith("/proof")) {
        expect(JSON.parse(String(init?.body))).toEqual({ assetId: "asset-1" });
        submitted = true;
        return json({ id: "proof-1" });
      }
      return json({ ...payment, provider: "Manual", simulationAvailable: false, proofStatus: submitted ? "Pending" : null });
    });
    render(<Page />);
    expect(await screen.findByText("demo@example.test")).toBeVisible();
    expect(screen.queryByRole("button", { name: /محاكاة/ })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("صورة إيصال التحويل"), { target: { files: [new File(["synthetic"], "proof.png", { type: "image/png" })] } });
    fireEvent.click(screen.getByRole("button", { name: "إرسال إثبات التحويل" }));
    expect(await screen.findByText("الإثبات قيد المراجعة")).toBeVisible();
    expect(screen.queryByRole("link", { name: "عرض الإيصال" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "إرسال إثبات التحويل" })).not.toBeInTheDocument();
  });

  it("withholds simulations when the server capability is off", async () => {
    global.fetch = vi.fn(async () => json({ ...payment, simulationAvailable: false }));
    render(<Page />);
    expect(await screen.findByText("طريقة الدفع غير متاحة لهذا الطلب. تواصل مع الأكاديمية.")).toBeVisible();
    expect(screen.queryByRole("button", { name: /محاكاة/ })).not.toBeInTheDocument();
  });

  it("shows a denied record without a persistent loading state or payment actions", async () => {
    global.fetch = vi.fn(async () => json({}, 404));
    render(<Page />);
    expect(await screen.findByRole("alert")).toHaveTextContent("طلب الدفع غير متاح لهذا الحساب.");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.queryByText(payment.player)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /محاكاة|إعادة محاولة الدفع/ })).not.toBeInTheDocument();
  });

  it("links a superseded payment to the current request without allowing a write", async () => {
    global.fetch = vi.fn(async () => json({ ...payment, isCurrent: false, currentPaymentId: "new-payment" }));
    render(<Page />);
    expect(await screen.findByRole("link", { name: "فتح طلب الدفع الحالي" })).toHaveAttribute("href", "/guardian/checkout/new-payment");
    expect(screen.queryByRole("button", { name: /محاكاة|إعادة محاولة الدفع/ })).not.toBeInTheDocument();
  });

  it("replays the same replacement request after a lost reply", async () => {
    let attempts = 0;
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return json({ token: "csrf" });
      if (init?.method === "POST") {
        expect(new Headers(init.headers).get("Idempotency-Key")).toBe("stable-retry-key");
        if (++attempts === 1) throw new TypeError("connection lost");
        return json({ paymentId: "next-payment" });
      }
      return json({ ...payment, status: "Failed" });
    });
    render(<Page />);
    fireEvent.click(await screen.findByRole("button", { name: "إعادة محاولة الدفع" }));
    fireEvent.click(await screen.findByRole("button", { name: "إعادة إرسال نفس الطلب" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/guardian/checkout/next-payment"));
    expect(vi.mocked(pendingFinancialIntent).mock.calls).toEqual([
      ["guardian-payment-retry:pay-1", {}], ["guardian-payment-retry:pay-1", {}],
    ]);
  });
});
