"use client";
import { PersonPortrait } from "../components/person-portrait";
import { EnrollmentTransfers, type EnrollmentTransfer } from "./enrollment-transfers";
import { CompensationCards, type Compensation } from "./player-compensation";

import Link from "next/link";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { enrollmentReceiptQuery, paymentQueueBack } from "../lib/payment-queue-navigation";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { trainingFormatName } from "./group-training-format";
import { DailyNutritionSummary, GuardianSportCatalog } from "./guardian-content";
import { Bell, CalendarDays, Gift, MessageCircle, RefreshCw, Star, Trophy, UserPlus, Utensils } from "lucide-react";
import { AcademyActivityPreview } from "./academy-activities";
import styles from "./guardian-home.module.css";
import enrollmentStyles from "./enrollment-request-filters.module.css";
import { formatDateAr, formatMoneyAr, formatSessionDateAr, formatTimeAr, statusLabel, workflowStatusLabels } from "../lib/formatters";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { GuardianSponsoredCard, GuardianSponsorSettingsLink } from "./guardian-sponsored";
import { webAuthRequest } from "../lib/web-auth-request";
import { PaymentProofPreview } from "../components/payment-proof-preview";

const statusLabels = workflowStatusLabels;
const weekdayLabels: Record<string, string> = { Sunday: "الأحد", Monday: "الاثنين", Tuesday: "الثلاثاء", Wednesday: "الأربعاء", Thursday: "الخميس", Friday: "الجمعة", Saturday: "السبت" };
const childCountRules = new Intl.PluralRules("ar-EG");
type Period = { id: string; plan: string; startDate: string; endDate?: string; frozenFromDate?: string; remainingSessions?: number; status: string };
type Session = { id: string; sessionDate: string; startTime: string };
type UpcomingMatch = { id: string; matchDate: string; startTime: string; isUpcoming: boolean; children: string[] };
type SportEnrollment = { nextSession?: Session; coaches?: { id: string; name: string; groupId: string }[]; registrationFormat?: string; id: string; sportId: string; sport: string; group: string; branch: string; period?: Period };
type Child = { id: string; arabicName: string; dateOfBirth: string; gender?: number; photoReference?: string; sports: SportEnrollment[] };
type Home = { academyName: string; guardianName: string; today: string; trainingToday?: string; urgentAlerts?: {id:string;message:string}[]; children: Child[]; sportContexts: { sportId: string; sport: string; children: string[]; childCount: number }[] };

function age(birth: string, today: string) { const b = new Date(`${birth}T00:00:00Z`); const t = new Date(`${today}T00:00:00Z`); let years = t.getUTCFullYear() - b.getUTCFullYear(); if (t.getUTCMonth() < b.getUTCMonth() || (t.getUTCMonth() === b.getUTCMonth() && t.getUTCDate() < b.getUTCDate())) years--; return years; }
function subscriptionText(period?: Period) { if (!period) return "لم يتم تفعيل اشتراك بعد"; if (period.status === "Frozen") return "الاشتراك مجمد"; if (period.status === "Cancelled") return "الاشتراك ملغي"; if (period.status === "Expiring") return "الاشتراك ينتهي قريبًا"; if (period.status === "Expired") return "الاشتراك منتهي"; return `${period.plan} · ${statusLabel(period.status)}`; }

function appointment(date: string, time: string) {
  const shortDate = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { day: "numeric", month: "short", timeZone: "UTC" });
  return `${shortDate.format(new Date(`${date}T00:00:00Z`))} · ${formatTimeAr(time)}`;
}

function childCountLabel(count: number) {
  switch (childCountRules.select(count)) {
    case "zero": return null;
    case "one": return "ملف واحد";
    case "two": return "ملفان";
    case "few": return `${count} ملفات`;
    case "many": return `${count} ملفًا`;
    default: return `${count} ملف`;
  }
}

