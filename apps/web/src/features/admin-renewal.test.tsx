import { webcrypto } from "node:crypto";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminRenewalFlow } from "./subscriptions";

const push = vi.fn();
let query = new URLSearchParams();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), useSearchParams: () => query }));

const row = { id: "enrollment-1", playerId: "player-1", playerCode: "NG-0003", player: "عمر أحمد حسن", sport: "كرة القدم", branch: "مدينة نصر", group: "مجموعة أ", guardianPhone: "+201000000002", period: null };
const details = { enrollment: { ...row, sportId: "sport-1" }, current: null, previewStart: "2026-09-28", plans: [{ id: "plan-1", arabicName: "اشتراك شهري", planType: "Duration", price: 900, currency: "EGP", durationDays: 30 }] };
const quote = { enrollmentId: "enrollment-1", planId: "plan-1", quoteHash: "a".repeat(64), start: "2026-09-28", end: "2026-10-27", sessions: null, originalAmount: 900, discountAmount: 180, finalAmount: 720, currency: "EGP" };
const payment = { id: "payment-1", renewalId: "renewal-1", sportEnrollmentId: "enrollment-1", subscriptionPlanId: "plan-1", amount: 720, currency: "EGP", isCurrent: true, status: "Pending" };

function response(data: unknown, ok = true) { return { ok, json: async () => data }; }

