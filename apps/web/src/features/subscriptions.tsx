"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge } from "../components/dashboard-shell";
import { ManualPaymentReview, PaymentRequestQueue } from "./manual-payments";
import { CashPendingPayment } from "./cash-pending-payment";
import { GuardianShell } from "../components/guardian-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { apiProblemMessage } from "../lib/api-problem";
import { planLimits } from "./plan-catalog-values";
import { SubscriptionSessionGrant } from "./subscription-session-grant";
import { useGoalData } from "./player-goals";
import { renewalFromPaymentHref, renewalPaymentHref, renewalReceiptQuery } from "../lib/renewal-request-navigation";
import { RenewalList } from "./renewal-list";
import { paymentQueueBack } from "../lib/payment-queue-navigation";
import { periodListHref, periodLists, periodListScope, periodScopeQuery, type PeriodListKind } from "../lib/subscription-period-navigation";
import { formatDateAr, formatDateTimeAr, formatMoneyAr, planTypeLabel, statusLabel, workflowStatusLabels } from "../lib/formatters";

type Row = Record<string, string | number | boolean | null> & { id: string };
const listConfig = {
  current: { title: "الاشتراكات الحالية", endpoint: "/api/v1/subscriptions/periods?state=active" },
  expiring: { title: "الاشتراكات التي تنتهي قريبًا", endpoint: "/api/v1/subscriptions/periods?state=expiring" },
  expired: { title: "الاشتراكات المنتهية", endpoint: "/api/v1/subscriptions/periods?state=expired" },
} as const;
const money = formatMoneyAr;

export function SubscriptionOperationalList({ kind }: { kind: keyof typeof listConfig | "payments" | "renewals" }) {
  if (kind === "payments") return <PaymentRequestQueue />;
  if (kind === "renewals") return <RenewalList />;
  return <SubscriptionList key={kind} kind={kind} />;
}

function SubscriptionList({ kind }: { kind: keyof typeof listConfig }) {
  const config = listConfig[kind];
  const { term: search } = periodListScope(useSearchParams());
  const { data, error, status, reload } = useGoalData<Row[]>(config.endpoint, 0, "تعذر تحميل القائمة.");
  const term = search.trim().toLocaleLowerCase("ar");
  const filtered = data?.filter(row => [row.player, row.playerCode, row.sport, row.plan, row.reference]
    .some(value => typeof value === "string" && value.toLocaleLowerCase("ar").includes(term))) ?? [];
  const selectedQuery = periodScopeQuery(kind, search);
  function updateSearch(value: string) {
    window.history.replaceState(null, "", periodListHref(kind, value.slice(0, 120)));
  }

  return <>
    <PageHeader title={config.title} context="الأعضاء والاشتراكات" />
    <nav className="adjustment-actions" aria-label="حالة الاشتراكات">
      {(Object.keys(periodLists) as PeriodListKind[]).map(item => <Link key={item} aria-current={item === kind ? "page" : undefined}
        className={`button ${item === kind ? "button-primary" : "button-secondary"}`} href={periodListHref(item, search)}>{periodLists[item]}</Link>)}
    </nav>
    {error ? <><ErrorState message={status === 401 || status === 403 ? "قائمة الاشتراكات غير متاحة لحسابك." : error} />
      {status !== 401 && status !== 403 && <button type="button" className="secondary-button" onClick={reload}>إعادة المحاولة</button>}
    </> : !data ? <LoadingState /> : <>
      {(data.length > 0 || search) && <FilterToolbar onReset={search ? () => updateSearch("") : undefined}>
        <SearchInput value={search} onChange={updateSearch} label="بحث الاشتراكات"
          placeholder="اسم اللاعب أو كوده أو الرياضة أو الباقة" />
      </FilterToolbar>}
      <section className="list-card">
        {filtered.length === 0 ? <EmptyState message={term ? "لا توجد اشتراكات مطابقة للبحث." :
          kind === "expiring" ? "لا توجد اشتراكات سارية تنتهي خلال 7 أيام." :
          kind === "expired" ? "لا توجد اشتراكات منتهية." : "لا توجد اشتراكات حالية."} /> :
          <AdminDataTable label={config.title} rows={filtered} rowKey={row => row.id} columns={[
            { key: "player", header: "اللاعب", width: "minmax(155px, 1.3fr)", primary: true, render: row => <>{String(row.player ?? "—")}{row.playerCode && <small><bdi>{String(row.playerCode)}</bdi></small>}</> },
            { key: "context", header: "الرياضة والباقة", width: "minmax(170px, 1.3fr)", render: row => <>{String(row.sport ?? "—")}<small>{String(row.plan ?? "—")}</small></> },
            { key: "period", header: "الفترة", width: "minmax(145px, 1fr)", render: row =>
              <>{formatDateAr(String(row.startDate ?? ""))}<small>{row.endDate ? formatDateAr(String(row.endDate)) : "بالحصص"}</small></> },
            { key: "status", header: "الحالة", width: "120px", render: row => <StatusBadge status={String(row.status ?? "Pending")} label={workflowStatusLabels[String(row.status)]} /> },
            { key: "actions", header: "الإجراءات", width: "150px", render: row => <RowActions actions={[
              { label: "عرض الاشتراك", href: `/dashboard/subscriptions/periods/${row.id}?${selectedQuery}` },
              ...(row.canRenew === true ? [{ label: "تجديد", href: `/dashboard/subscriptions/renew/new?enrollmentId=${row.sportEnrollmentId}` }] : []),
            ]} /> },
          ]} />}
      </section>
    </>}
  </>;
}

export { RenewalDiscountDetail } from "./renewal-discount";
export { PlanList } from "./plan-catalog";
export { PlanForm } from "./plan-editor";

