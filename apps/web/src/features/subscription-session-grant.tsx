"use client";
import { FormEvent, useEffect, useRef, useState } from "react";
import { ConfirmationDialog, ErrorState } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";

export type SessionGrantPeriod = {
  id: string; planType: string; status: string; startDate: string; endDate?: string;
  initialSessions?: number | null; remainingSessions?: number | null; version: number; canGrantSessions?: boolean;
};
type GrantBody = { sessions: number; endDate: string; reason: string; expectedVersion: number };
type Intent = { key: string; body: GrantBody; adjustmentId?: string };

/** The grant is a subscription adjustment; prices are never calculated or sent here. */
export function SubscriptionSessionGrant({ period, onSaved, onOpen, onPendingChange }: {
  period: SessionGrantPeriod; onSaved: () => Promise<unknown> | unknown;
  onOpen?: () => void; onPendingChange?: (pending: boolean) => void;
}) {
  const [open, setOpen] = useState(false), [confirming, setConfirming] = useState(false);
  const [count, setCount] = useState(""), [end, setEnd] = useState(period.endDate ?? "");
  const [reason, setReason] = useState(""), [error, setError] = useState("");
  const [busy, setBusy] = useState(false), [locked, setLocked] = useState(false);
  const intent = useRef<Intent | null>(null), running = useRef(false), mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const eligible = period.canGrantSessions === true && period.status !== "Cancelled" &&
    ["Sessions", "Combined"].includes(period.planType) && period.remainingSessions != null;
  if (!eligible) return null;

  function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!Number.isInteger(Number(count)) || Number(count) < 1 || Number(count) > 10000 || !end || !reason.trim()) {
      setError("راجع عدد الحصص ونهاية الفترة وسبب الإضافة."); return;
    }
    setError(""); setConfirming(true);
  }
  async function execute() {
    if (running.current) return;
    running.current = true; setBusy(true); setError("");
    const current = intent.current ?? { key: crypto.randomUUID(), body: { sessions: Number(count), endDate: end, reason: reason.trim(), expectedVersion: period.version } };
    intent.current = current; setLocked(true); onPendingChange?.(true);
    let posting = false;
    try {
      if (!current.adjustmentId) {
        posting = true;
        const response = await csrfRequest(`/api/v1/subscriptions/periods/${period.id}/sessions`, "POST", current.body, { "Idempotency-Key": current.key }, { announce: false });
        const result = await response.json() as { adjustmentId?: string };
        if (!result.adjustmentId) throw new Error("لم يصل مرجع الإضافة؛ أعد التحقق من الطلب نفسه.");
        current.adjustmentId = result.adjustmentId;
        posting = false;
      }
      const response = await fetch(`/api/v1/subscriptions/periods/${period.id}`);
      if (!response.ok) throw new Error("تعذر تأكيد السجل المحفوظ. أعد التحقق من الإضافة نفسها.");
      const stored = await response.json() as SessionGrantPeriod & { adjustments: { id: string; type: string; sessionsDelta?: number; oldRemainingSessions?: number; newRemainingSessions?: number; newEndDate?: string }[] };
      const audit = stored.adjustments?.find(a => a.id === current.adjustmentId);
      if (stored.id !== period.id || stored.endDate !== current.body.endDate || stored.initialSessions !== period.initialSessions ||
        audit?.type !== "SessionsAdded" || audit.sessionsDelta !== current.body.sessions || audit.newEndDate !== current.body.endDate ||
        typeof audit.oldRemainingSessions !== "number" || typeof audit.newRemainingSessions !== "number" ||
        audit.newRemainingSessions - audit.oldRemainingSessions !== current.body.sessions) {
        throw new Error("لم تتأكد مطابقة الحصص والتاريخ المحفوظين. أعد التحقق من الإضافة نفسها.");
      }
      if (!mounted.current) return;
      intent.current = null; setLocked(false); setOpen(false); setConfirming(false); onPendingChange?.(false);
      announceSaveSuccess("حُفظت إضافة الحصص ونهاية الاشتراك بعد التحقق من السجل.");
      // A refresh failure after verified persistence must not offer another grant.
      try { await onSaved(); } catch { setError("حُفظت الإضافة؛ أعد تحميل تفاصيل الاشتراك لعرض الرصيد الحالي."); }
    } catch (caught) {
      if (!mounted.current) return;
      if (posting && caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500) {
        intent.current = null; setLocked(false); onPendingChange?.(false);
      }
      setConfirming(false); setError(caught instanceof Error ? caught.message : "تعذر تأكيد الإضافة. أعد التحقق من الطلب نفسه.");
    } finally { running.current = false; if (mounted.current) setBusy(false); }
  }
  return <section className="subscription-session-grant">
    {!open ? <button type="button" onClick={() => { onOpen?.(); setOpen(true); }}>إضافة حصص وتحديد نهاية الاشتراك</button> : <>
      {error && <ErrorState message={error} />}
      <form className="adjustment-form" onSubmit={review}>
        <fieldset disabled={busy || locked}>
          <legend>إضافة حصص إلى الاشتراك نفسه</legend>
          <label>عدد الحصص الإضافية<input aria-label="عدد الحصص الإضافية" type="number" min="1" max="10000" step="1" required value={count} onChange={e => setCount(e.target.value)} /></label>
          <label>نهاية الاشتراك الجديدة<input aria-label="نهاية الاشتراك الجديدة" type="date" min={period.endDate ?? period.startDate} required value={end} onChange={e => setEnd(e.target.value)} /></label>
          <label>سبب الإضافة<textarea aria-label="سبب الإضافة" maxLength={500} required value={reason} onChange={e => setReason(e.target.value)} /></label>
        </fieldset>
        <p className="form-hint">تضاف الحصص إلى رصيد هذه الفترة وتتغير نهايتها كلها. السعر والتحصيلات كما هي.</p>
        {!locked && <div className="form-actions"><button type="submit" disabled={busy}>مراجعة الإضافة</button><button type="button" disabled={busy} onClick={() => setOpen(false)}>رجوع</button></div>}
      </form>
      {locked && <button type="button" disabled={busy} onClick={() => void execute()}>{busy ? "جارٍ التحقق…" : "إعادة التحقق من الإضافة نفسها"}</button>}
      <ConfirmationDialog open={confirming} title="تأكيد إضافة الحصص" message={`ستضاف ${count} حصة إلى رصيد الاشتراك وتصبح نهاية الفترة كلها ${formatDateAr(end)}. السعر والتحصيلات كما هي.`} confirmLabel="تأكيد إضافة الحصص" busy={busy} onConfirm={() => void execute()} onCancel={() => { if (!locked) setConfirming(false); }} />
    </>}
  </section>;
}
