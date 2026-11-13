import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StaffFamilyRenewal } from "./staff-family-renewal";
import { rememberCashFamily } from "../lib/pending-cash-family";
vi.mock("../lib/pending-financial-intent", () => ({ pendingFinancialIntent: vi.fn(async () => "staff-family-stable-key") }));
afterEach(() => { cleanup(); sessionStorage.clear(); vi.restoreAllMocks(); });
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const family = { id: "trusted-family", name: "أسرة اختبار", phone: "01000000014" };
const options = { items: [{ id: "e1", playerId: "p1", player: "طفل اختبار", sport: "كرة القدم", branch: "الفرع الأول", plans: [{ id: "plan", name: "شهر", amount: "1000.00", currency: "EGP" }] }, { id: "e2", playerId: "p2", player: "شقيق اختبار", sport: "السباحة", branch: "الفرع الثاني", plans: [] }] };
const shiftPath = "/api/v1/finance/cash-registers/sessions/shift";
const readyShift = { session: { id: "shift", closedAtUtc: null }, canCollect: true, canCrossBranchPayment: false };
async function pick() { fireEvent.change(await screen.findByLabelText("اسم ولي الأمر أو رقم هاتفه"), { target: { value: family.phone } }); fireEvent.click(screen.getByRole("button", { name: "البحث عن الأسرة" })); fireEvent.click(await screen.findByRole("button", { name: "اختيار أسرة اختبار" })); await screen.findByLabelText("تجديد طفل اختبار · كرة القدم"); }
describe("staff family price preview", () => {
  it("reviews shift-bound cash prices before consent, then locks editing and displays the server receipt", async () => {
    const writes: { path: string; body: unknown }[] = []; global.fetch = vi.fn(async (path, init) => { const p = String(path); if (p === shiftPath) return response(readyShift); if (p.includes("/families?")) return response({ items: [family] }); if (p.endsWith("/options")) return response(options); if (p.endsWith("/csrf")) return response({ token: "csrf" }); writes.push({ path: p, body: JSON.parse(String(init?.body)) }); if (p.endsWith("/family-quote")) return response({ quoteHash: "shift-bound-hash", family: { currency: "EGP", originalAmount: "1000.00", discountAmount: "123.45", totalAmount: "876.55", items: [] } }); return response({ amount: "876.55", currency: "EGP", items: [{ receiptId: "receipt", amount: "876.55", currency: "EGP" }] }); });
    render(<StaffFamilyRenewal cashSessionId="shift" />); await pick(); fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · كرة القدم")); fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم"), { target: { value: "plan" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" })); await screen.findByRole("region", { name: "تحصيل الأسرة النقدي" }); expect(writes).toEqual([{ path: "/api/v1/finance/cash-registers/sessions/shift/family-quote", body: { guardianId: family.id, lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }] } }]);
    expect(screen.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox", { name: "استلمت إجمالي الأسرة نقدًا وراجعت الباقات" })); fireEvent.click(screen.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات" })); await screen.findByRole("heading", { name: "تم تحصيل وتجديد الأسرة" }); expect(screen.getByRole("button", { name: "تعديل الأبناء والباقات" })).toBeDisabled(); expect(screen.getByRole("button", { name: "البحث عن الأسرة" })).toBeDisabled(); expect(writes[1].body).toEqual({ guardianId: family.id, quoteHash: "shift-bound-hash", lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }] });
  });
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

