"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { apiProblemMessage } from "../lib/api-problem";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./cash-receipt-void.module.css";

type Review = {
  id: string;
  receiptNumber: string;
  player: string;
  plan: string;
  amount: string;
  currency: string;
  startDate: string;
  endDate?: string;
  cashEnabled: boolean;
  sessions: { id: string; name: string }[];
};
type Quote = { amount: string; currency: string; balance: string; quoteHash: string };
type PendingVoid = { body: { cashSessionId: string; reason: string | null; quoteHash: string }; key: string };
const loadFailure = "تعذر تحميل مراجعة العكس. أعد المحاولة.";
const uncertainFailure = "تعذر تأكيد نتيجة العكس. أعد تأكيد العملية نفسها قبل أي صرف آخر.";

export function CashReceiptVoid({ receiptId, returnQuery }: { receiptId: string; returnQuery?: string }) {
  return <VoidEditor key={receiptId} receiptId={receiptId} returnQuery={returnQuery} />;
}

function VoidEditor({ receiptId, returnQuery }: { receiptId: string; returnQuery?: string }) {
  const root = `/api/v1/finance/cash-registers/receipts/${receiptId}`;
  const receipt = `/dashboard/subscriptions/receipts/${receiptId}${returnQuery ? `?${returnQuery}` : ""}`;
  const [review, setReview] = useState<Review | null>(null);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [session, setSession] = useState("");
  const [reason, setReason] = useState("");
  const [revision, setRevision] = useState(0);
  const [loadError, setLoadError] = useState<{ message: string; retry: boolean }>();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [saved, setSaved] = useState(false);
  const pending = useRef<PendingVoid | null>(null);
  const preview = useRef<HTMLDivElement>(null);
  const result = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    const request = new AbortController();
    void fetch(`${root}/void-options`, { cache: "no-store", signal: request.signal }).then(async response => {
      if (!response.ok) {
        const message = await apiProblemMessage(response, response.status >= 500 ? loadFailure : "العكس غير متاح لهذا الإيصال.");
        if (!request.signal.aborted) setLoadError({ message, retry: response.status >= 500 || response.status === 429 });
        return;
      }
      const data = await response.json() as Review;
      if (!data?.id || !Array.isArray(data.sessions)) throw new Error();
      if (!request.signal.aborted) setReview(data);
    }).catch(() => {
      if (!request.signal.aborted) setLoadError({ message: loadFailure, retry: true });
    });
    return () => request.abort();
  }, [root, revision]);

  useEffect(() => {
    if (saved) result.current?.focus();
    else if (quote && !uncertain) preview.current?.focus();
  }, [quote, saved, uncertain]);

  function invalidate() {
    setQuote(null);
    setError("");
    pending.current = null;
  }

  async function requestQuote() {
    if (busy || !session || pending.current) return;
    invalidate();
    setBusy(true);
    try {
      const response = await csrfRequest(`${root}/void-quote`, "POST", { cashSessionId: session, reason: reason.trim() || null }, {}, { announce: false });
      const data = await response.json().catch(() => null) as Quote | null;
      if (!data?.quoteHash || typeof data.balance !== "string") throw new Error();
      setQuote(data);
    } catch (caught) {
      setError(caught instanceof DashboardRequestError || caught instanceof TypeError ? caught.message : "تعذر تحميل نتيجة المراجعة. أعد المحاولة.");
    } finally {
      setBusy(false);
    }
  }

  async function confirm() {
    if (busy || !session || !quote || saved) return;
    setBusy(true);
    setError("");
    pending.current ??= { body: { cashSessionId: session, reason: reason.trim() || null, quoteHash: quote.quoteHash }, key: crypto.randomUUID() };
    try {
      const response = await csrfRequest(`${root}/void`, "POST", pending.current.body, { "Idempotency-Key": pending.current.key }, { announce: false });
      const acknowledgement = await response.json();
      if (typeof acknowledgement?.id !== "string" || !acknowledgement.id) throw new Error();
      pending.current = null;
      setUncertain(false);
      setSaved(true);
    } catch (caught) {
      if (caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500) {
        if (!uncertain || caught.status === 409 || caught.status === 422) {
          invalidate();
          setUncertain(false);
        }
        setError(caught.message);
      } else {
        setUncertain(true);
        setError(uncertainFailure);
      }
    } finally {
      setBusy(false);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    void (quote ? confirm() : requestQuote());
  }

  return <>
    <PageHeader title="عكس إيصال نقدي" context="المالية / إجراء المالك" back={{ label: "العودة إلى الإيصال", href: receipt }} />
    {loadError && <div className={styles.loadError}>
      <ErrorState message={loadError.message} />
      {loadError.retry && <button type="button" className="secondary-button" onClick={() => {
        setLoadError(undefined);
        setRevision(value => value + 1);
      }}>إعادة تحميل المراجعة</button>}
    </div>}
    {!review && !loadError && <LoadingState message="جارٍ تحميل مراجعة العكس…" />}
    {saved && <section className="detail-card" role="status">
      <h2 ref={result} tabIndex={-1}>تم عكس الإيصال وإلغاء الفترة المرتبطة بنجاح.</h2>
      <Link className="primary-button" href={`/dashboard/finance/cash-registers/sessions/${session}`}>عرض رصيد الوردية</Link>
    </section>}
    {review && !saved && <section className="detail-card">
      <h2>{review.player}</h2>
      <p>{review.plan} · <bdi>{review.receiptNumber}</bdi></p>
      <div className={`cash-session-stats ${styles.stats}`}>
        <div><small>المبلغ المطلوب ردّه</small><strong>{formatMoneyAr(review.amount, review.currency)}</strong></div>
        <div><small>فترة الاشتراك التي ستُلغى</small><strong className={styles.periodDate}>{formatDateAr(review.startDate)}</strong>
          <small>{review.endDate ? `حتى ${formatDateAr(review.endDate)}` : "رصيد حصص"}</small></div>
      </div>
      <p>يُرد كامل المبلغ نقدًا وتُلغى الفترة المرتبطة. يظل الإيصال متاحًا في السجل.</p>
      {!review.cashEnabled || !review.sessions.length ? <div className={styles.loadError}>
        <p>{review.cashEnabled ? "افتح وردية صرف باسمك في فرع التحصيل وبعملة الإيصال." : "الصرف النقدي غير مفعّل لهذه الأكاديمية."}</p>
        <Link className="secondary-button" href="/dashboard/finance/cash-registers">فتح إدارة الصناديق</Link>
      </div> : <form className="form-shell" onSubmit={submit}>
        <label>وردية صرف العكس<select required value={session} disabled={busy || uncertain} onChange={event => {
          setSession(event.target.value);
          invalidate();
        }}><option value="">اختر ورديتك</option>{review.sessions.map(shift => <option key={shift.id} value={shift.id}>{shift.name}</option>)}</select></label>
        <label>السبب (اختياري)<textarea className={styles.reason} maxLength={500} rows={2} value={reason} disabled={busy || uncertain} onChange={event => {
          setReason(event.target.value);
          invalidate();
        }} /></label>
        {quote && <div className="detail-card" ref={preview} tabIndex={-1} role="region" aria-label="مراجعة عكس الإيصال">
          <strong>سيُصرف {formatMoneyAr(quote.amount, quote.currency)}</strong>
          <p>رصيد الوردية قبل العكس {formatMoneyAr(quote.balance, quote.currency)}</p>
        </div>}
        {error && <ErrorState message={error} />}
        {uncertain && <p role="status">النتيجة غير مؤكدة؛ التفاصيل مقفلة حتى إعادة نفس المحاولة.</p>}
        <div className="form-actions">
          <button type="submit" className="primary-button" disabled={busy || !session}>
            {busy ? "جارٍ تنفيذ الطلب…" : uncertain ? "إعادة تأكيد العملية نفسها" : quote ? "تأكيد الصرف وإلغاء الفترة" : "مراجعة عكس الإيصال"}
          </button>
          {quote && !uncertain && <button className="secondary-button" type="button" disabled={busy} onClick={() => void requestQuote()}>تحديث المراجعة</button>}
        </div>
      </form>}
    </section>}
  </>;
}
