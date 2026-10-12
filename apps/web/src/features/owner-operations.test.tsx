import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DailyStaffReport } from "./daily-staff-report";
import { MediaUpload } from "./media-upload";
import { ProductSaleForm } from "./product-sale-form";
import { NewsManagement } from "./news-management";
import { PaymentRequestQueue } from "./manual-payments";
import { DateFilterDefaults } from "../components/date-filter-defaults";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams() }));

const response = (value: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(value), { status }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
describe("owner-review operations", () => {
  it("defaults to month-to-date, omits empty branches and never leaves a loader after a queue failure", async () => {
    global.fetch = vi.fn(input => String(input).includes("payment-requests") ? response({}, 503) : response({})) as typeof fetch;
    render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PaymentRequestQueue /></DateFilterDefaults>);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل طلبات الدفع");
    expect(fetch).toHaveBeenCalledWith("/api/v1/manage/payment-requests?status=Pending&from=2026-09-01&to=2026-09-28");
    expect(screen.queryByText("جارٍ التحميل…")).not.toBeInTheDocument();
  });
  it("uses the daily coach record, explicit absence and a coach dropdown with date bounds", async () => {
    global.fetch = vi.fn(input => String(input).includes("/manage/") ? response({ items: [{ id: "c1", name: "كريم" }] }) : response({ from: "2026-09-01", to: "2026-09-28", totalDeductions: 100, currency: "EGP", totalCount: 1, items: [{ id: "r1", name: "كريم", workDate: "2026-09-28", status: "Absent", lateMinutes: 0, approvedDeduction: 100 }] })) as typeof fetch;
    render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><DailyStaffReport onPlayers={() => {}} /></DateFilterDefaults>);
    expect(await screen.findByText("غائب")).toBeVisible();
    expect(screen.getByLabelText("المدرب")).toHaveDisplayValue("كل المدربين");
    expect(screen.getByLabelText("من تاريخ")).toHaveValue("2026-09-01");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("المدرب"), { target: { value: "c1" } });
    fireEvent.change(screen.getByLabelText("حالة الحضور"), { target: { value: "Absent" } });
    fireEvent.click(screen.getByRole("button", { name: "تطبيق الفلاتر" }));
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining("coachId=c1&status=Absent")));
    expect(screen.getByRole("link", { name: "تصدير CSV" })).toHaveAttribute("href", expect.stringContaining("/staff-daily/export?"));
  });
  it("offers a real player/group upload without technical metadata or a publication toggle", async () => {
    global.fetch = vi.fn(input => String(input).includes("options") ? response({ groups: [{ id: "g1", arabicName: "براعم" }] }) : response([{ id: "p1", arabicName: "لاعب تجريبي" }])) as typeof fetch;
    render(<MediaUpload />);
    expect(await screen.findByRole("option", { name: "لاعب تجريبي" })).toBeInTheDocument();
    expect(screen.getByLabelText("الصورة أو الفيديو")).toHaveAttribute("type", "file");
    expect(screen.queryByLabelText(/وقت الالتقاط|الترتيب|مرجع الصورة|النوع|ينشر/)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("رفع إلى"), { target: { value: "group" } });
    expect(screen.getByLabelText("المجموعة")).toBeVisible();
    expect(screen.getByRole("option", { name: "براعم" })).toBeInTheDocument();
  });
  it("retries an uncertain stock sale with the same key, without trusting a client amount", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const writes: RequestInit[] = [];
    global.fetch = vi.fn((input, init) => {
      const url = String(input);
      if (url.includes("/auth/csrf")) return response({ token: "csrf" });
      if (url.includes("/manage/product-sales/quote")) return response({ unitPrice: "90.00", amount: "180.00", currency: "EGP", stockQuantity: 3, cashEnabled: true, sessions: [{ id: "shift", name: "ورديتي" }] });
      if (url.includes("/manage/product-sales") && init?.method === "POST") { writes.push(init); return writes.length === 1 ? response({ message: "انقطع الاتصال" }, 503) : response({ id: "sale", amount: "180.00", currency: "EGP" }); }
      if (url.includes("catalog")) return response({ arabicName: "حذاء", displayPrice: 100, discountPercentage: 10, stockQuantity: 3, isActive: true, currency: "EGP" });
      if (url.includes("options")) return response({ branches: [{ id: "b1", arabicName: "فرع تجريبي" }] });
      return response([]);
    }) as typeof fetch;
    render(<ProductSaleForm id="product" />);
    await screen.findByText("حذاء");
    fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b1" } });
    fireEvent.change(screen.getByLabelText("الكمية"), { target: { value: "2" } });
    fireEvent.change(screen.getByLabelText("طريقة الدفع"), { target: { value: "Cash" } });
    await screen.findByLabelText("وردية التحصيل"); fireEvent.change(screen.getByLabelText("وردية التحصيل"), { target: { value: "shift" } });
    fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" }));
    await screen.findByRole("alert");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد التحصيل والبيع" }));
    expect(await screen.findByText("تم التحصيل وخصم المخزون")).toBeVisible();
    expect(writes).toHaveLength(2);
    expect(writes[0].headers).toEqual(writes[1].headers);
    expect(JSON.parse(String(writes[0].body))).toEqual({ productId: "product", quantity: 2, branchId: "b1", playerId: null, paymentMethod: "Cash", expectedAmount: "180.00", cashSessionId: "shift" });
    expect(screen.getByLabelText("الفرع")).toBeDisabled();
  });
  it("creates a seven-day draft separately from publish, with automatic date metadata", async () => {
    const writes: unknown[] = [];
    global.fetch = vi.fn((input, init) => String(input).includes("/csrf") ? response({ token: "csrf" }) : init ? (writes.push(JSON.parse(String(init.body))), response({ id: "news" })) : response([])) as typeof fetch;
    render(<NewsManagement />);
    expect(screen.getByLabelText("مدة الظهور بالأيام")).toHaveValue(7);
    expect(screen.queryByLabelText("التاريخ")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("عنوان الخبر"), { target: { value: "خبر تجريبي" } });
    fireEvent.change(screen.getByLabelText("تفاصيل الخبر"), { target: { value: "تفاصيل الخبر" } });
    fireEvent.click(screen.getByRole("button", { name: "حفظ كمسودة" }));
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({ visibleDays: 7, publish: false });
  });
});
