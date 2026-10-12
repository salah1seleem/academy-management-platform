"use client";
import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Review = { id: string; receiptNumber: string; playerId: string; player: string; plan: string; amount: string; currency: string; startDate: string; endDate?: string; cashEnabled: boolean; sessions: { id: string; name: string }[]; balance?: string; quoteHash?: string };
export function CashReceiptVoid({ receiptId }: { receiptId: string }) { return <VoidEditor key={receiptId} receiptId={receiptId} />; }
function VoidEditor({ receiptId }: { receiptId: string }) {
  const root = `/api/v1/finance/cash-registers/receipts/${receiptId}`, receipt = `/dashboard/subscriptions/receipts/${receiptId}`;
  const [review, setReview] = useState<Review | null>(null), [session, setSession] = useState(""), [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [saved, setSaved] = useState(false), [error, setError] = useState("");
  const pending = useRef<{ body: { cashSessionId: string; reason: string | null; quoteHash: string }; key: string } | null>(null);
  useEffect(() => { let active = true; void fetch(`${root}/void-options`, { cache: "no-store" }).then(async r => { if (!r.ok) { const p = await r.json(); throw new Error(p.title ?? "العكس غير متاح لهذا الإيصال."); } return r.json(); }).then(d => { if (active) setReview(d); }).catch(e => { if (active) setError(e instanceof TypeError ? "تعذر الاتصال لتحميل المراجعة." : e.message); }); return () => { active = false; }; }, [root]);
  function invalidate() { if (review) setReview({ ...review, quoteHash: undefined, balance: undefined }); pending.current = null; setError(""); }
  async function submit(e: FormEvent) {
    e.preventDefault(); if (busy || !review || !session || saved) return;
    setBusy(true); setError("");
    try {
      if (!review.quoteHash) {
        const response = await csrfRequest(`${root}/void-quote`, "POST", { cashSessionId: session, reason: reason.trim() || null }, {}, { announce: false });
        setReview(await response.json()); return;
      }
      pending.current ??= { body: { cashSessionId: session, reason: reason.trim() || null, quoteHash: review.quoteHash }, key: crypto.randomUUID() };
      await csrfRequest(`${root}/void`, "POST", pending.current.body, { "Idempotency-Key": pending.current.key });
      pending.current = null; setUncertain(false); setSaved(true);
    } catch (caught) {
      if (pending.current && (!(caught instanceof DashboardRequestError) || caught.status >= 500)) setUncertain(true);
      setError(caught instanceof DashboardRequestError ? caught.message : "تعذر تأكيد النتيجة. أعد المحاولة بنفس التفاصيل؛ لا تصرف المبلغ مرة أخرى.");
    } finally { setBusy(false); }
  }
  return <><PageHeader title="عكس إيصال نقدي" context="المالية / إجراء المالك" action={{ label: "الإيصال الأصلي", href: receipt, tone: "secondary" }} />
    {error && <ErrorState message={error} />}
    {!review && !error && <LoadingState />}
    {saved && <section className="detail-card" role="status"><h2>تم عكس الإيصال وإلغاء الفترة المرتبطة بنجاح.</h2><p>الإيصال الأصلي وتاريخ الاشتراك محفوظان؛ اللاعب وفتراته الأخرى لا تُحذف.</p><div className="form-actions"><Link className="primary-button" href={receipt}>عرض الإيصال وحركة العكس</Link><Link className="secondary-button" href={`/dashboard/finance/cash-registers/sessions/${session}`}>عرض رصيد الوردية</Link></div></section>}
    {review && !saved && <section className="detail-card"><h2>{review.player}</h2><p>{review.plan} · <span dir="ltr">{review.receiptNumber}</span></p><div className="cash-session-stats"><div><small>القيمة الكاملة المطلوب ردّها</small><strong>{formatMoneyAr(review.amount, review.currency)}</strong></div><div><small>فترة الخدمة التي ستُلغى</small><strong>{formatDateAr(review.startDate)}</strong><small>{review.endDate ? `حتى ${formatDateAr(review.endDate)}` : "رصيد حصص"}</small></div></div>
      <p>ردّ نقدي كامل وإلغاء فترة الاشتراك المرتبطة في عملية واحدة، وليس استردادًا جزئيًا. لا يحذف الإيصال أو يعيد فتح مديونية، ولا يلغي الفترات الأخرى.</p>
      {!review.cashEnabled || !review.sessions.length ? <p>لا توجد وردية شخصية مخولة للصرف في فرع التحصيل وبعملته. جهّز ورديتك ثم أعد فتح الصفحة.</p> : <form className="form-shell" onSubmit={submit}>
        <label>وردية صرف العكس<select required value={session} disabled={busy || uncertain} onChange={e => { setSession(e.target.value); invalidate(); }}><option value="">اختر ورديتك</option>{review.sessions.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>السبب (اختياري)<textarea maxLength={500} value={reason} disabled={busy || uncertain} onChange={e => { setReason(e.target.value); invalidate(); }} /></label>
        {review.quoteHash && <div className="detail-card" role="region" aria-label="مراجعة عكس الإيصال"><strong>سيُصرف {formatMoneyAr(review.amount, review.currency)}</strong><p>رصيد الوردية قبل العكس {formatMoneyAr(review.balance, review.currency)}</p><p>ستُلغى الفترة المرتبطة، ويظل السجل الأصلي محفوظًا.</p></div>}
        {uncertain && <p role="status">النتيجة غير مؤكدة؛ التفاصيل مقفلة حتى إعادة نفس المحاولة.</p>}
        <div className="form-actions"><button className="primary-button" disabled={busy || !session}>{review.quoteHash ? "تأكيد الصرف وإلغاء الفترة" : "مراجعة عكس الإيصال"}</button>{review.quoteHash && !uncertain && <button className="secondary-button" type="button" disabled={busy} onClick={invalidate}>تحديث المراجعة</button>}</div>
      </form>}
    </section>}
  </>;
}