export function GuardianHome() {
  const router = useRouter();
  const [data, setData] = useState<Home | null>(null);
  const [matches, setMatches] = useState<UpcomingMatch[]>([]);
  const [error, setError] = useState("");
  const [feedback, setFeedback] = useState<"Rating" | "Complaint">();
  const [logoutBusy, setLogoutBusy] = useState(false);
  const [logoutError, setLogoutError] = useState("");

  useEffect(() => {
    const request = new AbortController();
    fetch("/api/v1/guardian/home", { signal: request.signal, cache: "no-store" }).then(async response => {
      if (response.status === 401 || response.status === 403) return router.replace("/guardian/sign-in");
      if (!response.ok) throw new Error();
      setData(await response.json() as Home);
    }).catch(() => { if (!request.signal.aborted) setError("تعذر تحميل الصفحة الرئيسية."); });
    fetch("/api/v1/my-matches?upcomingOnly=true", { signal: request.signal, cache: "no-store" }).then(async response => {
      if (!response.ok) return;
      const items: unknown = await response.json();
      if (Array.isArray(items)) setMatches(items.filter(item => item.isUpcoming));
    }).catch(() => {});
    return () => request.abort();
  }, [router]);

  async function logout() {
    if (logoutBusy) return;
    setLogoutBusy(true);
    setLogoutError("");
    try {
      const response = await webAuthRequest("/api/v1/auth", "logout");
      if (!response.ok && response.status !== 401) throw new Error("تعذر تسجيل الخروج. حاول مرة أخرى.");
      window.location.replace("/guardian/sign-in");
    } catch (caught) {
      setLogoutError(caught instanceof Error ? caught.message : "تعذر تسجيل الخروج.");
      setLogoutBusy(false);
    }
  }

  if (error) return <GuardianShell><ErrorState message={error} /></GuardianShell>;
  if (!data) return <GuardianShell><LoadingState /></GuardianShell>;

  const nutritionDate = data.trainingToday ?? data.today;
  const childrenLabel = childCountLabel(data.children.length);
  const feedbackHref = (playerId: string, kind: "Rating" | "Complaint") =>
    `/guardian/children/${playerId}/feedback?kind=${kind}`;

  return <GuardianShell>
    <header className="guardian-hero">
      <div className={styles.hero}>
        <div><span>مرحبًا {data.guardianName}</span><h1>{data.academyName}</h1></div>
        <Link className={styles.bell} href="/guardian/notifications" aria-label="التنبيهات"><Bell size={22} /></Link>
      </div>
    </header>
    {(data.urgentAlerts ?? []).map(alert => <aside className="guardian-urgent-alert" key={alert.id} role="alert">
      <strong>تحديث عاجل يخص المواعيد</strong><p>{alert.message}</p>
    </aside>)}
    <section id="children" className="guardian-section">
      <div className="guardian-section-title"><h2>الأبناء</h2>{childrenLabel && <span>{childrenLabel}</span>}</div>
      {data.children.length === 0 ? <EmptyState message="لا يوجد أبناء مرتبطون بهذا الحساب." />
        : <div className="guardian-child-grid">{data.children.map(child => {
          const childHref = `/guardian/children/${child.id}`;
          const session = child.sports.flatMap(sport => sport.nextSession ? [sport.nextSession] : [])
            .sort((a, b) => `${a.sessionDate} ${a.startTime}`.localeCompare(`${b.sessionDate} ${b.startTime}`))[0];
          const match = matches.find(item => item.children?.includes(child.id));
          const coach = child.sports.flatMap(sport => sport.coaches ?? [])[0];
          return <article className="guardian-child-card" key={child.id}>
            <div className="guardian-child-head">
              <PersonPortrait className="guardian-avatar" name={child.arabicName} photo={child.photoReference} gender={child.gender} />
              <div><h3>{child.arabicName}</h3>{!child.sports.length && <small>{age(child.dateOfBirth, data.today)} سنة</small>}</div>
            </div>
            <div className="subscription-mini">
              {child.sports.map(sport => <div className={styles.enrollment} key={sport.id}>
                <div><b>{sport.sport}</b><small> · {age(child.dateOfBirth, data.today)} سنة · {sport.branch}</small></div>
                <div className={styles.status}>
                  <span className={`status-pill ${sport.period?.status.toLowerCase() ?? "empty"}`}>{subscriptionText(sport.period)}</span>
                  <Link href={`/guardian/renew/${sport.id}`}>{!sport.period ? "اختيار باقة والاشتراك" : ["Expiring", "Expired"].includes(sport.period.status) ? "تجديد الآن" : "تجديد الاشتراك"}</Link>
                </div>
              </div>)}
            </div>
            {coach && <Link className={styles.coach} href={`${feedbackHref(child.id, "Rating")}&groupId=${encodeURIComponent(coach.groupId)}&coachId=${encodeURIComponent(coach.id)}`}>
              المدرب <b>{coach.name}</b>
            </Link>}
            <nav className={styles.shortcuts} aria-label={`اختصارات ملف ${child.arabicName}`}>
              <Link href={`${childHref}#schedule`}><CalendarDays size={18} aria-hidden /><b>{session ? "الحصة القادمة" : "جدول التدريب"}</b>{session && <small>{appointment(session.sessionDate, session.startTime)}</small>}</Link>
              {match && <Link href={`/guardian/matches?playerId=${child.id}&matchId=${match.id}`}><Trophy size={18} aria-hidden /><b>المباراة القادمة</b><small>{appointment(match.matchDate, match.startTime)}</small></Link>}
              <Link href={`${childHref}/nutrition?day=${nutritionDate}`}><Utensils size={18} aria-hidden /><b>مكتبة التغذية</b></Link>
            </nav>
            <Link className="guardian-card-cta" href={childHref}>ملف {child.arabicName.split(" ")[0]} والتقرير</Link>
          </article>;
        })}</div>}
    </section>
    <section className="guardian-section">
      <h2>العمليات السريعة</h2>
      <nav className={styles.actions} aria-label="إجراءات الاشتراك">
        <Link href="/guardian/enrollment-requests/new"><UserPlus size={21} aria-hidden />اشتراك جديد</Link>
        <a href="#children"><RefreshCw size={21} aria-hidden />تجديد الاشتراك</a>
        <Link href="/guardian/renew-for-another"><Gift size={21} aria-hidden />تجديد اشتراك لغيره</Link>
        <button onClick={() => setFeedback("Rating")}><Star size={21} aria-hidden />تقييم المدربين</button>
        <button onClick={() => setFeedback("Complaint")}><MessageCircle size={21} aria-hidden />شكوى أو اقتراح</button>
      </nav>
      {feedback && <div className="guardian-section">
        <div className="guardian-section-title"><h3>اختر اللاعب</h3><button className="secondary-button" onClick={() => setFeedback(undefined)}>إلغاء</button></div>
        {data.children.length ? <div className="guardian-list">{data.children.map(child => <Link key={child.id} href={feedbackHref(child.id, feedback)}>{child.arabicName}</Link>)}</div> : <EmptyState message="لا يوجد أبناء مرتبطون بهذا الحساب." />}
      </div>}
    </section>
    {data.children.length > 0 && <section className="guardian-section">
      <div className="guardian-section-title"><h2>اقتراحات غذائية لليوم</h2></div>
      <div className="guardian-child-grid">
        {data.children.map(child => <article key={child.id}>
          <Link href={`/guardian/children/${child.id}/nutrition?day=${nutritionDate}`}>{child.arabicName}</Link>
          <DailyNutritionSummary playerId={child.id} day={nutritionDate} showTitle={false} />
        </article>)}
      </div>
    </section>}
    {data.sportContexts.length > 0 && <section className="guardian-section">
      <div className="guardian-section-title"><h2>رياضات الأبناء</h2></div>
      <div className="sport-context-grid">{data.sportContexts.map(item => <article key={item.sportId}><span className="sport-context-icon">●</span><div><h3>{item.sport}</h3><p>{item.children.join("، ")}</p></div><b>{item.childCount}</b></article>)}</div>
    </section>}
    <details className="guardian-section">
      <summary>متابعة التطور وخدمات الأسرة</summary>
      <nav className="child-content-links" aria-label="متابعة التطور">
        {data.children.map(child => <div key={child.id}>
          <strong>{child.arabicName}</strong>
          <Link href={`/guardian/children/${child.id}/goals`}>أهداف التطور</Link>
          <Link href={`/guardian/children/${child.id}/skills`}>خطوات التعلم</Link>
          <Link href={`/guardian/children/${child.id}/badges`}>شارات المجهود</Link>
          <Link href={`/guardian/children/${child.id}/certificates`}>الإنجازات والشهادات</Link>
        </div>)}
      </nav>
      <div className="row-actions">
        <Link className="secondary-button" href="/guardian/renew-family">مراجعة تجديد الأبناء معًا</Link>
        <Link className="secondary-button" href="/guardian/day-pass">زيارة يومية مستقلة عن التدريب</Link>
        <Link className="secondary-button" href="/guardian/rewards">مكافآت الأسرة</Link>
      </div>
    </details>
    <GuardianSponsoredCard /><GuardianSponsorSettingsLink />
    <GuardianSportCatalog />
    <AcademyActivityPreview guardian />
    <footer className={styles.accountActions}>
      {logoutError && <p className="error-message" role="alert">{logoutError}</p>}
      <button type="button" className="secondary-button" disabled={logoutBusy} onClick={() => void logout()}>
        {logoutBusy ? "جارٍ تسجيل الخروج…" : "تسجيل الخروج"}
      </button>
    </footer>
  </GuardianShell>;
}

