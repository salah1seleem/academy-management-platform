import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminEnrollmentRequestDetails, AdminEnrollmentRequests, ChildProfile, GuardianHome, GuardianRequestList, NewEnrollmentForm } from "./guardian-core";

const push = vi.fn();
const incoming = new URLSearchParams();
vi.mock("next/navigation", () => ({ usePathname: () => "/guardian", useSearchParams: () => incoming, useRouter: () => ({ push, replace: vi.fn() }) }));

const home = { academyName: "أكاديمية النجوم الرياضية", guardianName: "سارة محمود", today: "2026-09-28", children: [{ id: "omar", arabicName: "عمر أحمد محمود", dateOfBirth: "2016-04-12", sports: [{ id: "football", sportId: "f", sport: "كرة القدم", group: "مجموعة أ", branch: "مدينة نصر", period: { plan: "شهرية", startDate: "2026-09-01", endDate: "2026-10-01", status: "Expiring" } }, { id: "swim", sportId: "s", sport: "السباحة", group: "مجموعة ب", branch: "التجمع", period: undefined }] }, { id: "mariam", arabicName: "مريم أحمد محمود", dateOfBirth: "2018-07-03", sports: [{ id: "m-football", sportId: "f", sport: "كرة القدم", group: "براعم", branch: "الزقازيق", period: { plan: "شهرية", startDate: "2026-08-01", endDate: "2026-08-31", status: "Expired" } }] }], sportContexts: [{ sportId: "f", sport: "كرة القدم", children: ["عمر أحمد محمود", "مريم أحمد محمود"], childCount: 2 }, { sportId: "s", sport: "السباحة", children: ["عمر أحمد محمود"], childCount: 1 }] };
const ok = (value: unknown) => Promise.resolve(new Response(JSON.stringify(value), { status: 200 }));

beforeEach(() => { push.mockReset(); global.fetch = vi.fn(() => ok(home)); });
afterEach(() => { cleanup(); for (const key of [...incoming.keys()]) incoming.delete(key); window.history.replaceState(null, "", "/"); });

