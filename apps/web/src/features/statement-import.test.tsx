import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StatementImport } from "./statement-import";
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams("from=2026-11-03&to=2026-11-04&page=2") }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
const options = { branches: [{ id: "branch", name: "فرع العرض" }], providers: ["InternalTest"] };
const preview = { quoteHash: "quote", count: 2, reused: 0, matched: 1, differences: 1, items: [
  { number: 2, lineReference: "1", paymentReference: "DEMO-1", result: "Matched", gross: "900", fee: "5", net: "895", currency: "EGP", expected: "900", expectedCurrency: "EGP" },
  { number: 3, lineReference: "2", paymentReference: "DEMO-MISSING", result: "Unmatched", gross: "100", fee: "0", net: "100", currency: "EGP" },
] };
const csv = "line_reference,payment_reference,settlement_date,gross,fee,net,currency,note\n1,DEMO-1,2026-09-28,900,5,895,EGP,";
async function fill() {
  await screen.findByLabelText("الفرع");
  for (const [label, value] of [["الفرع", "branch"], ["اسم الحساب المختصر", "DEMO"], ["مرجع الكشف", "STATEMENT"], ["المزود المسجل", "InternalTest"]]) fireEvent.change(screen.getByLabelText(label), { target: { value } });
  const file = { name: "demo.csv", size: csv.length, arrayBuffer: async () => new TextEncoder().encode(csv).buffer };
  fireEvent.change(screen.getByLabelText("ملف الكشف CSV"), { target: { files: [file] } });
  await screen.findByText("الملف المختار: demo.csv");
}
describe("statement CSV import", () => {
  it("shows differences, locks preview and retries the identical batch after a lost response", async () => {
    const writes: string[] = []; let attempts = 0;
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/options")) return response(options);
      if (String(path).endsWith("/csrf")) return response({ token: "csrf" });
      if (String(path).endsWith("/preview")) return response(preview);
      writes.push(String(init?.body)); if (++attempts === 1) throw new Error("النتيجة غير مؤكدة، أعد المحاولة.");
      return response({ created: 2, reused: 0, count: 2, items: preview.items.map((r, i) => ({ ...r, id: "saved-" + i })) });
    });
    render(<StatementImport />); await fill(); expect(screen.getByRole("link", { name: "تنزيل قالب CSV" })).toHaveAttribute("href", "/api/v1/finance/settlement-comparisons/import/template");
    fireEvent.click(screen.getByRole("button", { name: "معاينة الكشف" })); await screen.findByText("لا يوجد تحصيل مطابق للمرجع"); expect(screen.getByLabelText("ملف الكشف CSV")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "حفظ نتائج الكشف كاملة" })); await screen.findByText("النتيجة غير مؤكدة، أعد المحاولة."); expect(screen.queryByRole("button", { name: "تعديل الكشف" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد الحفظ بنفس الكشف" }));
    await screen.findByText("تم التحقق من حفظ أسطر الكشف (2).");
    expect(writes).toHaveLength(2); expect(writes[0]).toBe(writes[1]);
    const links = screen.getAllByRole("link", { name: "عرض المقارنة" });
    expect(links).toHaveLength(2);
    expect(links[0]).toHaveAttribute("href", "/dashboard/finance/settlement-comparisons/saved-0?from=2026-11-03&to=2026-11-04&page=2");
  });
  it("invalid or oversized files clear old content and cannot be submitted", async () => {
    global.fetch = vi.fn(async () => response(options)); render(<StatementImport />); await fill();
    for (const file of [{ name: "demo.xlsx", size: 1 }, { name: "demo.csv", size: 131073 }]) {
      fireEvent.change(screen.getByLabelText("ملف الكشف CSV"), { target: { files: [file] } });
      await screen.findByText("اختر ملف CSV بترميز UTF-8 لا يتجاوز 128 كيلوبايت."); expect(screen.getByRole("button", { name: "معاينة الكشف" })).toBeDisabled(); expect(screen.queryByText("الملف المختار: demo.csv")).not.toBeInTheDocument();
    }
  });
  it("replaces a preview only after explicit editing and surfaces row errors without saving", async () => {
    let attempts = 0; const paths: string[] = [];
    global.fetch = vi.fn(async path => { paths.push(String(path)); if (String(path).endsWith("/options")) return response(options); if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); return ++attempts === 1 ? response(preview) : response({ title: "السطر 3: المبلغ غير صحيح، لا تُحفظ أي أسطر." }, 422); });
    render(<StatementImport />); await fill(); fireEvent.click(screen.getByRole("button", { name: "معاينة الكشف" })); await screen.findByRole("button", { name: "تعديل الكشف" }); fireEvent.click(screen.getByRole("button", { name: "تعديل الكشف" })); expect(screen.getByLabelText("مرجع الكشف")).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: "معاينة الكشف" })); await screen.findByText("السطر 3: المبلغ غير صحيح، لا تُحفظ أي أسطر."); expect(screen.queryByRole("button", { name: "حفظ نتائج الكشف كاملة" })).not.toBeInTheDocument(); expect(paths).not.toContain("/api/v1/finance/settlement-comparisons/import");
  });
  it("fails closed when owner access is revoked", async () => { global.fetch = vi.fn(async () => response({}, 403)); render(<StatementImport />); await screen.findByText("مطابقة الكشف متاحة للمالك فقط."); expect(screen.queryByLabelText("ملف الكشف CSV")).not.toBeInTheDocument(); });
  it("ignores a stale file read after a newer file selection", async () => {
    global.fetch = vi.fn(async () => response(options)); render(<StatementImport />); await screen.findByLabelText("ملف الكشف CSV"); let finish: (bytes: ArrayBuffer) => void = () => {};
    fireEvent.change(screen.getByLabelText("ملف الكشف CSV"), { target: { files: [{ name: "old.csv", size: 1, arrayBuffer: () => new Promise<ArrayBuffer>(resolve => { finish = resolve; }) }] } });
    fireEvent.change(screen.getByLabelText("ملف الكشف CSV"), { target: { files: [{ name: "wrong.xlsx", size: 1 }] } }); finish(new TextEncoder().encode(csv).buffer);
    await waitFor(() => expect(screen.getByRole("button", { name: "معاينة الكشف" })).toBeDisabled()); expect(screen.queryByText("الملف المختار: old.csv")).not.toBeInTheDocument();
  });
  it.each([409, 403])("recovers an explicit save response of %s", async status => {
    global.fetch = vi.fn(async path => {
      const url = String(path);
      if (url.endsWith("/options")) return response(options);
      if (url.endsWith("/csrf")) return response({ token: "csrf" });
      if (url.endsWith("/preview")) return response(preview);
      return response({ title: "رفض تجريبي للحفظ." }, status);
    });
    render(<StatementImport />);
    await fill();
    fireEvent.click(screen.getByRole("button", { name: "معاينة الكشف" }));
    fireEvent.click(await screen.findByRole("button", { name: "حفظ نتائج الكشف كاملة" }));
    await screen.findByText("رفض تجريبي للحفظ.");
    expect(screen.queryByRole("button", { name: "إعادة تأكيد الحفظ بنفس الكشف" })).not.toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "معاينة كشف التسوية" })).not.toBeInTheDocument();
    if (status === 409) {
      expect(screen.getByLabelText("ملف الكشف CSV")).toBeEnabled();
      expect(screen.getByText("الملف المختار: demo.csv")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "معاينة الكشف" })).toBeEnabled();
    } else {
      expect(screen.queryByLabelText("ملف الكشف CSV")).not.toBeInTheDocument();
    }
  });
});
