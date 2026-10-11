import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlanForm } from "./plan-editor";
import NewPlayerPage from "../app/dashboard/players/new/page";
const push = vi.fn(); vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
const options = { sports: [{ id: "swim", arabicName: "السباحة" }], branches: [{ id: "a", arabicName: "مدينة نصر" }, { id: "b", arabicName: "التجمع" }], categories: [], groups: [] };
const plan = { id: "p", arabicName: "سباحة شهرية", englishName: "Swim", sportId: "swim", sport: "السباحة", planType: "Combined", price: "600.00", currency: "EGP", durationDays: 30, sessionCount: 8, displayOrder: 2, allBranches: false, branchIds: ["a"], revision: 3, isActive: true, versions: [{ revision: 3, arabicName: "سباحة شهرية", price: "600.00", currency: "EGP", planType: "Combined", durationDays: 30, sessionCount: 8, published: true, effectiveAtUtc: "2026-10-11T08:00:00Z" }] };
const ok = (data: unknown) => new Response(JSON.stringify(data), { status: 200, headers: { "Content-Type": "application/json" } });
function mock(restricted = false) { global.fetch = vi.fn(async (input, init) => { const url = String(input); if (url.endsWith("/auth/csrf")) return ok({ token: "csrf" }); if (url.includes("structure/options")) return ok(options); if (url.includes("my-access")) return ok({ allBranches: !restricted }); if (init?.method === "PUT") return new Response(null, { status: 204 }); return ok(plan); }) as typeof fetch; }
afterEach(() => { cleanup(); vi.clearAllMocks(); });
describe("plan publication and captured terms", () => {
  it("edits a branch-specific plan with preserved names, price string and expected revision", async () => {
    mock(); render(<PlanForm id="p" />); await screen.findByDisplayValue("سباحة شهرية"); expect(screen.getByLabelText("الاسم الإنجليزي — اختياري")).toHaveValue("Swim"); expect(screen.getByLabelText("مدينة نصر")).toBeChecked(); expect(screen.getByLabelText("التجمع")).not.toBeChecked();
    fireEvent.click(screen.getByLabelText("التجمع")); fireEvent.click(screen.getByRole("button", { name: "حفظ الباقة" })); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/plans"));
    const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body))).toMatchObject({ price: "600.00", expectedRevision: 3, allBranches: false, branchIds: ["a", "b"], durationDays: 30, sessionCount: 8 });
  });
  it("never submits stale session limits when switching a combined plan to duration", async () => {
    mock(); render(<PlanForm id="p" />); await screen.findByDisplayValue("سباحة شهرية"); fireEvent.change(screen.getByLabelText("نوع الخدمة"), { target: { value: "Duration" } }); expect(screen.queryByLabelText("عدد الحصص")).not.toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "حفظ الباقة" })); await waitFor(() => expect(push).toHaveBeenCalled());
    const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body)).sessionCount).toBeNull();
  });
  it("shows immutable revision history without edit/delete history controls", async () => {
    mock(); render(<PlanForm id="p" readOnly />); await screen.findByRole("heading", { name: "تاريخ الأسعار وشروط الخدمة" }); expect(screen.getByText(/نسخة 3/)).toBeVisible(); expect(screen.queryByRole("button", { name: /حذف نسخة|تعديل نسخة/ })).not.toBeInTheDocument();
  });
  it("restricts branch administrators to explicit branch publication", async () => {
    mock(true); render(<PlanForm />); await screen.findByLabelText("مدينة نصر"); expect(screen.queryByLabelText("كل فروع الأكاديمية")).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "حفظ الباقة" })).toBeDisabled();
  });
  it("filters the New Player package dropdown by sport AND branch, clearing stale selection", async () => {
    global.fetch = vi.fn(async input => String(input).includes("structure/options") ? ok(options) : String(input).includes("discount-rules") ? ok({ items: [] }) : ok([plan])) as typeof fetch;
    render(<NewPlayerPage />); await screen.findByRole("option", { name: /سباحة شهرية/ }); fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "swim" } }); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "a" } }); fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "p" } }); expect(screen.getByLabelText("الباقة")).toHaveValue("p");
    fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b" } }); expect(screen.queryByRole("option", { name: /سباحة شهرية/ })).not.toBeInTheDocument(); expect(screen.getByLabelText("الباقة")).toHaveValue(""); expect(screen.getByRole("button", { name: "حساب المبلغ المستحق" })).toBeDisabled();
  });
});