type Options = { children: { id: string; name: string }[]; sports: { id: string; name: string }[]; branches: { id: string; name: string }[] };
const guardianEnrollmentPath = "/guardian/enrollment-requests";
const guardianEnrollmentApi = "/api/v1/guardian/enrollment-requests";

export function NewEnrollmentForm() {
  const router = useRouter();
  const [kind, setKind] = useState<"existing" | "new" | "">("");
  const [options, setOptions] = useState<Options | null>(null);
  const [optionsError, setOptionsError] = useState("");
  const [optionsAttempt, setOptionsAttempt] = useState(0);
  const [submitError, setSubmitError] = useState("");
  const [busy, setBusy] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const sending = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/guardian/enrollment-request-options", { cache: "no-store", signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error();
        return response.json() as Promise<Options>;
      })
      .then(value => { if (!controller.signal.aborted) setOptions(value); })
      .catch(() => { if (!controller.signal.aborted) setOptionsError("تعذر تحميل الأطفال والرياضات والفروع."); });
    return () => controller.abort();
  }, [optionsAttempt]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current) return;
    const form = new FormData(event.currentTarget);
    const body = {
      requestType: kind === "existing" ? 1 : 2,
      existingPlayerId: kind === "existing" ? form.get("playerId") : null,
      newChildArabicName: kind === "new" ? String(form.get("name") ?? "").trim() : null,
      newChildDateOfBirth: kind === "new" ? form.get("dob") : null,
      newChildGender: kind === "new" && form.get("gender") ? Number(form.get("gender")) : null,
      sportId: form.get("sportId"),
      preferredBranchId: form.get("branchId"),
      notes: String(form.get("notes") ?? "").trim() || null,
    };
    sending.current = true;
    setBusy(true);
    setSubmitError("");
    try {
      const key = await pendingFinancialIntent("guardian-enrollment-request", body);
      const response = await csrfRequest(guardianEnrollmentApi, "POST", body, { "Idempotency-Key": key }, { announce: false });
      const result = await response.json() as { id?: string };
      if (typeof result.id !== "string" || !/^[\da-f-]{36}$/i.test(result.id)) throw new Error();
      clearFinancialIntent("guardian-enrollment-request");
      router.push(`${guardianEnrollmentPath}?created=${result.id}`);
    } catch (caught) {
      const rejected = caught instanceof DashboardRequestError && [400, 401, 403, 404, 409, 422, 429].includes(caught.status);
      if (rejected) {
        clearFinancialIntent("guardian-enrollment-request");
        setSubmitError(caught.message);
      } else {
        setSubmitError("تعذر تأكيد وصول الطلب. أعد إرسال الطلب نفسه أو تحقّق من قائمة الطلبات.");
      }
      setRetrying(!rejected);
      sending.current = false;
      setBusy(false);
    }
  }

  function choose(next: typeof kind) {
    setKind(next);
    setSubmitError("");
  }

  return <GuardianShell>
    <Link className="back-link" href={guardianEnrollmentPath}>العودة إلى الطلبات</Link>
    <header className="guardian-page-header"><h1>اشتراك جديد</h1></header>
    {!kind && <section className="request-kind-grid" aria-label="نوع طلب الاشتراك">
      <button type="button" onClick={() => choose("existing")}><b>إضافة رياضة لطفل موجود</b></button>
      <button type="button" onClick={() => choose("new")}><b>إضافة طفل جديد</b></button>
    </section>}
    {kind && !options && !optionsError && <LoadingState />}
    {kind && optionsError && <section className="guardian-form">
      <ErrorState message={optionsError} />
      <button className="secondary-button" type="button" onClick={() => { setOptionsError(""); setOptionsAttempt(value => value + 1); }}>إعادة تحميل الاختيارات</button>
      <button type="button" className="secondary-button" onClick={() => choose("")}>تغيير نوع الطلب</button>
    </section>}
    {kind && options && <form className="guardian-form" onSubmit={event => void submit(event)} aria-busy={busy || undefined}>
      <fieldset className="enrollment-form-fields" disabled={busy} aria-label="بيانات الطلب" inert={retrying || undefined}>
        {kind === "existing" ? <label>الطفل<select name="playerId" required><option value="">اختر الطفل</option>{options.children.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label> : <>
          <label>اسم الطفل<input name="name" required maxLength={180} /></label>
          <label>تاريخ الميلاد<input name="dob" type="date" required /></label>
          <label>النوع — اختياري<select name="gender"><option value="">غير محدد</option><option value="1">ذكر</option><option value="2">أنثى</option></select></label>
        </>}
        <label>الرياضة<select name="sportId" required><option value="">اختر الرياضة</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        <label>الفرع المفضل<select name="branchId" required><option value="">اختر الفرع</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        <label>ملاحظات — اختياري<textarea name="notes" maxLength={600} /></label>
      </fieldset>
      {!retrying && <p className="form-hint">تختار الأكاديمية المجموعة بعد المراجعة. تقديم الطلب لا يتطلب دفعًا.</p>}
      {submitError && <ErrorState message={submitError} />}
      <button className="primary-button" disabled={busy}>{busy ? "جارٍ الإرسال…" : retrying ? "إعادة إرسال الطلب" : "إرسال طلب الاشتراك"}</button>
      {!retrying && <button type="button" className="secondary-button" disabled={busy} onClick={() => choose("")}>تغيير نوع الطلب</button>}
    </form>}
  </GuardianShell>;
}