beforeEach(() => {
  push.mockReset();
  query = new URLSearchParams();
  sessionStorage.clear();
  vi.stubGlobal("crypto", webcrypto);
  global.fetch = vi.fn(async input => {
    const url = String(input);
    if (url.endsWith("/quote?planId=plan-1")) return response(quote) as Response;
    if (url.includes("admin-renewals/enrollments/enrollment-1")) return response(details) as Response;
    if (url.includes("admin-renewals/enrollments")) return response([row]) as Response;
    if (url.includes("auth/csrf")) return response({ token: "csrf" }) as Response;
    if (url.endsWith("/subscriptions/payments/payment-1")) return response(payment) as Response;
    if (url.includes("admin-renewals")) return response({ renewalId: "renewal-1", paymentId: "payment-1" }) as Response;
    return response({}) as Response;
  }) as typeof fetch;
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function openReview() {
  query = new URLSearchParams("enrollmentId=enrollment-1");
  return render(<AdminRenewalFlow />);
}

async function requestRenewal() {
  const create = screen.getByRole("button", { name: "إنشاء طلب التجديد" });
  await waitFor(() => expect(create).toBeEnabled());
  fireEvent.click(create);
}

describe("admin renewal flow", () => {
  it("uses the canonical enrollment and the quoted period and amount before posting", async () => {
    const baseline = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation(async (input, init) => String(input).endsWith("/period-alias")
      ? Response.json(details) : baseline(input, init));
    query = new URLSearchParams("enrollmentId=period-alias");
    render(<AdminRenewalFlow />);
    const option = await screen.findByRole("radio");
    expect(screen.getByRole("button", { name: "إنشاء طلب التجديد" })).toBeDisabled();
    expect(screen.getByText(/٣٠ يومًا/)).toBeVisible();
    fireEvent.click(option);
    const summary = within(await screen.findByRole("region", { name: "عرض التجديد" }));
    expect(summary.getByText("المبلغ المطلوب").parentElement).toHaveTextContent("720");
    expect(summary.getByText("الخصم").parentElement).toHaveTextContent("180");
    expect(summary.getByText("نهاية الفترة المتوقعة").parentElement).toHaveTextContent("27 أكتوبر 2026");
    expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
    await requestRenewal();
    await waitFor(() => expect(push).toHaveBeenCalled());
    const posted = vi.mocked(fetch).mock.calls.find(([input, init]) => String(input).endsWith("/admin-renewals") && init?.method === "POST");
    expect(JSON.parse(String(posted?.[1]?.body))).toEqual({ sportEnrollmentId: "enrollment-1", subscriptionPlanId: "plan-1", expectedQuoteHash: quote.quoteHash });
  });

  it("keeps the second option when the first quote arrives last", async () => {
    let replyFirst!: (result: Response) => void;
    const second = { ...details.plans[0], id: "plan-2", arabicName: "اشتراك بالحصة", planType: "Sessions", price: 500, sessionCount: 8 };
    vi.mocked(fetch).mockImplementation(async input => {
      const url = String(input);
      if (url.endsWith("/quote?planId=plan-1")) return new Promise(resolve => { replyFirst = resolve; });
      if (url.endsWith("/quote?planId=plan-2")) return Response.json({ ...quote, planId: "plan-2", originalAmount: 500, discountAmount: 0, finalAmount: 500, end: null, sessions: 8 });
      return Response.json({ ...details, plans: [...details.plans, second] });
    });
    openReview();
    const radios = await screen.findAllByRole("radio");
    fireEvent.click(radios[0]);
    fireEvent.click(radios[1]);
    const summary = within(await screen.findByRole("region", { name: "عرض التجديد" }));
    expect(summary.getByText("المبلغ المطلوب").parentElement).toHaveTextContent("500");
    expect(summary.queryByText("نهاية الفترة المتوقعة")).not.toBeInTheDocument();
    expect(summary.getByText("الحصص").parentElement).toHaveTextContent("8");
    await act(async () => replyFirst(Response.json(quote)));
    expect(summary.queryByText(/720/)).not.toBeInTheDocument();
  });

  it("requires a matching quote after a service failure and a new selection after stale pricing", async () => {
    const baseline = vi.mocked(fetch).getMockImplementation()!;
    let reads = 0;
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith("/quote?planId=plan-1")) {
        if (++reads === 1) return Response.json({}, { status: 503 });
        if (reads === 2) return Response.json({ ...quote, enrollmentId: "foreign" });
        return Response.json(quote);
      }
      if (url.endsWith("/admin-renewals") && init?.method === "POST") return Response.json({ message: "تغيّر السعر أو الفترة المتوقعة." }, { status: 409 });
      return baseline(input, init);
    });
    openReview();
    fireEvent.click(await screen.findByRole("radio"));
    for (let attempt = 0; attempt < 2; attempt++) {
      const retry = await screen.findByRole("button", { name: "إعادة قراءة العرض" });
      expect(screen.getByRole("button", { name: "إنشاء طلب التجديد" })).toBeDisabled();
      fireEvent.click(retry);
    }
    await requestRenewal();
    expect(await screen.findByRole("alert")).toHaveTextContent("تغيّر السعر");
    expect(screen.getByRole("radio")).not.toBeChecked();
    expect(screen.getByRole("button", { name: "إنشاء طلب التجديد" })).toBeDisabled();
    expect(push).not.toHaveBeenCalled();
  });
  it("a late unfiltered response never replaces the newer searched player", async () => {
    let initial!: (value: Response) => void, searched!: (value: Response) => void;
    global.fetch = vi.fn(input => new Promise<Response>(resolve => { if (String(input).endsWith("?search=")) initial = resolve; else searched = resolve; }));
    render(<AdminRenewalFlow />);
    fireEvent.change(screen.getByPlaceholderText("اسم اللاعب أو الكود أو هاتف ولي الأمر"), { target: { value: "NEW-1" } });
    await act(async () => searched(response([{ ...row, id: "new-enrollment", player: "لاعب البحث الجديد", playerCode: "NEW-1" }]) as Response));
    expect(await screen.findByText(/لاعب البحث الجديد/)).toBeVisible();
    await act(async () => initial(response([row]) as Response));
    expect(screen.getByText(/لاعب البحث الجديد/)).toBeVisible(); expect(screen.queryByText(/عمر أحمد حسن/)).not.toBeInTheDocument();
  });
  it("searches by player, code, or guardian phone", async () => { render(<AdminRenewalFlow />); expect(await screen.findByPlaceholderText("اسم اللاعب أو الكود أو هاتف ولي الأمر")).toBeVisible(); expect(await screen.findByText(/عمر أحمد حسن/)).toBeVisible(); });
  it("shows enrollment context and Arabic plan values", async () => { openReview(); fireEvent.click(await screen.findByRole("radio")); expect(await screen.findByText("بداية الفترة المتوقعة")).toBeVisible(); expect(screen.getByText("بالمدة", { exact: false })).toBeVisible(); expect(screen.queryByText("Duration")).not.toBeInTheDocument(); expect(screen.queryByText("EGP")).not.toBeInTheDocument(); });
  it("shows that creation has no immediate financial effect", async () => { openReview(); expect(await screen.findByText(/يبدأ التجديد بعد تأكيد الدفع/)).toBeVisible(); });
  it("creates a request and navigates to its payment", async () => { openReview(); fireEvent.click(await screen.findByRole("radio")); await requestRenewal(); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/payments/payment-1")); });
  it("renders server conflicts in Arabic", async () => {
    const read = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation(async (input, init) => String(input).endsWith("/admin-renewals")
      ? Response.json({ message: "يوجد طلب تجديد ودفع جارٍ بالفعل لهذا التسجيل." }, { status: 409 })
      : read(input, init));
    openReview();
    fireEvent.click(await screen.findByRole("radio"));
    await requestRenewal();
    expect(await screen.findByRole("alert")).toHaveTextContent("يوجد طلب تجديد ودفع جارٍ بالفعل");
  });

  it("shows a retry on search failure without claiming there are no matches", async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new TypeError("Network error"));
    render(<AdminRenewalFlow />);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل تسجيلات اللاعبين");
    expect(screen.queryByText(/لا توجد تسجيلات|0 نتيجة/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
    expect(await screen.findByText(row.player)).toBeVisible();
  });

  it("clears the previous player's review on URL changes and shows the new request in progress", async () => {
    const { rerender } = openReview();
    expect(await screen.findByText("الاشتراك الحالي")).toBeVisible();
    const read = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation(async (input, init) => String(input).endsWith("/foreign-enrollment")
      ? Response.json({}, { status: 404 }) : read(input, init));
    query = new URLSearchParams("enrollmentId=foreign-enrollment");
    rerender(<AdminRenewalFlow />);
    expect(screen.getByRole("status")).toHaveTextContent("جارٍ تحميل بيانات التجديد");
    expect(screen.queryByText(row.player, { exact: false })).not.toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent("هذا التسجيل غير متاح للتجديد");
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "العودة إلى البحث" })).toHaveAttribute("href", "/dashboard/subscriptions/renew/new");
  });

  it("reuses the request key after a lost response and never announces a false save", async () => {
    const read = vi.mocked(fetch).getMockImplementation()!;
    const attempts: { key: string | null; body: BodyInit | null | undefined }[] = [];
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      if (!String(input).endsWith("/admin-renewals")) return read(input, init);
      attempts.push({ key: new Headers(init?.headers).get("Idempotency-Key"), body: init?.body });
      if (attempts.length === 1) return Response.json({});
      return Response.json({ paymentId: "payment-1", renewalId: "renewal-1" });
    });
    const notice = vi.fn();
    window.addEventListener("academy-save-success", notice);
    try {
      openReview();
      fireEvent.click(await screen.findByRole("radio"));
      await requestRenewal();
      expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تأكيد إنشاء الطلب");
      expect(screen.getByRole("radio")).toBeDisabled();
      expect(push).not.toHaveBeenCalled();
      fireEvent.click(screen.getByRole("button", { name: "إعادة محاولة الإنشاء" }));
      await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/payments/payment-1"));
      expect(attempts).toHaveLength(2);
      expect(attempts[0].key).toBeTruthy();
      expect(attempts[1]).toEqual(attempts[0]);
      expect(notice).not.toHaveBeenCalled();
    } finally {
      window.removeEventListener("academy-save-success", notice);
    }
  });
  it.each([
    { kind: "Single", paymentId: "payment-1", message: "يوجد طلب تجديد مفتوح لهذا التسجيل." },
    { kind: "Single", paymentId: null, message: "يتابع طلب الدفع موظف لديه صلاحية المدفوعات." },
    { kind: "Family", paymentId: null, message: "هذا التسجيل ضمن طلب تجديد أسرة مفتوح." },
  ])("blocks new quotes for $kind and only shows an authorized payment link", async openRenewal => {
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...details, openRenewal }));
    openReview();
    expect(await screen.findByText(openRenewal.message)).toBeVisible();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "إنشاء طلب التجديد" })).not.toBeInTheDocument();
    const link = screen.queryByRole("link", { name: "فتح طلب الدفع الحالي" });
    if (openRenewal.paymentId) expect(link).toHaveAttribute("href", "/dashboard/subscriptions/payments/payment-1");
    else expect(link).not.toBeInTheDocument();
  });

  it("re-reads the authorized review after a concurrent open renewal conflict", async () => {
    const baseline = vi.mocked(fetch).getMockImplementation()!;
    let raced = false;
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith("/admin-renewals") && init?.method === "POST") {
        raced = true;
        return Response.json({ message: "يوجد طلب تجديد مفتوح لهذا التسجيل." }, { status: 409 });
      }
      if (raced && url.endsWith("/enrollment-1")) return Response.json({ ...details, openRenewal: { kind: "Single", paymentId: "payment-1" } });
      return baseline(input, init);
    });
    openReview();
    fireEvent.click(await screen.findByRole("radio"));
    await requestRenewal();
    expect(await screen.findByRole("link", { name: "فتح طلب الدفع الحالي" })).toHaveAttribute("href", "/dashboard/subscriptions/payments/payment-1");
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it.each([{ sportEnrollmentId: "different-enrollment" }, { amount: 721 }, { currency: "USD" }])("rejects a mismatched payment %j and reads again without re-creating the renewal", async mismatch => {
    const baseline = vi.mocked(fetch).getMockImplementation()!;
    let reads = 0;
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      if (String(input).endsWith("/subscriptions/payments/payment-1") && ++reads === 1) {
        return Response.json({ ...payment, ...mismatch });
      }
      return baseline(input, init);
    });
    openReview();
    fireEvent.click(await screen.findByRole("radio"));
    await requestRenewal();
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر التحقق من مطابقة طلب الدفع");
    expect(push).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "التحقق من طلب الدفع" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/payments/payment-1"));
    expect(reads).toBe(2);
    expect(vi.mocked(fetch).mock.calls.filter(([url, init]) => String(url).endsWith("/admin-renewals") && init?.method === "POST")).toHaveLength(1);
  });

  it("confirms an acknowledged creation without opening a payment outside the staff role", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...details, canReadPayment: false }));
    openReview();
    fireEvent.click(await screen.findByRole("radio"));
    await requestRenewal();
    expect(await screen.findByText("تم إنشاء طلب التجديد.")).toBeVisible();
    expect(push).not.toHaveBeenCalled();
    expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).includes("/subscriptions/payments/"))).toBe(false);
  });
  it("keeps the selected enrollment and does not show all players again", async () => { query = new URLSearchParams("enrollmentId=enrollment-1"); render(<AdminRenewalFlow />); expect(await screen.findByText("مراجعة التجديد")).toBeVisible(); expect(screen.queryByText("اختر تسجيل اللاعب")).not.toBeInTheDocument(); expect(screen.queryByPlaceholderText("اسم اللاعب أو الكود أو هاتف ولي الأمر")).not.toBeInTheDocument(); expect(vi.mocked(fetch).mock.calls.filter(([input]) => String(input).includes("admin-renewals/enrollments"))).toHaveLength(1); });
});
