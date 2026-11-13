import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DailyStaffReport } from "./daily-staff-report";
import { MediaUpload } from "./media-upload";
import { ProductSaleForm } from "./product-sale-form";
import { NewsManagement } from "./news-management";
import { PaymentRequestQueue } from "./manual-payments";
import { DateFilterDefaults } from "../components/date-filter-defaults";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams() }));

const response = (value: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(value), { status }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); });
describe("owner-review operations", () => {
  it("searches a payment by the full reference, preserves filters and retries the exact read", async () => {
    const reference = "E847D253062D";
    const requests: URL[] = [];
    let fails = true;
    global.fetch = vi.fn(input => {
      const url = new URL(String(input), "http://localhost");
      if (!url.pathname.endsWith("/payment-requests")) return response({});
      requests.push(url);
      if (url.searchParams.has("reference") && fails) {
        fails = false;
        return response({}, 503);
      }
      return response({
        pending: 5, products: 3,
        items: url.searchParams.has("reference") ? [{ id: "exact", reference, player: "أسرة تجريبية", plan: "زجاجة مياه", type: "Product", status: "Pending", amount: 180, currency: "EGP" }] : [],
      });
    }) as typeof fetch;
    render(<PaymentRequestQueue />);
    await screen.findByText("لا توجد طلبات مطابقة.");
    fireEvent.change(screen.getByLabelText("نوع الطلب"), { target: { value: "Product" } });
    const input = screen.getByRole("textbox", { name: "رقم الطلب" });
    fireEvent.change(input, { target: { value: "123" } });
    fireEvent.click(screen.getByRole("button", { name: /^بحث$/ }));
    expect(screen.getByRole("alert")).toHaveTextContent("أدخل رقم الطلب كاملًا");
    expect(requests.every(url => !url.searchParams.has("reference"))).toBe(true);
    fireEvent.change(input, { target: { value: ` ${reference.toLowerCase()} ` } });
    fireEvent.click(screen.getByRole("button", { name: /^بحث$/ }));
    await screen.findByText("تعذر تحميل طلبات الدفع.");
    fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الطلبات" }));
    const link = await screen.findByRole("link", { name: "مراجعة" });
    expect(link).toHaveAttribute("href", expect.stringContaining(`reference=${reference.toLowerCase()}`));
    expect(screen.getByText(reference)).toBeVisible();
    expect(requests.at(-1)?.href).toBe(requests.at(-2)?.href);
    fireEvent.click(screen.getByRole("button", { name: "مسح رقم الطلب" }));
    await screen.findByText("لا توجد طلبات مطابقة.");
    expect(requests.at(-1)?.searchParams.get("type")).toBe("Product");
    expect(requests.at(-1)?.searchParams.has("reference")).toBe(false);
  });
  it("shows the cancelled family status and uses the review authority from each pending row", async () => {
    const item = { player: "أسرة تجريبية", sport: "تجديد الأسرة", branch: "فروع التسجيلات", plan: "تسجيلان", amount: 1440, currency: "EGP", hasProof: false };
    global.fetch = vi.fn(input => String(input).includes("payment-requests") ? response({
      pending: 2, newEnrollments: 1, renewals: 1, products: 0,
      items: [
        { ...item, id: "family-cancelled", type: "FamilyRenewal", status: "Cancelled" },
        { ...item, id: "family-read", type: "FamilyRenewal", status: "Pending", canReview: false },
        { ...item, id: "enrollment-edit", type: "Enrollment", status: "Pending", canReview: true, sport: "كرة القدم", branch: "فرع العرض" },
      ],
    }) : response({})) as typeof fetch;
    render(<PaymentRequestQueue />);
    expect(await screen.findByText("ملغي", { exact: true })).toBeVisible();
    expect(screen.getByRole("option", { name: "ملغي" })).toHaveValue("Cancelled");
    expect(screen.getAllByRole("link", { name: "عرض الطلب" })).toHaveLength(2);
    expect(screen.getByRole("link", { name: "مراجعة" })).toHaveAttribute("href", expect.stringContaining("/enrollment-requests/enrollment-edit?"));
    expect(screen.queryByText("فروع التسجيلات", { exact: true })).not.toBeInTheDocument();
  });
  it("preserves unpriced and zero values and identifies otherwise identical renewal requests", async () => {
    const item = { player: "لاعب تجريبي", sport: "السباحة", branch: "فرع العرض", plan: "شهر", status: "Pending", hasProof: true, createdAtUtc: "2026-09-28T00:00:00Z" };
    const items = [
      { ...item, type: "Enrollment", id: "unpriced", reference: "C104A7BC0182", amount: null, currency: null },
      { ...item, type: "Enrollment", id: "zero", reference: "D104A7BC0182", amount: 0, currency: "EGP" },
      { ...item, type: "Renewal", id: "first", reference: "F8A12C09-1182-4300-AA00-000000000001", amount: 721.25, currency: "EGP" },
      { ...item, type: "Renewal", id: "second", reference: "F8A12C09-1182-4300-AA00-000000000002", amount: 721.25, currency: "EGP" },
    ];
    global.fetch = vi.fn(input => String(input).includes("payment-requests") ? response({ pending: 4, newEnrollments: 2, renewals: 2, products: 0, items }) : response([])) as typeof fetch;
    render(<PaymentRequestQueue />);
    for (const [index, expected] of ["المبلغ غير محدد", "0 جنيه", "721.25 جنيه", "721.25 جنيه"].entries()) {
      const item = items[index];
      const row = (await screen.findByText(item.reference)).closest('[role="row"]') as HTMLElement;
      expect(within(row).getByText(expected)).toBeVisible();
      const href = within(row).getByRole("link", { name: "مراجعة" }).getAttribute("href")!;
      const destination = new URL(href, window.location.origin);
      expect(destination.pathname).toBe(item.type === "Enrollment" ? `/dashboard/enrollment-requests/${item.id}` : `/dashboard/subscriptions/payments/${item.id}`);
      expect(destination.searchParams.get("origin")).toBe("payment-queue");
    }
  });
  it("defaults to month-to-date, omits empty branches and never leaves a loader after a queue failure", async () => {
    global.fetch = vi.fn(input => String(input).includes("payment-requests") ? response({}, 503) : response({})) as typeof fetch;
    render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PaymentRequestQueue /></DateFilterDefaults>);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل طلبات الدفع");
    expect(fetch).toHaveBeenCalledWith("/api/v1/manage/payment-requests?status=Pending&from=2026-09-01&to=2026-09-28&page=1&pageSize=25", expect.objectContaining({ cache: "no-store", signal: expect.anything() }));
    expect(screen.queryByText("جارٍ التحميل…")).not.toBeInTheDocument();
  });
  it("does not show the previous branch's payment actions while a new branch is loading", async () => {
    let finishPrevious: (value: Response) => void = () => {};
    const item = { type: "Renewal", sport: "السباحة", plan: "شهر", status: "Pending", amount: 500, createdAtUtc: "2026-09-28T00:00:00Z" };
    const fetcher = vi.fn((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/structure/options")) return response({ branches: [{ id: "30000000-0000-0000-0000-000000000020", arabicName: "فرع السباحة" }] });
      if (!url.includes("payment-requests")) return response([]);
      if (url.includes("branchId=30000000-0000-0000-0000-000000000020")) return response({ pending: 1, renewals: 1, items: [{ ...item, id: "new", player: "لاعب الفرع المختار" }] });
      return new Promise<Response>(resolve => { finishPrevious = resolve; });
    });
    global.fetch = fetcher as typeof fetch;
    render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PaymentRequestQueue /></DateFilterDefaults>);
    await screen.findByRole("option", { name: "فرع السباحة" });
    fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "30000000-0000-0000-0000-000000000020" } });
    await screen.findByText("لاعب الفرع المختار");
    finishPrevious(await response({ pending: 1, items: [{ ...item, id: "old", player: "لاعب من النطاق السابق" }] }));
    await waitFor(() => expect(screen.getByRole("link", { name: "مراجعة" })).toHaveAttribute("href", expect.stringContaining("/dashboard/subscriptions/payments/new?")));
    expect(screen.queryByText("لاعب من النطاق السابق")).not.toBeInTheDocument();
    expect(fetcher.mock.calls.map(([url])=>String(url)).filter(url=>url.includes("payment-requests"))).toHaveLength(2);
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
    global.fetch = vi.fn(input => String(input).includes("media-groups")
      ? response({ items: [{ id: "g1", arabicName: "براعم" }], hasMore: false })
      : response({ items: [{ id: "p1", arabicName: "لاعب تجريبي" }], hasMore: false })) as typeof fetch;
    render(<MediaUpload />);
    expect(await screen.findByRole("option", { name: "لاعب تجريبي" })).toBeInTheDocument();
    expect(screen.getByLabelText("الصورة أو الفيديو")).toHaveAttribute("type", "file");
    expect(screen.queryByLabelText(/وقت الالتقاط|الترتيب|مرجع الصورة|النوع|ينشر/)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/النشر إلى/), { target: { value: "group" } });
    expect(await screen.findByRole("option", { name: "براعم" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /^المجموعة/ })).toBeVisible();
  });
  it("retries an uncertain stock sale with the same key, without trusting a client amount", async () => {
    const writes: RequestInit[] = [];
    global.fetch = vi.fn((input, init) => {
      const url = String(input);
      if (url.includes("/auth/csrf")) return response({ token: "csrf" });
      if (url.includes("/by-request?")) return response({}, 404);
      if (url.includes("/manage/product-sales/quote")) return response({ unitPrice: "90.00", amount: "180.00", currency: "EGP", stockQuantity: 3, cashEnabled: true, sessions: [{ id: "shift", name: "ورديتي" }] });
      if (url.includes("/manage/product-sales") && init?.method === "POST") { writes.push(init); return writes.length === 1 ? response({ message: "انقطع الاتصال" }, 503) : response({ id: "sale", status: "Confirmed", amount: "180.00", currency: "EGP" }); }
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
    fireEvent.click(screen.getByRole("button", { name: "مراجعة التحصيل والبيع" }));
    fireEvent.click(screen.getByRole("button", { name: "تسجيل البيع" }));
    await screen.findByRole("alert");
    fireEvent.click(screen.getByRole("button", { name: "التحقق من تسجيل البيع" }));
    fireEvent.click(await screen.findByRole("button", { name: "إعادة إرسال الطلب نفسه" }));
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
    fireEvent.click(screen.getByRole("button",{name:"إنشاء خبر"}));
    expect(screen.getByLabelText("مدة الظهور بالأيام")).toHaveValue(7);
    expect(screen.queryByLabelText("التاريخ")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("عنوان الخبر"), { target: { value: "خبر تجريبي" } });
    fireEvent.change(screen.getByLabelText("تفاصيل الخبر"), { target: { value: "تفاصيل الخبر" } });
    fireEvent.click(screen.getByRole("button", { name: "حفظ كمسودة" }));
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({ visibleDays: 7, publish: false });
  });
});
