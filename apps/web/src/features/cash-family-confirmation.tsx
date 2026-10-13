"use client";
import Link from "next/link";
import { useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { pendingFinancialIntent } from "../lib/pending-financial-intent";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";

type Body = { guardianId: string; quoteHash: string; lines: { sportEnrollmentId: string; subscriptionPlanId: string }[] };
type Saved = { amount: string; currency: string; items: { receiptId: string; amount: string; currency: string; player?: string; sport?: string }[] };
export function CashFamilyConfirmation({ sessionId, body, amount, currency, onLocked }: { sessionId: string; body: Body; amount: string; currency: string; onLocked: () => void }) {
  const [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState(""), [saved, setSaved] = useState<Saved | null>(null);
  const [attempted, setAttempted] = useState(false);
  const pending = useRef<{ body: Body; key: string } | null>(null), sending = useRef(false);
  async function collect() {
    if (!confirmed || sending.current || saved) return; sending.current = true; setBusy(true); setAttempted(true); setError(""); onLocked();
    try {
      const scope = `cash-family:${sessionId}:${body.guardianId}`;
      pending.current ??= { body, key: await pendingFinancialIntent(scope, body) };
      const r = await csrfRequest(`/api/v1/finance/cash-registers/sessions/${sessionId}/family-collections`, "POST", pending.current.body, { "Idempotency-Key": pending.current.key });
      setSaved(await r.json()); // Keep the intent: reopening the same reviewed quote replays it.
    } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد نتيجة التحصيل. أعد المحاولة بنفس الطلب دون تحصيل النقدية مرة أخرى."); }
    finally { sending.current = false; setBusy(false); }
  }
  return <section className={styles.card} aria-label="تحصيل الأسرة النقدي">
    <h2>{saved ? "تم تحصيل وتجديد الأسرة" : "تأكيد استلام نقدية الأسرة"}</h2>
    {error && <p role="alert" className="error-message">{error}</p>}
    {saved ? <>
      <strong>{formatMoneyAr(saved.amount, saved.currency)}</strong>
      <div className={styles.receipts}>{saved.items.map((r, i) => <Link className={styles.receiptLink} key={r.receiptId} href={`/dashboard/subscriptions/receipts/${r.receiptId}`}>إيصال التسجيل {i + 1}{r.player && ` — ${r.player} · ${r.sport}`} · {formatMoneyAr(r.amount, r.currency)}</Link>)}</div>
      <Link className={styles.receiptLink} href={`/dashboard/finance/cash-registers/sessions/${sessionId}`}>مراجعة رصيد الوردية</Link>
    </> : <>
      <p>المبلغ المستلم {formatMoneyAr(amount, currency)}. يُجدد الجميع معًا أو لا يُسجل أي تحصيل.</p>
      <label className={styles.choice}><input type="checkbox" checked={confirmed} disabled={busy || attempted} onChange={e => setConfirmed(e.target.checked)} />استلمت إجمالي الأسرة نقدًا وراجعت الباقات</label>
      <button className="primary-button" disabled={!confirmed || busy} onClick={() => void collect()}>{busy ? "جارٍ تأكيد التحصيل…" : attempted ? "إعادة تأكيد نفس التحصيل" : "تحصيل وتجديد كل التسجيلات"}</button>
    </>}
  </section>;
}
