"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { FamilyCashBody, PendingCashFamily, forgetCashFamily, recallCashFamily, rememberCashFamily } from "../lib/pending-cash-family";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";

type Saved = { amount: string; currency: string; items: { receiptId: string; amount: string; currency: string; player?: string; sport?: string }[] };
type Result = ({ status: "Confirmed"; canDiscard: false } & Saved) | { status: "NotRecorded"; canDiscard: boolean };
export function CashFamilyConfirmation({ sessionId, body, amount, currency, onLocked, onUnlocked }: { sessionId: string; body: FamilyCashBody; amount: string; currency: string; onLocked: () => void; onUnlocked?: () => void }) {
  const [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState(""), [saved, setSaved] = useState<Saved | null>(null);
  const [attempted, setAttempted] = useState(false), [canDiscard, setCanDiscard] = useState(false);
  const pending = useRef<PendingCashFamily | null>(null), sending = useRef(false);
  const base = `/api/v1/finance/cash-registers/sessions/${sessionId}`;
  async function resultFor(intent: PendingCashFamily): Promise<Result> {
    const reference = "reference" in intent.body ? intent.body.reference : undefined;
    return await (await csrfRequest(`${base}/family-collection-result`, "POST", { key: intent.key, reference }, {}, { announce: false })).json() as Result;
  }
  function settle(value: Saved) { setSaved(value); setError(""); setCanDiscard(false); forgetCashFamily(sessionId); pending.current = null; }
  async function check(intent: PendingCashFamily) {
    try {
      const result = await resultFor(intent);
      if (result.status === "Confirmed") settle(result);
      else { setCanDiscard(result.canDiscard); setError(result.canDiscard ? "هذه المحاولة غير مسجلة وانتهت صلاحيتها. يمكنك إلغاءها ثم مراجعة سعر جديد." : "لم يثبت التحصيل بعد. أعد إرسال الطلب نفسه فقط أو تحقق مجددًا."); }
      return result;
    } catch { setError("تعذر التحقق من نتيجة التحصيل الآن. احتفظ بهذه الصفحة وأعد التحقق؛ لا تبدأ تحصيلًا آخر."); return null; }
  }
  useEffect(() => {
    const found = recallCashFamily(sessionId);
    if (!found) return;
    pending.current = found;
    queueMicrotask(() => { setAttempted(true); onLocked(); void check(found); });
    // A persisted attempt, including one restored after reload, wins over a new quote.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);
  async function collect() {
    if ((!confirmed && !pending.current) || sending.current || saved) return; sending.current = true; setBusy(true); setError("");
    try {
      const intent = pending.current ?? { body, key: crypto.randomUUID() };
      if (!pending.current) rememberCashFamily(sessionId, intent);
      pending.current = intent; setAttempted(true); onLocked();
      const endpoint = "reference" in intent.body ? `${base}/cross-branch/families/collect` : `${base}/family-collections`;
      const response = await csrfRequest(endpoint, "POST", intent.body, { "Idempotency-Key": intent.key }, { announce: false });
      settle(await response.json() as Saved);
    } catch (cause) {
      if (pending.current) {
        const outcome = await check(pending.current);
        if (outcome?.status === "NotRecorded" && cause instanceof DashboardRequestError && [409, 422].includes(cause.status)) {
          forgetCashFamily(sessionId); pending.current = null; setAttempted(false); setConfirmed(false); setCanDiscard(false); onUnlocked?.(); setError(`${cause.message} لم يُسجل تحصيل؛ راجع المبلغ ثم حاول مجددًا.`);
        }
      } else setError(cause instanceof Error ? cause.message : "تعذر حفظ محاولة التحصيل؛ لم نرسل الطلب.");
    }
    finally { sending.current = false; setBusy(false); }
  }
  function discard() {
    if (!canDiscard || busy) return;
    forgetCashFamily(sessionId); pending.current = null; setAttempted(false); setConfirmed(false); setCanDiscard(false); setError(""); onUnlocked?.();
  }
  return <section className={styles.card} aria-label="تحصيل الأسرة النقدي">
    <h2>{saved ? "تم تحصيل وتجديد الأسرة" : attempted ? "متابعة محاولة تحصيل الأسرة" : "تأكيد استلام نقدية الأسرة"}</h2>
    {error && <p role="alert" className="error-message">{error}</p>}
    {saved ? <>
      <strong>{formatMoneyAr(saved.amount, saved.currency)}</strong>
      <div className={styles.receipts}>{saved.items.map((r, i) => <Link className={styles.receiptLink} key={r.receiptId} href={`/dashboard/subscriptions/receipts/${r.receiptId}`}>إيصال التسجيل {i + 1}{r.player && ` — ${r.player} · ${r.sport}`} · {formatMoneyAr(r.amount, r.currency)}</Link>)}</div>
      <Link className={styles.receiptLink} href={`/dashboard/finance/cash-registers/sessions/${sessionId}`}>مراجعة رصيد الوردية</Link>
    </> : <>
      <p>{amount ? `المبلغ المستلم ${formatMoneyAr(amount, currency)}. ` : "محاولة تحصيل سابقة في هذه الوردية. "}يُجدد الجميع معًا أو لا يُسجل أي تحصيل.</p>
      {!attempted && <label className={styles.choice}><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />استلمت إجمالي الأسرة نقدًا وراجعت الباقات</label>}
      <button className="primary-button" disabled={busy || (!attempted && !confirmed)} onClick={() => void collect()}>{busy ? "جارٍ التحقق…" : attempted ? "إعادة إرسال نفس المحاولة" : "تحصيل وتجديد كل التسجيلات"}</button>
      {attempted && <button className="secondary-button" disabled={busy} onClick={() => { if (pending.current) void check(pending.current); }}>التحقق من نتيجة المحاولة</button>}
      {canDiscard && <button className="secondary-button" disabled={busy} onClick={discard}>إلغاء محاولة لم تسجل</button>}
    </>}
  </section>;
}
