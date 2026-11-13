"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { FamilyCashBody, PendingCashFamily, forgetCashFamily, recallCashFamily, rememberCashFamily } from "../lib/pending-cash-family";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";

type Saved = { amount: string; currency: string; items: { receiptId: string; amount: string; currency: string; player?: string; sport?: string }[] };
type Result = ({ status: "Confirmed" } & Saved) | { status: "NotRecorded"; canDiscard: boolean };
type Attempt = "unknown" | "retry" | "discard";

function positiveCents(value: unknown) {
  if (typeof value !== "string" || !/^\d+(?:\.\d{1,2})?$/.test(value)) return null;
  const cents = Math.round(Number(value) * 100);
  return Number.isSafeInteger(cents) && cents > 0 ? cents : null;
}

function verifiedReceiptSet(result: Saved, expectedCount: number, reviewedAmount: string, reviewedCurrency: string) {
  const total = positiveCents(result?.amount);
  if (total === null || !/^[A-Z]{3}$/.test(result.currency) || (reviewedAmount && (total !== positiveCents(reviewedAmount) || result.currency !== reviewedCurrency))
    || !Array.isArray(result.items) || result.items.length !== expectedCount) throw new Error();
  const receiptIds = new Set<string>();
  let sum = 0;
  for (const item of result.items) {
    const cents = positiveCents(item?.amount);
    if (cents === null || item.currency !== result.currency || typeof item.receiptId !== "string" || !item.receiptId || receiptIds.has(item.receiptId)
      || (item.player != null && typeof item.player !== "string") || (item.sport != null && typeof item.sport !== "string")) throw new Error();
    receiptIds.add(item.receiptId);
    sum += cents;
  }
  if (sum !== total) throw new Error();
  return result;
}