type RequestRow = { id: string; child: string; sport: string; branch: string; status: string; guardianVisibleReason?: string; createdAtUtc: string; createdSportEnrollmentId?: string; paymentRequestId?: string };
export function GuardianRequestList({ created }: { created?: string }) {
  const [rows, setRows] = useState<RequestRow[] | null>(null);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    fetch(guardianEnrollmentApi, { cache: "no-store", signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error();
        return response.json() as Promise<RequestRow[]>;
      })
      .then(value => { if (!controller.signal.aborted) setRows(value); })
      .catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => controller.abort();
  }, [attempt]);
  const saved = created ? rows?.find(row => row.id === created) : undefined;
  const visibleRows = saved && rows ? [saved, ...rows.filter(row => row.id !== saved.id)] : rows;
  return <GuardianShell>
    <header className="guardian-page-header">
      <h1>طلبات الاشتراك</h1>
      <Link className="guardian-card-cta" href={`${guardianEnrollmentPath}/new`}>طلب اشتراك جديد</Link>
    </header>
    {error ? <section className="guardian-form">
      <ErrorState message="تعذر تحميل طلبات الاشتراك." />
      <button className="secondary-button" type="button" onClick={() => { setError(false); setAttempt(value => value + 1); }}>إعادة تحميل الطلبات</button>
    </section> : !rows ? <LoadingState /> : rows.length === 0 ? <EmptyState message="لا توجد طلبات اشتراك حتى الآن." /> : <div className="guardian-request-list">
      {saved && <p role="status" className="guardian-request-saved">تم إرسال طلب الاشتراك.</p>}
      {visibleRows?.map(row => <article key={row.id}>
        <div><h2>{row.child}</h2><p>{row.sport} · {row.branch}</p><small>{formatDateAr(row.createdAtUtc)}</small></div>
        <span className={`status-pill ${row.status.toLowerCase()}`}>{statusLabels[row.status]}</span>
        {row.guardianVisibleReason && <p className="request-reason">{row.guardianVisibleReason}</p>}
        {row.status === "Approved" && row.createdSportEnrollmentId && (row.paymentRequestId
          ? <Link href="/guardian/receipts">عرض الإيصالات</Link>
          : <Link href={`/guardian/renew/${row.createdSportEnrollmentId}`}>إكمال الاشتراك / اختيار الباقة</Link>)}
      </article>)}
    </div>}
  </GuardianShell>;
}

type ProfileEnrollment = SportEnrollment & {
  nextPeriod?: { id: string; startDate: string };
  recurring: { dayOfWeek: string; startTime: string; endTime: string }[];
  transfers?: EnrollmentTransfer[];
  sessionMoves?: { date: string; group: string }[];
  upcoming: { id: string; group?: string; sessionDate: string; startTime: string; endTime: string }[];
};
type Profile = {
  trainingToday?: string;
  compensations?: Compensation[];
  player: { id: string; arabicName: string; dateOfBirth: string; photoReference?: string };
  enrollments: ProfileEnrollment[];
  attendance: { id: string; sessionDate: string; sport: string; group: string; status: string }[];
  attendanceSummary: { present: number; absent: number };
  evaluations: { id: string; sport: string; evaluationDate: string; reportingPeriod: string }[];
};

function GuardianProfileSubscriptions({ profile }: { profile: Profile }) {
  const child = encodeURIComponent(profile.player.id);
  const periodHref = (id: string) => `/guardian/subscriptions/periods/${encodeURIComponent(id)}?child=${child}`;
  return <section className="profile-section">
    <h2>الاشتراكات</h2>
    {profile.enrollments.length === 0 && <EmptyState message="لا توجد رياضات مسجلة لهذا اللاعب." />}
    {profile.enrollments.map(({ id, sport, period, nextPeriod }) => <article className={styles.subscriptionPeriods} key={id} aria-label={sport}>
      <div className="profile-row subscription-profile">
        <div>
          <b>{sport}</b>
          <small>{subscriptionText(period)}</small>
          {period && <small>{formatDateAr(period.startDate)}{period.endDate && <> — {formatDateAr(period.endDate)}</>}
            {period.remainingSessions != null && <> · الحصص المتبقية {period.remainingSessions}</>}</small>}
        </div>
        <div className="row-actions">
          {period && <Link href={periodHref(period.id)}>تفاصيل حالة الاشتراك</Link>}
          <Link href={`/guardian/renew/${encodeURIComponent(id)}`}>{period ? "تجديد الاشتراك" : "اختيار باقة والاشتراك"}</Link>
        </div>
      </div>
      {nextPeriod && nextPeriod.id !== period?.id && <div className="profile-row">
        <div><b>الفترة التالية</b><small>تبدأ {formatDateAr(nextPeriod.startDate)}</small></div>
        <div className="row-actions"><Link href={periodHref(nextPeriod.id)}>تفاصيل الفترة التالية</Link></div>
      </div>}
    </article>)}
  </section>;
}

function GuardianTrainingSchedule({ profile, refresh, refreshing }: { profile: Profile; refresh: () => void; refreshing: boolean }) {
  return <section id="schedule" className="profile-section" tabIndex={-1} aria-labelledby="guardian-schedule-title" aria-busy={refreshing}>
    <div className={`guardian-section-title ${styles.profileScheduleTitle}`}>
      <h2 id="guardian-schedule-title">التدريب والمواعيد</h2>
      <button type="button" className="secondary-button" onClick={refresh} disabled={refreshing}>{refreshing ? "جارٍ تحديث المواعيد…" : "تحديث المواعيد"}</button>
    </div>
    {profile.enrollments.length === 0 && <EmptyState message="لا توجد رياضات مسجلة لهذا اللاعب." />}
    {profile.enrollments.map(enrollment => {
      const makeups = new Set(profile.compensations
        ?.filter(grant => grant.sportEnrollmentId === enrollment.id)
        .flatMap(grant => grant.bookings)
        .filter(booking => booking.state === "Reserved")
        .map(booking => booking.trainingSessionId));
      return <article className={`schedule-card ${styles.profileSchedule}`} key={enrollment.id}>
        <h3>{enrollment.sport} · {trainingFormatName(enrollment.registrationFormat)}</h3>
        <p>{enrollment.group} · {enrollment.branch}</p>
        <EnrollmentTransfers scheduled transfers={enrollment.transfers} moves={enrollment.sessionMoves} />
        {enrollment.upcoming.length ? <ul className={styles.profileAppointments}>
          {enrollment.upcoming.map(session => <li key={session.id}>
            <b>{session.group ?? enrollment.group}</b>
            {makeups.has(session.id) && <span className={styles.makeupLabel}>حصة تعويضية</span>}
            <small>{formatSessionDateAr(session.sessionDate)} · من {formatTimeAr(session.startTime)} إلى {formatTimeAr(session.endTime)}</small>
          </li>)}
        </ul> : <p>لا توجد حصص قادمة منشورة.</p>}
        {!!enrollment.recurring.length && <details>
          <summary>مواعيد المجموعة الأسبوعية</summary>
          {enrollment.recurring.map(slot => <small key={`${slot.dayOfWeek}-${slot.startTime}`}>
            {weekdayLabels[slot.dayOfWeek]} · من {formatTimeAr(slot.startTime)} إلى {formatTimeAr(slot.endTime)}
          </small>)}
        </details>}
      </article>;
    })}
  </section>;
}