describe("Guardian core experience", () => {
  it("keeps the academy date and filter scope in the request and return links", async () => {
    incoming.set("from", "2026-11-13");
    incoming.set("to", "2026-11-13");
    incoming.set("q", "لاعب اصطناعي");
    incoming.set("status", "Pending");
    global.fetch = vi.fn(input => String(input).includes("structure/options") ? ok({ sports: [], branches: [] }) : ok([{
      id: "request", child: "لاعب اصطناعي", guardian: "ولي أمر اصطناعي", sport: "كرة القدم", branch: "فرع العرض",
      status: "Pending", requestType: "NewChild", createdOn: "2026-11-13", createdAtUtc: "2026-11-12T22:00:00Z",
    }]));
    const list = render(<AdminEnrollmentRequests />);
    expect(await screen.findByText("لاعب اصطناعي")).toBeVisible();
    expect(list.container.querySelector("time")).toHaveAttribute("datetime", "2026-11-13");
    const detailUrl = new URL(screen.getByRole("link", { name: "عرض" }).getAttribute("href")!, window.location.origin);
    expect(detailUrl.searchParams.get("q")).toBe("لاعب اصطناعي");
    expect(detailUrl.searchParams.get("from")).toBe("2026-11-13");
    expect(detailUrl.searchParams.get("status")).toBe("Pending");
    list.unmount();
    render(<AdminEnrollmentRequestDetails id="request" />);
    const returnUrl = new URL(screen.getByRole("link", { name: "العودة إلى طلبات الاشتراك" }).getAttribute("href")!, window.location.origin);
    expect(returnUrl.pathname).toBe("/dashboard/enrollment-requests");
    expect(returnUrl.searchParams.get("to")).toBe("2026-11-13");
    expect(returnUrl.searchParams.get("q")).toBe("لاعب اصطناعي");
  });

  it("rejects a reversed period before reading and saves an explicit all-dates reset", async () => {
    incoming.set("from", "2026-11-13");
    incoming.set("to", "2026-11-13");
    global.fetch = vi.fn(input => String(input).includes("structure/options") ? ok({ sports: [], branches: [] }) : ok([]));
    render(<AdminEnrollmentRequests />);
    await screen.findByText("لا توجد طلبات اشتراك مطابقة.");
    vi.mocked(global.fetch).mockClear();
    fireEvent.change(screen.getByLabelText("من تاريخ"), { target: { value: "2026-11-14" } });
    expect(screen.getByRole("alert")).toHaveTextContent("تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه.");
    expect(screen.queryByText("لا توجد طلبات اشتراك مطابقة.")).not.toBeInTheDocument();
    expect(global.fetch).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "إعادة الضبط" }));
    await screen.findByText("لا توجد طلبات اشتراك مطابقة.");
    expect(screen.getByLabelText("من تاريخ")).toHaveValue("");
    expect(new URLSearchParams(window.location.search).get("allDates")).toBe("true");
    expect(global.fetch).toHaveBeenCalledWith("/api/v1/enrollment-requests?", expect.anything());
  });
  it("filters enrollment requests by workflow states, not subscription states", async () => {
    global.fetch = vi.fn((input) => String(input).includes("structure/options") ? ok({ sports: [], branches: [] }) : ok([]));
    render(<AdminEnrollmentRequests />);
    const filter = await screen.findByRole("combobox", { name: "حالة الطلب" });
    expect(within(filter).getAllByRole("option").map(x => x.getAttribute("value"))).toEqual(["", "Pending", "UnderReview", "Approved", "Rejected", "Cancelled"]);
    fireEvent.change(filter, { target: { value: "UnderReview" } });
    await waitFor(() => expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("status=UnderReview"), expect.objectContaining({ signal: expect.any(AbortSignal) })));
  });
  it("keeps the results of the latest enrollment filters when an older read arrives later", async () => {
    let finishOld!: (value: Response) => void;
    const old = new Promise<Response>(resolve => { finishOld = resolve; });
    global.fetch = vi.fn(input => {
      const url = String(input);
      if (url.includes("structure/options")) return ok({ sports: [], branches: [] });
      if (url.includes("status=Pending")) return old;
      if (url.includes("status=Rejected")) return ok([{
        id: "latest", child: "نتيجة الفلتر الأخير", guardian: "ولي أمر العرض", sport: "السباحة", branch: "فرع العرض",
        status: "Rejected", requestType: "NewChild", createdAtUtc: "2026-11-09T10:00:00Z",
      }]);
      return ok([]);
    });
    render(<AdminEnrollmentRequests />);
    const filter = await screen.findByRole("combobox", { name: "حالة الطلب" });
    fireEvent.change(filter, { target: { value: "Pending" } });
    fireEvent.change(filter, { target: { value: "Rejected" } });
    expect(await screen.findByText("نتيجة الفلتر الأخير")).toBeVisible();
    await act(async () => { finishOld(Response.json([])); });
    expect(screen.getByText("نتيجة الفلتر الأخير")).toBeVisible();
  });
  it("retries the last filter without reporting an empty result on a failed read", async () => {
    let failed = false;
    global.fetch = vi.fn(input => {
      const url = String(input);
      if (url.includes("structure/options")) return ok({ sports: [], branches: [] });
      if (url.includes("status=Rejected") && !failed) {
        failed = true;
        return Promise.resolve(new Response(null, { status: 503 }));
      }
      return ok([]);
    });
    render(<AdminEnrollmentRequests />);
    fireEvent.change(screen.getByRole("combobox", { name: "حالة الطلب" }), { target: { value: "Rejected" } });
    const retry = await screen.findByRole("button", { name: "إعادة تحميل الطلبات" });
    expect(screen.getByRole("alert")).toHaveTextContent("تعذر تحميل طلبات الاشتراك.");
    expect(screen.queryByText("0 نتيجة")).not.toBeInTheDocument();
    expect(screen.queryByText("لا توجد طلبات اشتراك مطابقة.")).not.toBeInTheDocument();
    fireEvent.click(retry);
    expect(await screen.findByText("لا توجد طلبات اشتراك مطابقة.")).toBeVisible();
  });
  it("shows the three primary actions", async () => { render(<GuardianHome />); const nav = await screen.findByRole("navigation", { name: "إجراءات الاشتراك" }); expect(within(nav).getByRole("link", { name: /اشتراك جديد/ })).toBeVisible(); expect(within(nav).getByRole("link", { name: /تجديد الاشتراك$/ })).toBeVisible(); expect(within(nav).getByRole("link", { name: /تجديد اشتراك لغيره/ })).toBeVisible(); });
  it("renders one child card with multiple sport badges", async () => { render(<GuardianHome />); const heading = await screen.findByRole("heading", { name: "عمر أحمد محمود" }); const card = heading.closest("article")!; expect(within(card).getAllByText("كرة القدم").length).toBeGreaterThan(0); expect(within(card).getAllByText("السباحة").length).toBeGreaterThan(0); expect(screen.getAllByRole("heading", { name: "عمر أحمد محمود" })).toHaveLength(1); });
  it("deduplicates sport context across children", async () => { render(<GuardianHome />); const context = (await screen.findByRole("heading", { name: "رياضات الأبناء" })).closest("section")!; expect(within(context).getAllByRole("heading", { name: "كرة القدم" })).toHaveLength(1); expect(within(context).getAllByRole("heading", { name: "السباحة" })).toHaveLength(1); });
  it("shows expiring expired and no-subscription states with working renewal links", async () => { render(<GuardianHome />); expect(await screen.findByText("الاشتراك ينتهي قريبًا")).toBeVisible(); expect(screen.getByText("الاشتراك منتهي")).toBeVisible(); expect(screen.getByText("لم يتم تفعيل اشتراك بعد")).toBeVisible(); expect(screen.getAllByRole("link", { name: /تجديد الآن|اختيار باقة/ }).length).toBeGreaterThanOrEqual(3); });
  it("offers existing-child and new-child choices without group selection", async () => { global.fetch = vi.fn(() => ok({ children: [{ id: "omar", name: "عمر" }], sports: [{ id: "s", name: "السباحة" }], branches: [{ id: "b", name: "التجمع" }] })); render(<NewEnrollmentForm />); fireEvent.click(screen.getByRole("button", { name: /إضافة رياضة لطفل موجود/ })); expect(await screen.findByLabelText("الطفل")).toBeVisible(); expect(screen.queryByLabelText(/المجموعة/)).not.toBeInTheDocument(); });
  it("shows minimal new child fields", async () => { global.fetch = vi.fn(() => ok({ children: [], sports: [{ id: "s", name: "السباحة" }], branches: [{ id: "b", name: "التجمع" }] })); render(<NewEnrollmentForm />); fireEvent.click(screen.getByRole("button", { name: /إضافة طفل جديد/ })); expect(await screen.findByLabelText("اسم الطفل")).toBeVisible(); expect(screen.getByLabelText("تاريخ الميلاد")).toBeVisible(); expect(screen.getByLabelText(/النوع/)).toBeVisible(); });
  it("tracks request statuses and exposes approved next action", async () => { global.fetch = vi.fn(() => ok([{ id: "r1", child: "عمر", sport: "السباحة", branch: "التجمع", status: "Approved", createdAtUtc: "2026-09-28T00:00:00Z", createdSportEnrollmentId: "e1" }, { id: "r2", child: "مريم", sport: "كرة القدم", branch: "مدينة نصر", status: "Rejected", guardianVisibleReason: "لا توجد مجموعة مناسبة", createdAtUtc: "2026-09-28T00:00:00Z" }])); render(<GuardianRequestList />); expect(await screen.findByText("مقبول")).toBeVisible(); expect(screen.getByText("مرفوض")).toBeVisible(); expect(screen.getByText("لا توجد مجموعة مناسبة")).toBeVisible(); expect(screen.getByRole("link", { name: "إكمال الاشتراك / اختيار الباقة" })).toHaveAttribute("href", "/guardian/renew/e1"); });
  it("integrates the five required content areas plus attendance and subscriptions", async () => { global.fetch = vi.fn(() => ok({ player: { id: "omar", arabicName: "عمر", dateOfBirth: "2016-01-01" }, enrollments: [{ id: "e1", sportId: "f", sport: "كرة القدم", group: "مجموعة أ", branch: "مدينة نصر", recurring: [{ dayOfWeek: "Sunday", startTime: "18:00:00", endTime: "19:30:00" }], upcoming: [], period: { plan: "شهرية", startDate: "2026-09-01", endDate: "2026-10-01", status: "Active" } }], attendance: [{ id: "a1", sessionDate: "2026-09-27", sport: "كرة القدم", group: "مجموعة أ", status: "Present" }], attendanceSummary: { present: 1, absent: 0 }, evaluations: [{ id: "v1", sport: "كرة القدم", evaluationDate: "2026-09-20", reportingPeriod: "شهري" }] })); render(<ChildProfile playerId="omar" />); expect(await screen.findByRole("heading", { name: "تقرير اللاعب" })).toBeVisible(); expect(screen.getByRole("heading", { name: "التدريب والمواعيد" })).toBeVisible(); expect(screen.getByRole("link", { name: /التغذية والصحة/ })).toBeVisible(); expect(screen.getByRole("link", { name: /الإصابات والاستشارات الطبية/ })).toBeVisible(); expect(screen.getByRole("link", { name: /الصور والفيديوهات/ })).toBeVisible(); expect(screen.getByRole("heading", { name: "الحضور" })).toBeVisible(); expect(screen.getByRole("heading", { name: "الاشتراكات" })).toBeVisible(); expect(screen.getByRole("link", { name: "عرض التقرير" })).toHaveAttribute("href", "/guardian/evaluations/v1?child=omar"); });
  it("labels an exact booked session, then refreshes the same child's released booking", async () => {
    const slot = { id: "makeup", group: "المجموعة البديلة", sessionDate: "2026-11-13", startTime: "17:00:00", endTime: "18:00:00" };
    const enrollment = { id: "e1", sportId: "swim", sport: "السباحة", group: "مجموعة أ", branch: "فرع العرض", recurring: [], upcoming: [slot] };
    const booking = { id: "b1", trainingSessionId: slot.id, group: slot.group, date: slot.sessionDate, startTime: slot.startTime, endTime: slot.endTime, units: 1, consumed: 0, state: "Reserved", version: 1 };
    const grant = { id: "g1", sportEnrollmentId: enrollment.id, sport: enrollment.sport, cause: "CancelledSession", unit: "Session", units: 1, consumed: 0, allocated: 1, validFrom: "2026-11-01", validTo: "2026-12-01", sourceDate: "2026-10-25", bookings: [booking] };
    const profile = { player: { id: "omar", arabicName: "لاعب العرض", dateOfBirth: "2016-01-01" }, trainingToday: "2026-11-08", enrollments: [enrollment], compensations: [grant], attendance: [], attendanceSummary: { present: 0, absent: 0 }, evaluations: [] };
    let released = false;
    global.fetch = vi.fn(() => ok(released ? { ...profile, enrollments: [{ ...enrollment, upcoming: [] }], compensations: [{ ...grant, allocated: 0, bookings: [{ ...booking, state: "Cancelled" }] }] } : profile));
    render(<ChildProfile playerId="omar" />);
    const schedule = await screen.findByRole("region", { name: "التدريب والمواعيد" });
    expect(within(schedule).getByText("حصة تعويضية")).toBeVisible();
    expect(within(screen.getByLabelText("التعويضات المعتمدة")).getByText("من 5:00 مساءً إلى 6:00 مساءً")).toBeVisible();
    released = true;
    fireEvent.click(within(schedule).getByRole("button", { name: "تحديث المواعيد" }));
    expect(await screen.findByText("أُلغي الحجز")).toBeVisible();
    expect(within(schedule).queryByText("حصة تعويضية")).not.toBeInTheDocument();
    expect(screen.getByText("متاح للحجز حصة واحدة")).toBeVisible();
  });
  it("links the current and future periods to the same child without a duplicate upcoming period", async () => {
    const current = { id: "current", plan: "باقة السباحة", startDate: "2026-11-01", endDate: "2026-11-30", remainingSessions: 5, status: "Active" };
    const next = { id: "next", startDate: "2026-12-01" };
    const profile = { player: { id: "omar", arabicName: "لاعب العرض", dateOfBirth: "2016-01-01" },
      enrollments: [
        { id: "swim", sportId: "s", sport: "السباحة", period: current, nextPeriod: next, recurring: [], upcoming: [] },
        { id: "basket", sportId: "b", sport: "كرة السلة", period: { ...current, ...next, id: "basket-next", status: "Scheduled" },
          nextPeriod: { ...next, id: "basket-next" }, recurring: [], upcoming: [] },
        { id: "football", sportId: "f", sport: "كرة القدم", recurring: [], upcoming: [] },
      ],
      attendance: [], attendanceSummary: { present: 0, absent: 0 }, evaluations: [] };
    global.fetch = vi.fn(() => ok(profile));
    render(<ChildProfile playerId="omar" />);
    const swim = within(await screen.findByRole("article", { name: "السباحة" }));
    expect(swim.getByText(/الحصص المتبقية 5/)).toBeVisible();
    expect(swim.getByRole("link", { name: "تفاصيل حالة الاشتراك" })).toHaveAttribute("href", "/guardian/subscriptions/periods/current?child=omar");
    expect(swim.getByRole("link", { name: "تفاصيل الفترة التالية" })).toHaveAttribute("href", "/guardian/subscriptions/periods/next?child=omar");
    expect(within(screen.getByRole("article", { name: "كرة السلة" })).queryByText("الفترة التالية")).not.toBeInTheDocument();
    expect(within(screen.getByRole("article", { name: "كرة القدم" })).getByRole("link", { name: "اختيار باقة والاشتراك" })).toHaveAttribute("href", "/guardian/renew/football");
  });
  it("focuses the schedule after the profile response populates a deep link", async () => {
    const originalUrl = window.location.href;
    window.history.replaceState(null, "", "#schedule");
    global.fetch = vi.fn(() => ok({ player: { id: "omar", arabicName: "لاعب العرض" }, enrollments: [], attendance: [], attendanceSummary: { present: 0, absent: 0 }, evaluations: [] }));
    try {
      render(<ChildProfile playerId="omar" />);
      expect(await screen.findByRole("region", { name: "التدريب والمواعيد" })).toHaveFocus();
    } finally {
      window.history.replaceState(null, "", originalUrl);
    }
  });
  it("distinguishes expired credit and past bookings without interpreting absence", async () => {
    const profile = { player: { id: "omar", arabicName: "لاعب العرض", dateOfBirth: "2016-01-01" }, trainingToday: "2026-11-08", enrollments: [], attendance: [], attendanceSummary: { present: 0, absent: 0 }, evaluations: [] };
    const grant = { id: "g1", sport: "السباحة", cause: "CancelledSession", unit: "Minute", units: 120, consumed: 0, allocated: 60, validFrom: "2026-10-01", validTo: "2026-11-01", sourceDate: "2026-09-25", bookings: [{ id: "b1", group: "مجموعة أ", date: "2026-10-30", units: 60, consumed: 0, state: "Reserved", version: 1 }] };
    global.fetch = vi.fn(() => ok({ ...profile, compensations: [grant] }));
    render(<ChildProfile playerId="omar" />);
    expect(await screen.findByText("انتهت صلاحية الرصيد")).toBeVisible();
    expect(screen.getByText("انتهى الموعد")).toBeVisible();
    expect(screen.queryByText(/متاح للحجز/)).not.toBeInTheDocument();
  });
  it("renders an empty request state", async () => { global.fetch = vi.fn(() => ok([])); render(<GuardianRequestList />); expect(await screen.findByText("لا توجد طلبات اشتراك حتى الآن.")).toBeVisible(); });
  it("renders a useful loading state", () => { global.fetch = vi.fn(() => new Promise<Response>(() => undefined)); render(<GuardianHome />); expect(screen.getByText("جارٍ تحميل البيانات…")).toBeVisible(); });
  it("renders an Arabic error state", async () => { global.fetch = vi.fn(async () => new Response(null, { status: 500 })); render(<GuardianHome />); await waitFor(() => expect(screen.getByText("تعذر تحميل الصفحة الرئيسية.")).toBeVisible()); });
  it("offers the required admin request filters", async () => { global.fetch = vi.fn((input) => String(input).includes("structure/options") ? ok({ sports: [{ id: "s", arabicName: "السباحة" }], branches: [{ id: "b", arabicName: "التجمع" }] }) : ok([])); render(<AdminEnrollmentRequests />); expect(await screen.findByLabelText("حالة الطلب")).toBeVisible(); expect(screen.getByLabelText("رياضة الطلب")).toBeVisible(); expect(screen.getByLabelText("فرع الطلب")).toBeVisible(); expect(screen.getByLabelText("من تاريخ")).toBeVisible(); expect(screen.getByLabelText("إلى تاريخ")).toBeVisible(); });
  it("keeps enrollment decisions out of a read-only administrator's workspace", async () => {
    global.fetch = vi.fn(input => String(input).endsWith("/payment")
      ? ok({ id: "r1", status: "Pending" })
      : ok({ canManage: false, request: { id: "r1", child: "لاعب العرض", status: "Pending" }, groups: [], playerCandidates: [] }));
    render(<AdminEnrollmentRequestDetails id="r1" />);
    expect(await screen.findByText("لديك صلاحية عرض الطلب فقط.")).toBeVisible();
    expect(screen.getByText("قيد الانتظار")).toBeVisible();
    expect(screen.queryByRole("button", { name: "بدء المراجعة" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
  });
  it("requires payment data and a proof link for the same enrollment request", async () => {
    const proofAssetId = "ef501775-16fc-4e34-aac2-aa45c1377721";
    const proofUrl = `/api/v1/assets/${proofAssetId}`;
    let evidence = { id: "another-request", status: "Pending", proofAssetId, proofUrl };
    global.fetch = vi.fn(input => String(input).endsWith("/payment")
      ? ok(evidence)
      : ok({ request: { id: "r1", child: "لاعب العرض", status: "Pending", requiresPaymentProof: true }, groups: [], playerCandidates: [] }));
    render(<AdminEnrollmentRequestDetails id="r1" />);
    const retry = await screen.findByRole("button", { name: "التحقق من حالة الطلب" });
    expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
    evidence = { ...evidence, id: "r1", proofUrl: "/api/v1/assets/ffffffff-ffff-ffff-ffff-ffffffffffff" };
    fireEvent.click(retry);
    await screen.findByRole("button", { name: "التحقق من حالة الطلب" });
    expect(screen.queryByRole("link", { name: "عرض إثبات الدفع" })).not.toBeInTheDocument();
    evidence = { ...evidence, status: "Approved", proofUrl };
    fireEvent.click(screen.getByRole("button", { name: "التحقق من حالة الطلب" }));
    expect(await screen.findByText(/تغيّرت حالة الطلب أثناء القراءة/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
    evidence = { ...evidence, status: "Pending" };
    fireEvent.click(screen.getByRole("button", { name: "التحقق من حالة الطلب" }));
    expect(await screen.findByRole("link", { name: "عرض إثبات الدفع" })).toHaveAttribute("href", proofUrl);
    expect(screen.getByRole("button", { name: "رفض الطلب" })).toBeEnabled();
  });
  it("shows only explicit safe player matches and keeps rejection reason optional", async () => { global.fetch = vi.fn(input => String(input).endsWith("/payment") ? ok({ id: "r1", status: "UnderReview" }) : ok({ request: { id: "r1", requestType: "NewChild", child: "ليلى علي", guardian: "سارة", sport: "السباحة", branch: "التجمع", status: "UnderReview" }, groups: [{ id: "g", name: "ناشئات السباحة" }], playerCandidates: [{ id: "p", name: "ليلى علي", playerCode: "PLR-100", dateOfBirth: "2018-02-03" }] })); render(<AdminEnrollmentRequestDetails id="r1" />); expect(await screen.findByLabelText(/مطابقة لاعب موجود/)).toHaveTextContent("PLR-100"); expect(screen.getByLabelText("المجموعة")).toBeVisible(); expect(screen.getByLabelText(/سبب الرفض.*اختياري/)).not.toBeRequired(); expect(screen.getByRole("button", { name: "رفض الطلب" })).toBeEnabled(); });
  it.each(["EGP", undefined])("requires a decoded proof and verified money before acceptance (currency %s)", async currency => {
    const proofAssetId = "ef501775-16fc-4e34-aac2-aa45c1377721";
    global.fetch = vi.fn(input => String(input).endsWith("/payment")
      ? ok({ id: "r1", status: "Pending", amount: 720, currency, proofAssetId, proofUrl: `/api/v1/assets/${proofAssetId}` })
      : ok({ request: { id: "r1", child: "لاعب اختبار", status: "Pending", requiresPaymentProof: true }, groups: [{ id: "group", name: "مجموعة العرض" }], playerCandidates: [] }));
    const view = render(<AdminEnrollmentRequestDetails id="r1" />);
    fireEvent.change(await screen.findByLabelText("المجموعة"), { target: { value: "group" } });
    const accept = screen.getByRole("button", { name: "قبول وإنشاء التسجيل" });
    expect(accept).toBeDisabled();
    fireEvent.error(view.container.querySelector("img[alt='صورة إثبات دفع الاشتراك']")!);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر فتح صورة إثبات الدفع");
    expect(accept).toBeDisabled();
    expect(screen.getByRole("button", { name: "رفض الطلب" })).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الصورة" }));
    const image = view.container.querySelector("img[alt='صورة إثبات دفع الاشتراك']")!;
    Object.defineProperties(image, { naturalWidth: { value: 24 }, complete: { value: true }, decode: { value: async () => undefined } });
    fireEvent.load(image);
    expect(await screen.findByRole("img", { name: "صورة إثبات دفع الاشتراك" })).toBeVisible();
    if (currency) {
      expect(accept).toBeEnabled();
      fireEvent.click(accept);
      expect(await screen.findByRole("dialog")).toHaveTextContent("720 جنيه");
    } else {
      expect(accept).toBeDisabled();
      expect(screen.getByText("تعذر التحقق من مبلغ الطلب وعملته.")).toBeVisible();
    }
  });
  it.each([false, true])("allows placement without payment only when the request has no plan (%s)", async requiresPaymentProof => {
    global.fetch = vi.fn(input => String(input).endsWith("/payment")
      ? ok({ id: "r1", status: "UnderReview" })
      : ok({ request: { id: "r1", child: "لاعب اختبار", status: "UnderReview", requiresPaymentProof }, groups: [{ id: "group", name: "مجموعة العرض" }], playerCandidates: [] }));
    render(<AdminEnrollmentRequestDetails id="r1" />);
    fireEvent.change(await screen.findByLabelText("المجموعة"), { target: { value: "group" } });
    const approve = screen.getByRole("button", { name: "قبول وإنشاء التسجيل" });
    if (requiresPaymentProof) {
      expect(approve).toBeDisabled();
      expect(screen.getByText("لم يصل إثبات الدفع بعد.")).toBeVisible();
    } else {
      expect(approve).toBeEnabled();
      fireEvent.click(approve);
      expect(await screen.findByRole("dialog")).toHaveTextContent("مجموعة العرض");
      expect(screen.getByRole("dialog")).not.toHaveTextContent("تحصيل");
    }
  });
});
