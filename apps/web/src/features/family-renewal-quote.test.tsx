import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { FamilyRenewalQuote } from "./family-renewal-quote";
const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
vi.mock("../lib/pending-financial-intent", () => ({ pendingFinancialIntent: vi.fn(async () => "same-family-request-key"), clearFinancialIntent: vi.fn() }));

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const options = { items: [
  { id: "football", playerId: "child1", player: "طفل اختبار", sport: "القدم", branch: "الفرع الأول", plans: [{ id: "p1", name: "شهر القدم", amount: "1000.00", currency: "EGP" }] },
  { id: "swimming", playerId: "child1", player: "طفل اختبار", sport: "السباحة", branch: "الفرع الثاني", plans: [{ id: "p2", name: "شهر السباحة", amount: "500.00", currency: "EGP" }] },
  { id: "sibling", playerId: "child2", player: "شقيق اختبار", sport: "القدم", branch: "الفرع الأول", plans: [] },
] };
const quote = { quoteHash: "hash", pricingDate: "2026-09-28", currency: "EGP", originalAmount: "1500.00", discountAmount: "0.00", totalAmount: "1500.00", items: options.items.slice(0, 2).map(e => ({ sportEnrollmentId: e.id, subscriptionPlanId: e.plans[0].id, player: e.player, sport: e.sport, branch: e.branch, plan: e.plans[0].name, originalAmount: e.plans[0].amount, discountAmount: "0.00", finalAmount: e.plans[0].amount, currency: "EGP" })) };
async function select() {
  await screen.findByLabelText("تجديد طفل اختبار · القدم");
  fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · القدم"));
  fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · القدم"), { target: { value: "p1" } });
  fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · السباحة"));
  fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · السباحة"), { target: { value: "p2" } });
}
describe("family renewal preview", () => {
  it("keeps sports separate and uses only server amounts without creating a payment", async () => {
    const announce = vi.spyOn(window, "dispatchEvent");
    const writes: { path: string; body: unknown }[] = [];
    global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("/options")) return response(options); if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); writes.push({ path: String(path), body: JSON.parse(String(init?.body)) }); return response(quote); });
    render(<FamilyRenewalQuote />); await select(); expect(screen.getByLabelText("تجديد شقيق اختبار · القدم")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }));
    await screen.findByRole("region", { name: "ملخص تجديد الأسرة" });
    expect(announce.mock.calls.some(([event]) => event.type === "academy-save-success")).toBe(false);
    expect(writes).toEqual([{ path: "/api/v1/guardian/subscriptions/family/quote", body: { lines: [{ sportEnrollmentId: "football", subscriptionPlanId: "p1" }, { sportEnrollmentId: "swimming", subscriptionPlanId: "p2" }] } }]);
    expect(screen.getByText(/لم يُنشأ طلب دفع/)).toBeVisible(); expect(screen.getByRole("button", { name: "متابعة الدفع للأسرة" })).toBeEnabled(); expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "تعديل التسجيلات والباقات" })); expect(screen.getByLabelText("الباقة لـطفل اختبار · القدم")).toHaveValue("p1"); expect(screen.queryByRole("region", { name: "ملخص تجديد الأسرة" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · السباحة")); expect(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" })).toBeEnabled();
  });
  it("does not quote incomplete choices or retain stale quotes after an error", async () => {
    global.fetch = vi.fn(async path => String(path).endsWith("/options") ? response(options) : String(path).endsWith("/csrf") ? response({ token: "csrf" }) : response({ title: "إحدى الباقات غير متاحة لهذا التسجيل والفرع." }, 422));
    render(<FamilyRenewalQuote />); await screen.findByLabelText("تجديد طفل اختبار · القدم"); expect(screen.getByRole("button", { name: "مراجعة إجمالي 0 تسجيل" })).toBeDisabled(); fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · القدم")); expect(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · القدم"), { target: { value: "p1" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" })); await screen.findByRole("alert"); expect(screen.queryByRole("region", { name: "ملخص تجديد الأسرة" })).not.toBeInTheDocument(); expect(screen.getByLabelText("تجديد طفل اختبار · القدم")).toBeEnabled();
  });
  it("fails closed when guardian access is unavailable", async () => { global.fetch = vi.fn(async () => response({}, 403)); render(<FamilyRenewalQuote />); await screen.findByRole("alert"); expect(screen.queryByRole("checkbox")).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "مراجعة إجمالي 0 تسجيل" })).toBeDisabled(); });
  it("shows an explicit empty family state", async () => { global.fetch = vi.fn(async () => response({ items: [] })); render(<FamilyRenewalQuote />); await screen.findByText("لا توجد تسجيلات متاحة لأبنائك."); expect(screen.queryByRole("checkbox")).not.toBeInTheDocument(); });
  it("does not expose technical network errors or fabricate a saved result", async () => {
    global.fetch = vi.fn(async path => String(path).endsWith("/options") ? response(options) : Promise.reject(new TypeError("Failed to fetch INTERNAL")));
    render(<FamilyRenewalQuote />); await select(); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }));
    await screen.findByText("تعذر الاتصال لمراجعة التجديد. لم يُنشأ طلب دفع؛ حاول مجددًا."); expect(screen.queryByText(/INTERNAL/)).not.toBeInTheDocument(); expect(screen.queryByRole("region", { name: "ملخص تجديد الأسرة" })).not.toBeInTheDocument();
  });
  it("retries a lost creation response with identical lines, quote and key", async () => {
    const writes: unknown[] = []; let attempts = 0; push.mockClear();
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/options")) return response(options);
      if (String(path).endsWith("/csrf")) return response({ token: "csrf" });
      if (String(path).endsWith("/quote")) return response(quote);
      writes.push(JSON.parse(String(init?.body))); if (++attempts === 1) throw new TypeError("INTERNAL CONNECTION"); return response({ id: "family-request" });
    });
    render(<FamilyRenewalQuote />); await select(); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" })); await screen.findByRole("region", { name: "ملخص تجديد الأسرة" }); fireEvent.click(screen.getByRole("button", { name: "متابعة الدفع للأسرة" })); await screen.findByRole("alert");
    expect(screen.getByRole("button", { name: "تعديل التسجيلات والباقات" })).toBeDisabled(); expect(screen.queryByText(/INTERNAL/)).not.toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد نفس الطلب" })); await vi.waitFor(() => expect(push).toHaveBeenCalledWith("/guardian/family-renewals/family-request")); expect(writes).toHaveLength(2); expect(writes[0]).toEqual(writes[1]);
  });
  it("allows price review after a definite stale-quote rejection", async () => {
    global.fetch = vi.fn(async path => String(path).endsWith("/options") ? response(options) : String(path).endsWith("/csrf") ? response({ token: "csrf" }) : String(path).endsWith("/quote") ? response(quote) : response({ title: "تغير السعر. راجع الإجمالي." }, 409));
    render(<FamilyRenewalQuote />); await select(); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" })); await screen.findByRole("region", { name: "ملخص تجديد الأسرة" }); fireEvent.click(screen.getByRole("button", { name: "متابعة الدفع للأسرة" })); await screen.findByText("تغير السعر. راجع الإجمالي."); expect(screen.getByRole("button", { name: "تعديل التسجيلات والباقات" })).toBeEnabled();
  });
});