describe("cash shift eligibility", () => {
  it("shows the open request on only its own row and sends normalized phone digits", async () => {
    const find = vi.fn(async path => {
      const url = String(path);
      if (url === shiftPath) return response(readyShift);
      if (url.includes("/families?")) return response({ items: [family] });
      return response({ items: [{ ...options.items[0], hasOpenRequest: true }, { ...options.items[0], id: "e2", player: "شقيق متاح" }] });
    });
    global.fetch = find;
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    const input = await screen.findByLabelText("اسم ولي الأمر أو رقم هاتفه");
    fireEvent.change(input, { target: { value: "٠١٠ ٠٠٠٠-٠٠١٤" } });
    expect(input).toHaveAttribute("dir", "ltr");
    fireEvent.click(screen.getByRole("button", { name: "البحث عن الأسرة" }));
    fireEvent.click(await screen.findByRole("button", { name: "اختيار أسرة اختبار" }));
    expect(await screen.findByText("لهذا التسجيل طلب دفع مفتوح.")).toBeVisible();
    expect(screen.getByLabelText("تجديد طفل اختبار · كرة القدم")).toBeDisabled();
    expect(screen.getByLabelText("تجديد شقيق متاح · كرة القدم")).toBeEnabled();
    expect(find.mock.calls.map(([path]) => path)).toContain("/api/v1/manage/family-renewals/quotes/families?search=01000000014");
  });
  it("retains selected plans and the rejection on the page when the price changes", async () => {
    global.fetch = vi.fn(async path => {
      const url = String(path);
      if (url === shiftPath) return response(readyShift);
      if (url.includes("/families?")) return response({ items: [family] });
      if (url.endsWith("/options")) return response(options);
      if (url.endsWith("/csrf")) return response({ token: "csrf" });
      if (url.endsWith("/family-quote")) return response({ quoteHash: "q".repeat(64), family: { currency: "EGP", originalAmount: "1000.00", discountAmount: "0.00", totalAmount: "1000.00", items: [] } });
      if (url.endsWith("/family-collection-result")) return response({ status: "NotRecorded", canDiscard: false });
      return response({ title: "تغير السعر. حدّث المراجعة." }, 409);
    });
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    await pick();
    fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · كرة القدم"));
    fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم"), { target: { value: "plan" } });
    fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "استلمت إجمالي الأسرة نقدًا وراجعت الباقات" }));
    fireEvent.click(screen.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("تغير السعر. حدّث المراجعة.");
    expect(await screen.findByLabelText("الباقة لـطفل اختبار · كرة القدم")).toHaveValue("plan");
    expect(screen.queryByRole("region", { name: "تحصيل الأسرة النقدي" })).not.toBeInTheDocument();
  });
  it.each([
    { status: 409, closedAtUtc: "2026-11-09T08:00:00Z", message: "هذه الوردية مقفلة. افتح وردية للتحصيل." },
    { status: 403, closedAtUtc: null, message: "هذه الوردية غير متاحة للتحصيل بحسابك." },
  ])("rechecks the shift after a rejected collection ($status)", async ({ status, closedAtUtc, message }) => {
    let submitted = false;
    global.fetch = vi.fn(async path => {
      const url = String(path);
      if (url === shiftPath) return response(submitted ? { ...readyShift, canCollect: false, session: { id: "shift", closedAtUtc } } : readyShift);
      if (url.includes("/families?")) return response({ items: [family] });
      if (url.endsWith("/options")) return response(options);
      if (url.endsWith("/csrf")) return response({ token: "csrf" });
      if (url.endsWith("/family-quote")) return response({ quoteHash: "a".repeat(64), family: { currency: "EGP", originalAmount: "1000.00", discountAmount: "0.00", totalAmount: "1000.00", items: [] } });
      if (url.endsWith("/family-collection-result")) return response({ status: "NotRecorded", canDiscard: Boolean(closedAtUtc) });
      submitted = true;
      return response({ title: message }, status);
    });
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    await pick();
    fireEvent.click(screen.getByLabelText("تجديد طفل اختبار · كرة القدم"));
    fireEvent.change(screen.getByLabelText("الباقة لـطفل اختبار · كرة القدم"), { target: { value: "plan" } });
    fireEvent.click(screen.getByRole("button", { name: "مراجعة إجمالي 1 تسجيل" }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "استلمت إجمالي الأسرة نقدًا وراجعت الباقات" }));
    fireEvent.click(screen.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByRole("button", { name: "البحث عن الأسرة" })).not.toBeInTheDocument();
  });
  it("hides family search on a closed shift and points back to the same shift", async () => {
    global.fetch = vi.fn(async () => response({ session: { id: "shift", closedAtUtc: "2026-11-09T08:00:00Z" }, canCollect: false }));
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("هذه الوردية مقفلة");
    expect(screen.queryByLabelText("اسم ولي الأمر أو رقم هاتفه")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "العودة إلى الوردية" })).toHaveAttribute("href", "/dashboard/finance/cash-registers/sessions/shift");
    expect(screen.queryByRole("link", { name: "تجديد لاعب منفرد" })).not.toBeInTheDocument();
  });
  it.each([403, 404])("treats inaccessible shift %s without exposing search", async status => {
    global.fetch = vi.fn(async () => response({}, status));
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("غير متاحة للتحصيل");
    expect(screen.queryByRole("button", { name: "البحث عن الأسرة" })).not.toBeInTheDocument();
  });
  it("retries temporary and mismatched shift reads before displaying any search", async () => {
    let reads = 0;
    global.fetch = vi.fn(async () => {
      reads++;
      return reads === 1 ? response({}, 503) : reads === 2 ? response({ ...readyShift, session: { id: "other" } }) : response(readyShift);
    });
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    fireEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));
    fireEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));
    expect(await screen.findByLabelText("اسم ولي الأمر أو رقم هاتفه")).toBeVisible();
    expect(reads).toBe(3);
  });
  it("recovers an existing result even when the shift can no longer take payments", async () => {
    rememberCashFamily("shift", { key: "already-confirmed", body: { guardianId: family.id, quoteHash: "a".repeat(64), lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }] } });
    const writes: string[] = [];
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return response({ token: "csrf" });
      if (String(path) === shiftPath) return response({ ...readyShift, canCollect: false, session: { id: "shift", closedAtUtc: "2026-11-09T08:00:00Z" } });
      if (init?.method === "POST") writes.push(String(path));
      return response({ status: "Confirmed", amount: "876.55", currency: "EGP", items: [{ receiptId: "receipt", amount: "876.55", currency: "EGP" }] });
    });
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    await screen.findByRole("heading", { name: "تم تحصيل وتجديد الأسرة" });
    expect(writes).toEqual([shiftPath + "/family-collection-result"]);
    expect(screen.queryByRole("button", { name: "البحث عن الأسرة" })).not.toBeInTheDocument();
  });
  it("lets an authorized cashier change scope during search without showing the stale results", async () => {
    let resolvePrevious: ((value: Response) => void) | undefined;
    let previousSignal: AbortSignal | null | undefined;
    global.fetch = vi.fn(async (path, init) => {
      const url = String(path);
      if (url === shiftPath) return response({ ...readyShift, canCrossBranchPayment: true });
      if (url.endsWith("/csrf")) return response({ token: "csrf" });
      if (url.includes("/cross-branch/families/search")) return response({ items: [{ name: "أسرة في فرع آخر", reference: "CPF1.sample" }] });
      previousSignal = init?.signal;
      return new Promise<Response>(resolve => { resolvePrevious = resolve; });
    });
    render(<StaffFamilyRenewal cashSessionId="shift" />);
    fireEvent.change(await screen.findByLabelText("اسم ولي الأمر أو رقم هاتفه"), { target: { value: "أسرة" } });
    const search = screen.getByRole("button", { name: "البحث عن الأسرة" });
    fireEvent.click(search);
    expect(search).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: "تحصيل أسرة لديها أبناء في فروع أخرى" }));
    expect(previousSignal?.aborted).toBe(true);
    fireEvent.click(search);
    expect(await screen.findByRole("button", { name: "اختيار أسرة في فرع آخر" })).toBeVisible();
    resolvePrevious?.(response({ items: [family] }));
    await waitFor(() => expect(screen.queryByRole("button", { name: "اختيار أسرة اختبار" })).not.toBeInTheDocument());
  });
});
