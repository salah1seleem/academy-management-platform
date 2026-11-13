import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlanForm, seasonPrice } from "./plan-editor";
import NewPlayerPage from "../app/dashboard/players/new/page";
const push = vi.fn(); vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), useSearchParams: () => new URLSearchParams() }));
const options = { sports: [{ id: "swim", arabicName: "السباحة" }], branches: [{ id: "a", arabicName: "مدينة نصر" }, { id: "b", arabicName: "التجمع" }], categories: [], groups: [] };
const plan = { id: "p", arabicName: "سباحة شهرية", englishName: "Swim", sportId: "swim", sport: "السباحة", planType: "Combined", price: "600.00", currency: "EGP", durationDays: 30, sessionCount: 8, displayOrder: 2, allBranches: false, branchIds: ["a"], revision: 3, isActive: true, versions: [{ revision: 3, arabicName: "سباحة شهرية", price: "600.00", currency: "EGP", planType: "Combined", durationDays: 30, sessionCount: 8, published: true, effectiveAtUtc: "2026-10-11T08:00:00Z" }] };
const ok = (data: unknown) => new Response(JSON.stringify(data), { status: 200, headers: { "Content-Type": "application/json" } });
function mock(restricted = false) { global.fetch = vi.fn(async (input, init) => { const url = String(input); if (url.endsWith("/me")) return ok({ role: "AcademyOwner" }); if (url.endsWith("/auth/csrf")) return ok({ token: "csrf" }); if (url.includes("structure/options")) return ok(options); if (url.includes("my-access")) return ok({ allBranches: !restricted }); if (init?.method === "PUT") return new Response(null, { status: 204 }); return ok(plan); }) as typeof fetch; }
afterEach(() => { cleanup(); vi.clearAllMocks(); });
describe("plan publication and captured terms", () => {
  it("calculates season totals without floating point money", () => {
    expect(seasonPrice("600.00", 4)).toBe("2400.00"); expect(seasonPrice("0.29", 3)).toBe("0.87"); expect(seasonPrice(".29", 3)).toBe("0.87"); expect(seasonPrice("100.001", 3)).toBe(""); expect(seasonPrice("1e6", 2)).toBe("");
  });
  it("saves five calendar service months with four paid months and no duplicate day duration", async () => {
    mock(); render(<PlanForm id="p" />); await screen.findByDisplayValue("سباحة شهرية"); fireEvent.change(screen.getByLabelText("طريقة التدريب"), { target: { value: "Season" } });
    fireEvent.change(screen.getByLabelText("السعر الشهري المرجعي"), { target: { value: "600.00" } }); fireEvent.change(screen.getByLabelText("عدد الأشهر المدفوعة"), { target: { value: "4" } }); expect(screen.getByLabelText("أشهر الخدمة")).toHaveValue(5); expect(screen.getByLabelText(/السعر \(/)).toHaveValue("2400.00"); expect(screen.queryByLabelText("عدد الأيام")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "حفظ الباقة" })); await waitFor(() => expect(push).toHaveBeenCalled());
    const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body))).toMatchObject({ price: "2400.00", referenceMonthlyPrice: "600.00", chargedMonths: 4, durationMonths: 5, durationDays: null, sessionCount: null, trainingFormat: 4, serviceKind: 1 });
  });
  it("edits a branch-specific plan with preserved names, price string and expected revision", async () => {
    mock(); render(<PlanForm id="p" />); await screen.findByDisplayValue("سباحة شهرية"); expect(screen.getByLabelText("الاسم الإنجليزي — اختياري")).toHaveValue("Swim"); expect(screen.getByLabelText("مدينة نصر")).toBeChecked(); expect(screen.getByLabelText("التجمع")).not.toBeChecked();
    fireEvent.click(screen.getByLabelText("التجمع")); fireEvent.click(screen.getByRole("button", { name: "حفظ الباقة" })); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/plans"));
    const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body))).toMatchObject({ price: "600.00", expectedRevision: 3, allBranches: false, branchIds: ["a", "b"], durationDays: 30, sessionCount: 8 });
  });
  it("never submits stale session limits when switching a combined plan to duration", async () => {
    mock(); render(<PlanForm id="p" />); await screen.findByDisplayValue("سباحة شهرية"); fireEvent.change(screen.getByLabelText("نظام الباقة"), { target: { value: "Duration" } }); expect(screen.queryByLabelText("عدد الحصص")).not.toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "حفظ الباقة" })); await waitFor(() => expect(push).toHaveBeenCalled());
    const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body)).sessionCount).toBeNull();
  });
  it("shows immutable revision history without edit/delete history controls", async () => {
    mock(); render(<PlanForm id="p" readOnly />); await screen.findByRole("heading", { name: "تاريخ الأسعار وشروط الخدمة" }); expect(screen.getByText(/نسخة 3/)).toBeVisible(); expect(screen.queryByRole("button", { name: /حذف نسخة|تعديل نسخة/ })).not.toBeInTheDocument();
  });
  it("never displays a blank editor for an inaccessible plan", async () => {
    mock();
    const request = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation((input, init) => String(input).endsWith("/plans/foreign")
      ? Promise.resolve(new Response(null, { status: 404 })) : request(input, init));
    render(<PlanForm id="foreign" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("الباقة غير متاحة في نطاقك.");
    expect(screen.queryByRole("button", { name: "حفظ الباقة" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "العودة إلى باقات الاشتراك" })).toHaveAttribute("href", "/dashboard/subscriptions/plans");
  });
  it("blocks a direct edit link to a plan whose publication exceeds the branch scope", async () => {
    mock(true);
    const request = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation((input, init) => String(input).endsWith("/plans/p")
      ? Promise.resolve(ok({ ...plan, canManagePublication: false })) : request(input, init));
    render(<PlanForm id="p" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعديل هذه الباقة يحتاج صلاحية على كل فروع نشرها.");
    expect(screen.queryByRole("button", { name: "حفظ الباقة" })).not.toBeInTheDocument();
  });
  it("retries a failed editor read before presenting loaded terms", async () => {
    mock();
    const request = vi.mocked(fetch).getMockImplementation()!;
    let failed = false;
    vi.mocked(fetch).mockImplementation((input, init) => {
      if (!failed && String(input).endsWith("/plans/p")) {
        failed = true;
        return Promise.resolve(new Response(null, { status: 503 }));
      }
      return request(input, init);
    });
    render(<PlanForm id="p" />);
    await screen.findByRole("alert");
    expect(screen.queryByLabelText("الاسم العربي")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة التحميل" }));
    expect(await screen.findByDisplayValue("سباحة شهرية")).toBeVisible();
  });
  it("restricts branch administrators to explicit branch publication", async () => {
    mock(true); render(<PlanForm />); await screen.findByLabelText("مدينة نصر"); expect(screen.queryByLabelText("كل فروع الأكاديمية")).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "حفظ الباقة" })).toBeDisabled();
  });
  it("keeps the chosen calendar unit while clearing and retyping the month count", async () => {
    mock(); render(<PlanForm id="p" />);
    await screen.findByDisplayValue("سباحة شهرية");
    fireEvent.change(screen.getByLabelText("وحدة المدة"), { target: { value: "Months" } });
    const months = screen.getByLabelText("أشهر الخدمة");
    fireEvent.change(months, { target: { value: "" } });
    expect(months).toHaveValue(null);
    expect(screen.getByLabelText("وحدة المدة")).toHaveValue("Months");
    fireEvent.change(months, { target: { value: "6" } });
    fireEvent.click(screen.getByRole("button", { name: "حفظ الباقة" }));
    await waitFor(() => expect(push).toHaveBeenCalled());
    const call = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "PUT")!;
    expect(JSON.parse(String(call[1]?.body))).toMatchObject({ durationMonths: 6, durationDays: null, sessionCount: 8 });
  });
  it("blocks an excessive season total and preserves the draft regular price when switching back", async () => {
    mock(); render(<PlanForm id="p" />);
    await screen.findByDisplayValue("سباحة شهرية");
    const price = () => screen.getByLabelText(/السعر \(جنيه\)/);
    fireEvent.change(price(), { target: { value: "625.50" } });
    fireEvent.change(screen.getByLabelText("طريقة التدريب"), { target: { value: "Season" } });
    fireEvent.change(screen.getByLabelText("السعر الشهري المرجعي"), { target: { value: "999999999" } });
    fireEvent.change(screen.getByLabelText("عدد الأشهر المدفوعة"), { target: { value: "2" } });
    expect(price()).toHaveValue("1999999998.00");
    expect(price()).toHaveAccessibleDescription(/الحد الأقصى لسعر الباقة/);
    expect(screen.getByRole("button", { name: "حفظ الباقة" })).toBeDisabled();
    expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "PUT")).toBe(false);
    fireEvent.change(screen.getByLabelText("طريقة التدريب"), { target: { value: "Group" } });
    expect(price()).toHaveValue(625.5);
    expect(screen.queryByText(/الحد الأقصى لسعر الباقة/)).not.toBeInTheDocument();
  });
  it("filters the New Player package dropdown by sport AND branch, clearing stale selection", async () => {
    global.fetch = vi.fn(async input => String(input).includes("structure/options") ? ok(options) : String(input).includes("discount-rules") ? ok({ items: [] }) : ok([plan])) as typeof fetch;
    render(<NewPlayerPage />); await screen.findByRole("option", { name: /سباحة شهرية/ }); fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "swim" } }); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "a" } }); fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "p" } }); expect(screen.getByLabelText("الباقة")).toHaveValue("p");
    fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b" } }); expect(screen.queryByRole("option", { name: /سباحة شهرية/ })).not.toBeInTheDocument(); expect(screen.getByLabelText("الباقة")).toHaveValue(""); expect(screen.getByRole("button", { name: "حساب المبلغ المستحق" })).toBeDisabled();
  });
});
