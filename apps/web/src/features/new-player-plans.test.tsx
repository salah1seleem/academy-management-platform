import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import NewPlayerPage from "../app/dashboard/players/new/page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), back: vi.fn() }) }));
beforeEach(() => {
  global.fetch = vi.fn(input => Promise.resolve(new Response(JSON.stringify(
    String(input).includes("structure/options") ? {
      branches: [], categories: [], groups: [],
      sports: [{ id: "football", arabicName: "كرة القدم" }, { id: "swimming", arabicName: "سباحة" }, { id: "tennis", arabicName: "تنس" }],
    } : String(input).includes("discount-rules") ? { items: [] } : [
      { id: "monthly", arabicName: "اشتراك شهري", sportId: "football", price: 900, currency: "EGP", isActive: true },
      { id: "swim", arabicName: "باقة سباحة", sportId: "swimming", price: 500, currency: "EGP", isActive: true },
      { id: "inactive", arabicName: "باقة متوقفة", sportId: "football", price: 100, currency: "EGP", isActive: false },
    ]
  )))) as typeof fetch;
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("shows active packages before choosing a sport and selects the package sport automatically", async () => {
  render(<NewPlayerPage />);
  expect(await screen.findByRole("option", { name: /اشتراك شهري/ })).toBeVisible();
  expect(screen.getByRole("option", { name: /باقة سباحة/ })).toBeVisible();
  expect(screen.queryByRole("option", { name: /باقة متوقفة/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "monthly" } });
  expect(screen.getByLabelText("الرياضة")).toHaveValue("football");
  expect(screen.getByLabelText("الباقة")).toHaveValue("monthly");
  expect(screen.queryByRole("option", { name: /باقة سباحة/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "swimming" } });
  expect(screen.getByLabelText("الباقة")).toHaveValue("");
  expect(screen.getByRole("option", { name: /باقة سباحة/ })).toBeVisible();
  expect(screen.queryByRole("option", { name: /اشتراك شهري/ })).not.toBeInTheDocument();
});

it("explains when the chosen sport has no active packages without allowing collection", async () => {
  render(<NewPlayerPage />);
  await screen.findByRole("option", { name: /اشتراك شهري/ });
  fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "tennis" } });
  expect(screen.getByText(/لا توجد باقات نشطة لهذه الرياضة/)).toBeVisible();
  expect(screen.getByRole("button", { name: "تسجيل وتأكيد التحصيل" })).toBeDisabled();
});
