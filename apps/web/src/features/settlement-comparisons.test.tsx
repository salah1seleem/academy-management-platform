import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SettlementComparisons, SettlementComparisonDetail, SettlementComparisonForm } from "./settlement-comparisons";
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(window.location.search) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); });
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
const row = { id: "statement", accountTag: "DEMO", statementReference: "كشف اصطناعي", lineReference: "1", settlementDate: "2026-09-28", result: "Matched", gross: "900.00", fee: "5.00", net: "895.00", currency: "EGP", expected: "900.00", expectedCurrency: "EGP", receiptId: "receipt", paymentReference: "DEMO-REF", actor: "مالك تجريبي" };
const formOptions = { branches: [{ id: "branch", name: "فرع تجريبي" }], providers: ["InternalTest"] };
async function fillForm() {
  await screen.findByLabelText("الفرع");
  for (const [label, value] of [["الفرع", "branch"], ["اسم الحساب المختصر", "DEMO"], ["مرجع الكشف", "statement"], ["مرجع السطر", "1"], ["المزود المسجل", "InternalTest"], ["مرجع الدفع في الكشف", "DEMO-REF"], ["إجمالي الكشف", "900"], ["الرسوم الواردة", "5"], ["صافي الكشف", "895"]]) {
    fireEvent.change(screen.getByLabelText(label, { exact: true }), { target: { value } });
  }
}
describe("Owner manual statement comparison", () => {
  it("clears default dates and paginates without inventing a payment", async () => {
    const paths: string[] = []; global.fetch = vi.fn(async path => { paths.push(String(path)); return response({ total: 26, page: String(path).includes("page=2") ? 2 : 1, pageSize: 25, items: [row] }); });
    render(<SettlementComparisons />); await screen.findByText("كشف اصطناعي"); fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" })); await waitFor(() => expect(paths.at(-1)).toBe("/api/v1/finance/settlement-comparisons?page=1")); expect(screen.getByLabelText("من تاريخ")).toHaveValue(""); await screen.findByText("كشف اصطناعي"); fireEvent.click(screen.getByRole("button", { name: /^التالي$/ })); await waitFor(() => expect(paths.at(-1)).toContain("page=2"));
  });
  it("shows original receipt gross separately from fees and net", async () => { global.fetch = vi.fn(async () => response(row)); render(<SettlementComparisonDetail id="statement" />); await screen.findByText("مطابق يدويًا"); expect(screen.getByText("895 جنيه")).toBeInTheDocument(); expect(screen.getByText("5 جنيه")).toBeInTheDocument(); expect(screen.getByRole("link", { name: "الإيصال الأصلي" })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/receipt"); expect(screen.queryByRole("button", { name: /تفعيل|دفع/ })).not.toBeInTheDocument(); });
  it("fails closed if owner access is revoked", async () => { global.fetch = vi.fn(async () => response({}, 403)); render(<SettlementComparisonForm />); await screen.findByText("مطابقة الكشف متاحة للمالك فقط."); expect(screen.queryByLabelText("مرجع الكشف")).not.toBeInTheDocument(); });
  it("locks reviewed inputs and retries the identical source line after a lost response", async () => {
    const writes: string[] = []; let attempt = 0;
    global.fetch = vi.fn(async (path, init) => { const p = String(path); if (p.endsWith("/options")) return response(formOptions); if (p.endsWith("/csrf")) return response({ token: "csrf" }); if (p.endsWith("/preview")) return response({ ...row, quoteHash: "quote" }); writes.push(String(init?.body)); if (++attempt === 1) throw new Error("تعذر الوصول؛ أعد المحاولة."); return response(row); });
    render(<SettlementComparisonForm />); await fillForm();
    fireEvent.click(screen.getByRole("button", { name: "معاينة المقارنة" })); await screen.findByRole("button", { name: "حفظ نتيجة المقارنة" }); expect(screen.getByLabelText("مرجع الكشف")).toBeDisabled(); fireEvent.click(screen.getByRole("button", { name: "حفظ نتيجة المقارنة" })); await screen.findByText("تعذر الوصول؛ أعد المحاولة."); expect(screen.queryByRole("button", { name: "تعديل البيانات" })).not.toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد الحفظ بنفس البيانات" })); await screen.findByText("تم حفظ نتيجة المقارنة دون تغيير التحصيل أو الاشتراك."); expect(writes).toHaveLength(2); expect(writes[0]).toBe(writes[1]);
  });
  it("keeps the date scope and page in the detail link and back link", async () => {
    const scope = "from=2026-09-01&to=2026-09-28&page=2";
    window.history.replaceState(null, "", "?" + scope);
    global.fetch = vi.fn(async path => response(String(path).endsWith("/statement") ? row : { total: 26, page: 2, pageSize: 25, items: [row] }));
    const page = render(<SettlementComparisons />);
    expect(await screen.findByRole("link", { name: "عرض المقارنة" })).toHaveAttribute("href", "/dashboard/finance/settlement-comparisons/statement?" + scope);
    page.rerender(<SettlementComparisonDetail id="statement" />);
    await screen.findByText("مطابق يدويًا");
    expect(screen.getByRole("link", { name: "العودة إلى المطابقة" })).toHaveAttribute("href", "/dashboard/finance/settlement-comparisons?" + scope);
  });
  it("does not request reversed dates and retries a real list failure without a false empty state", async () => {
    const read = vi.fn(async () => response({ total: 0, page: 1, pageSize: 25, items: [] }));
    read.mockResolvedValueOnce(response({}, 503));
    global.fetch = read;
    render(<SettlementComparisons />);
    await screen.findByText("تعذر تحميل المطابقة.");
    expect(screen.queryByText("لا توجد مقارنات ضمن الفترة")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
    await screen.findByText("لا توجد مقارنات ضمن الفترة");
    expect(screen.queryByRole("navigation", { name: "صفحات المطابقة" })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("من تاريخ"), { target: { value: "2099-01-01" } });
    expect(screen.getByRole("alert")).toHaveTextContent("اختر تاريخ بداية");
    expect(read).toHaveBeenCalledTimes(2);
  });
  it.each([409, 403])("handles a confirmed %s differently from an unknown save result", async status => {
    global.fetch = vi.fn(async path => {
      const p = String(path);
      if (p.endsWith("/options")) return response(formOptions);
      if (p.endsWith("/csrf")) return response({ token: "csrf" });
      if (p.endsWith("/preview")) return response({ ...row, quoteHash: "quote" });
      return response({ title: "رفض تجريبي للحفظ." }, status);
    });
    render(<SettlementComparisonForm />);
    await fillForm();
    fireEvent.click(screen.getByRole("button", { name: "معاينة المقارنة" }));
    fireEvent.click(await screen.findByRole("button", { name: "حفظ نتيجة المقارنة" }));
    await screen.findByText("رفض تجريبي للحفظ.");
    expect(screen.queryByRole("button", { name: "إعادة تأكيد الحفظ بنفس البيانات" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "مطابق يدويًا" })).not.toBeInTheDocument();
    if (status === 409) {
      const reference = screen.getByLabelText("مرجع الكشف");
      expect(reference).toBeEnabled();
      expect(reference).toHaveValue("statement");
    } else expect(screen.queryByLabelText("مرجع الكشف")).not.toBeInTheDocument();
  });
});
