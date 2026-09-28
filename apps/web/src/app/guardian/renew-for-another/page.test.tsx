import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import Page from "./page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

beforeEach(() => {
  global.fetch = vi.fn()
    .mockResolvedValueOnce({ ok: true, json: async () => ({ token: "csrf" }) })
    .mockResolvedValueOnce({ ok: true, json: async () => ({ playerDisplayName: "عمر أحمد حسن", sport: "كرة القدم", academyName: "أكاديمية النجوم الرياضية", plans: [{ id: "plan-1", arabicName: "اشتراك شهري", planType: "Duration", price: 900, currency: "EGP" }] }) });
});

describe("renew subscription for another player", () => {
  it("resolves a code into minimal beneficiary confirmation without public search", async () => {
    render(<Page />);
    fireEvent.change(screen.getByLabelText("كود التجديد"), { target: { value: "RNW-DEMO-NG-0003-7K9M" } });
    fireEvent.click(screen.getByRole("button", { name: "تحقق من الكود" }));
    expect(await screen.findByText("عمر أحمد حسن")).toBeVisible();
    expect(screen.getByText("كرة القدم")).toBeVisible();
    expect(screen.getByText("أكاديمية النجوم الرياضية")).toBeVisible();
    expect(screen.getByText("اشتراك شهري")).toBeVisible();
    expect(screen.queryByText(/هاتف|ولي الأمر|طبي|حضور|تقييم|بحث عن لاعب/)).not.toBeInTheDocument();
  });
});