type Adjustment = { id: string; type: string; effectiveDate: string; daysDelta?: number; sessionsDelta?: number; oldRemainingSessions?: number; newRemainingSessions?: number; oldEndDate?: string; newEndDate?: string; reason: string; performedBy: string; performedAtUtc: string };
type PeriodDetails = { id: string; sportEnrollmentId: string; player: string; playerCode: string; sport: string; branch: string; group: string; plan: string; planType: "Duration" | "Sessions" | "Combined"; startDate: string; endDate?: string; frozenFromDate?: string; initialSessions?: number; remainingSessions?: number; status: string; version: number; asOfDate?: string; canRenew?: boolean; canPause?: boolean; canDeductDays?: boolean; canCancel?: boolean; canGrantSessions?: boolean; priceSnapshot: number | null; currencySnapshot: string; receiptId?: string; origin?: string; waiver?: { reason: string; waivedAmount: string; currency: string; approvedBy: string; createdAtUtc: string }; adjustments: Adjustment[] };
type ActionKind = "freeze" | "resume" | "deduct" | "cancel";
const adjustmentLabels: Record<string, string> = { FreezeStarted: "بدء التجميد", FreezeEnded: "استئناف الاشتراك", DaysAdded: "إضافة أيام", SessionsAdded: "إضافة حصص وتحديد نهاية الاشتراك", DaysDeducted: "خصم أيام", Cancelled: "إلغاء الاشتراك" };

function addCalendarDays(value: string, days: number) { const date = new Date(`${value}T00:00:00Z`); date.setUTCDate(date.getUTCDate() + days); return date.toISOString().slice(0, 10); }
function dayDifference(from: string, to: string) { return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000); }

export function SubscriptionPeriodDetail({ id, embedded = false }: { id: string; embedded?: boolean }) {
  const scope = periodListScope(useSearchParams());
  return <SubscriptionPeriodView key={id} id={id} embedded={embedded} scope={scope} />;
}

const periodActionTitles: Record<ActionKind, string> = {
  freeze: "تجميد الاشتراك", resume: "استئناف الاشتراك", deduct: "خصم أيام", cancel: "إلغاء الاشتراك",
};

