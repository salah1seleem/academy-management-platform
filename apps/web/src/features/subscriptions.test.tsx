import { webcrypto } from "node:crypto";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { GuardianSubscriptionStatus, PlanList, RenewalDiscountDetail, SubscriptionPeriodDetail } from "./subscriptions";

vi.mock("next/navigation", () => ({ usePathname: () => "/guardian", useSearchParams: () => new URLSearchParams(), useRouter: () => ({ push: vi.fn() }) }));
beforeEach(() => { vi.stubGlobal("crypto", webcrypto); sessionStorage.clear(); global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "p1", arabicName: "اشتراك شهري", sport: "كرة القدم", sportId: "s1", planType: "Duration", price: 900, currency: "EGP", durationDays: 30, isActive: true }] }); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe("subscription plan list", () => {
  it("renders list columns, add action, and only functional row actions", async () => {
    const defaultRead = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation((input, init) => {
      if (String(input).endsWith("/me")) return Promise.resolve(Response.json({ role: "AcademyOwner" }));
      if (String(input).endsWith("/my-access")) return Promise.resolve(Response.json({ allBranches: true }));
      return defaultRead(input, init);
    });
    render(<PlanList />);
    expect(await screen.findByRole("link", { name: "إضافة باقة" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/new");
    expect(screen.getByRole("link", { name: "اشتراك شهري" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/p1");
    expect(screen.getByText("كرة القدم")).toBeVisible();
    expect(screen.getByText("بالمدة")).toBeVisible();
    expect(screen.getByText(/900/)).toBeVisible();
    expect(screen.getByText("٣٠ يومًا")).toBeVisible();
    expect(screen.getByRole("link", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/p1/edit");
    expect(screen.getByRole("button", { name: "إيقاف" })).toBeVisible();
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "غير موجود" } });
    expect(screen.queryByText("اشتراك شهري")).not.toBeInTheDocument();
  });
});

const discountRenewal = { id: "renewal-1", player: "عمر أحمد", sport: "كرة القدم", plan: "اشتراك شهري", originalAmount: 900, discountType: undefined, discountValue: undefined, discountAmount: 0, finalAmount: 900, currency: "EGP", status: "PaymentInProgress", paymentStatus: "Pending", version: 11, canModify: true, adjustments: [] };
async function renderDiscount(data: object = discountRenewal) { global.fetch = vi.fn().mockResolvedValue(response(data)) as typeof fetch; render(<RenewalDiscountDetail id="renewal-1" />); await screen.findByText("عمر أحمد"); await screen.findByRole("heading", { name: "سجل الخصم" }); }

describe("renewal discount management", () => {
  it("shows Apply Discount and no coupon or promo fields", async () => { await renderDiscount(); expect(screen.getByRole("button", { name: "تطبيق خصم" })).toBeVisible(); expect(screen.queryByLabelText(/كوبون|coupon|promo|برومو/i)).not.toBeInTheDocument(); });
  it("supports percentage and fixed amount forms with a required reason", async () => { await renderDiscount(); fireEvent.click(screen.getByRole("button", { name: "تطبيق خصم" })); expect(screen.getByLabelText("نوع الخصم")).toHaveTextContent("نسبة مئوية"); fireEvent.change(screen.getByLabelText("نوع الخصم"), { target: { value: "FixedAmount" } }); expect(screen.getByLabelText("نوع الخصم")).toHaveValue("FixedAmount"); expect(screen.getByLabelText("سبب الخصم")).toBeRequired(); });
  it("previews original discount and final values as non-authoritative", async () => { await renderDiscount(); fireEvent.click(screen.getByRole("button", { name: "تطبيق خصم" })); fireEvent.change(screen.getByLabelText("قيمة الخصم"), { target: { value: "10" } }); expect(screen.getByText("قيمة الخصم بعد الحفظ").parentElement).toHaveTextContent(/90/); expect(screen.getByText("المبلغ المتوقع").parentElement).toHaveTextContent(/810/); expect(screen.getByText("عند الحفظ سيُلغى طلب الدفع الحالي ويصدر طلب بالمبلغ الجديد.")).toBeVisible(); });
  it("submits only discount command fields", async () => { global.fetch = vi.fn().mockResolvedValueOnce(response(discountRenewal)).mockResolvedValueOnce(response({ token: "csrf" })).mockResolvedValueOnce(response({ renewalRequestId: "renewal-1", adjustmentId: "a1" })).mockResolvedValueOnce(response({ ...discountRenewal, discountType: "Percentage", discountValue: 10, discountAmount: 90, finalAmount: 810 })) as typeof fetch; render(<RenewalDiscountDetail id="renewal-1" />); await screen.findByText("عمر أحمد"); fireEvent.click(screen.getByRole("button", { name: "تطبيق خصم" })); fireEvent.change(screen.getByLabelText("قيمة الخصم"), { target: { value: "10" } }); fireEvent.change(screen.getByLabelText("سبب الخصم"), { target: { value: "خصم إخوة" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ الخصم" })); await waitFor(() => expect(vi.mocked(fetch).mock.calls.length).toBeGreaterThan(2)); const body = JSON.parse(String((vi.mocked(fetch).mock.calls[2][1] as RequestInit).body)); expect(body).toEqual({ type: "Percentage", value: 10, reason: "خصم إخوة", expectedVersion: 11 }); expect(body).not.toHaveProperty("finalAmount"); expect(body).not.toHaveProperty("discountAmount"); });
  it("shows Arabic status, original discount, final value, and edit action", async () => { await renderDiscount({ ...discountRenewal, discountType: "Percentage", discountValue: 10, discountAmount: 90, finalAmount: 810 }); expect(screen.getByText("جارٍ الدفع")).toBeVisible(); expect(screen.getByText("قيد الانتظار")).toBeVisible(); expect(screen.getByText("الخصم").parentElement).toHaveTextContent(/90.*10%/); expect(screen.getByText("المبلغ المطلوب").parentElement).toHaveTextContent(/810/); expect(screen.queryByText("PaymentInProgress")).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "تعديل الخصم" })).toBeVisible(); });
  it("hides all discount mutation for confirmed renewals", async () => { await renderDiscount({ ...discountRenewal, status: "Paid", paymentStatus: "Confirmed", canModify: false, discountType: "Percentage", discountValue: 10, discountAmount: 90, finalAmount: 810 }); expect(screen.queryByRole("button", { name: /خصم/ })).not.toBeInTheDocument(); });
  it("renders append-only audit without edit/delete controls", async () => { await renderDiscount({ ...discountRenewal, discountType: "FixedAmount", discountValue: 100, discountAmount: 100, finalAmount: 800, adjustments: [{ id: "a1", previousFinalAmount: 900, newFinalAmount: 800, reason: "قرار إداري", performedBy: "منى السيد", createdAtUtc: "2026-09-28T10:00:00Z", newDiscountType: "FixedAmount", newDiscountValue: 100 }] }); expect(screen.getByText("قرار إداري")).toBeVisible(); expect(screen.getByText(/منى السيد/)).toBeVisible(); expect(screen.queryByRole("button", { name: /حذف السجل|تعديل السجل/ })).not.toBeInTheDocument(); });
  it("shows stale conflict from the backend", async () => { global.fetch = vi.fn().mockResolvedValueOnce(response(discountRenewal)).mockResolvedValueOnce(response({ token: "csrf" })).mockResolvedValueOnce(response({ message: "تغير طلب التجديد منذ فتح الصفحة." }, false)).mockResolvedValueOnce(response(discountRenewal)) as typeof fetch; render(<RenewalDiscountDetail id="renewal-1" />); await screen.findByText("عمر أحمد"); fireEvent.click(screen.getByRole("button", { name: "تطبيق خصم" })); fireEvent.change(screen.getByLabelText("قيمة الخصم"), { target: { value: "10" } }); fireEvent.change(screen.getByLabelText("سبب الخصم"), { target: { value: "اختبار" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ الخصم" })); expect(await screen.findByRole("alert")).toHaveTextContent("تغير طلب التجديد منذ فتح الصفحة"); });

  it("finds a saved discount after the page remounts without sending another write", async () => {
    const command = "/api/v1/subscriptions/renewals/renewal-1/discount";
    const attempts: string[] = [];
    let recorded = false;
    const fetcher = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith("/csrf")) return response({ token: "csrf" });
      if (url === command) {
        const key = new Headers(init?.headers).get("Idempotency-Key")!;
        attempts.push(key);
        recorded = true;
        throw new TypeError("connection lost");
      }
      if (url === `${command}/attempt`) {
        expect(new Headers(init?.headers).get("Idempotency-Key")).toBe(attempts[0]);
        return response({ renewalRequestId: "renewal-1", adjustmentId: "saved" });
      }
      return response(recorded ? { ...discountRenewal, discountType: "Percentage", discountValue: 10.25, discountAmount: 92.25, finalAmount: 807.75 } : discountRenewal);
    });
    vi.stubGlobal("fetch", fetcher);
    const first = render(<RenewalDiscountDetail id="renewal-1" />);
    fireEvent.click(await screen.findByRole("button", { name: "تطبيق خصم" }));
    fireEvent.change(screen.getByLabelText("قيمة الخصم"), { target: { value: "١٠٫٢٥" } });
    fireEvent.change(screen.getByLabelText("سبب الخصم"), { target: { value: "سبب محفوظ" } });
    fireEvent.click(screen.getByRole("button", { name: "حفظ الخصم" }));
    await screen.findByRole("button", { name: "التحقق من نتيجة الحفظ" });
    expect(sessionStorage.getItem("academy-finance:renewal-discount:renewal-1")).not.toContain("سبب محفوظ");
    first.unmount();
    render(<RenewalDiscountDetail id="renewal-1" />);
    fireEvent.click(await screen.findByRole("button", { name: "التحقق من نتيجة الحفظ" }));
    expect(await screen.findByText("تم العثور على تعديل الخصم المحفوظ.")).toBeVisible();
    expect(await screen.findByText("807.75 جنيه")).toBeVisible();
    expect(attempts).toHaveLength(1);
    expect(sessionStorage.getItem("academy-finance:renewal-discount:renewal-1")).toBeNull();
  });
});

const basePeriod = {
  id: "period-1", sportEnrollmentId: "enrollment-1", player: "عمر أحمد", playerCode: "NG-0001", sport: "كرة القدم", branch: "مدينة نصر", group: "مجموعة أ",
  plan: "اشتراك شهري", planType: "Duration", startDate: "2026-09-01", endDate: "2026-10-31", initialSessions: null,
  remainingSessions: null, status: "Active", version: 7, asOfDate: "2026-10-01", canRenew: true, canPause: true, canDeductDays: true, canCancel: true, priceSnapshot: 900, currencySnapshot: "EGP", receiptId: "receipt-1", adjustments: [],
};
const response = (data: unknown, ok = true) => ({ ok, status: ok ? 200 : 409, json: async () => data });
function initial(data: object = basePeriod) { global.fetch = vi.fn().mockResolvedValue(response(data)) as typeof fetch; }
async function renderDetail(data: object = basePeriod) { initial(data); render(<SubscriptionPeriodDetail id="period-1" />); await screen.findByRole("heading", { name: "عمر أحمد · NG-0001" }); }

describe("subscription period adjustments", () => {
  it("shows only valid actions for an active duration period", async () => {
    await renderDetail();
    expect(screen.getByRole("button", { name: "تجميد" })).toBeVisible(); expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "خصم أيام" })).toBeVisible(); expect(screen.getByRole("button", { name: "إلغاء الاشتراك" })).toBeVisible(); expect(screen.queryByRole("button", { name: "استئناف" })).not.toBeInTheDocument();
  });

  it("shows Resume only for a frozen period while keeping timed adjustments", async () => {
    await renderDetail({ ...basePeriod, status: "Frozen", frozenFromDate: "2026-09-25" });
    expect(screen.getByRole("button", { name: "استئناف" })).toBeVisible(); expect(screen.queryByRole("button", { name: "تجميد" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument();
  });

  it("hides freeze and day actions for sessions-only plans", async () => {
    await renderDetail({ ...basePeriod, planType: "Sessions", endDate: undefined, remainingSessions: 8 });
    expect(screen.queryByRole("button", { name: "تجميد" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "خصم أيام" })).not.toBeInTheDocument(); expect(screen.getByText(/الباقة بالحصة فقط/)).toBeVisible();
  });

  it("shows no mutation action for a cancelled period", async () => {
    await renderDetail({ ...basePeriod, status: "Cancelled" });
    expect(screen.queryByRole("button", { name: "تجميد" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "خصم أيام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إلغاء الاشتراك" })).not.toBeInTheDocument();
  });

  it("keeps the adjustment reason optional and the day range bounded", async () => {
    await renderDetail(); fireEvent.click(screen.getByRole("button", { name: "خصم أيام" }));
    expect(screen.getByLabelText("سبب التعديل")).not.toBeRequired(); expect(screen.getByLabelText("سبب التعديل")).toHaveAttribute("maxlength", "500"); expect(screen.getByLabelText("عدد الأيام")).toHaveAttribute("min", "1"); expect(screen.getByLabelText("عدد الأيام")).toHaveAttribute("max", "60");
  });

  it("submits an adjustment without a reason", async () => {
    global.fetch = vi.fn().mockResolvedValueOnce(response(basePeriod)).mockResolvedValueOnce(response({ token: "csrf" })).mockResolvedValueOnce(response({})).mockResolvedValueOnce(response(basePeriod)) as typeof fetch;
    render(<SubscriptionPeriodDetail id="period-1" />); await screen.findByRole("heading", { name: "عمر أحمد · NG-0001" }); fireEvent.click(screen.getByRole("button", { name: "خصم أيام" })); fireEvent.change(screen.getByLabelText("عدد الأيام"), { target: { value: "5" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" }));
    fireEvent.click(screen.getByRole("button", { name: "تأكيد الخصم" })); await waitFor(() => expect(vi.mocked(fetch).mock.calls.length).toBeGreaterThan(2)); const body = JSON.parse(String((vi.mocked(fetch).mock.calls[2][1] as RequestInit).body)); expect(body.reason).toBe("");
  });

  it("requires freeze confirmation and explains its entitlement effect", async () => {
    await renderDetail(); fireEvent.click(screen.getByRole("button", { name: "تجميد" })); fireEvent.change(screen.getByLabelText("التاريخ الفعلي"), { target: { value: "2026-09-28" } }); fireEvent.change(screen.getByLabelText("سبب التعديل"), { target: { value: "سفر" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("تأكيد تجميد الاشتراك"); expect(screen.getByRole("dialog")).toHaveTextContent("لن يتغير التحصيل أو الإيصال");
  });

  it("requires deduct confirmation", async () => {
    await renderDetail(); fireEvent.click(screen.getByRole("button", { name: "خصم أيام" })); fireEvent.change(screen.getByLabelText("عدد الأيام"), { target: { value: "2" } }); fireEvent.change(screen.getByLabelText("سبب التعديل"), { target: { value: "تصحيح" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("سيتم خصم 2 يوم");
  });

  it("uses the exact no-refund cancellation confirmation", async () => {
    await renderDetail(); fireEvent.click(screen.getByRole("button", { name: "إلغاء الاشتراك" })); fireEvent.change(screen.getByLabelText("التاريخ الفعلي"), { target: { value: "2026-09-28" } }); fireEvent.change(screen.getByLabelText("سبب التعديل"), { target: { value: "طلب إداري" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("سيتم إلغاء صلاحية هذه الفترة فقط. لن يتم حذف التحصيل أو الإيصال ولن يتم رد الأموال تلقائيًا."); expect(screen.queryByLabelText(/رد الأموال|Refund/i)).not.toBeInTheDocument();
  });

  it("previews resume end while keeping the backend authoritative", async () => {
    await renderDetail({ ...basePeriod, status: "Frozen", frozenFromDate: "2026-09-25" }); fireEvent.click(screen.getByRole("button", { name: "استئناف" })); fireEvent.change(screen.getByLabelText("التاريخ الفعلي"), { target: { value: "2026-09-28" } });
    expect(screen.getByText((_, element) => element?.classList.contains("adjustment-preview") ?? false)).toHaveTextContent("3 نوفمبر 2026 — الحساب النهائي من الخادم");
  });

  it("renders adjusted end date and immutable audit history", async () => {
    await renderDetail({ ...basePeriod, endDate: "2026-11-05", adjustments: [{ id: "a1", type: "DaysAdded", effectiveDate: "2026-09-28", daysDelta: 5, oldEndDate: "2026-10-31", newEndDate: "2026-11-05", reason: "تعويض", performedBy: "منى السيد", performedAtUtc: "2026-09-28T10:00:00Z" }] });
    expect(screen.getByText(/1 سبتمبر 2026 — 5 نوفمبر 2026/)).toBeVisible(); expect(screen.getAllByText("إضافة أيام")).toHaveLength(1); expect(screen.getByText("تعويض")).toBeVisible(); expect(screen.getByText("منى السيد", { exact: false })).toBeVisible();
  });

  it("sends only command fields and reports a stale conflict", async () => {
    global.fetch = vi.fn().mockResolvedValueOnce(response(basePeriod)).mockResolvedValueOnce(response({ token: "csrf" })).mockResolvedValueOnce(response({ message: "تغير الاشتراك منذ فتح الصفحة." }, false)).mockResolvedValueOnce(response({ ...basePeriod, version: 8 })) as typeof fetch;
    render(<SubscriptionPeriodDetail id="period-1" />); await screen.findByRole("heading", { name: "عمر أحمد · NG-0001" }); fireEvent.click(screen.getByRole("button", { name: "خصم أيام" })); fireEvent.change(screen.getByLabelText("عدد الأيام"), { target: { value: "5" } }); fireEvent.change(screen.getByLabelText("سبب التعديل"), { target: { value: "تعويض" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" })); fireEvent.click(screen.getByRole("button", { name: "تأكيد الخصم" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("تغير الاشتراك منذ فتح الصفحة"); const call = vi.mocked(fetch).mock.calls[2]; const body = JSON.parse(String((call[1] as RequestInit).body)); expect(body).toEqual({ direction: 2, days: 5, reason: "تعويض", expectedVersion: 7 }); expect(body).not.toHaveProperty("newEndDate"); expect(body).not.toHaveProperty("status");
  });

  it("shows loading and load failure states", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("offline")) as typeof fetch; render(<SubscriptionPeriodDetail id="period-1" />); expect(screen.getByRole("status")).toBeVisible(); expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل تفاصيل الاشتراك");
  });
});

describe("guardian subscription adjustment visibility", () => {
  it("shows a safe frozen state and simple history", async () => {
    initial({ id: "period-1", plan: "اشتراك شهري", sport: "كرة القدم", startDate: "2026-09-01", endDate: "2026-10-31", frozenFromDate: "2026-09-25", status: "Frozen", adjustments: [{ type: "FreezeStarted", effectiveDate: "2026-09-25" }] }); render(<GuardianSubscriptionStatus id="period-1" childId="child-1" />);
    expect(await screen.findByText("مجمد")).toBeVisible(); expect(screen.getByText("الاشتراك مجمد.")).toBeVisible(); expect(screen.queryByText("سبب داخلي")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "العودة لملف الطفل" })).toHaveAttribute("href", "/guardian/children/child-1");
  });

  it("shows cancelled state without refund or reversal controls", async () => {
    initial({ id: "period-1", plan: "اشتراك شهري", sport: "كرة القدم", startDate: "2026-09-01", endDate: "2026-10-31", status: "Cancelled", adjustments: [{ type: "Cancelled", effectiveDate: "2026-09-28" }] }); render(<GuardianSubscriptionStatus id="period-1" />);
    expect(await screen.findByText("ملغي")).toBeVisible(); expect(screen.getByText("الاشتراك ملغي.")).toBeVisible(); expect(screen.queryByRole("button")).not.toBeInTheDocument(); expect(screen.queryByText(/Refund|استرداد|رد الأموال/i)).not.toBeInTheDocument();
  });

  it("reports guardian load errors", async () => {
    global.fetch = vi.fn().mockResolvedValue(response({}, false)) as typeof fetch; render(<GuardianSubscriptionStatus id="missing" />); await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("تعذر تحميل حالة الاشتراك"));
    expect(screen.getByRole("link", { name: "العودة إلى الأبناء" })).toHaveAttribute("href", "/guardian#children");
  });

  it("retries the same period and shows an exhausted session balance without an end date", async () => {
    const read = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 503 }))
      .mockResolvedValueOnce(Response.json({ id: "session-only", plan: "باقة الحصص", sport: "السباحة", startDate: "2026-11-01",
        endDate: null, remainingSessions: 0, status: "Expired", adjustments: [] }));
    global.fetch = (url, init) => String(url).includes("/adjustments") ? read(url, init) : Promise.resolve(Response.json({}));
    render(<GuardianSubscriptionStatus id="session-only" childId="child-1" />);
    expect(screen.getByRole("heading", { name: "حالة الاشتراك" })).toBeVisible();
    fireEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));
    expect(await screen.findByText("باقة الحصص")).toBeVisible();
    expect(screen.getByText("الحصص المتبقية").parentElement).toHaveTextContent("0");
    expect(screen.queryByText("نهاية الاشتراك")).not.toBeInTheDocument();
    expect(read.mock.calls.map(([url]) => url)).toEqual(Array(2).fill("/api/v1/guardian/subscriptions/periods/session-only/adjustments"));
  });

  it("hides a read period when returning to a tab with revoked access", async () => {
    const read = vi.fn()
      .mockResolvedValueOnce(Response.json({ id: "period", plan: "باقة سابقة", sport: "السباحة", startDate: "2026-11-01", status: "Active", adjustments: [] }))
      .mockResolvedValueOnce(new Response(null, { status: 404 }));
    global.fetch = (url, init) => String(url).includes("/adjustments") ? read(url, init) : Promise.resolve(Response.json({}));
    const previous = Object.getOwnPropertyDescriptor(document, "hidden");
    let hidden = false;
    Object.defineProperty(document, "hidden", { configurable: true, get: () => hidden });
    try {
      render(<GuardianSubscriptionStatus id="period" />);
      expect(await screen.findByText("باقة سابقة")).toBeVisible();
      hidden = true;
      fireEvent(document, new Event("visibilitychange"));
      expect(screen.queryByText("باقة سابقة")).not.toBeInTheDocument();
      hidden = false;
      fireEvent(document, new Event("visibilitychange"));
      expect(await screen.findByRole("alert")).toHaveTextContent("هذا الاشتراك غير متاح.");
      expect(screen.queryByText("باقة سابقة")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "إعادة المحاولة" })).not.toBeInTheDocument();
    } finally {
      if (previous) Object.defineProperty(document, "hidden", previous);
      else Reflect.deleteProperty(document, "hidden");
    }
  });

  it("does not display a successful response for a different period", async () => {
    global.fetch = vi.fn(async () => Response.json({ id: "other", plan: "باقة طفل آخر", status: "Active", adjustments: [] }));
    render(<GuardianSubscriptionStatus id="requested" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("هذا الاشتراك غير متاح.");
    expect(screen.queryByText("باقة طفل آخر")).not.toBeInTheDocument();
  });

  it("keeps the selected period when the previous response arrives last", async () => {
    let finishPrevious!: (value: Response) => void;
    const previous = new Promise<Response>(resolve => { finishPrevious = resolve; });
    global.fetch = vi.fn((url: RequestInfo | URL) => {
      if (String(url).includes("/previous/adjustments")) return previous;
      if (String(url).includes("/selected/adjustments")) {
        return Promise.resolve(Response.json({ id: "selected", plan: "الباقة الحالية", status: "Active", adjustments: [] }));
      }
      return Promise.resolve(Response.json({}));
    });
    const { rerender } = render(<GuardianSubscriptionStatus id="previous" />);
    rerender(<GuardianSubscriptionStatus id="selected" />);
    expect(await screen.findByText("الباقة الحالية")).toBeVisible();
    await act(async () => { finishPrevious(Response.json({ id: "previous", plan: "الباقة السابقة", status: "Active", adjustments: [] })); });
    expect(screen.getByText("الباقة الحالية")).toBeVisible();
    expect(screen.queryByText("الباقة السابقة")).not.toBeInTheDocument();
  });
});
