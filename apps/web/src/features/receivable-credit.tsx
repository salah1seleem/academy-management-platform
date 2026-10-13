"use client";
import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { pendingFinancialIntent } from "../lib/pending-financial-intent";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";

type Row = { id: string; player: string; description: string; branch: string; currency: string; outstanding: string };
type Options = { source: Row; targets: Row[] };
type Side = { id: string; description: string; before: string; after: string };
type Quote = { quoteHash: string; player: string; currency: string; amount: string; source: Side; target: Side; reason: string };
type Body = { sourceDocumentId: string; targetDocumentId: string; amount: string; reason: string; quoteHash: string };
export function ReceivableCredit({ sourceId }: { sourceId: string }) {
  const [data, setData] = useState<Options | null>(null), [error, setError] = useState(""), [target, setTarget] = useState(""), [amount, setAmount] = useState(""), [reason, setReason] = useState("");
  const [quote, setQuote] = useState<Quote | null>(null), [busy, setBusy] = useState(false), [locked, setLocked] = useState(false), [consent, setConsent] = useState(false), [saved, setSaved] = useState(false);
  const pending = useRef<{ body: Body; key: string } | null>(null), sending = useRef(false);
  useEffect(() => { const abort = new AbortController(); fetch(`/api/v1/finance/credit-applications/${sourceId}/options`, { signal: abort.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => { if (!abort.signal.aborted) setError("استخدام الرصيد الدائن متاح للمالك ومن مطالبة ذات فائض مثبت فقط."); }); return () => abort.abort(); }, [sourceId]);
  async function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (sending.current || !target || !amount || !reason.trim()) return; sending.current = true; setBusy(true); setError("");
    try { const r = await csrfRequest("/api/v1/finance/credit-applications/quote", "POST", { sourceDocumentId: sourceId, targetDocumentId: target, amount, reason: reason.trim() }, {}, { announce: false }); setQuote(await r.json()); setConsent(false); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر تحميل المراجعة. حاول مرة أخرى."); }
    finally { setBusy(false); sending.current = false; }
  }
  async function approve() {
    if (!quote || !consent || sending.current || saved) return; sending.current = true; setBusy(true); setLocked(true); setError("");
    try {
      const body = { sourceDocumentId: sourceId, targetDocumentId: quote.target.id, amount: quote.amount, reason: quote.reason, quoteHash: quote.quoteHash };
      pending.current ??= { body, key: await pendingFinancialIntent(`receivable-credit:${sourceId}`, body) };
      await csrfRequest("/api/v1/finance/credit-applications/approve", "POST", pending.current.body, { "Idempotency-Key": pending.current.key }); setSaved(true);
    } catch (e) {
      setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد نتيجة التخصيص. أعد نفس الطلب؛ لا تنشئ حركة أخرى.");
      if (e instanceof DashboardRequestError && e.status === 409) { pending.current = null; setLocked(false); setQuote(null); setConsent(false); }
    } finally { setBusy(false); sending.current = false; }
  }
  return <div className={styles.page}><PageHeader title="استخدام الرصيد الدائن" context="المالية / المطالبات" />{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <section className={styles.card}>
    <h2>{data.source.player}</h2><p>{data.source.description} · {data.source.branch}</p><p className={styles.notice}>تسوية بين مطالبتين لنفس اللاعب والعملة؛ لا قبض جديد ولا إيصال أو استرداد نقدي. لا يُستخدم مبلغ اشتراك لمجرد أنه غير مخصص.</p>
    {saved ? <><div role="status"><h3>تم استخدام الرصيد الدائن دون قبض جديد</h3></div><Link className={styles.receiptLink} href={`/dashboard/finance/receivables/${target}`}>عرض المطالبة المستفيدة</Link></> : <>
      <form className="form-shell" onSubmit={e => void review(e)}>
        <label>المطالبة المستفيدة<select required disabled={busy || locked} value={target} onChange={e => { setTarget(e.target.value); setQuote(null); }}><option value="">اختر مطالبة قائمة</option>{data.targets.map(p => <option key={p.id} value={p.id}>{p.description} · {p.branch} · {formatMoneyAr(p.outstanding, p.currency)}</option>)}</select></label>
        <label>المبلغ المراد استخدامه<input type="number" min="0.01" max="999999999" step="0.01" required disabled={busy || locked} value={amount} onChange={e => { setAmount(e.target.value); setQuote(null); }} /></label>
        <label>سبب التخصيص<textarea required maxLength={400} disabled={busy || locked} value={reason} onChange={e => { setReason(e.target.value); setQuote(null); }} /></label>
        {!locked && <div className="form-actions"><button className="primary-button" disabled={busy || !target || !amount || !reason.trim()}>مراجعة استخدام الرصيد</button></div>}
      </form>
      {quote && <section className={styles.card} aria-label="مراجعة الرصيد الدائن"><h3>{formatMoneyAr(quote.amount, quote.currency)}</h3><p>الرصيد الدائن المتبقي: {formatMoneyAr(quote.source.after.replace(/^-/, ""), quote.currency)}</p><p>قائم المطالبة المستفيدة: {formatMoneyAr(quote.target.before, quote.currency)} ← {formatMoneyAr(quote.target.after, quote.currency)}</p><p>{quote.reason}</p><label className={styles.choice}><input type="checkbox" checked={consent} disabled={busy || locked} onChange={e => setConsent(e.target.checked)} />أعتمد استخدام الرصيد دون قبض جديد</label><button className="primary-button" disabled={busy || !consent} onClick={() => void approve()}>{busy ? "جارٍ الاعتماد…" : locked ? "إعادة تأكيد نفس التخصيص" : "اعتماد استخدام الرصيد"}</button></section>}
      {data.targets.length === 0 && <p role="status">لا توجد مطالبة قائمة لنفس اللاعب والعملة.</p>}
    </>}
    <Link className="back-link" href={`/dashboard/finance/receivables/${sourceId}`}>العودة إلى المطالبة الأصلية</Link>
  </section>}</div>;
}