export function ChildProfile({ playerId }: { playerId: string }) {
  return <ChildProfileContent key={playerId} playerId={playerId} />;
}

function ChildProfileContent({ playerId }: { playerId: string }) {
  const [data, setData] = useState<Profile | null>(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  function refresh() {
    if (refreshing) return;
    setError("");
    setRefreshing(true);
    setRevision(value => value + 1);
  }

  useEffect(() => {
    const request = new AbortController();
    fetch(`/api/v1/guardian/children/${encodeURIComponent(playerId)}/profile`, { cache: "no-store", signal: request.signal })
      .then(async response => {
        if (response.status === 404) {
          if (!request.signal.aborted) setError("ملف اللاعب غير متاح لهذا الحساب.");
          return;
        }
        if (!response.ok) throw new Error();
        const profile = await response.json() as Profile;
        if (profile.player.id !== playerId) throw new Error();
        if (!request.signal.aborted) setData(profile);
      })
      .catch(() => { if (!request.signal.aborted) setError("تعذر تحميل ملف اللاعب. أعد المحاولة."); })
      .finally(() => { if (!request.signal.aborted) setRefreshing(false); });
    return () => request.abort();
  }, [playerId, revision]);

  const loaded = !!data;
  useEffect(() => {
    const target = window.location.hash.slice(1);
    if (!loaded || !["evaluations", "schedule"].includes(target)) return;
    const section = document.getElementById(target);
    section?.scrollIntoView?.({ block: "start" });
    section?.focus({ preventScroll: true });
  }, [loaded]);

  return <GuardianShell>
    <Link className="back-link" href="/guardian">العودة للأبناء</Link>
    {error ? <><ErrorState message={error} /><button type="button" className="secondary-button" onClick={refresh}>إعادة تحميل الملف</button></>
      : !data ? <LoadingState /> : <>
        <header className="child-profile-hero">
          <span className="guardian-avatar large">{data.player.arabicName[0]}</span>
          <div><h1>{data.player.arabicName}</h1><div className="sport-badges">{data.enrollments.map(enrollment => <span key={enrollment.id}>{enrollment.sport}</span>)}</div></div>
        </header>
        <section id="evaluations" className="profile-section" tabIndex={-1} aria-labelledby="player-evaluations-title"><h2 id="player-evaluations-title">تقرير اللاعب</h2>{data.evaluations.length === 0 ? <EmptyState message="لا توجد تقييمات منشورة بعد." /> : data.evaluations.map(x => <article className="profile-row" key={x.id}><div><b>{x.sport}</b><small>{formatDateAr(x.evaluationDate)} · {x.reportingPeriod}</small></div><Link href={`/guardian/evaluations/${x.id}?child=${encodeURIComponent(data.player.id)}`}>عرض التقرير</Link></article>)}</section>
        <GuardianTrainingSchedule profile={data} refresh={refresh} refreshing={refreshing} />
        <CompensationCards items={data.compensations} family today={data.trainingToday} />
        <nav className="child-content-links" aria-label="محتوى ملف الطفل"><Link href={`/guardian/children/${playerId}/feedback?kind=Rating`}><b>تقييم المدرب</b><span>اختر نجومك من 1 إلى 5</span></Link><Link href={`/guardian/children/${playerId}/feedback`}><b>ملاحظتك للإدارة</b><span>شكوى أو اقتراح أو تقييم مدرب</span></Link><Link href={`/guardian/children/${playerId}/visits`}><b>الدخول والخروج</b><span>زيارات الطفل · مستقلة عن الحضور</span></Link><Link href={`/guardian/children/${playerId}/card`}><b>بطاقة اللاعب</b><span>QR قابل للإلغاء · بديل عن البحث اليدوي</span></Link><Link href={`/guardian/children/${playerId}/nutrition`}><b>التغذية والصحة</b><span>مكتبة معلومات عامة</span></Link><Link href={`/guardian/children/${playerId}/medical`}><b>الإصابات والاستشارات الطبية</b><span>السجلات المنشورة فقط</span></Link><Link href={`/guardian/children/${playerId}/gallery`}><b>الصور والفيديوهات</b><span>معرض الطفل المنشور</span></Link></nav><section className="profile-section"><div className="guardian-section-title"><h2>الحضور</h2><span>حاضر {data.attendanceSummary.present} · غائب {data.attendanceSummary.absent}</span></div>{data.attendance.length === 0 ? <EmptyState message="لا يوجد حضور مسجل." /> : data.attendance.map(x => <article className="profile-row" key={x.id}><div><b>{x.sport}</b><small>{formatDateAr(x.sessionDate)} · {x.group}</small></div><span className={`status-pill ${x.status.toLowerCase()}`}>{statusLabels[x.status]}</span></article>)}</section><GuardianProfileSubscriptions profile={data} />
      </>}
  </GuardianShell>;
}

type AdminRow = { id: string; guardian: string; child: string; requestType: string; sport: string; branch: string; status: string; createdOn?: string };
type StructureOptions = { sports: { id: string; arabicName: string }[]; branches: { id: string; arabicName: string }[] };
const staffEnrollmentPath = "/dashboard/enrollment-requests";
const enrollmentStatuses = ["Pending", "UnderReview", "Approved", "Rejected", "Cancelled"];
const enrollmentScopeKeys = ["q", "status", "sportId", "branchId", "from", "to", "allDates"];
type EnrollmentScope = { q: string; status: string; sportId: string; branchId: string; from: string; to: string };

function enrollmentScopeQuery(scope: EnrollmentScope) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(scope)) if (value) query.set(key, value);
  if (!scope.from && !scope.to) query.set("allDates", "true");
  return query;
}

