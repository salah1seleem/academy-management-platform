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

it("keeps compatible group and package selected together and filters private training from group offers", async () => {
  global.fetch = vi.fn(input => Promise.resolve(Response.json(String(input).includes("structure/options") ? {
    branches: [{ id: "b", arabicName: "فرع اختبار" }], categories: [{ id: "c", arabicName: "عمرية اختبار" }], sports: [{ id: "swim", arabicName: "سباحة" }], groups: [
      { id: "group", arabicName: "جماعي اختبار", branchId: "b", sportId: "swim", ageCategoryId: "c", registrationFormat: "Group" },
      { id: "private", arabicName: "خاص اختبار", branchId: "b", sportId: "swim", ageCategoryId: "c", registrationFormat: "Private" },
    ],
  } : String(input).includes("discount-rules") ? { items: [] } : [
    { id: "season", arabicName: "موسم اختبار", sportId: "swim", price: 2400, currency: "EGP", isActive: true, trainingFormat: "Season" },
    { id: "private-plan", arabicName: "باقة خاص اختبار", sportId: "swim", price: 900, currency: "EGP", isActive: true, trainingFormat: "Private" },
  ]))) as typeof fetch;
  render(<NewPlayerPage />); await screen.findByRole("option", { name: /موسم اختبار/ }); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b" } }); fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "swim" } }); fireEvent.change(screen.getByLabelText("الفئة العمرية"), { target: { value: "c" } });
  fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "season" } }); expect(screen.queryByRole("option", { name: "خاص اختبار" })).not.toBeInTheDocument(); fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "group" } }); expect(screen.getByLabelText("الباقة")).toHaveValue("season"); expect(screen.getByLabelText("المجموعة")).toHaveValue("group"); expect(screen.queryByRole("option", { name: /باقة خاص اختبار/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "" } }); fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "private-plan" } }); fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "private" } }); expect(screen.getByLabelText("الباقة")).toHaveValue("private-plan"); expect(screen.getByLabelText("المجموعة")).toHaveValue("private");
});

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
