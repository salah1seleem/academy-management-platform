"use client";

import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { clearFinancialIntent, pendingFinancialIntent, readFinancialIntentKey } from "../lib/pending-financial-intent";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./family-renewal-quote.module.css";

type Options = {
  enrollment: { id: string; playerId: string; player: string; sport: string; branch: string };
  plans: { id: string; arabicName: string; price: string; currency: string }[];
};
type Quote = {
  quoteHash: string; plan: string; waivedAmount: string; payable: string; currency: string;
  startDate: string; endDate?: string; sessions?: number; reason: string;
};
type Outcome = { sportEnrollmentId: string; playerId: string; periodId: string; source: string };
type AttemptCheck = "missing" | "unavailable" | "denied";
const api = "/api/v1/finance/free-training";

async function readOutcome(response: Response, enrollmentId: string): Promise<Outcome> {
  const value: Outcome = await response.json();
  if (value?.sportEnrollmentId !== enrollmentId || value.source !== "ApprovedWaiver" ||
      typeof value.playerId !== "string" || !value.playerId ||
      typeof value.periodId !== "string" || !value.periodId) throw Error();
  return value;
}

export function FreeTraining({ enrollmentId }: { enrollmentId: string }) {
  const id = enrollmentId.toLowerCase();
  return <FreeTrainingForm key={id} enrollmentId={id} />;
}