function SubscriptionPeriodView({ id, embedded, scope }: { id: string; embedded: boolean; scope: ReturnType<typeof periodListScope> }) {
  const [record, setRecord] = useState<{ data?: PeriodDetails; message?: string; retry?: boolean }>();
  const [revision, setRevision] = useState(0);
  const [error, setError] = useState("");
  const [mode, setMode] = useState<ActionKind | null>(null);
  const [reason, setReason] = useState("");
  const [date, setDate] = useState("");
  const [days, setDays] = useState("");
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [sessionLocked, setSessionLocked] = useState(false);
  const saving = useRef(false);
  const reviewedVersion = useRef<number | undefined>(undefined);
  const data = record?.data;
  const endpoint = `/api/v1/subscriptions/periods/${encodeURIComponent(id)}`;
  const intentScope = `subscription-period:${id}`;

  function load() { setRecord(undefined); setRevision(n => n + 1); }
  useEffect(() => {
    const abort = new AbortController();
    void fetch(endpoint, { signal: abort.signal, cache: "no-store" }).then(async response => {
      if (abort.signal.aborted) return;
      if (response.status === 401 || response.status === 403 || response.status === 404) {
        setRecord({ message: "هذه الفترة غير متاحة لحسابك." });
        return;
      }
      if (!response.ok) throw Error();
      const period: PeriodDetails = await response.json();
      if (period?.id?.toLowerCase() !== id.toLowerCase() || !Array.isArray(period.adjustments)) throw Error();
      if (!abort.signal.aborted) setRecord({ data: period });
    }).catch(() => { if (!abort.signal.aborted) setRecord({ message: "تعذر تحميل تفاصيل الاشتراك.", retry: true }); });
    return () => abort.abort();
  }, [endpoint, id, revision]);

  const dated = data?.planType !== "Sessions" && Boolean(data?.endDate);
  const today = data?.asOfDate;
  const earliestResume = data?.frozenFromDate ? addCalendarDays(data.frozenFromDate, 1) : undefined;
  const canFreeze = data?.canPause && dated && data.status === "Active";
  const canResume = data?.canPause && data.status === "Frozen" && earliestResume && (!today || earliestResume <= today);
  const canCancel = data?.canCancel && data.status !== "Cancelled";
  const maxDays = data?.endDate && dated ? Math.min(365, dayDifference(data.startDate, data.endDate)) : 0;
  const canDeduct = data?.canDeductDays && data.status !== "Cancelled" && maxDays > 0;
  const dayCount = Number(days);
  const daysValid = Number.isInteger(dayCount) && dayCount >= 1 && dayCount <= maxDays;
  const earliestDate = mode === "freeze" ? data?.startDate : mode === "resume" ? earliestResume : undefined;
  const latestDate = mode === "freeze" || mode === "resume" ? today : undefined;
  const dateValid = Boolean(date && (!earliestDate || date >= earliestDate) && (!latestDate || date <= latestDate));
  const valid = mode === "deduct" ? daysValid : dateValid;
  const preview = data?.endDate && mode === "resume" && data.frozenFromDate && dateValid
    ? addCalendarDays(data.endDate, dayDifference(data.frozenFromDate, date))
    : data?.endDate && mode === "deduct" && daysValid ? addCalendarDays(data.endDate, -dayCount) : null;

  function choose(next: ActionKind) {
    reviewedVersion.current = data?.version;
    setMode(next); setReason(""); setDays(""); setError("");
    setDate(next === "resume" ? earliestResume ?? "" : "");
  }

  async function execute() {
    if (!data || !mode || !valid || saving.current || reviewedVersion.current === undefined) return;
    const version = reviewedVersion.current;
    saving.current = true; setBusy(true); setError("");
    try {
      const action = mode === "deduct" ? "days" : mode;
      const body = mode === "deduct" ? { direction: 2, days: dayCount, reason, expectedVersion: version } :
        { effectiveDate: date, reason, expectedVersion: version };
      const key = await pendingFinancialIntent(intentScope, { action, body });
      await csrfRequest(`${endpoint}/${action}`, "POST", body, { "Idempotency-Key": key });
      clearFinancialIntent(intentScope);
      setMode(null); setConfirming(false); load();
    } catch (caught) {
      setConfirming(false);
      setError(caught instanceof Error ? caught.message : "تعذر تأكيد تعديل الاشتراك.");
      if (caught instanceof DashboardRequestError && [401, 403, 404, 409].includes(caught.status)) {
        clearFinancialIntent(intentScope);
        setMode(null); load();
      }
    } finally {
      saving.current = false; setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !valid) return;
    if (mode === "resume") void execute();
    else setConfirming(true);
  }

  const dialog = mode === "cancel" ? {
    title: "تأكيد إلغاء الاشتراك", message: "سيتم إلغاء صلاحية هذه الفترة فقط. لن يتم حذف التحصيل أو الإيصال ولن يتم رد الأموال تلقائيًا.", label: "تأكيد الإلغاء",
  } : mode === "freeze" ? {
    title: "تأكيد تجميد الاشتراك", message: "سيُوقف استهلاك الحصص المؤهلة خلال التجميد، ولن يتغير التحصيل أو الإيصال.", label: "تأكيد التجميد",
  } : { title: "تأكيد خصم الأيام", message: `سيتم خصم ${days} يوم من هذه الفترة. يحتفظ النظام بالتحصيل والإيصال الأصليين.`, label: "تأكيد الخصم" };

  const receiptQuery = periodScopeQuery(scope.kind, scope.term);
  receiptQuery.set("periodId", id);
  return <>
    {!embedded && <PageHeader title="تفاصيل الاشتراك" context="الأعضاء والاشتراكات"
      back={{ label: `العودة إلى الاشتراكات ${periodLists[scope.kind]}`, href: periodListHref(scope.kind, scope.term) }} />}
    {error && <ErrorState message={error} />}
    {!data ? record ? <>
      <ErrorState message={record.message ?? "هذه الفترة غير متاحة لحسابك."} />
      {record.retry && <button type="button" className="secondary-button" onClick={load}>إعادة المحاولة</button>}
    </> : <LoadingState /> : <section className="detail-card subscription-period-detail">
      <div className="subscription-period-identity">
        <h2>{data.player} · <bdi>{data.playerCode}</bdi></h2>
        <StatusBadge status={data.status} label={statusLabel(data.status)} />
      </div>
      <div className="period-summary period-detail-facts">
        <div><span>الرياضة والمجموعة</span><b>{data.sport} · {data.group}</b></div>
        <div><span>الباقة</span><b>{data.plan} · {planTypeLabel(data.planType)}</b></div>
        <div><span>الفترة</span><b>{formatDateAr(data.startDate)} — {data.endDate ? formatDateAr(data.endDate) : "بالحصص"}</b></div>
        {data.remainingSessions != null && <div><span>الحصص المتبقية</span><b>{data.remainingSessions} حصة</b></div>}
        {data.frozenFromDate && <div><span>مجمد منذ</span><b>{formatDateAr(data.frozenFromDate)}</b></div>}
      </div>
      {data.origin === "ApprovedWaiver" && <p className="status-pill">تدريب مجاني بإعفاء معتمد — لا يوجد تحصيل أو إيصال</p>}
      {data.waiver && <section className="adjustment-history">
        <h3>سند الإعفاء</h3><p>{data.waiver.reason}</p>
        <p>{data.waiver.approvedBy} · {formatDateTimeAr(data.waiver.createdAtUtc)}</p>
        <p>القيمة المعفاة: {money(data.waiver.waivedAmount, data.waiver.currency)}</p>
      </section>}
      {(data.canRenew || data.receiptId) && <div className="adjustment-actions">
        {data.canRenew && <Link className="primary-action" href={`/dashboard/subscriptions/renew/new?enrollmentId=${data.sportEnrollmentId}`}>تجديد الاشتراك</Link>}
        {data.receiptId && <Link className="button button-secondary" href={`/dashboard/subscriptions/receipts/${data.receiptId}?${receiptQuery}`}>عرض الإيصال المحفوظ</Link>}
      </div>}
      {(canFreeze || canResume || canDeduct || canCancel) && <div className="adjustment-actions">
        {canFreeze && <button type="button" disabled={sessionLocked || busy} onClick={() => choose("freeze")}>تجميد</button>}
        {canResume && <button type="button" disabled={sessionLocked || busy} onClick={() => choose("resume")}>استئناف</button>}
        {canDeduct && <button type="button" disabled={sessionLocked || busy} onClick={() => choose("deduct")}>خصم أيام</button>}
        {canCancel && <button type="button" disabled={sessionLocked || busy} className="danger-button" onClick={() => choose("cancel")}>إلغاء الاشتراك</button>}
      </div>}
      {data.canPause && data.status === "Frozen" && earliestResume && !canResume &&
        <p className="form-hint">يمكن الاستئناف ابتداءً من {formatDateAr(earliestResume)}.</p>}
      <SubscriptionSessionGrant key={data.id} period={data} onSaved={load} onOpen={() => setMode(null)} onPendingChange={setSessionLocked} />
      {data.planType === "Sessions" && <p className="form-hint">الباقة بالحصة فقط لا تحتوي مدة زمنية قابلة للتجميد أو تعديل الأيام.</p>}
      {mode && <form className="adjustment-form" onSubmit={submit}>
        <h2>{periodActionTitles[mode]}</h2>
        {mode === "deduct" ? <label>عدد الأيام<input aria-label="عدد الأيام" type="number" min="1" max={maxDays} required
          value={days} onChange={event => setDays(event.target.value)} />
          <small>الحد الأقصى لهذه الفترة {maxDays} يوم.</small></label>
          : <label>{mode === "resume" ? "تاريخ الاستئناف" : mode === "freeze" ? "تاريخ بدء التجميد" : "تاريخ سريان الإلغاء"}
            <input aria-label="التاريخ الفعلي" type="date" min={earliestDate} max={latestDate} required value={date} onChange={event => setDate(event.target.value)} />
          </label>}
        <label>سبب التعديل (اختياري)<textarea aria-label="سبب التعديل" maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></label>
        {preview && <p className="adjustment-preview">النهاية المتوقعة {mode === "resume" ? "بعد الاستئناف" : "بعد الخصم"} <b>{formatDateAr(preview)}</b> — الحساب النهائي من الخادم.</p>}
        <div className="form-actions">
          <button className="primary-button" disabled={busy || !valid}>{busy ? "جارٍ الحفظ…" : "متابعة"}</button>
          <button type="button" disabled={busy} onClick={() => setMode(null)}>إلغاء</button>
        </div>
      </form>}
      <section className="adjustment-history">
        <h2>سجل التعديلات</h2>
        {data.adjustments.length === 0 ? <p className="muted">لا توجد تعديلات على هذه الفترة.</p> : data.adjustments.map(item => <article key={item.id}>
          <div><b>{adjustmentLabels[item.type] ?? "تعديل اشتراك"}</b><small>{formatDateAr(item.effectiveDate)} · {item.performedBy}</small></div>
          {item.reason && <p>{item.reason}</p>}
          {item.sessionsDelta != null && <p>أضيفت {item.sessionsDelta} حصة · الرصيد {item.oldRemainingSessions} ← {item.newRemainingSessions}</p>}
          {item.type === "SessionsAdded" && item.newEndDate && <p>نهاية الاشتراك كله: {formatDateAr(item.newEndDate)}</p>}
          {item.daysDelta != null && <span>{item.daysDelta > 0 ? `+${item.daysDelta}` : item.daysDelta} يوم</span>}
          {item.oldEndDate && item.newEndDate && item.type !== "SessionsAdded" && <p>النهاية من {formatDateAr(item.oldEndDate)} إلى {formatDateAr(item.newEndDate)}</p>}
        </article>)}
      </section>
    </section>}
    <ConfirmationDialog open={confirming} title={dialog.title} message={dialog.message} confirmLabel={dialog.label} busy={busy}
      onConfirm={() => void execute()} onCancel={() => setConfirming(false)} />
  </>;
}

