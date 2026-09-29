import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { GuardianSubscriptionStatus, PlanList, SubscriptionPeriodDetail } from "./subscriptions";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "p1", arabicName: "اشتراك شهري", sport: "كرة القدم", sportId: "s1", planType: "Duration", price: 900, currency: "EGP", durationDays: 30, isActive: true }] }); });
afterEach(() => cleanup());

describe("subscription plan list", () => {
  it("renders list columns, add action, and only functional row actions", async () => {
    render(<PlanList />);
    expect(screen.getByRole("link", { name: "إضافة باقة" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/new");
    expect(await screen.findByText("اشتراك شهري")).toBeVisible();
    expect(screen.getByText(/كرة القدم · Duration · 900 EGP · 30 يوم/)).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/p1");
    expect(screen.getByRole("link", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/subscriptions/plans/p1/edit");
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "غير موجود" } });
    expect(screen.queryByText("اشتراك شهري")).not.toBeInTheDocument();
  });
});

const basePeriod = {
  id: "period-1", player: "عمر أحمد", playerCode: "NG-0001", sport: "كرة القدم", branch: "مدينة نصر", group: "مجموعة أ",
  plan: "اشتراك شهري", planType: "Duration", startDate: "2026-09-01", endDate: "2026-10-31", initialSessions: null,
  remainingSessions: null, status: "Active", version: 7, priceSnapshot: 900, currencySnapshot: "EGP", receiptId: "receipt-1", adjustments: [],
};
const response = (data: unknown, ok = true) => ({ ok, json: async () => data });
function initial(data: object = basePeriod) { global.fetch = vi.fn().mockResolvedValue(response(data)) as typeof fetch; }
async function renderDetail(data: object = basePeriod) { initial(data); render(<SubscriptionPeriodDetail id="period-1" />); await screen.findByText("عمر أحمد · NG-0001"); }

describe("subscription period adjustments", () => {
  it("shows only valid actions for an active duration period", async () => {
    await renderDetail();
    expect(screen.getByRole("button", { name: "تجميد" })).toBeVisible(); expect(screen.getByRole("button", { name: "إضافة أيام" })).toBeVisible(); expect(screen.getByRole("button", { name: "خصم أيام" })).toBeVisible(); expect(screen.getByRole("button", { name: "إلغاء الاشتراك" })).toBeVisible(); expect(screen.queryByRole("button", { name: "استئناف" })).not.toBeInTheDocument();
  });

  it("shows Resume only for a frozen period while keeping timed adjustments", async () => {
    await renderDetail({ ...basePeriod, status: "Frozen", frozenFromDate: "2026-09-25" });
    expect(screen.getByRole("button", { name: "استئناف" })).toBeVisible(); expect(screen.queryByRole("button", { name: "تجميد" })).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "إضافة أيام" })).toBeVisible();
  });

  it("hides freeze and day actions for sessions-only plans", async () => {
    await renderDetail({ ...basePeriod, planType: "Sessions", endDate: undefined, remainingSessions: 8 });
    expect(screen.queryByRole("button", { name: "تجميد" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "خصم أيام" })).not.toBeInTheDocument(); expect(screen.getByText(/الباقة بالحصة فقط/)).toBeVisible();
  });

  it("shows no mutation action for a cancelled period", async () => {
    await renderDetail({ ...basePeriod, status: "Cancelled" });
    expect(screen.queryByRole("button", { name: "تجميد" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "خصم أيام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "إلغاء الاشتراك" })).not.toBeInTheDocument();
  });

  it("requires a reason and bounded days in the day form", async () => {
    await renderDetail(); fireEvent.click(screen.getByRole("button", { name: "إضافة أيام" }));
    expect(screen.getByLabelText("سبب التعديل")).toBeRequired(); expect(screen.getByLabelText("سبب التعديل")).toHaveAttribute("maxlength", "500"); expect(screen.getByLabelText("عدد الأيام")).toHaveAttribute("min", "1"); expect(screen.getByLabelText("عدد الأيام")).toHaveAttribute("max", "365");
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
    expect(screen.getByText((_, element) => element?.classList.contains("adjustment-preview") ?? false)).toHaveTextContent("2026-11-03 — الحساب النهائي من الخادم");
  });

  it("renders adjusted end date and immutable audit history", async () => {
    await renderDetail({ ...basePeriod, endDate: "2026-11-05", adjustments: [{ id: "a1", type: "DaysAdded", effectiveDate: "2026-09-28", daysDelta: 5, oldEndDate: "2026-10-31", newEndDate: "2026-11-05", reason: "تعويض", performedBy: "منى السيد", performedAtUtc: "2026-09-28T10:00:00Z" }] });
    expect(screen.getByText(/2026-09-01 — 2026-11-05/)).toBeVisible(); expect(screen.getAllByText("إضافة أيام")).toHaveLength(2); expect(screen.getByText("تعويض")).toBeVisible(); expect(screen.getByText("منى السيد", { exact: false })).toBeVisible();
  });

  it("sends only command fields and reports a stale conflict", async () => {
    global.fetch = vi.fn().mockResolvedValueOnce(response(basePeriod)).mockResolvedValueOnce(response({ token: "csrf" })).mockResolvedValueOnce(response({ message: "تغير الاشتراك منذ فتح الصفحة." }, false)) as typeof fetch;
    render(<SubscriptionPeriodDetail id="period-1" />); await screen.findByText("عمر أحمد · NG-0001"); fireEvent.click(screen.getByRole("button", { name: "إضافة أيام" })); fireEvent.change(screen.getByLabelText("عدد الأيام"), { target: { value: "5" } }); fireEvent.change(screen.getByLabelText("سبب التعديل"), { target: { value: "تعويض" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("تغير الاشتراك منذ فتح الصفحة"); const call = vi.mocked(fetch).mock.calls[2]; const body = JSON.parse(String((call[1] as RequestInit).body)); expect(body).toEqual({ direction: 1, days: 5, reason: "تعويض", expectedVersion: 7 }); expect(body).not.toHaveProperty("newEndDate"); expect(body).not.toHaveProperty("status");
  });

  it("shows loading and load failure states", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("offline")) as typeof fetch; render(<SubscriptionPeriodDetail id="period-1" />); expect(screen.getByRole("status")).toBeVisible(); expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل تفاصيل الاشتراك");
  });
});

describe("guardian subscription adjustment visibility", () => {
  it("shows a safe frozen state and simple history", async () => {
    initial({ id: "period-1", plan: "اشتراك شهري", sport: "كرة القدم", startDate: "2026-09-01", endDate: "2026-10-31", frozenFromDate: "2026-09-25", status: "Frozen", adjustments: [{ type: "FreezeStarted", effectiveDate: "2026-09-25" }] }); render(<GuardianSubscriptionStatus id="period-1" />);
    expect(await screen.findByText("مجمد")).toBeVisible(); expect(screen.getByText("الاشتراك مجمد.")).toBeVisible(); expect(screen.queryByText("سبب داخلي")).not.toBeInTheDocument();
  });

  it("shows cancelled state without refund or reversal controls", async () => {
    initial({ id: "period-1", plan: "اشتراك شهري", sport: "كرة القدم", startDate: "2026-09-01", endDate: "2026-10-31", status: "Cancelled", adjustments: [{ type: "Cancelled", effectiveDate: "2026-09-28" }] }); render(<GuardianSubscriptionStatus id="period-1" />);
    expect(await screen.findByText("ملغي")).toBeVisible(); expect(screen.getByText("الاشتراك ملغي.")).toBeVisible(); expect(screen.queryByRole("button")).not.toBeInTheDocument(); expect(screen.queryByText(/Refund|استرداد|رد الأموال/i)).not.toBeInTheDocument();
  });

  it("reports guardian load errors", async () => {
    global.fetch = vi.fn().mockResolvedValue(response({}, false)) as typeof fetch; render(<GuardianSubscriptionStatus id="missing" />); await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("تعذر تحميل حالة الاشتراك"));
  });
});