export function AdminEnrollmentRequests() {
  const defaults = useDateFilterDefaults();
  const incoming = useSearchParams();
  const explicitDates = incoming?.has("from") || incoming?.has("to") || incoming?.get("allDates") === "true";
  const [scope, setScope] = useState<EnrollmentScope>(() => ({
    q: (incoming?.get("q") ?? "").slice(0, 120), status: incoming?.get("status") ?? "",
    sportId: incoming?.get("sportId") ?? "", branchId: incoming?.get("branchId") ?? "",
    from: explicitDates ? incoming?.get("from") ?? "" : defaults.from,
    to: explicitDates ? incoming?.get("to") ?? "" : defaults.to,
  }));
  const update = (change: Partial<EnrollmentScope>) => {
    const next = { ...scope, ...change };
    setScope(next);
    window.history.replaceState(null, "", `${staffEnrollmentPath}?${enrollmentScopeQuery(next)}`);
  };
  const { q, status, sportId, branchId, from, to } = scope;
  const invalidDates = !!from && !!to && from > to;
  const [options, setOptions] = useState<StructureOptions>({ sports: [], branches: [] });
  const [optionsError, setOptionsError] = useState("");
  const [result, setResult] = useState<{ key: string; rows?: AdminRow[]; error?: string } | null>(null);
  const [attempt, setAttempt] = useState(0);
  const filters = new URLSearchParams();
  if (status) filters.set("status", status);
  if (sportId) filters.set("sportId", sportId);
  if (branchId) filters.set("branchId", branchId);
  if (from) filters.set("from", from);
  if (to) filters.set("to", to);
  const query = filters.toString();
  const scopeQuery = enrollmentScopeQuery(scope);
  const requestKey = `${attempt}:${query}`;
  const current = result?.key === requestKey ? result : null;
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/manage/structure/options", { signal: controller.signal })
      .then(response => { if (!response.ok) throw new Error(); return response.json() as Promise<StructureOptions>; })
      .then(value => { if (!controller.signal.aborted) setOptions(value); })
      .catch(() => { if (!controller.signal.aborted) setOptionsError("تعذر تحميل اختيارات الفلاتر."); });
    return () => controller.abort();
  }, []);
  useEffect(() => {
    if (invalidDates) return;
    const controller = new AbortController();
    fetch(`/api/v1/enrollment-requests?${query}`, { cache: "no-store", signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error();
        const rows: unknown = await response.json();
        if (!Array.isArray(rows)) throw new Error();
        if (!controller.signal.aborted) setResult({ key: requestKey, rows });
      })
      .catch(() => { if (!controller.signal.aborted) setResult({ key: requestKey, error: "تعذر تحميل طلبات الاشتراك." }); });
    return () => controller.abort();
  }, [query, requestKey, invalidDates]);
  const visible = current?.rows?.filter(row => `${row.guardian} ${row.child} ${row.sport} ${row.branch}`.includes(q.trim())) ?? [];
  const reset = () => update({ q: "", status: "", sportId: "", branchId: "", from: "", to: "" });
  return <><PageHeader title="طلبات الاشتراك الجديدة" context="اللاعبون / طلبات الاشتراك" />
    {optionsError && <ErrorState message={optionsError} />}
    <FilterToolbar onReset={reset}>
      <SearchInput value={q} onChange={value => update({ q: value.slice(0, 120) })} placeholder="الطفل أو ولي الأمر أو الرياضة" />
      <select aria-label="حالة الطلب" value={status} onChange={e => update({ status: e.target.value })}><option value="">كل الحالات</option>{enrollmentStatuses.map(value => <option key={value} value={value}>{statusLabels[value]}</option>)}</select>
      <select aria-label="رياضة الطلب" value={sportId} onChange={e => update({ sportId: e.target.value })}><option value="">كل الرياضات</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select>
      <select aria-label="فرع الطلب" value={branchId} onChange={e => update({ branchId: e.target.value })}><option value="">كل الفروع</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select>
      <fieldset className={enrollmentStyles.dates}>
        <legend>تاريخ الطلب بتوقيت الأكاديمية</legend>
        <label>من<input aria-label="من تاريخ" aria-invalid={invalidDates} aria-describedby={invalidDates ? "enrollment-date-error" : undefined} type="date" value={from} onChange={e => update({ from: e.target.value })} /></label>
        <label>إلى<input aria-label="إلى تاريخ" aria-invalid={invalidDates} aria-describedby={invalidDates ? "enrollment-date-error" : undefined} type="date" value={to} onChange={e => update({ to: e.target.value })} /></label>
        {invalidDates && <p id="enrollment-date-error" role="alert">تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه.</p>}
      </fieldset>
    </FilterToolbar>
    {!invalidDates && <section className="list-card">{!current ? <LoadingState /> : current.error ? <><ErrorState message={current.error} /><button type="button" className="secondary-button" onClick={() => setAttempt(value => value + 1)}>إعادة تحميل الطلبات</button></> : visible.length === 0 ? <EmptyState message="لا توجد طلبات اشتراك مطابقة." /> : <AdminDataTable label="طلبات الاشتراك" rows={visible} rowKey={x => x.id} columns={[
    { key: "child", header: "الطفل", width: "minmax(150px, 1.25fr)", primary: true, render: x => <><strong>{x.child}</strong><small>{x.requestType === "NewChild" ? "طفل جديد" : "رياضة جديدة"}</small></> },
    { key: "guardian", header: "ولي الأمر", width: "minmax(135px, 1fr)", render: x => x.guardian },
    { key: "context", header: "الرياضة والفرع", width: "minmax(160px, 1.2fr)", render: x => <>{x.sport}<small>{x.branch}</small></> },
    { key: "date", header: "تاريخ الطلب", width: "112px", render: x => x.createdOn ? <time dateTime={x.createdOn}>{formatDateAr(x.createdOn)}</time> : "غير متاح" },
    { key: "status", header: "الحالة", width: "120px", render: x => <StatusBadge status={x.status} /> },
    { key: "actions", header: "الإجراءات", width: "100px", render: x => <RowActions actions={[{ label: "عرض", href: `${staffEnrollmentPath}/${x.id}?${scopeQuery}` }]} /> },
  ]} />}</section>}</>;
}

type AdminDetails = {
  canManage?: boolean;
  request: {
    id: string;
    requestType: "ExistingChildNewSport" | "NewChild";
    child: string;
    guardian: string;
    sport: string;
    branch: string;
    status: string;
    notes?: string;
    guardianVisibleReason?: string;
    requiresPaymentProof?: boolean;
  };
  groups: { id: string; name: string; registrationFormat?: string; trainingLevelId?: string; trainingLevel?: string }[];
  playerCandidates: { id: string; name: string; playerCode: string; dateOfBirth: string }[];
};
type EnrollmentPayment = { id: string; status: string; subscriptionPlanId?: string; amount?: number; currency?: string; proofAssetId?: string | null; proofUrl?: string | null; discountReason?: string; paymentRequestId?: string; receiptId?: string | null };
type EnrollmentDecision = { kind: "approve" | "reject"; proofAssetId: string | null };