type GuardianPeriod = { id: string; plan: string; sport: string; startDate: string; endDate?: string; frozenFromDate?: string; remainingSessions?: number; status: string; origin?: string; adjustments: { type: string; effectiveDate: string; daysDelta?: number; sessionsDelta?: number; oldRemainingSessions?: number; newRemainingSessions?: number; newEndDate?: string }[] };
export function GuardianSubscriptionStatus({ id, childId }: { id: string; childId?: string }) {
  const endpoint = `/api/v1/guardian/subscriptions/periods/${encodeURIComponent(id)}/adjustments`;
  const { data, error, status, reload } = useGoalData<GuardianPeriod>(endpoint, 0, "تعذر تحميل حالة الاشتراك.");
  const period = data?.id === id && typeof data.status === "string" && Array.isArray(data.adjustments) ? data : null;
  const unavailable = status === 401 || status === 403 || status === 404 || (status === 200 && !period);

  return <GuardianShell>
    <Link className="back-link" href={childId ? `/guardian/children/${encodeURIComponent(childId)}` : "/guardian#children"}>{childId ? "العودة لملف الطفل" : "العودة إلى الأبناء"}</Link>
    <header className="guardian-page-header">
      {period && <span>{period.sport}</span>}
      <h1>حالة الاشتراك</h1>
      {period && <p>{period.plan}</p>}
      {period?.origin === "ApprovedWaiver" && <p>تدريب مجاني بإعفاء معتمد</p>}
    </header>
    {unavailable ? <ErrorState message="هذا الاشتراك غير متاح." /> : error ? <div className="profile-section">
      <ErrorState message={error} />
      <button type="button" className="secondary-button" onClick={reload}>إعادة المحاولة</button>
    </div> : !period ? <LoadingState message="جارٍ تحميل حالة الاشتراك…" /> : <>
      <section className="profile-section" aria-label="بيانات فترة الاشتراك">
        <div className="period-summary">
          <div><span>الحالة الحالية</span><b className={`status-pill ${period.status.toLowerCase()}`}>{statusLabel(period.status)}</b></div>
          <div><span>بداية الاشتراك</span><b>{formatDateAr(period.startDate)}</b></div>
          {period.endDate && <div><span>نهاية الاشتراك</span><b>{formatDateAr(period.endDate)}</b></div>}
          {period.frozenFromDate && <div><span>مجمد منذ</span><b>{formatDateAr(period.frozenFromDate)}</b></div>}
          {period.remainingSessions != null && <div><span>الحصص المتبقية</span><b>{period.remainingSessions}</b></div>}
        </div>
      </section>
      <section className="profile-section adjustment-history">
        <h2>آخر تحديثات الاشتراك</h2>
        {period.adjustments.length === 0 ? <EmptyState message="لا توجد تعديلات على هذه الفترة." /> : period.adjustments.map((item, index) => <article key={`${item.type}-${item.effectiveDate}-${index}`}>
          <div><b>{adjustmentLabels[item.type] ?? "تعديل اشتراك"}</b><small>{formatDateAr(item.effectiveDate)}</small></div>
          {item.type === "FreezeStarted" && <p>الاشتراك مجمد.</p>}
          {item.type === "FreezeEnded" && <p>تم استئناف الاشتراك{item.newEndDate ? ` حتى ${formatDateAr(item.newEndDate)}` : ""}.</p>}
          {item.type === "SessionsAdded" && <p>أضيفت {item.sessionsDelta} حصة إلى الرصيد ({item.oldRemainingSessions} ← {item.newRemainingSessions}){item.newEndDate ? ` ونهاية الاشتراك كله ${formatDateAr(item.newEndDate)}` : ""}.</p>}
          {item.type === "DaysAdded" && <p>تم تمديد الاشتراك{item.newEndDate ? ` حتى ${formatDateAr(item.newEndDate)}` : ""}.</p>}
          {item.type === "DaysDeducted" && <p>تم تعديل نهاية الاشتراك{item.newEndDate ? ` إلى ${formatDateAr(item.newEndDate)}` : ""}.</p>}
          {item.type === "Cancelled" && <p>الاشتراك ملغي.</p>}
        </article>)}
      </section>
    </>}
  </GuardianShell>;
}

type AdminRenewalEnrollmentRow = {
  id: string;
  playerId: string;
  playerCode: string;
  player: string;
  sport: string;
  branch: string;
  group: string;
  guardianPhone?: string;
  period?: { id: string; plan: string; startDate: string; endDate?: string; status: string };
};
type AdminRenewalPlan = { id: string; arabicName: string; planType: string; price: number; currency: string; durationDays?: number; durationMonths?: number; chargedMonths?: number; sessionCount?: number };
type AdminRenewalQuote = { enrollmentId: string; planId: string; quoteHash: string; start: string; end: string | null; sessions: number | null; originalAmount: number; discountAmount: number; finalAmount: number; currency: string };
type AdminRenewalContext = {
  enrollment: Omit<AdminRenewalEnrollmentRow, "period" | "guardianPhone"> & { sportId: string };
  current?: { id: string; plan: string; startDate: string; endDate?: string; status: string };
  previewStart: string;
  plans: AdminRenewalPlan[];
  openRenewal?: { kind: "Single" | "Family"; paymentId?: string | null } | null;
  canReadPayment?: boolean;
};

