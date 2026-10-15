"use client";
import { useEffect, useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { PrivateCorrectionAction } from "./private-correction-action";

type Preview = { quoteHash: string; action: string; message: string; currency: string; dueDate: string; currentPosted: number; targetAmount: number | null; difference: number | null };
type Entry = { id: string; revision: number; kind: string; delta: number; currency: string; dueDate: string; actor: string };
const labels: Record<string,string> = { Accrual: "ترحيل فرق العمولة", Review: "حفظ استثناء للمراجعة", Reversal: "تأكيد عكس العمولة غير المسددة" };
const historyLabels: Record<string,string> = { Accrual: "ترحيل عمولة", Review: "استثناء بلا أثر مالي", Reversal: "عكس عمولة غير مسددة", Correction: "تصحيح عمولة مستقل بعد الاعتماد" };
export function PrivatePostingAction({ coachId, deliveryId, onChanged }: { coachId: string; deliveryId: string; onChanged: () => Promise<void> }) {
  const [open, setOpen] = useState(false), [preview, setPreview] = useState<Preview | null>(null), [history, setHistory] = useState<Entry[]>([]), [error,setError] = useState(""), [message,setMessage] = useState(""), [busy,setBusy] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null), keys = useRef(new Map<string,string>()); const path = `/api/v1/manage/payroll/coaches/${coachId}/private-services/${deliveryId}`;
  useEffect(() => { const node = dialog.current; if (open && node && !node.open) node.showModal(); return () => node?.close(); }, [open]);
  async function inspect() {
    setOpen(true); setPreview(null); setError(""); setMessage("");
    try {
      const [p,h] = await Promise.all([fetch(`${path}/posting-preview`,{cache:"no-store"}),fetch(`${path}/entries`,{cache:"no-store"})]);
      if (!p.ok || !h.ok) throw new Error("تعذر مراجعة الاستحقاق أو لا توجد صلاحية."); setPreview(await p.json()); setHistory(await h.json());
    } catch { setError("تعذر مراجعة الاستحقاق أو لا توجد صلاحية. أعد فتح المراجعة بعد التحقق من الاتصال."); }
  }
  async function approve() {
    if (!preview || !labels[preview.action] || busy) return; setBusy(true); setError("");
    const body = { action:preview.action,quoteHash:preview.quoteHash }; const signature = JSON.stringify(body); if (!keys.current.has(signature)) keys.current.set(signature,crypto.randomUUID());
    try { await csrfRequest(`${path}/entries`,"POST",body,{"Idempotency-Key":keys.current.get(signature)!}); await onChanged(); setOpen(false); setMessage(preview.action === "Review" ? "تم حفظ الاستثناء دون خصم أو سداد." : "تم حفظ فرق العمولة في كشف الرواتب دون تكرار أو سداد."); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد الحفظ أو تحديث المصدر. أعد المحاولة بنفس التأكيد دون إنشاء حركة أخرى."); } finally { setBusy(false); }
  }
  return <div className="private-posting-action"><button className="secondary-button" onClick={() => void inspect()}>مراجعة الاستحقاق</button>{message && <p role="status">{message}</p>}
    {open && <dialog ref={dialog} className="detail-card payroll-dialog" aria-label="مراجعة عمولة الخاص" onCancel={e => { if(busy)e.preventDefault();else setOpen(false); }}><h2>مراجعة عمولة الخاص</h2>{error && <ErrorState message={error} />}{!preview && !error && <LoadingState />}
      {preview && <><div className="private-posting-metrics"><div><small>المرحّل سابقًا</small><strong>{formatMoneyAr(preview.currentPosted,preview.currency)}</strong></div><div><small>التقييم المستوفي الحالي</small><strong>{preview.targetAmount === null ? "يتطلب مراجعة" : formatMoneyAr(preview.targetAmount,preview.currency)}</strong></div><div><small>الفرق — ليس دفعة</small><strong>{preview.difference === null ? "غير محسوب" : formatMoneyAr(preview.difference,preview.currency)}</strong></div><div><small>تاريخ القيد</small><strong>{formatDateAr(preview.dueDate)}</strong></div></div><p>{preview.message}</p>
      {preview.action === "ReviewPending" && preview.difference !== null && preview.difference !== 0 && <PrivateCorrectionAction path={path} onChanged={async () => { await onChanged(); setOpen(false); setMessage("تم حفظ تصحيح العمولة في الشهر المختار دون تغيير الكشف الأصلي أو صرف جديد."); }} />}
      <h3>سجل المصدر</h3>{!history.length ? <p>لم يُرحّل أي مبلغ من هذه الحصة.</p> : <ul>{history.map(e => <li key={e.id}>{historyLabels[e.kind] ?? "حركة مصدر"} · {formatMoneyAr(e.delta,e.currency)} · {formatDateAr(e.dueDate)} · {e.actor} · نسخة {e.revision}</li>)}</ul>}</>}
      <div className="form-actions">{preview && labels[preview.action] && <button className="primary-button" disabled={busy} onClick={() => void approve()}>{labels[preview.action]}</button>}<button className="secondary-button" disabled={busy} onClick={() => setOpen(false)}>إغلاق المراجعة</button></div>
    </dialog>}
  </div>;
}
