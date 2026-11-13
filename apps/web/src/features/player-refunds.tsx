"use client";

import Link from "next/link";
import { FormEvent, useEffect, useId, useRef, useState } from "react";
import { ConfirmationDialog, ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { clearFinancialIntent, pendingFinancialIntent, readFinancialIntentKey } from "../lib/pending-financial-intent";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Collection = { id: string; allocated: number; available: string; currency: string; paymentMethod?: string; confirmedAtUtc?: string; receiptNumber?: string };
type Refund = { id: string; collectionId: string; amount: number; currency: string; payoutMethod: "Online" | "Cash" | null; createdAtUtc?: string; reason?: string; cashSessionId?: string; matchesAttempt?: boolean };
type Data = { collections: Collection[]; refunds: Refund[]; canRecord: boolean };
type Quote = { amount: string; available: string; currency: string; balance?: string; quoteHash?: string; cashEnabled: boolean; sessions: { id: string; name: string }[] };
type RefundBody = { collectionId: string; amount: string; reason: string | null; cashSessionId?: string; quoteHash?: string };
type Attempt = { key: string; body?: RefundBody; currency?: string; recordedId?: string };

function receiptReference(collection: Collection) {
  return collection.receiptNumber ? `إيصال ${collection.receiptNumber.slice(-8)}` : "";
}

async function readHistory(path: string, attempt?: Attempt, signal = AbortSignal.timeout(12000)): Promise<Data> {
  const response = await fetch(path, {
    cache: "no-store", signal, ...(attempt && { headers: { "Idempotency-Key": attempt.key } }),
  });
  if ([401, 403, 404].includes(response.status)) throw new DashboardRequestError("سجل الاستردادات غير متاح لهذا الحساب.", response.status);
  if (!response.ok) throw Error();
  const data: Data = await response.json();
  if (!Array.isArray(data.collections) || !Array.isArray(data.refunds)) throw Error();
  return data;
}

function confirmedRefund(data: Data, attempt: Attempt) {
  const refund = data.refunds.find(row => attempt.recordedId ? row.id === attempt.recordedId : row.matchesAttempt);
  if (!refund?.id || !refund.collectionId || !Number.isFinite(refund.amount) || refund.amount <= 0 ||
      !refund.currency || (refund.payoutMethod !== "Online" && refund.payoutMethod !== "Cash")) return;
  const body = attempt.body;
  if (body && (refund.collectionId !== body.collectionId || refund.amount !== Number(body.amount) ||
      refund.currency !== attempt.currency || refund.payoutMethod !== (body.cashSessionId ? "Cash" : "Online") ||
      (refund.cashSessionId ?? null) !== (body.cashSessionId ?? null) || (refund.reason ?? null) !== body.reason)) return;
  return refund;
}

export function PlayerRefunds({ playerId }: { playerId: string }) {
  return <RefundEditor key={playerId} playerId={playerId} />;
}

function RefundEditor({ playerId }: { playerId: string }) {
  const path = `/api/v1/people/players/${playerId}/refunds`;
  const scope = `player-refund:${playerId.toLowerCase()}`;
  const sourceDetails = useId();
  const sending = useRef(false);
  const outcomePanel = useRef<HTMLElement>(null);
  const [selected, setSelected] = useState("");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [method, setMethod] = useState("Online");
  const [session, setSession] = useState("");
  const [data, setData] = useState<Data | null>(null);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [attempt, setAttempt] = useState<Attempt | null>(null);
  const [checkedMissing, setCheckedMissing] = useState(false);
  const [error, setError] = useState("");
  const [retryLoad, setRetryLoad] = useState(false);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<Refund | null>(null);
  const [reviewing, setReviewing] = useState(false);
  const [restartReview, setRestartReview] = useState(false);
  const [revision, setRevision] = useState(0);
  const eligible = data?.collections.filter(c => Number(c.available) > 0) ?? [];
  const collection = eligible.find(c => c.id === selected);
  const validAmount = data?.canRecord === true && !!collection && /^\d+(\.\d{1,2})?$/.test(amount) &&
    Number(amount) > 0 && Number(amount) <= Math.min(Number(collection.available), 999999999);

  useEffect(() => {
    const abort = new AbortController();
    const key = readFinancialIntentKey(scope);
    const pending = key ? { key } : undefined;
    void readHistory(path, pending, AbortSignal.any([abort.signal, AbortSignal.timeout(12000)])).then(result => {
      if (abort.signal.aborted) return;
      setData(result);
      if (!pending) return;
      const refund = confirmedRefund(result, pending);
      if (refund) {
        clearFinancialIntent(scope);
        setSaved(refund);
      } else {
        setAttempt(pending);
        setCheckedMissing(!result.refunds.some(row => row.matchesAttempt));
      }
    }).catch(failure => {
      if (abort.signal.aborted) return;
      if (pending) setAttempt(pending);
      setRetryLoad(!(failure instanceof DashboardRequestError));
      setError(failure instanceof DashboardRequestError ? failure.message : "تعذر تحميل سجل الاستردادات.");
    });
    return () => abort.abort();
  }, [path, scope, revision]);

  useEffect(() => {
    if (saved || attempt) outcomePanel.current?.focus();
  }, [saved, attempt]);

  function clearReview() {
    setQuote(null);
    setError("");
    setSaved(null);
  }

  function finish(result: Data, refund: Refund) {
    clearFinancialIntent(scope);
    setData(result);
    setSaved(refund);
    setAttempt(null);
    setCheckedMissing(false);
    setSelected("");
    setAmount("");
    setReason("");
    setSession("");
    setQuote(null);
  }

  async function verify(pending: Attempt, startAnother = false) {
    const result = await readHistory(path, pending);
    const refund = confirmedRefund(result, pending);
    if (refund) {
      finish(result, refund);
      return;
    }
    if (pending.recordedId || result.refunds.some(row => row.matchesAttempt)) throw Error();
    setData(result);
    if (startAnother) {
      setAttempt(null);
      return;
    }
    setCheckedMissing(true);
  }

  async function check(startAnother = false) {
    if (!attempt || sending.current) return;
    sending.current = true;
    setBusy(true);
    setError("");
    setCheckedMissing(false);
    try {
      await verify(attempt, startAnother);
    } catch (failure) {
      setError(failure instanceof DashboardRequestError ? failure.message : "تعذر تأكيد الاسترداد من السجل. أعد التحقق.");
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  async function record(body: RefundBody, currency: string, retry?: Attempt) {
    if (sending.current) return;
    sending.current = true;
    setBusy(true);
    setError("");
    setSaved(null);
    setCheckedMissing(false);
    setReviewing(false);
    let pending = retry;
    try {
      pending ??= { key: await pendingFinancialIntent(scope, body), body, currency };
      const response = await csrfRequest(path, "POST", body, { "Idempotency-Key": pending.key }, {
        announce: false, signal: AbortSignal.timeout(12000),
      });
      const { id } = await response.json();
      if (typeof id !== "string" || !id) throw Error();
      pending = { ...pending, recordedId: id };
      await verify(pending);
    } catch (failure) {
      if (!retry && !pending?.recordedId && failure instanceof DashboardRequestError && failure.status >= 400 && failure.status < 500 && failure.status !== 408) {
        clearFinancialIntent(scope);
        setError(failure.message);
        if (failure.status === 409) {
          setQuote(null);
          await readHistory(path).then(setData).catch(() => {});
        }
      } else {
        if (pending) setAttempt(pending);
        else setError("تعذر بدء التسجيل. أعد المحاولة.");
      }
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  function formBody(): RefundBody {
    return { collectionId: selected, amount, reason: reason.trim() || null };
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current || attempt || !collection || !validAmount) return;
    if (method === "Online") {
      setError("");
      setReviewing(true);
      return;
    }
    const body = { ...formBody(), ...(session && { cashSessionId: session }) };
    if (quote?.quoteHash) {
      void record({ ...body, quoteHash: quote.quoteHash }, collection.currency);
      return;
    }
    sending.current = true;
    setBusy(true);
    setError("");
    setSaved(null);
    try {
      const response = await csrfRequest(`${path}/quote`, "POST", body, {}, { announce: false, signal: AbortSignal.timeout(12000) });
      const next: Quote = await response.json();
      if (Number(next.amount) !== Number(body.amount) || next.currency !== collection.currency ||
          !Number.isFinite(Number(next.available)) || !Array.isArray(next.sessions) ||
          (next.quoteHash && (!next.cashEnabled || !next.sessions.some(s => s.id === session)))) throw Error();
      if (Number(next.amount) > Number(next.available)) {
        setError("تغيّر المبلغ المتاح. راجعه قبل الصرف.");
        setQuote(null);
        await readHistory(path).then(setData).catch(() => {});
      } else {
        setQuote(next);
      }
    } catch (failure) {
      setError(failure instanceof DashboardRequestError ? failure.message : "تعذر تحميل مراجعة الصرف. أعد المحاولة.");
      if (failure instanceof DashboardRequestError && failure.status === 409) {
        setSession("");
        setQuote(null);
      }
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return <section className="detail-card" aria-label="استرداد اللاعب">
    <h2>الاسترداد</h2>
    {!data && !error && !attempt && <LoadingState message="جارٍ تحميل الاستردادات…" />}
    {error && <><ErrorState message={error} />{retryLoad && !data && !attempt && <button type="button" className="secondary-button" onClick={() => { setError(""); setRevision(value => value + 1); }}>إعادة تحميل السجل</button>}</>}
    {saved && <p ref={node => { outcomePanel.current = node; }} tabIndex={-1} role="status">تم تسجيل الاسترداد بنجاح بقيمة {formatMoneyAr(saved.amount, saved.currency)}. لا تصرف المبلغ مرة أخرى.</p>}
    {attempt && <div ref={node => { outcomePanel.current = node; }} tabIndex={-1} className="detail-card" role="region" aria-label="التحقق من الاسترداد">
      <p>{checkedMissing ? "لم يظهر الاسترداد في السجل حتى الآن. راجع ما صُرف فعليًا قبل المتابعة."
        : attempt.recordedId ? "وصل تأكيد التسجيل. تحقق من ظهوره في السجل قبل المتابعة."
          : "لم تتأكد نتيجة التسجيل. تحقق من السجل قبل أي صرف آخر."}</p>
      <div className="form-actions">
        <button type="button" className="primary-button" disabled={busy} onClick={() => void check()}>{busy ? "جارٍ التحقق…" : "التحقق من السجل"}</button>
        {checkedMissing && data?.canRecord === true && (attempt.body && attempt.currency
          ? <button type="button" className="secondary-button" disabled={busy} onClick={() => void record(attempt.body!, attempt.currency!, attempt)}>إعادة إرسال التسجيل نفسه</button>
          : <button type="button" className="secondary-button" disabled={busy} onClick={() => setRestartReview(true)}>بدء مراجعة أخرى</button>)}
      </div>
    </div>}
    {data && !attempt && (data.canRecord !== true ? <p className="muted">{data.refunds.length ? "يمكنك الاطلاع على سجل الاستردادات." : "لا توجد استردادات مسجلة."}</p> : eligible.length ? <details><summary>تسجيل استرداد</summary>
      <form className="form-shell" style={{ alignItems: "start" }} onSubmit={event => void submit(event)}>
        <label>التحصيل<select name="collection" aria-label="التحصيل" aria-describedby={collection ? sourceDetails : undefined} required value={selected} onChange={event => {
          setSelected(event.target.value);
          setAmount("");
          setSession("");
          setMethod(eligible.find(c => c.id === event.target.value)?.paymentMethod === "Cash" ? "Cash" : "Online");
          clearReview();
        }} disabled={busy}>
          <option value="">اختر التحصيل</option>
          {eligible.map(c => <option key={c.id} value={c.id}>{receiptReference(c) || formatDateAr(c.confirmedAtUtc)} · المتاح: {formatMoneyAr(c.available, c.currency)}</option>)}
        </select>{collection && <small id={sourceDetails}>تحصيل {formatDateAr(collection.confirmedAtUtc)} · المتاح {formatMoneyAr(collection.available, collection.currency)}</small>}</label>
        <label>قيمة الاسترداد<input name="amount" type="number" inputMode="decimal" min="0.01" step="0.01" max={collection && Math.min(Number(collection.available), 999999999)} required disabled={busy} value={amount} onChange={event => { setAmount(event.target.value); clearReview(); }} /></label>
        <label>طريقة رد المبلغ<select value={method} disabled={busy || collection?.paymentMethod === "Cash"} onChange={event => { setMethod(event.target.value); setSession(""); clearReview(); }}><option value="Online">تحويل مُنفّذ خارج النظام</option><option value="Cash">صرف نقدي من وردية مخولة</option></select></label>
        <label>السبب (اختياري)<textarea name="reason" rows={2} maxLength={500} disabled={busy} value={reason} onChange={event => { setReason(event.target.value); clearReview(); }} /></label>
        {data.collections.some(c => c.allocated > 0) && <p className="muted">المبلغ المخصص لمطالبة مستبعد من المتاح للاسترداد.</p>}
        {method === "Cash" && quote && <div className="detail-card" role="region" aria-label="مراجعة الاسترداد النقدي">
          {quote.sessions.length > 0 && <label>وردية الصرف<select value={session} disabled={busy} onChange={event => { setSession(event.target.value); setQuote({ ...quote, quoteHash: undefined, balance: undefined }); setError(""); }}><option value="">اختر ورديتك</option>{quote.sessions.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>}
          {!quote.cashEnabled ? <p>الصرف النقدي غير مفعّل.</p> : !quote.sessions.length ? <p>لا توجد وردية مخولة في فرع التحصيل وبعملته.</p> : !quote.quoteHash ? <p>اختر الوردية ثم راجع المبلغ قبل الصرف.</p> : <><strong>المبلغ {formatMoneyAr(quote.amount, quote.currency)}</strong><p>المتاح من التحصيل {formatMoneyAr(quote.available, quote.currency)} · رصيد الوردية {formatMoneyAr(quote.balance, quote.currency)}</p><p>الاسترداد لا يغيّر الاشتراك أو المستحقات.</p></>}
        </div>}
        <div className="form-actions">
          <button className="primary-button" disabled={busy || !validAmount}>{busy ? "جارٍ المعالجة…" : method === "Cash" ? quote?.quoteHash ? "تأكيد صرف الاسترداد" : "مراجعة الاسترداد النقدي" : "مراجعة الاسترداد"}</button>
          {quote?.quoteHash && <button type="button" className="secondary-button" disabled={busy} onClick={() => setQuote({ ...quote, quoteHash: undefined })}>تحديث المراجعة</button>}
        </div>
      </form>
    </details> : <p className="muted">لا يوجد مبلغ متاح للاسترداد المباشر.</p>)}
    {!!data?.refunds.length && <><h3>سجل الاستردادات</h3><ul>
      {data.refunds.map(refund => <li key={refund.id}>{formatMoneyAr(refund.amount, refund.currency)} · {refund.payoutMethod === "Cash" || refund.cashSessionId ? "صرف نقدي" : refund.payoutMethod === "Online" ? "تحويل خارجي" : "طريقة الصرف غير مسجلة"}{refund.createdAtUtc && ` · ${formatDateAr(refund.createdAtUtc)}`}{refund.reason && ` · ${refund.reason}`}{refund.cashSessionId && <> · <Link href={`/dashboard/finance/cash-registers/sessions/${refund.cashSessionId}`}>عرض قيد الصرف</Link></>}</li>)}
    </ul></>}
    <ConfirmationDialog open={reviewing} title="مراجعة الاسترداد" confirmLabel="تسجيل التحويل المنفّذ" cancelLabel="العودة للتعديل"
      message={`سجّل ${formatMoneyAr(amount, collection?.currency)}${collection && receiptReference(collection) ? ` من ${receiptReference(collection)}` : ""} بعد إتمام التحويل خارج النظام. يبقى الإيصال والاشتراك والمستحقات كما هي.`}
      onConfirm={() => { if (collection && validAmount) void record(formBody(), collection.currency); }} onCancel={() => setReviewing(false)} />
    <ConfirmationDialog open={restartReview} title="مراجعة الاسترداد السابق" confirmLabel="راجعت السجل، متابعة" cancelLabel="العودة للتحقق"
      message="تحقق من حركة التحويل أو الوردية قبل بدء مراجعة أخرى. إذا سبق الصرف، لا تصرف المبلغ مرة أخرى."
      onConfirm={() => { setRestartReview(false); void check(true); }} onCancel={() => setRestartReview(false)} />
  </section>;
}
