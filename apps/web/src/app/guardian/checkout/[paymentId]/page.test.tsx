import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import Page from "./page";
vi.mock("next/navigation", () => ({ useParams: () => ({ paymentId: "pay-1" }) }));
beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ id: "pay-1", providerReference: "ITP-1", amount: 900, currency: "EGP", status: "Pending", player: "عمر أحمد محمود", sport: "كرة القدم", plan: "اشتراك شهري" }) }); });
describe("internal test checkout", () => {
  it("shows the warning and test states without unsupported payment brands", async () => {
    render(<Page />); expect(await screen.findByText("بيئة دفع تجريبية — لا توجد أموال حقيقية")).toBeVisible();
    expect(screen.getByRole("button", { name: "محاكاة نجاح الدفع" })).toBeVisible();
    expect(screen.getByRole("button", { name: "محاكاة فشل الدفع" })).toBeVisible();
    expect(screen.queryByText(/Cash|InstaPay|Vodafone|Apple Pay|Geidea|Fawry/i)).not.toBeInTheDocument();
  });
});