export function CashFamilyConfirmation({ sessionId, body, amount, currency, onLocked, onUnlocked }: {
  sessionId: string; body: FamilyCashBody; amount: string; currency: string; onLocked: () => void; onUnlocked?: (message?: string) => void;
}) {
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState<Saved | null>(null);
  const [attempt, setAttempt] = useState<Attempt | null>(null);
  const pending = useRef<PendingCashFamily | null>(null);
  const reviewed = useRef<{ amount: string; currency: string } | null>(null);
  const running = useRef(false);
  const mounted = useRef(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const base = `/api/v1/finance/cash-registers/sessions/${encodeURIComponent(sessionId)}`;

  function settle(result: Saved, intent: PendingCashFamily) {
    const verified = verifiedReceiptSet(result, intent.body.lines.length, reviewed.current?.amount ?? "", reviewed.current?.currency ?? "");
    if (!mounted.current || pending.current !== intent) return;
    setSaved(verified);
    setError("");
    forgetCashFamily(sessionId);
    pending.current = null;
  }

  async function check(intent: PendingCashFamily) {
    try {
      const reference = "reference" in intent.body ? intent.body.reference : undefined;
      const response = await csrfRequest(`${base}/family-collection-result`, "POST", { key: intent.key, reference }, {}, { announce: false });
      const result = await response.json() as Result;
      if (result?.status === "Confirmed") {
        settle(result, intent);
      } else if (result?.status === "NotRecorded" && typeof result.canDiscard === "boolean") {
        if (mounted.current && pending.current === intent) {
          setAttempt(result.canDiscard ? "discard" : "retry");
          setError(result.canDiscard ? "انتهت المحاولة من دون تسجيل تحصيل. يمكنك إلغاءها." : "لم يُسجل تحصيل لهذه المحاولة. يمكنك إعادة إرسالها بالمبلغ نفسه.");
        }
      } else throw new Error();
      return result;
    } catch {
      if (mounted.current && pending.current === intent) {
        setAttempt("unknown");
        setError("تعذر التحقق من نتيجة التحصيل. أعد التحقق قبل إرسال أي طلب آخر.");
      }
      return null;
    }
  }

  async function verify(intent: PendingCashFamily) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    try { await check(intent); }
    finally {
      running.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  useEffect(() => {
    mounted.current = true;
    let active = true;
    const found = recallCashFamily(sessionId);
    if (found) {
      pending.current = found;
      queueMicrotask(() => {
        if (!active) return;
        setAttempt("unknown");
        onLocked();
        void verify(found);
      });
    }
    return () => { active = false; mounted.current = false; };
    // The saved intent for the shift is read only on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);

  useEffect(() => { if (saved) heading.current?.focus(); }, [saved]);

  async function collect() {
    if (running.current || saved || (pending.current ? attempt !== "retry" : !confirmed)) return;
    running.current = true;
    setBusy(true);
    setError("");
    try {
      const intent = pending.current ?? { body, key: crypto.randomUUID() };
      if (!pending.current) {
        rememberCashFamily(sessionId, intent);
        reviewed.current = { amount, currency };
        onLocked();
      }
      pending.current = intent;
      setAttempt("unknown");
      const endpoint = "reference" in intent.body ? `${base}/cross-branch/families/collect` : `${base}/family-collections`;
      const response = await csrfRequest(endpoint, "POST", intent.body, { "Idempotency-Key": intent.key }, { announce: false });
      settle(await response.json() as Saved, intent);
    } catch (cause) {
      if (!mounted.current) return;
      if (pending.current) {
        const outcome = await check(pending.current);
        if (outcome?.status === "NotRecorded" && mounted.current && cause instanceof DashboardRequestError && [403, 404, 409, 422].includes(cause.status)) {
          forgetCashFamily(sessionId);
          pending.current = null;
          setAttempt(null);
          setConfirmed(false);
          const message = cause.message;
          if (onUnlocked) onUnlocked(message);
          else setError(message);
        }
      } else setError(cause instanceof Error ? cause.message : "تعذر حفظ محاولة التحصيل قبل الإرسال.");
    } finally {
      running.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  function discard() {
    if (attempt !== "discard" || running.current) return;
    forgetCashFamily(sessionId);
    pending.current = null;
    setAttempt(null);
    setConfirmed(false);
    setError("");
    onUnlocked?.();
  }

  return <section className={styles.card} aria-label="تحصيل الأسرة النقدي">
    <h2 ref={heading} tabIndex={-1}>{saved ? "تم تحصيل وتجديد الأسرة" : attempt ? "متابعة محاولة التحصيل" : "قبض نقدية الأسرة"}</h2>
    {error && <p role="alert" className="error-message">{error}</p>}
    {saved ? <>
      <strong className={styles.cashReceived}>{formatMoneyAr(saved.amount, saved.currency)}</strong>
      <div className={styles.receipts}>{saved.items.map((r, i) => <Link className={styles.receiptLink} key={r.receiptId} href={`/dashboard/subscriptions/receipts/${r.receiptId}`}>
        {r.player && r.sport ? `إيصال ${r.player} · ${r.sport}` : `إيصال التسجيل ${i + 1}`} · {formatMoneyAr(r.amount, r.currency)}
      </Link>)}</div>
      <Link className={styles.receiptLink} href={`/dashboard/finance/cash-registers/sessions/${sessionId}`}>مراجعة رصيد الوردية</Link>
    </> : attempt ? <>
      <button className={attempt === "unknown" ? "primary-button" : "secondary-button"} disabled={busy} onClick={() => { if (pending.current) void verify(pending.current); }}>{busy ? "جارٍ التحقق…" : "التحقق من نتيجة المحاولة"}</button>
      {attempt === "retry" && <button className="primary-button" disabled={busy} onClick={() => void collect()}>إعادة إرسال نفس المحاولة</button>}
      {attempt === "discard" && <button className="secondary-button" disabled={busy} onClick={discard}>إلغاء محاولة لم تسجل</button>}
    </> : <>
      <p>المطلوب {formatMoneyAr(amount, currency)}. يصدر إيصال لكل تسجيل بعد تأكيد القبض.</p>
      <label className={styles.choice}><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />استلمت إجمالي الأسرة نقدًا وراجعت الباقات</label>
      <button className="primary-button" disabled={busy || !confirmed} onClick={() => void collect()}>تحصيل وتجديد كل التسجيلات</button>
    </>}
  </section>;
}