const adminRenewalRoute = "/dashboard/subscriptions/renew/new";
const adminRenewalApi = "/api/v1/subscriptions/admin-renewals";
const adminRenewalSearchLimit = 50;

export function AdminRenewalFlow() {
  const params = useSearchParams();
  const enrollmentId = params.get("enrollmentId") ?? "";
  const [search, setSearch] = useState("");

  return <>
    <PageHeader title={enrollmentId ? "مراجعة التجديد" : "تجديد اشتراك"} context="الاشتراكات"
      secondaryActions={enrollmentId
        ? [{ label: "العودة إلى البحث", href: adminRenewalRoute }]
        : [{ label: "تجديد عدة أبناء", href: "/dashboard/family-renewals/quote" }]} />
    {enrollmentId
      ? <AdminRenewalReview key={enrollmentId} enrollmentId={enrollmentId} />
      : <AdminRenewalSearch search={search} onSearch={setSearch} />}
  </>;
}

function AdminRenewalSearch({ search, onSearch }: { search: string; onSearch: (value: string) => void }) {
  const router = useRouter();
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ search: string; revision: number; rows?: AdminRenewalEnrollmentRow[]; error?: string }>();
  const current = result?.search === search && result.revision === revision ? result : undefined;
  const capped = (current?.rows?.length ?? 0) >= adminRenewalSearchLimit;

  useEffect(() => {
    const controller = new AbortController();
    fetch(`${adminRenewalApi}/enrollments?search=${encodeURIComponent(search)}`, { signal: controller.signal, cache: "no-store" })
      .then(async response => {
        if (!response.ok) throw new Error();
        const rows = await response.json() as AdminRenewalEnrollmentRow[];
        if (!controller.signal.aborted) setResult({ search, revision, rows });
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ search, revision, error: "تعذر تحميل تسجيلات اللاعبين." });
      });
    return () => controller.abort();
  }, [search, revision]);

  return <>
    <FilterToolbar resultCount={capped ? undefined : current?.rows?.length} onReset={search ? () => onSearch("") : undefined}>
      <SearchInput value={search} onChange={onSearch} label="البحث عن لاعب" placeholder="اسم اللاعب أو الكود أو هاتف ولي الأمر" />
    </FilterToolbar>
    <section className="list-card">
      <h2>اختر تسجيل اللاعب</h2>
      {capped && <p className="muted">تُعرض حتى {adminRenewalSearchLimit} نتيجة. ضيّق البحث بالاسم أو كود اللاعب.</p>}
      {current?.error ? <>
        <ErrorState message={current.error} />
        <button className="button button-secondary" type="button" onClick={() => setRevision(value => value + 1)}>إعادة المحاولة</button>
      </> : !current?.rows ? <LoadingState message="جارٍ تحميل تسجيلات اللاعبين…" />
      : current.rows.length === 0 ? <EmptyState message={search ? "لا توجد تسجيلات مطابقة. جرّب الاسم أو كود اللاعب." : "لا توجد تسجيلات متاحة للتجديد."} />
      : <AdminDataTable label="اختيار اشتراك للتجديد" rows={current.rows} rowKey={row => row.id} columns={[
        { key: "player", header: "اللاعب", primary: true, render: row => <><strong>{row.player}</strong><small><bdi>{row.playerCode}</bdi></small></> },
        { key: "sport", header: "الرياضة", render: row => row.sport },
        { key: "group", header: "الفرع والمجموعة", render: row => <>{row.branch}<small>{row.group}</small></> },
        { key: "plan", header: "الاشتراك الحالي", render: row => row.period ? <>{row.period.plan}<small>{statusLabel(row.period.status)}{row.period.endDate && ` · حتى ${formatDateAr(row.period.endDate)}`}</small></> : "دون اشتراك سابق" },
        { key: "action", header: "الإجراء", render: row => <button className="button button-secondary" type="button" onClick={() => router.push(`${adminRenewalRoute}?enrollmentId=${encodeURIComponent(row.id)}`)}>اختيار</button> },
      ]} />}
    </section>
  </>;
}