export function AdminEnrollmentRequestDetails({ id }: { id: string }) {
  return <EnrollmentRequestReview key={id} id={id} />;
}

function EnrollmentRequestReview({ id }: { id: string }) {
  const endpoint = `/api/v1/enrollment-requests/${id}`;
  const incoming = useSearchParams();
  const returnScope = new URLSearchParams();
  for (const key of enrollmentScopeKeys) {
    const value = incoming?.get(key);
    if (value) returnScope.set(key, value);
  }
  const [data, setData] = useState<AdminDetails | null>(null);
  const [payment, setPayment] = useState<EnrollmentPayment | null>(null);
  const [readyProof, setReadyProof] = useState<string>();
  const [checking, setChecking] = useState(true);
  const [readError, setReadError] = useState("");
  const [writeError, setWriteError] = useState("");
  const [unverified, setUnverified] = useState(false);
  const [busy, setBusy] = useState(false);
  const [decision, setDecision] = useState<EnrollmentDecision | null>(null);
  const [group, setGroup] = useState("");
  const [levelReviewed, setLevelReviewed] = useState(false);
  const [matchPlayer, setMatchPlayer] = useState("");
  const [reason, setReason] = useState("");
  const operation = useRef(false);
  const readController = useRef<AbortController | null>(null);

  const load = useCallback(async () => {
    readController.current?.abort();
    const controller = new AbortController();
    readController.current = controller;
    const { signal } = controller;
    let details: AdminDetails | null = null;
    try {
      const response = await fetch(endpoint, { cache: "no-store", signal });
      if (!response.ok) {
        const message = response.status === 404 ? "طلب الاشتراك غير متاح." : "تعذر تحميل طلب الاشتراك.";
        throw new DashboardRequestError(message, response.status);
      }
      details = await response.json() as AdminDetails;
      if (signal.aborted) return;
      if (details?.request?.id !== id || !Array.isArray(details.groups) || !Array.isArray(details.playerCandidates)) {
        details = null;
        throw new Error();
      }
      setData(details);
      setGroup(previous => details?.groups.some(value => value.id === previous) ? previous : "");
      setMatchPlayer(previous => details?.playerCandidates.some(value => value.id === previous) ? previous : "");
      setLevelReviewed(false);
      const evidence = await fetch(`/api/v1/manage/enrollment-requests/${id}/payment`, { cache: "no-store", signal });
      if (evidence.status === 403 && details.request.requiresPaymentProof === false) {
        setUnverified(false);
        return;
      }
      if (!evidence.ok) throw new DashboardRequestError("تعذر التحقق من بيانات الدفع.", evidence.status);
      const savedPayment = await evidence.json() as EnrollmentPayment;
      if (signal.aborted) return;
      if (savedPayment.id !== id || (savedPayment.proofAssetId
        ? !/^[a-f0-9-]{36}$/i.test(savedPayment.proofAssetId) || savedPayment.proofUrl !== `/api/v1/assets/${savedPayment.proofAssetId}`
        : !!savedPayment.proofUrl)) throw new Error();
      if (savedPayment.status !== details.request.status)
        throw new DashboardRequestError("تغيّرت حالة الطلب أثناء القراءة. تحقّق منها مجددًا.", 409);
      setPayment(savedPayment);
      setUnverified(false);
    } catch (caught) {
      if (signal.aborted) return;
      if (!details || (caught instanceof DashboardRequestError && [401, 404].includes(caught.status))) setData(null);
      setReadError(caught instanceof DashboardRequestError ? caught.message : details ? "تعذر التحقق من بيانات الدفع." : "تعذر تحميل طلب الاشتراك.");
    } finally {
      if (!signal.aborted) setChecking(false);
    }
  }, [endpoint, id]);

  useEffect(() => {
    let disposed = false;
    queueMicrotask(() => { if (!disposed) void load(); });
    return () => {
      disposed = true;
      readController.current?.abort();
    };
  }, [load]);

  async function refresh() {
    setChecking(true);
    setReadError("");
    setPayment(null);
    setReadyProof(undefined);
    await load();
  }

  const selectedGroup = data?.groups.find(value => value.id === group);
  const specialGroup = !!selectedGroup?.registrationFormat && selectedGroup.registrationFormat !== "Group";
  const needsProof = data?.request.requiresPaymentProof === true || payment?.subscriptionPlanId != null || payment?.amount != null || !!payment?.proofAssetId;
  const verifiedAmount = typeof payment?.amount === "number" && Number.isFinite(payment.amount) && payment.amount >= 0 &&
    typeof payment.currency === "string" && /^[A-Z]{3}$/.test(payment.currency) ? payment.amount : null;
  const openRequest = data?.request.status === "Pending" || data?.request.status === "UnderReview";
  const canAct = data?.canManage !== false && openRequest && !checking && !readError && !unverified && !busy;
  const proofReady = !!payment?.proofUrl && payment.proofUrl === readyProof;
  const canApprove = canAct && !!selectedGroup && (!needsProof || (proofReady && verifiedAmount !== null))
    && (!specialGroup || (levelReviewed && !!selectedGroup.trainingLevelId));

  async function act(kind: EnrollmentDecision["kind"] | "review", expectedProofAssetId: string | null = null) {
    if (operation.current || !canAct || (kind === "approve" && !canApprove)) return;
    operation.current = true;
    setBusy(true);
    setWriteError("");
    const body = kind === "approve"
      ? { trainingGroupId: group, matchExistingPlayerId: matchPlayer || null, adminNotes: null, trainingLevelId: selectedGroup?.trainingLevelId ?? null, levelReviewed, expectedProofAssetId }
      : kind === "reject" ? { guardianVisibleReason: reason.trim() || null, adminNotes: null, expectedProofAssetId } : {};
    try {
      await csrfRequest(`${endpoint}/${kind}`, "POST", body, {}, { announce: false });
      setUnverified(true);
      setDecision(null);
      await refresh();
    } catch (caught) {
      setDecision(null);
      if (caught instanceof DashboardRequestError && [400, 403, 422, 429].includes(caught.status)) {
        setWriteError(caught.message);
      } else {
        setUnverified(true);
        setPayment(null);
        setWriteError(caught instanceof DashboardRequestError && caught.status === 409
          ? `${caught.message} تحقّق من حالة الطلب قبل اتخاذ قرار آخر.`
          : "تحقّق من حالة الطلب قبل اتخاذ قرار آخر.");
      }
    } finally {
      operation.current = false;
      setBusy(false);
    }
  }

  return <>
    <PageHeader title="مراجعة طلب الاشتراك" context="طلبات الاشتراك" back={paymentQueueBack(incoming) ?? { href: returnScope.size ? `${staffEnrollmentPath}?${returnScope}` : staffEnrollmentPath, label: "العودة إلى طلبات الاشتراك" }} />
    {checking && <LoadingState />}
    {(readError || writeError) && <ErrorState message={readError || writeError} />}
    {(readError || unverified) && !checking && <button type="button" className="secondary-button enrollment-check-button" onClick={() => { setWriteError(""); void refresh(); }}>التحقق من حالة الطلب</button>}
    {data && <section className="detail-card enrollment-request-detail">
      <h2>{data.request.child}</h2>
      <dl>
        <div><dt>ولي الأمر</dt><dd>{data.request.guardian}</dd></div>
        <div><dt>الرياضة</dt><dd>{data.request.sport}</dd></div>
        <div><dt>الفرع المفضل</dt><dd>{data.request.branch}</dd></div>
        <div><dt>الحالة</dt><dd role="status">{statusLabels[data.request.status]}</dd></div>
      </dl>
      {data.request.notes && <p>{data.request.notes}</p>}
      {data.request.guardianVisibleReason && <p className="request-reason">{data.request.guardianVisibleReason}</p>}
      {verifiedAmount !== null && <p>{payment?.paymentRequestId ? "المبلغ المحصّل" : openRequest ? "المبلغ المطلوب" : "مبلغ الطلب"} <strong>{formatMoneyAr(verifiedAmount, payment?.currency)}</strong></p>}
      {payment?.discountReason && <p>{payment.discountReason}</p>}
      {payment?.receiptId && data.request.status === "Approved" && <div className="form-actions">
        <Link className="button button-primary"
          href={`/dashboard/subscriptions/receipts/${encodeURIComponent(payment.receiptId)}?${enrollmentReceiptQuery(id, incoming)}`}>عرض الإيصال</Link>
      </div>}
      {payment?.proofUrl && <section className="enrollment-proof" aria-labelledby="enrollment-proof-heading">
        <h3 id="enrollment-proof-heading">إثبات الدفع</h3>
        <PaymentProofPreview key={payment.proofUrl} url={payment.proofUrl} description="صورة إثبات دفع الاشتراك"
          linkLabel="عرض إثبات الدفع" onReady={ready => setReadyProof(ready ? payment.proofUrl ?? undefined : undefined)} />
      </section>}
      {openRequest && data.canManage === false && <p className="form-hint">لديك صلاحية عرض الطلب فقط.</p>}
      {openRequest && data.canManage !== false && !checking && !readError && !unverified && <div className="admin-request-actions" aria-busy={busy || undefined}>
        {data.request.status === "Pending" && <button type="button" className="secondary-button" disabled={!canAct} onClick={() => void act("review")}>بدء المراجعة</button>}
        <fieldset className="enrollment-form-fields" disabled={!canAct}>
          <legend>القبول</legend>
          {data.request.requestType === "NewChild" && data.playerCandidates.length > 0 && <label>مطابقة لاعب موجود<select value={matchPlayer} onChange={event => setMatchPlayer(event.target.value)}>
            <option value="">إنشاء لاعب جديد</option>
            {data.playerCandidates.map(candidate => <option key={candidate.id} value={candidate.id}>{candidate.name} · {candidate.playerCode} · {candidate.dateOfBirth}</option>)}
          </select></label>}
          {data.groups.length > 0 ? <label>المجموعة<select value={group} onChange={event => { setGroup(event.target.value); setLevelReviewed(false); }}>
            <option value="">اختر المجموعة المناسبة</option>
            {data.groups.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select></label> : <p className="form-hint">لا توجد مجموعة مناسبة لعمر اللاعب في هذه الرياضة والفرع.</p>}
          {specialGroup && (selectedGroup.trainingLevelId ? <label className="enrollment-level-choice">
            <input type="checkbox" checked={levelReviewed} onChange={event => setLevelReviewed(event.target.checked)} />
            <span>راجعت مستوى اللاعب ويطابق مستوى المجموعة ({selectedGroup.trainingLevel})</span>
          </label> : <p className="form-hint">حدد مستوى المجموعة قبل قبول الطلب.</p>)}
          {needsProof && !payment?.proofUrl && <p className="form-hint">لم يصل إثبات الدفع بعد.</p>}
          {needsProof && payment?.proofUrl && verifiedAmount === null && <p className="form-hint">تعذر التحقق من مبلغ الطلب وعملته.</p>}
          {needsProof && payment?.proofUrl && !proofReady && <p className="form-hint">راجع صورة الإثبات قبل القبول.</p>}
          <button type="button" className="primary-button" disabled={!canApprove} onClick={() => setDecision({ kind: "approve", proofAssetId: payment?.proofAssetId ?? null })}>قبول وإنشاء التسجيل</button>
        </fieldset>
        <fieldset className="enrollment-form-fields" disabled={!canAct}>
          <legend>الرفض</legend>
          <label>سبب الرفض الظاهر لولي الأمر (اختياري)<textarea maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></label>
          <button type="button" className="danger-button" onClick={() => setDecision({ kind: "reject", proofAssetId: payment?.proofAssetId ?? null })}>رفض الطلب</button>
        </fieldset>
      </div>}
    </section>}
    <ConfirmationDialog open={decision !== null}
      title={decision?.kind === "approve" ? "تأكيد قبول طلب الاشتراك" : "تأكيد رفض طلب الاشتراك"}
      message={decision?.kind === "approve" ? `سيُسجّل ${data?.request.child} في مجموعة ${selectedGroup?.name}.${verifiedAmount !== null ? ` سيُسجّل تحصيل ${formatMoneyAr(verifiedAmount, payment?.currency)} وتُفعّل الباقة.` : ""}`
        : reason.trim() ? "سيظهر سبب الرفض لولي الأمر في طلبات الاشتراك." : "سيظهر الطلب مرفوضًا لولي الأمر."}
      confirmLabel={decision?.kind === "approve" ? "تأكيد القبول" : "تأكيد الرفض"} tone={decision?.kind === "approve" ? "primary" : "danger"}
      busy={busy} onCancel={() => setDecision(null)} onConfirm={() => { if (decision) void act(decision.kind, decision.proofAssetId); }} />
  </>;
}
