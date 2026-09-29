import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import Page from "./page";
vi.mock("next/navigation", () => ({ useParams: () => ({ paymentId: "pay-1" }), useRouter: () => ({ push: vi.fn() }) }));
beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ id: "pay-1", providerReference: "ITP-1", amount: 900, currency: "EGP", status: "Pending", player: "عمر أحمد محمود", sport: "كرة القدم", plan: "اشتراك شهري" }) }); });
afterEach(cleanup);
describe("internal test checkout", () => {
  it("shows the warning and test states without unsupported payment brands", async () => {
    render(<Page />); expect(await screen.findByText("بيئة دفع تجريبية — لا توجد أموال حقيقية")).toBeVisible();
    expect(screen.getByRole("button", { name: "محاكاة نجاح الدفع" })).toBeVisible();
    expect(screen.getByRole("button", { name: "محاكاة فشل الدفع" })).toBeVisible();
    expect(screen.queryByText(/Cash|InstaPay|Vodafone|Apple Pay|Geidea|Fawry/i)).not.toBeInTheDocument();
  });
  it("shows a discounted guardian review without edit or coupon controls", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ id: "pay-1", providerReference: "ITP-1", amount: 810, currency: "EGP", status: "Pending", player: "عمر أحمد محمود", sport: "كرة القدم", plan: "اشتراك شهري", originalAmount: 900, discountType: "Percentage", discountValue: 10, discountAmount: 90, finalAmount: 810, currentPaymentId: "pay-1", isCurrent: true }) });
    render(<Page />); expect(await screen.findByText("السعر الأصلي")).toBeVisible(); expect(screen.getByText("الخصم").parentElement).toHaveTextContent(/10%.*٩٠/); expect(screen.getByText("الإجمالي بعد الخصم").parentElement).toHaveTextContent(/٨١٠/); expect(screen.queryByLabelText(/خصم|coupon|promo/i)).not.toBeInTheDocument();
  });
});
