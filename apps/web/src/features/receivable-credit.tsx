"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { FormEvent, useEffect, useRef, useState } from "react";
import { EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { clearFinancialIntent, pendingFinancialIntent, readFinancialIntentKey } from "../lib/pending-financial-intent";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";

type Row = { id: string; player: string; description: string; branch: string; currency: string; outstanding: string };
type Options = { source: Row; targets: Row[] };
type Side = { id: string; description: string; before: string; after: string };
type Quote = { quoteHash: string; player: string; currency: string; amount: string; source: Side; target: Side; reason: string };
type Body = { sourceDocumentId: string; targetDocumentId: string; amount: string; reason: string; quoteHash: string };
type Outcome = { id: string; sourceDocumentId: string; targetDocumentId: string; targetBranchId: string; amount: string; currency: string; sourceRemaining: string; targetRemaining: string };
type AttemptCheck = "missing" | "unavailable" | "denied";
const api = "/api/v1/finance/credit-applications";

async function readOutcome(response: Response, sourceId: string): Promise<Outcome> {
  const value: Outcome = await response.json();
  if (value?.sourceDocumentId !== sourceId || !value.id || !value.targetDocumentId ||
      !value.targetBranchId || !/^[A-Z]{3}$/.test(value.currency) ||
      ![value.amount, value.sourceRemaining, value.targetRemaining].every(amount => /^\d+(\.\d{1,2})?$/.test(amount)) ||
      Number(value.amount) <= 0) throw Error();
  return value;
}

export function ReceivableCredit({ sourceId }: { sourceId: string }) {
  const search = useSearchParams();
  const id = sourceId.toLowerCase();
  const query = new URLSearchParams();
  for (const key of ["state", "branchId"]) {
    const value = search.get(key);
    if (value) query.set(key, value);
  }
  return <ScopedCredit key={id} sourceId={id} suffix={query.size ? `?${query}` : ""} />;
}

function ScopedCredit({ sourceId, suffix }: { sourceId: string; suffix: string }) {
  const [result, setResult] = useState<{ data?: Options; error?: string; retry?: boolean }>();
  const [revision, setRevision] = useState(0);
  const [error, setError] = useState("");
  const [target, setTarget] = useState("");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [quote, setQuote] = useState<Quote | null>(null);
  const [busy, setBusy] = useState(false);
  const [consent, setConsent] = useState(false);
  const [saved, setSaved] = useState<Outcome | null>(null);
  const [attemptKey, setAttemptKey] = useState<string | null>();
  const [attemptCheck, setAttemptCheck] = useState<AttemptCheck>();
  const [checkRevision, setCheckRevision] = useState(0);
  const sending = useRef(false);
  const reviewPanel = useRef<HTMLElement>(null);
  const data = result?.data;
  const selected = data?.targets.find(row => row.id === target);
  const credit = data ? -Number(data.source.outstanding) : 0;
  const limit = selected ? Math.min(credit, Number(selected.outstanding), 999999999) : 0;
  const value = Number(amount);
  const amountError = selected && amount
    ? value > limit ? "المبلغ يتجاوز الحد المتاح لهذه المطالبة."
      : !Number.isFinite(value) || value <= 0 || Math.round(value * 100) / 100 !== value ? "أدخل مبلغًا موجبًا برقمين بعد العلامة العشرية على الأكثر." : ""
    : "";
  const canReview = !!selected && !!amount && !amountError && !!reason.trim();
  const intentScope = `receivable-credit:${sourceId}`;

  function refresh() {
    setResult(undefined);
    setRevision(value => value + 1);
  }

  useEffect(() => {
    let active = true;
    queueMicrotask(() => { if (active) setAttemptKey(readFinancialIntentKey(intentScope)); });
    return () => { active = false; };
  }, [intentScope]);

  useEffect(() => {
    if (!attemptKey || saved) return;
    const abort = new AbortController();
    void fetch(`${api}/${sourceId}/attempt`, {
      headers: { "Idempotency-Key": attemptKey }, cache: "no-store", signal: abort.signal,
    }).then(async response => {
      if (abort.signal.aborted) return;
      if (response.status === 404) { setAttemptCheck("missing"); return; }
      if (response.status === 401 || response.status === 403) { setAttemptCheck("denied"); return; }
      if (!response.ok) throw Error();
      const outcome = await readOutcome(response, sourceId);
      if (abort.signal.aborted) return;
      clearFinancialIntent(intentScope);
      setSaved(outcome);
    }).catch(() => { if (!abort.signal.aborted) setAttemptCheck("unavailable"); });
    return () => abort.abort();
  }, [attemptKey, checkRevision, intentScope, saved, sourceId]);

  useEffect(() => {
    if (attemptKey !== null || saved) return;
    const abort = new AbortController();
    void fetch(`${api}/${sourceId}/options`, { signal: abort.signal, cache: "no-store" })
      .then(async response => {
        if (abort.signal.aborted) return;
        if ([401, 403, 404].includes(response.status)) {
          setResult({ error: response.status === 404 ? "هذه المطالبة غير متاحة لك." : "استخدام الرصيد الدائن متاح للمالك فقط." });
          return;
        }
        if (response.status === 409 || response.status === 422) {
          setResult({ error: "لا يوجد رصيد دائن متاح في هذه المطالبة." });
          return;
        }
        if (!response.ok) throw Error();
        const value: Options = await response.json();
        if (abort.signal.aborted) return;
        if (value.source?.id !== sourceId || !Array.isArray(value.targets) ||
            !Number.isFinite(Number(value.source.outstanding)) || Number(value.source.outstanding) >= 0) throw Error();
        setResult({ data: value });
      })
      .catch(() => {
        if (!abort.signal.aborted) setResult({ error: "تعذر تحميل بيانات الرصيد الدائن.", retry: true });
      });
    return () => abort.abort();
  }, [attemptKey, revision, saved, sourceId]);

  useEffect(() => {
    if (quote) reviewPanel.current?.focus();
  }, [quote]);

  async function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current || !canReview || !selected || !data) return;
    sending.current = true; setBusy(true); setError(""); setQuote(null); setConsent(false);
    try {
      const response = await csrfRequest(`${api}/quote`, "POST", {
        sourceDocumentId: sourceId, targetDocumentId: selected.id, amount, reason: reason.trim(),
      }, {}, { announce: false });
      const reviewed: Quote = await response.json();
      if (reviewed.source?.id !== sourceId || reviewed.target?.id !== selected.id ||
          reviewed.currency !== data.source.currency || Number(reviewed.amount) !== Number(amount) ||
          reviewed.reason !== reason.trim() || !/^[a-f0-9]{64}$/i.test(reviewed.quoteHash)) throw Error();
      setQuote(reviewed);
    } catch (e) {
      setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد مراجعة الرصيد. أعد المحاولة.");
      if (e instanceof DashboardRequestError && e.status === 409) refresh();
    } finally {
      setBusy(false); sending.current = false;
    }
  }

  async function approve() {
    if (!quote || !consent || sending.current || saved) return;
    sending.current = true; setBusy(true); setError("");
    let key: string | undefined;
    try {
      const body: Body = { sourceDocumentId: sourceId, targetDocumentId: quote.target.id, amount: quote.amount, reason: quote.reason, quoteHash: quote.quoteHash };
      key = await pendingFinancialIntent(intentScope, body);
      const response = await csrfRequest(`${api}/approve`, "POST", body, { "Idempotency-Key": key }, { announce: false });
      const outcome = await readOutcome(response, sourceId);
      if (outcome.targetDocumentId !== body.targetDocumentId || Number(outcome.amount) !== Number(body.amount)) throw Error();
      clearFinancialIntent(intentScope);
      setSaved(outcome);
    } catch (e) {
      if (key && (!(e instanceof DashboardRequestError) || e.status === 0 || e.status >= 500 || e.status === 408 || e.status === 429)) {
        setAttemptCheck(undefined);
        setAttemptKey(key);
      } else {
        clearFinancialIntent(intentScope);
        setQuote(null);
        setConsent(false);
        refresh();
        setError(e instanceof DashboardRequestError ? e.message : "تعذر بدء التسوية. راجع القيم وحاول مرة أخرى.");
      }
    } finally {
      setBusy(false); sending.current = false;
    }
  }

  function reviewAgain() {
    clearFinancialIntent(intentScope);
    setAttemptKey(null);
    setAttemptCheck(undefined);
    setQuote(null);
    setConsent(false);
    refresh();
  }

  const sourceQuery = new URLSearchParams(suffix);
  if (saved && Number(saved.sourceRemaining) === 0 && sourceQuery.get("state") === "credit") sourceQuery.delete("state");
  const sourceHref = `/dashboard/finance/receivables/${sourceId}${sourceQuery.size ? `?${sourceQuery}` : ""}`;
  return <div className={styles.page}>
    <PageHeader title="استخدام الرصيد الدائن" context="المالية / المطالبات" back={{ label: "العودة إلى المطالبة الأصلية", href: sourceHref }} />
    {error && <ErrorState message={error} />}
    {saved ? <section className={styles.card}>
      <div role="status"><h2>تم استخدام الرصيد الدائن دون قبض جديد</h2></div>
      <p>المبلغ المستخدم <strong>{formatMoneyAr(saved.amount, saved.currency)}</strong></p>
      <p>الرصيد الدائن بعد التسوية <strong>{formatMoneyAr(saved.sourceRemaining, saved.currency)}</strong></p>
      <p>المتبقي على المطالبة المستفيدة <strong>{formatMoneyAr(saved.targetRemaining, saved.currency)}</strong></p>
      <Link className={styles.receiptLink} href={`/dashboard/finance/receivables/${saved.targetDocumentId}?branchId=${saved.targetBranchId}`}>عرض المطالبة المستفيدة</Link>
      <details><summary>رقم التسوية</summary><bdi>{saved.id}</bdi></details>
    </section> : attemptKey ? <section className={styles.card}>
      {attemptCheck === "missing" ? <>
        <p role="status">لا توجد تسوية محفوظة لهذه المحاولة. راجع الأرصدة الحالية قبل الاعتماد.</p>
        <button className="primary-button" onClick={reviewAgain}>فتح مراجعة جديدة</button>
      </> : attemptCheck === "denied" ? <ErrorState message="تعذر فتح نتيجة التسوية من هذا الحساب." />
        : attemptCheck === "unavailable" ? <>
          <ErrorState message="تعذر التحقق من نتيجة التسوية." />
          <button className="secondary-button" onClick={() => { setAttemptCheck(undefined); setCheckRevision(n => n + 1); }}>التحقق من النتيجة</button>
        </> : <LoadingState message="جارٍ التحقق من نتيجة التسوية…" />}
    </section> : result?.error ? <>
      <ErrorState message={result.error} />
      {result.retry && <button type="button" className="secondary-button" onClick={refresh}>إعادة المحاولة</button>}
    </> : !data || attemptKey === undefined ? <LoadingState /> : <section className={styles.card}>
      <h2>{data.source.player}</h2>
      <p>{data.source.description} · {data.source.branch}</p>
      {quote ? <section ref={reviewPanel} tabIndex={-1} className={styles.review} aria-label="مراجعة الرصيد الدائن">
        <h3>مراجعة استخدام {formatMoneyAr(quote.amount, quote.currency)}</h3>
        <p>الرصيد الدائن بعد التسوية {formatMoneyAr(Math.abs(Number(quote.source.after)), quote.currency)} · كان {formatMoneyAr(Math.abs(Number(quote.source.before)), quote.currency)}</p>
        <p><strong>{quote.target.description}</strong><br />المتبقي على المطالبة {formatMoneyAr(quote.target.after, quote.currency)} · كان {formatMoneyAr(quote.target.before, quote.currency)}</p>
        <p>{quote.reason}</p>
        <label className={styles.choice}><input type="checkbox" checked={consent} disabled={busy} onChange={e => setConsent(e.target.checked)} />أعتمد استخدام الرصيد دون قبض جديد</label>
        <div className="form-actions">
          <button className="primary-button" disabled={busy || !consent} onClick={() => void approve()}>{busy ? "جارٍ الاعتماد…" : "اعتماد استخدام الرصيد"}</button>
          <button className="secondary-button" disabled={busy} onClick={() => { setQuote(null); setConsent(false); }}>تعديل التسوية</button>
        </div>
      </section> : <>
        <p>الرصيد الدائن المتاح <strong>{formatMoneyAr(credit, data.source.currency)}</strong></p>
        {data.targets.length === 0 ? <EmptyState message="لا توجد مطالبة قائمة لنفس اللاعب والعملة." /> : <>
          <form className={`form-shell ${styles.creditForm}`} onSubmit={e => void review(e)}>
            <label>المطالبة المستفيدة<select required disabled={busy} value={selected?.id ?? ""} onChange={e => setTarget(e.target.value)}>
              <option value="">اختر مطالبة قائمة</option>
              {data.targets.map(row => <option key={row.id} value={row.id}>{row.description} · {row.branch} · {formatMoneyAr(row.outstanding, row.currency)}</option>)}
            </select></label>
            <div className={styles.creditAmount}>
              <label htmlFor="credit-amount">المبلغ المراد استخدامه</label>
              <input id="credit-amount" type="number" inputMode="decimal" min="0.01" max={limit || undefined} step="0.01" required disabled={busy || !selected}
                value={amount} onChange={e => setAmount(e.target.value)} aria-invalid={!!amountError}
                aria-describedby={selected ? `credit-amount-limit${amountError ? " credit-amount-error" : ""}` : undefined} />
              {selected && <small id="credit-amount-limit">أقصى مبلغ لهذه المطالبة {formatMoneyAr(limit, data.source.currency)}</small>}
              {amountError && <small id="credit-amount-error" role="alert">{amountError}</small>}
            </div>
            <label>سبب التخصيص<textarea required maxLength={400} disabled={busy} value={reason} onChange={e => setReason(e.target.value)} /></label>
            <div className="form-actions"><button className="primary-button" disabled={busy || !canReview}>{busy ? "جارٍ تجهيز المراجعة…" : "مراجعة استخدام الرصيد"}</button></div>
          </form>
        </>}
      </>}
    </section>}
  </div>;
}
