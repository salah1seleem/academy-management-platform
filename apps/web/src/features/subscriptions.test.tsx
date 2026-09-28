import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PlanList } from "./subscriptions";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "p1", arabicName: "اشتراك شهري", sport: "كرة القدم", sportId: "s1", planType: "Duration", price: 900, currency: "EGP", durationDays: 30, isActive: true }] }); });

describe("subscription plan list", () => {
  it("renders list columns, add action, and only functional row actions", async () => {
    render(<PlanList />);
    expect(screen.getByRole("link", { name: "إضافة باقة" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/new");
    expect(await screen.findByText("اشتراك شهري")).toBeVisible();
    expect(screen.getByText(/كرة القدم · Duration · 900 EGP · 30 يوم/)).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/p1");
    expect(screen.getByRole("link", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/p1/edit");
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "غير موجود" } });
    expect(screen.queryByText("اشتراك شهري")).not.toBeInTheDocument();
  });
});