function AdminRenewalReview({ enrollmentId }: { enrollmentId: string }) {
  const router = useRouter();
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ revision: number; context?: AdminRenewalContext; error?: string; retryable?: boolean }>();
  const [selectedPlan, setSelectedPlan] = useState("");
  const [quoteRevision, setQuoteRevision] = useState(0);
  const [quoteRead, setQuoteRead] = useState<{ key: string; quote?: AdminRenewalQuote; error?: string; retry?: boolean }>();
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState("");
  const [retryStep, setRetryStep] = useState<"create" | "read" | null>(null);
  const [confirmed, setConfirmed] = useState<{ paymentId: string | null } | null>(null);
  const createdRequest = useRef<{ paymentId: string; renewalId: string } | null>(null);
  const submitting = useRef(false);
  const mounted = useRef(false);
  const current = result?.revision === revision ? result : undefined;
  const context = current?.context;
  const plan = context?.plans.find(item => item.id === selectedPlan);
  const selectedEnrollmentId = !context?.openRenewal ? context?.enrollment.id : undefined;
  const planId = plan?.id;
  const quoteKey = `${selectedEnrollmentId}:${planId}:${revision}:${quoteRevision}`;
  const currentQuote = quoteRead?.key === quoteKey ? quoteRead : undefined;
  const quote = currentQuote?.quote;

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch(`${adminRenewalApi}/enrollments/${encodeURIComponent(enrollmentId)}`, { signal: controller.signal, cache: "no-store" })
      .then(async response => {
        if (controller.signal.aborted) return;
        if (response.status === 403 || response.status === 404) {
          setResult({ revision, error: "هذا التسجيل غير متاح للتجديد." });
          return;
        }
        if (!response.ok) throw new Error();
        const context = await response.json() as AdminRenewalContext;
        if (!controller.signal.aborted) setResult({ revision, context });
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ revision, error: "تعذر تحميل بيانات التجديد.", retryable: true });
      });
    return () => controller.abort();
  }, [enrollmentId, revision]);

  useEffect(() => {
    if (!selectedEnrollmentId || !planId) return;
    const controller = new AbortController();
    const path = `${adminRenewalApi}/enrollments/${encodeURIComponent(selectedEnrollmentId)}/quote?planId=${encodeURIComponent(planId)}`;
    fetch(path, { signal: controller.signal, cache: "no-store" }).then(async response => {
      if (controller.signal.aborted) return;
      if ([404, 409].includes(response.status)) {
        const message = await apiProblemMessage(response, "لم تعد هذه الباقة متاحة. اختر من الباقات المحدّثة.");
        if (controller.signal.aborted) return;
        setSelectedPlan("");
        setRevision(value => value + 1);
        setSubmitError(message);
        return;
      }
      if ([401, 403].includes(response.status)) {
        setQuoteRead({ key: quoteKey, error: "عرض التجديد غير متاح لحسابك." });
        return;
      }
      if (!response.ok) throw new Error();
      const value = await response.json() as AdminRenewalQuote;
      if (value?.enrollmentId !== selectedEnrollmentId || value.planId !== planId || !/^[a-f0-9]{64}$/i.test(value.quoteHash) ||
        typeof value.start !== "string" || !value.start || typeof value.currency !== "string" || !value.currency ||
        ![value.originalAmount, value.discountAmount, value.finalAmount].every(amount => Number.isFinite(amount) && amount >= 0) ||
        Math.round(value.originalAmount * 100) - Math.round(value.discountAmount * 100) !== Math.round(value.finalAmount * 100)) throw new Error();
      if (!controller.signal.aborted) setQuoteRead({ key: quoteKey, quote: value });
    }).catch(() => {
      if (!controller.signal.aborted) setQuoteRead({ key: quoteKey, error: "تعذر تأكيد تفاصيل عرض التجديد.", retry: true });
    });
    return () => controller.abort();
  }, [selectedEnrollmentId, planId, quoteKey]);

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!context || context.openRenewal || !plan || !quote || submitting.current || confirmed) return;
    submitting.current = true;
    setBusy(true);
    setSubmitError("");
    const body = { sportEnrollmentId: context.enrollment.id, subscriptionPlanId: plan.id, expectedQuoteHash: quote.quoteHash };
    const scope = `admin-renewal:${body.sportEnrollmentId}:${body.subscriptionPlanId}`;
    try {
      if (!createdRequest.current) {
        const key = await pendingFinancialIntent(scope, body);
        const response = await csrfRequest(adminRenewalApi, "POST", body, { "Idempotency-Key": key }, { announce: false });
        const result = await response.json().catch(() => null) as { paymentId?: unknown; renewalId?: unknown } | null;
        if (typeof result?.paymentId !== "string" || !result.paymentId || typeof result.renewalId !== "string" || !result.renewalId) throw new Error();
        createdRequest.current = { paymentId: result.paymentId, renewalId: result.renewalId };
      }
      const request = createdRequest.current;
      if (context.canReadPayment !== false) {
        const response = await fetch(adminPaymentPath(request.paymentId), { cache: "no-store" });
        if (!response.ok) throw new Error();
        const payment = await response.json();
        if (payment?.id !== request.paymentId || payment.renewalId !== request.renewalId ||
          payment.sportEnrollmentId !== body.sportEnrollmentId || payment.subscriptionPlanId !== body.subscriptionPlanId ||
          payment.amount !== quote.finalAmount || payment.currency !== quote.currency ||
          payment.isCurrent !== true || !["Pending", "Confirmed"].includes(payment.status)) throw new Error();
      }
      clearFinancialIntent(scope);
      if (mounted.current) {
        const paymentId = context.canReadPayment === false ? null : request.paymentId;
        setConfirmed({ paymentId });
        if (paymentId) router.push(adminPaymentPage(paymentId));
      }
    } catch (caught) {
      if (!mounted.current) return;
      const rejected = !createdRequest.current && caught instanceof DashboardRequestError && caught.status < 500 && caught.status !== 408 && caught.status !== 499;
      setRetryStep(rejected ? null : createdRequest.current ? "read" : "create");
      if (rejected) {
        clearFinancialIntent(scope);
        if ([404, 409].includes(caught.status)) {
          setSelectedPlan("");
          setRevision(value => value + 1);
        }
      }
      setSubmitError(rejected ? caught.message : createdRequest.current ? "تعذر التحقق من مطابقة طلب الدفع. أعد قراءة نفس الطلب." : "تعذر تأكيد إنشاء الطلب. أعد المحاولة لنفس التسجيل والباقة.");
    } finally {
      submitting.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  if (current?.error) return <section className="detail-card">
    <ErrorState message={current.error} />
    {current.retryable && <button className="button button-secondary" type="button" onClick={() => setRevision(value => value + 1)}>إعادة المحاولة</button>}
  </section>;
  if (!context) return <LoadingState message="جارٍ تحميل بيانات التجديد…" />;

  return <section className="detail-card subscription-period-detail admin-renewal-review">
    <div className="period-summary">
      <div><span>اللاعب</span><b>{context.enrollment.player} · <bdi>{context.enrollment.playerCode}</bdi></b></div>
      <div><span>التسجيل</span><b>{context.enrollment.sport} · {context.enrollment.branch} · {context.enrollment.group}</b></div>
      <div><span>الاشتراك الحالي</span><b>{context.current ? `${context.current.plan} · ${statusLabel(context.current.status)}` : "لا يوجد اشتراك سابق"}</b></div>
      {context.current?.endDate && <div><span>نهاية الاشتراك الحالي</span><b>{formatDateAr(context.current.endDate)}</b></div>}
    </div>
    {context.openRenewal ? <div className="compact-notice">
      <p>{context.openRenewal.kind === "Family" ? "هذا التسجيل ضمن طلب تجديد أسرة مفتوح." : "يوجد طلب تجديد مفتوح لهذا التسجيل."}</p>
      {context.openRenewal.paymentId ? <Link className="button button-primary" href={adminPaymentPage(context.openRenewal.paymentId)}>فتح طلب الدفع الحالي</Link>
        : context.openRenewal.kind === "Single" && <p>يتابع طلب الدفع موظف لديه صلاحية المدفوعات.</p>}
    </div> : confirmed ? <div className="compact-notice" role="status">
      <p>تم إنشاء طلب التجديد.</p>
      {confirmed.paymentId ? <Link className="button button-primary" href={adminPaymentPage(confirmed.paymentId)}>فتح طلب الدفع</Link> : <p>يتابع طلب الدفع موظف لديه صلاحية المدفوعات.</p>}
    </div> : context.plans.length === 0 ? <EmptyState message="لا توجد باقات متاحة لهذا التسجيل." /> : <form onSubmit={create}>
      <fieldset className="renewal-plan-picker" disabled={busy || retryStep !== null}>
        <legend>اختر الباقة</legend>
        <div className="choice-list">{context.plans.map(item => <label key={item.id}>
          <input type="radio" name="admin-renewal-plan" checked={selectedPlan === item.id} onChange={() => { setSelectedPlan(item.id); setSubmitError(""); }} />
          <strong>{item.arabicName}</strong><span>{[
            planTypeLabel(item.planType),
            planLimits(item),
            item.chargedMonths ? `الأشهر المدفوعة ${item.chargedMonths}` : null,
            formatMoneyAr(item.price, item.currency),
          ].filter(Boolean).join(" · ")}</span>
        </label>)}</div>
      </fieldset>
      {plan && (currentQuote?.error ? <>
        <ErrorState message={currentQuote.error} />
        {currentQuote.retry && <button className="button button-secondary" type="button" onClick={() => setQuoteRevision(value => value + 1)}>إعادة قراءة العرض</button>}
      </> : quote ? <section className="detail-card" aria-label="عرض التجديد">
        <h3>المبلغ والفترة المتوقعة</h3>
        <div className="period-summary">
          <div><span>بداية الفترة المتوقعة</span><b>{formatDateAr(quote.start)}</b></div>
          {quote.end && <div><span>نهاية الفترة المتوقعة</span><b>{formatDateAr(quote.end)}</b></div>}
          {quote.sessions != null && <div><span>الحصص</span><b>{quote.sessions}</b></div>}
          {quote.discountAmount > 0 && <>
            <div><span>سعر الباقة</span><b>{formatMoneyAr(quote.originalAmount, quote.currency)}</b></div>
            <div><span>الخصم</span><b>{formatMoneyAr(quote.discountAmount, quote.currency)}</b></div>
          </>}
        </div>
        <div className="payment-review-total"><div><span>المبلغ المطلوب</span><strong>{formatMoneyAr(quote.finalAmount, quote.currency)}</strong></div></div>
      </section> : <LoadingState message="جارٍ قراءة عرض التجديد…" />)}
      <p className="compact-notice" role="note">يبدأ التجديد بعد تأكيد الدفع. تُحدد تواريخ الفترة عند التأكيد.</p>
      {submitError && <ErrorState message={submitError} />}
      <div className="form-actions">
        <button className="button button-primary" type="submit" disabled={!quote || busy}>
          {busy ? "جارٍ متابعة الطلب…" : retryStep === "read" ? "التحقق من طلب الدفع" : retryStep === "create" ? "إعادة محاولة الإنشاء" : "إنشاء طلب التجديد"}
        </button>
      </div>
    </form>}
  </section>;
}

