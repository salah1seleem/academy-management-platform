"use client";
import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { pendingFinancialIntent } from "../lib/pending-financial-intent";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";

type Options = { enrollment: { id: string; playerId: string; player: string; sport: string; branch: string }; plans: { id: string; arabicName: string; price: string; currency: string }[] };
type Quote = { quoteHash: string; player: string; sport: string; branch: string; plan: string; waivedAmount: string; payable: string; currency: string; startDate: string; endDate?: string; sessions?: number; reason: string };
type Body = { sportEnrollmentId: string; subscriptionPlanId: string; reason: string; quoteHash: string };
export function FreeTraining({ enrollmentId }: { enrollmentId: string }) {
  const [data, setData] = useState<Options | null>(null), [error, setError] = useState(""), [plan, setPlan] = useState(""), [reason, setReason] = useState("");
  const [quote, setQuote] = useState<Quote | null>(null), [busy, setBusy] = useState(false), [locked, setLocked] = useState(false), [consent, setConsent] = useState(false), [saved, setSaved] = useState<string | null>(null);
  const pending = useRef<{ body: Body; key: string } | null>(null), sending = useRef(false);
  useEffect(() => { const abort = new AbortController(); fetch(`/api/v1/finance/free-training/${enrollmentId}/options`, { signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => { if (!abort.signal.aborted) setError("اعتماد الإعفاء متاح للمالك وللتسجيلات المصرح بها فقط."); }); return () => abort.abort(); }, [enrollmentId]);
  async function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (sending.current || !plan || !reason.trim()) return; sending.current = true; setBusy(true); setError("");
    try { const r = await csrfRequest("/api/v1/finance/free-training/quote", "POST", { sportEnrollmentId: enrollmentId, subscriptionPlanId: plan, reason: reason.trim() }, {}, { announce: false }); setQuote(await r.json()); setConsent(false); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر تحميل المراجعة. حاول مرة أخرى."); }
    finally { setBusy(false); sending.current = false; }
  }
  async function approve() {
    if (!quote || !consent || sending.current || saved) return; sending.current = true; setBusy(true); setLocked(true); setError("");
    try {
      const body = { sportEnrollmentId: enrollmentId, subscriptionPlanId: plan, reason: quote.reason, quoteHash: quote.quoteHash };
      pending.current ??= { body, key: await pendingFinancialIntent(`free-training:${enrollmentId}`, body) };
      const r = await csrfRequest("/api/v1/finance/free-training/approve", "POST", pending.current.body, { "Idempotency-Key": pending.current.key }); setSaved((await r.json()).periodId);
    } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد نتيجة الاعتماد. أعد نفس الطلب؛ لا تنشئ اعتمادًا آخر."); }
    finally { setBusy(false); sending.current = false; }
  }
  return <div className={styles.page}><PageHeader title="اعتماد تدريب مجاني" context="ملف اللاعب / الإعفاء" />{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <section className={styles.card}>
    <h2>{data.enrollment.player} · {data.enrollment.sport}</h2><p>{data.enrollment.branch}</p>
    {saved ? <><h3 role="status">تم اعتماد الإعفاء وتسجيل فترة التدريب</h3><p>لا تحصيل ولا إيصال مالي لهذه العملية.</p><Link className={styles.receiptLink} href={`/dashboard/subscriptions/periods/${saved}`}>عرض الاشتراك المعتمد</Link></> : <>
      <form className="form-shell" onSubmit={e => void review(e)}>
        <label>الباقة المعفاة<select required disabled={busy || locked} value={plan} onChange={e => { setPlan(e.target.value); setQuote(null); }}><option value="">اختر الباقة</option>{data.plans.map(p => <option key={p.id} value={p.id}>{p.arabicName} · {formatMoneyAr(p.price, p.currency)}</option>)}</select></label>
        <label>سبب الإعفاء<textarea required maxLength={500} disabled={busy || locked} value={reason} onChange={e => { setReason(e.target.value); setQuote(null); }} /></label>
        {!locked && <button className="primary-button" disabled={busy || !plan || !reason.trim()}>مراجعة الإعفاء</button>}
      </form>
      {quote && <section className={styles.card} aria-label="مراجعة الإعفاء"><h3>{quote.plan}</h3><p>القيمة المعفاة: {formatMoneyAr(quote.waivedAmount, quote.currency)} · المستحق: {formatMoneyAr(quote.payable, quote.currency)}</p><p>{formatDateAr(quote.startDate)} — {quote.endDate ? formatDateAr(quote.endDate) : "حسب رصيد الحصص"}{quote.sessions != null && ` · ${quote.sessions} حصة`}</p><p>{quote.reason}</p><p className={styles.notice}>الإعفاء لهذه الفترة فقط؛ لا يسدد أو يلغي مديونية سابقة ولا يحجز مقعدًا تلقائيًا.</p><label className={styles.choice}><input type="checkbox" checked={consent} disabled={busy || locked} onChange={e => setConsent(e.target.checked)} />أعتمد التدريب المجاني دون قبض أي مبلغ</label><button className="primary-button" disabled={busy || !consent} onClick={() => void approve()}>{busy ? "جارٍ الاعتماد…" : locked ? "إعادة تأكيد نفس الاعتماد" : "اعتماد الإعفاء وتفعيل الفترة"}</button></section>}
    </>}
    <Link className="back-link" href={`/dashboard/players/${data.enrollment.playerId}`}>العودة إلى ملف اللاعب</Link>
  </section>}</div>;
}
