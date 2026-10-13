import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StaffFamilyRenewal } from "./staff-family-renewal";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const family = { id: "trusted-family", name: "أسرة اختبار", phone: "01000000014" };
const options = { items: [{ id: "e1", playerId: "p1", player: "طفل اختبار", sport: "كرة القدم", branch: "الفرع الأول", plans: [{ id: "plan", name: "شهر", amount: "1000.00", currency: "EGP" }] }, { id: "e2", playerId: "p2", player: "شقيق اختبار", sport: "السباحة", branch: "الفرع الثاني", plans: [] }] };
async function pick() { fireEvent.change(screen.getByLabelText("اسم ولي الأمر أو رقم هاتفه"), { target: { value: family.phone } }); fireEvent.click(screen.getByRole("button", { name: "البحث عن الأسرة" })); fireEvent.click(await screen.findByRole("button", { name: "اختيار أسرة اختبار" })); await screen.findByLabelText("تجديد طفل اختبار · كرة القدم"); }
describe("staff family price preview", () => {
  it("selects only authorized family lines, keeps server amounts and never creates financial records", async () => {
    const writes: { path: string; body: unknown }[] = []; global.fetch = vi.fn(async (path, init) => { const p = String(path); if (p.includes("/families?")) return response({ items: [family] }); if (p.endsWith("/options")) return response(options); if (p.endsWith("/csrf")) return response({ token: "csrf" }); writes.push({ path: p, body: JSON.parse(String(init?.body)) }); return response({ currency: "EGP", originalAmount: "1000.00", discountAmount: "123.45", totalAmount: "876.55", items: [{ sportEnrollmentId: "e1", player: "طفل اختبار", sport: "كرة القدم", branch: "الفرع الأول", plan: "شهر", finalAmount: "876.55", currency: "EGP" }] }); });
    render(<StaffFamilyRenewal />); expect(screen.getByRole("button", { name: "البحث عن الأسرة" })).toBeDisabled(); await pick(); expect(screen.getByLabelText("تجديد شقيق اختبار · السباحة")).toBeDisabled();
    fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · كرة القدم")); fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم"), { target: { value: "plan" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" }));
    expect(await screen.findByRole("region", { name: "مراجعة سعر الأسرة" })).toHaveTextContent("876.55"); expect(writes).toEqual([{ path: "/api/v1/manage/family-renewals/quotes/trusted-family/quote", body: { lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }] } }]);
    fireEvent.click(screen.getByRole("button", { name: "تعديل الأبناء والباقات" })); expect(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم")).toHaveValue("plan"); expect(screen.getByText(/لم يُسجل تحصيل/)).toBeVisible();
  });
  it("does not show names after forbidden search or leak technical server errors", async () => {
    global.fetch = vi.fn(async () => response({ detail: "SQL technical child names" }, 403)); render(<StaffFamilyRenewal />); fireEvent.change(screen.getByLabelText("اسم ولي الأمر أو رقم هاتفه"), { target: { value: family.phone } }); fireEvent.click(screen.getByRole("button", { name: "البحث عن الأسرة" })); expect(await screen.findByRole("alert")).not.toHaveTextContent("SQL"); expect(screen.queryByRole("region", { name: "نتائج الأسر" })).not.toBeInTheDocument();
  });
  it("keeps choices after a failed quote, and clears the old family and price on a fresh search", async () => {
    global.fetch = vi.fn(async path => { const p = String(path); if (p.includes("/families?")) return response({ items: [family] }); if (p.endsWith("/options")) return response(options); if (p.endsWith("/csrf")) return response({ token: "csrf" }); throw new Error("technical network failure"); }); render(<StaffFamilyRenewal />); await pick(); fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · كرة القدم")); fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم"), { target: { value: "plan" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" })); expect(await screen.findByRole("alert")).toHaveTextContent("لم يُنشأ دفع"); expect(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم")).toHaveValue("plan");
    fireEvent.change(screen.getByLabelText("اسم ولي الأمر أو رقم هاتفه"), { target: { value: "أسرة أخرى" } }); fireEvent.click(screen.getByRole("button", { name: "البحث عن الأسرة" })); await waitFor(() => expect(screen.queryByLabelText("تجديد طفل اختبار · كرة القدم")).not.toBeInTheDocument());
  });
});