function FreeTrainingForm({ enrollmentId }: { enrollmentId: string }) {
  const [options, setOptions] = useState<{ data?: Options; error?: string; retry?: boolean }>();
  const [optionsRevision, setOptionsRevision] = useState(0);
  const [error, setError] = useState("");
  const [plan, setPlan] = useState("");
  const [reason, setReason] = useState("");
  const [quote, setQuote] = useState<Quote | null>(null);
  const [busy, setBusy] = useState(false);
  const [consent, setConsent] = useState(false);
  const [saved, setSaved] = useState<Outcome | null>(null);
  const [attemptKey, setAttemptKey] = useState<string | null>();
  const [attemptCheck, setAttemptCheck] = useState<AttemptCheck>();
  const [checkRevision, setCheckRevision] = useState(0);
  const sending = useRef(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const errorRegion = useRef<HTMLDivElement>(null);
  const intentScope = `free-training:${enrollmentId}`;
  const data = options?.data;
  const selected = data?.plans.find(option => option.id === plan);
  const canReview = !!selected && !!reason.trim();

  function refreshOptions() {
    setOptions(undefined);
    setOptionsRevision(value => value + 1);
  }

  useEffect(() => {
    let active = true;
    queueMicrotask(() => { if (active) setAttemptKey(readFinancialIntentKey(intentScope)); });
    return () => { active = false; };
  }, [intentScope]);

  useEffect(() => {
    if (attemptKey !== null || saved) return;
    const abort = new AbortController();
    void fetch(`${api}/${enrollmentId}/options`, { signal: abort.signal, cache: "no-store" })
      .then(async response => {
        if (abort.signal.aborted) return;
        if ([401, 403, 404].includes(response.status)) {
          setOptions({ error: response.status === 404 ? "التسجيل غير متاح." : "اعتماد التدريب المجاني متاح للمالك فقط." });
          return;
        }
        if (!response.ok) throw Error();
        const value: Options = await response.json();
        if (value.enrollment?.id !== enrollmentId || !Array.isArray(value.plans)) throw Error();
        if (!abort.signal.aborted) setOptions({ data: value });
      })
      .catch(() => {
        if (!abort.signal.aborted) setOptions({ error: "تعذر تحميل بيانات الإعفاء.", retry: true });
      });
    return () => abort.abort();
  }, [attemptKey, enrollmentId, optionsRevision, saved]);

  useEffect(() => {
    if (!attemptKey || saved) return;
    const abort = new AbortController();
    void fetch(`${api}/${enrollmentId}/attempt`, {
      headers: { "Idempotency-Key": attemptKey }, signal: abort.signal, cache: "no-store",
    }).then(async response => {
      if (abort.signal.aborted) return;
      if (response.status === 404) { setAttemptCheck("missing"); return; }
      if ([401, 403].includes(response.status)) { setAttemptCheck("denied"); return; }
      if (!response.ok) throw Error();
      const outcome = await readOutcome(response, enrollmentId);
      if (abort.signal.aborted) return;
      clearFinancialIntent(intentScope);
      setSaved(outcome);
    }).catch(() => {
      if (!abort.signal.aborted) setAttemptCheck("unavailable");
    });
    return () => abort.abort();
  }, [attemptKey, checkRevision, enrollmentId, intentScope, saved]);

  useEffect(() => { if (quote || saved) heading.current?.focus(); }, [quote, saved]);
  useEffect(() => { if (error) errorRegion.current?.focus(); }, [error]);

  async function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current || !canReview || !selected) return;
    sending.current = true;
    setBusy(true);
    setError("");
    setConsent(false);
    const submittedReason = reason.trim();
    try {
      const response = await csrfRequest(`${api}/quote`, "POST", {
        sportEnrollmentId: enrollmentId, subscriptionPlanId: selected.id, reason: submittedReason,
      }, {}, { announce: false });
      const value: Quote = await response.json();
      if (!value.quoteHash || value.reason !== submittedReason || Number(value.payable) !== 0) throw Error();
      setQuote(value);
    } catch (cause) {
      if (cause instanceof DashboardRequestError && [401, 403, 404].includes(cause.status)) refreshOptions();
      setError(cause instanceof DashboardRequestError ? cause.message : "تعذر تحميل المراجعة. أعد المحاولة.");
    } finally {
      setBusy(false);
      sending.current = false;
    }
  }

  async function approve() {
    if (!quote || !selected || !consent || sending.current || saved) return;
    sending.current = true;
    setBusy(true);
    setError("");
    let key: string | undefined;
    try {
      const body = { sportEnrollmentId: enrollmentId, subscriptionPlanId: selected.id, reason: quote.reason, quoteHash: quote.quoteHash };
      key = await pendingFinancialIntent(intentScope, body);
      const response = await csrfRequest(`${api}/approve`, "POST", body, { "Idempotency-Key": key }, { announce: false });
      const outcome = await readOutcome(response, enrollmentId);
      clearFinancialIntent(intentScope);
      setSaved(outcome);
    } catch (cause) {
      const status = cause instanceof DashboardRequestError ? cause.status : 0;
      if (key && (status === 0 || status >= 500 || status === 408 || status === 429)) {
        setAttemptCheck(undefined);
        setAttemptKey(key);
      } else {
        clearFinancialIntent(intentScope);
        setQuote(null);
        setConsent(false);
        refreshOptions();
        setError(cause instanceof DashboardRequestError ? cause.message : "تعذر بدء الاعتماد. أعد المراجعة.");
      }
    } finally {
      setBusy(false);
      sending.current = false;
    }
  }

  function reviewAgain() {
    clearFinancialIntent(intentScope);
    setAttemptKey(null);
    setAttemptCheck(undefined);
    setQuote(null);
    setConsent(false);
    refreshOptions();
  }

  const playerId = saved?.playerId ?? (attemptCheck === "denied" ? undefined : data?.enrollment.playerId);
  const back = playerId ? {
    label: "العودة إلى اشتراكات اللاعب",
    href: `/dashboard/players/${encodeURIComponent(playerId)}?tab=subscriptions`,
  } : undefined;

  return <div className={`${styles.page} ${styles.waiverPage}`}>
    <PageHeader title="اعتماد تدريب مجاني" context="ملف اللاعب / الإعفاء" back={back} />
    {error && <div ref={errorRegion} tabIndex={-1}><ErrorState message={error} /></div>}
    {saved ? <section className={styles.card}>
      <h2 ref={heading} tabIndex={-1} role="status">تم اعتماد الإعفاء وتسجيل فترة التدريب</h2>
      <Link className={styles.receiptLink} href={`/dashboard/subscriptions/periods/${encodeURIComponent(saved.periodId)}`}>عرض الاشتراك المعتمد</Link>
    </section> : attemptKey ? <section className={styles.card}>
      {attemptCheck === "missing" ? <>
        <p role="status">لا يوجد اعتماد محفوظ لهذه المحاولة. راجع الاشتراك الحالي قبل الاعتماد.</p>
        <button type="button" className="primary-button" onClick={reviewAgain}>فتح مراجعة جديدة</button>
      </> : attemptCheck === "denied" ? <ErrorState message="تعذر فتح نتيجة الاعتماد من هذا الحساب." />
        : attemptCheck === "unavailable" ? <>
          <ErrorState message="تعذر التحقق من نتيجة الاعتماد." />
          <button type="button" className="secondary-button" onClick={() => { setAttemptCheck(undefined); setCheckRevision(value => value + 1); }}>التحقق من النتيجة</button>
        </> : <LoadingState message="جارٍ التحقق من نتيجة الاعتماد…" />}
    </section> : options?.error ? <>
      <ErrorState message={options.error} />
      {options.retry && <button type="button" className="secondary-button" onClick={refreshOptions}>إعادة المحاولة</button>}
    </> : !data || attemptKey === undefined ? <LoadingState /> : <section className={styles.card}>
      <h2>{data.enrollment.player}</h2>
      <p>{data.enrollment.sport} · {data.enrollment.branch}</p>
      {quote ? <section className={styles.review} aria-label="مراجعة الإعفاء">
        <h3 ref={heading} tabIndex={-1}>مراجعة الإعفاء</h3>
        <p>{quote.plan}</p>
        <p>قيمة الإعفاء <strong>{formatMoneyAr(quote.waivedAmount, quote.currency)}</strong></p>
        <p>المستحق <strong>{formatMoneyAr(quote.payable, quote.currency)}</strong></p>
        <p>تبدأ {formatDateAr(quote.startDate)}{quote.endDate && ` · تنتهي ${formatDateAr(quote.endDate)}`}{quote.sessions != null && ` · ${quote.sessions} حصة`}</p>
        <p>سبب الإعفاء <bdi>{quote.reason}</bdi></p>
        <p className={styles.notice}>الإعفاء يخص الفترة المعروضة. تسوية المديونية وحجز المقعد لهما إجراءات مستقلة.</p>
        <label className={styles.choice}>
          <input type="checkbox" checked={consent} disabled={busy} onChange={event => setConsent(event.target.checked)} />
          أعتمد التدريب المجاني دون قبض أي مبلغ
        </label>
        <div className={styles.reviewActions}>
          <button type="button" className="primary-button" disabled={busy || !consent} onClick={() => void approve()}>{busy ? "جارٍ الاعتماد…" : "اعتماد الإعفاء"}</button>
          <button type="button" className="secondary-button" disabled={busy} onClick={() => { setQuote(null); setConsent(false); }}>تعديل الإعفاء</button>
        </div>
      </section> : data.plans.length === 0 ? <EmptyState title="لا توجد باقات متاحة لهذا التسجيل" /> : <form className={`form-shell ${styles.creditForm}`} onSubmit={event => void review(event)}>
        <label>الباقة المعفاة
          <select required disabled={busy} value={selected?.id ?? ""} onChange={event => setPlan(event.target.value)}>
            <option value="">اختر الباقة</option>
            {data.plans.map(option => <option key={option.id} value={option.id}>{option.arabicName} · {formatMoneyAr(option.price, option.currency)}</option>)}
          </select>
        </label>
        <label>سبب الإعفاء<textarea required maxLength={500} disabled={busy} value={reason} onChange={event => setReason(event.target.value)} /></label>
        <button type="submit" className="primary-button" disabled={busy || !canReview}>{busy ? "جارٍ تحميل المراجعة…" : "مراجعة الإعفاء"}</button>
      </form>}
    </section>}
  </div>;
}