type AdminPaymentData = { provider?: string; canCollect?: boolean; canCollectCash?: boolean; canReviewProof?: boolean; canRetry?: boolean; canSimulate?: boolean; canEditDiscount?: boolean; branchId?: string; sportId?: string; groupId?: string; id: string; renewalId: string; providerReference: string; amount: number; currency: string; status: string; renewalStatus: string; player: string; playerCode: string; sport: string; plan: string; originalAmount: number; discountType?: string; discountValue?: number; discountReason?:string; discountAmount: number; finalAmount: number; createdAtUtc: string; confirmedAtUtc?: string; currentPaymentId?: string; isCurrent: boolean; receiptId?: string };
const adminPaymentPath = (id: string) => `/api/v1/subscriptions/payments/${encodeURIComponent(id)}`;
const adminPaymentPage = (id: string) => `/dashboard/subscriptions/payments/${encodeURIComponent(id)}`;

type PaymentAction = "online" | "retry" | "Success" | "Failed";

export function AdminPaymentDetail({ id }: { id: string }) {
  return <AdminPaymentScreen key={id} id={id} />;
}

function AdminPaymentScreen({ id }: { id: string }) {
  const router = useRouter();
  const query = useSearchParams();
  const queueBack = paymentQueueBack(query);
  const paymentPage = (paymentId: string) => renewalPaymentHref(paymentId, query);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ revision: number; data?: AdminPaymentData; error?: string; retryable?: boolean }>();
  const [actionError, setActionError] = useState("");
  const [busy, setBusy] = useState(false);
  const [confirmOnline, setConfirmOnline] = useState(false);
  const writing = useRef(false);
  const mounted = useRef(false);
  const current = result?.revision === revision ? result : undefined;
  const data = current?.data;
  const refresh = useCallback(async () => setRevision(value => value + 1), []);

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch(adminPaymentPath(id), { signal: controller.signal, cache: "no-store" })
      .then(async response => {
        if (controller.signal.aborted) return;
        if (response.status === 403 || response.status === 404) {
          setResult({ revision, error: "طلب الدفع غير متاح." });
          return;
        }
        if (!response.ok) throw new Error();
        const data = await response.json() as AdminPaymentData;
        if (!controller.signal.aborted) setResult({ revision, data });
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ revision, error: "تعذر تحميل طلب الدفع.", retryable: true });
      });
    return () => controller.abort();
  }, [id, revision]);

  const pending = data?.isCurrent === true && data.status === "Pending";
  const hasVerifiedAmount = typeof data?.amount === "number" && Number.isFinite(data.amount) && data.amount >= 0 &&
    typeof data.currency === "string" && /^[A-Z]{3}$/.test(data.currency);
  const canCollect = pending && hasVerifiedAmount && data?.canCollect === true;
  const canSimulate = pending && hasVerifiedAmount && data?.canSimulate === true;
  const canRetry = data?.isCurrent === true && data.canRetry === true;

  async function submit(action: PaymentAction) {
    if (!data || writing.current) return;
    if (action === "online" && !canCollect || action === "retry" && !canRetry || (action === "Success" || action === "Failed") && !canSimulate) return;
    writing.current = true;
    setBusy(true);
    setActionError("");
    let navigating = false;
    try {
      if (action === "retry") {
        const scope = `admin-payment-retry:${id}`;
        const body = {};
        const key = await pendingFinancialIntent(scope, body);
        const response = await csrfRequest(`${adminPaymentPath(id)}/retry`, "POST", body, { "Idempotency-Key": key }, { announce: false });
        const replacement = await response.json().catch(() => null) as { paymentId?: unknown } | null;
        if (typeof replacement?.paymentId !== "string" || !replacement.paymentId) throw new Error();
        clearFinancialIntent(scope);
        if (mounted.current) {
          router.push(paymentPage(replacement.paymentId));
          navigating = true;
        }
        return;
      }
      if (action === "online") {
        await csrfRequest(`/api/v1/manage/payments/${encodeURIComponent(id)}/collect`, "POST", {
          paymentMethod: "Online",
          expectedAmount: String(data.amount),
        }, {}, { announce: false });
      } else {
        await csrfRequest(`${adminPaymentPath(id)}/simulate`, "POST", { outcome: action }, {}, { announce: false });
      }
      if (mounted.current) await refresh();
    } catch (caught) {
      if (!mounted.current) return;
      setActionError(caught instanceof DashboardRequestError ? caught.message : "تعذر تأكيد العملية. راجع حالة طلب الدفع قبل المحاولة مرة أخرى.");
      if (action !== "retry") await refresh();
    } finally {
      writing.current = navigating;
      if (mounted.current && !navigating) {
        setBusy(false);
        setConfirmOnline(false);
      }
    }
  }

  return <>
    <PageHeader title="طلب دفع التجديد" context="طلبات الدفع" back={queueBack} />
    {actionError && <ErrorState message={actionError} />}
    {current?.error ? <section className="detail-card">
      <ErrorState message={current.error} />
      {current.retryable && <button className="button button-secondary" type="button" onClick={() => void refresh()}>إعادة تحميل الطلب</button>}
    </section> : !data ? <LoadingState message="جارٍ تحميل طلب الدفع…" /> : <>
      <section className="detail-card subscription-period-detail admin-renewal-review">
        <div className="payment-review-total">
          <div>
            <span>{data.status === "Confirmed" ? "المبلغ المحصّل" : pending ? "المبلغ المطلوب" : "مبلغ الطلب"}</span>
            <strong>{hasVerifiedAmount ? formatMoneyAr(data.amount, data.currency) : "غير متاح"}</strong>
          </div>
          <StatusBadge status={data.status} label={statusLabel(data.status)} />
        </div>
        <div className="period-summary">
          <div><span>اللاعب</span><b>{data.player} · <bdi dir="ltr">{data.playerCode}</bdi></b></div>
          <div><span>الرياضة والباقة</span><b>{data.sport} · {data.plan}</b></div>
          <div><span>حالة التجديد</span><StatusBadge status={data.renewalStatus} label={statusLabel(data.renewalStatus)} /></div>
          <div><span>تاريخ الطلب</span><b>{formatDateTimeAr(data.createdAtUtc)}</b></div>
          {hasVerifiedAmount && data.isCurrent && data.discountAmount > 0 && <>
            <div><span>قبل الخصم</span><b>{formatMoneyAr(data.originalAmount, data.currency)}</b></div>
            <div><span>الخصم</span><b>{formatMoneyAr(data.discountAmount, data.currency)}{data.discountReason && ` · ${data.discountReason}`}</b></div>
          </>}
        </div>
        {!data.isCurrent && <p className="compact-notice">هذا طلب سابق للتجديد.</p>}
        <div className="adjustment-actions">
          {!data.isCurrent && data.currentPaymentId && <Link className="button button-primary" href={paymentPage(data.currentPaymentId)}>فتح طلب الدفع الحالي</Link>}
          {data.receiptId && <Link className="button button-primary" href={`/dashboard/subscriptions/receipts/${encodeURIComponent(data.receiptId)}?${renewalReceiptQuery(id, query)}`}>عرض الإيصال</Link>}
          {canCollect && <button className="button button-primary" type="button" disabled={busy} onClick={() => setConfirmOnline(true)}>تأكيد تحصيل إلكتروني</button>}
          {canRetry && <button className="button button-primary" type="button" disabled={busy} onClick={() => void submit("retry")}>{busy ? "جارٍ فتح محاولة الدفع…" : "إعادة محاولة الدفع"}</button>}
          {pending && <Link className="button button-secondary" href={renewalFromPaymentHref(data.renewalId, query)}>{data.canEditDiscount === true ? "إدارة الخصم" : "تفاصيل التجديد"}</Link>}
        </div>
      </section>
      <ManualPaymentReview id={id} branchId={data.branchId} sportId={data.sportId} groupId={data.groupId} allowReview={pending && data.canReviewProof === true}
        amount={data.amount} currency={data.currency} onReviewStarted={() => setActionError("")} onReviewed={async (error) => {
          setActionError(error ?? "");
          await refresh();
        }} />
      {pending && hasVerifiedAmount && data.canCollectCash === true && <CashPendingPayment id={id} onCollected={refresh} />}
      {canSimulate && <section className="detail-card" aria-labelledby="payment-simulation-heading">
        <h2 id="payment-simulation-heading">محاكاة الدفع التجريبي</h2>
        <p className="compact-notice">تُسجّل نتيجة المحاكاة في الديمو فقط.</p>
        <div className="adjustment-actions">
          <button className="button button-secondary" type="button" disabled={busy} onClick={() => void submit("Success")}>محاكاة نجاح الدفع</button>
          <button className="button button-secondary" type="button" disabled={busy} onClick={() => void submit("Failed")}>محاكاة فشل الدفع</button>
        </div>
      </section>}
      <ConfirmationDialog open={confirmOnline} title="تأكيد التحصيل الإلكتروني"
        message={`أكد استلام ${formatMoneyAr(data.amount, data.currency)} إلكترونيًا. سيُسجّل التحصيل ويبدأ التجديد حسب الفترة المحددة.`}
        confirmLabel="تسجيل التحصيل" tone="primary" busy={busy}
        onConfirm={() => void submit("online")} onCancel={() => setConfirmOnline(false)} />
    </>}
  </>;
}
