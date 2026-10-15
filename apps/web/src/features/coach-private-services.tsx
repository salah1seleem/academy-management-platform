"use client";
import { FormEvent, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ConfirmationDialog, ErrorState, LoadingState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";

type Delivery = { id: string; player: string; serviceDate: string; ordinal: number; sessionCount: number; documentId: string; serviceNet: number; earned: number | null; contingent: number | null; requiresReview?: boolean; currency: string; variableMethod: string; variableValue: number; branch: string; sport: string };
type Data = { canReview: boolean; currency?: string; items: Delivery[] };
type Options = { packages: { id: string; documentId: string; player: string; plan: string; sessionCount: number; delivered: number }[]; documents: { id: string; player: string; plan: string; amount: string; currency: string; sessionCount: number }[]; sessions: { id: string; sessionDate: string; startTime: string; group: string }[] };
export function CoachPrivateServices({ coachId }: { coachId: string }) {
  const path = `/api/v1/manage/payroll/coaches/${coachId}/private-services`;
  const [data, setData] = useState<Data | null>(null), [options, setOptions] = useState<Options | null>(null), [error, setError] = useState(""), [hidden, setHidden] = useState(false), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  const [confirm, setConfirm] = useState<{ path: string; body: unknown; message: string } | null>(null);
  const load = useCallback(async (signal?: AbortSignal) => {
    const r = await fetch(path, { cache: "no-store", signal }); if (r.status === 403 || r.status === 404) { setHidden(true); return; }
    if (!r.ok) throw new Error("تعذر تحميل حصص الخاص المعتمدة."); const value: Data = await r.json(); setData(value); setHidden(false);
    if (value.canReview) { const o = await fetch(`${path}/options`, { cache: "no-store", signal }); if (!o.ok) throw new Error("تعذر تحميل مصادر الخدمة."); setOptions(await o.json()); }
  }, [path]);
  useEffect(() => { const c = new AbortController(); void Promise.resolve().then(() => load(c.signal)).catch(e => { if (!c.signal.aborted) setError(e.message); }); return () => c.abort(); }, [load]);
  async function save() {
    if (!confirm || busy) return; setBusy(true); setError(""); setMessage("");
    try { await csrfRequest(confirm.path, "POST", confirm.body); setConfirm(null); await load(); setMessage("تم حفظ مراجعة الخدمة. الأرقام تقييم للاستحقاق وليست ترحيلًا أو صرفًا للراتب."); }
    catch (e) { setConfirm(null); setError(e instanceof Error ? e.message : "تعذر حفظ الخدمة."); } finally { setBusy(false); }
  }
  function bind(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const f = new FormData(e.currentTarget), doc = options?.documents.find(d => d.id === f.get("document")); if (!doc) return;
    setConfirm({ path: `${path}/packages`, body: { documentId: doc.id, sessionCount: doc.sessionCount }, message: `${doc.player} · ${doc.plan} · ${doc.sessionCount} حصص متجانسة على المطالبة الموجودة. لا ينشأ طلب دفع أو فاتورة أخرى، ويثبت عدد الحصص للتوزيع.` });
  }
  function deliver(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const f = new FormData(e.currentTarget), p = options?.packages.find(p => p.id === f.get("package")), s = options?.sessions.find(s => s.id === f.get("session")); if (!p || !s) return;
    setConfirm({ path: `${path}/${p.id}/deliveries`, body: { trainingSessionId: s.id, ordinal: Number(f.get("ordinal")) }, message: `تأكيد تنفيذ ${p.player} · ${s.group} · ${formatDateAr(s.sessionDate)} · الحصة ${f.get("ordinal")} من ${p.sessionCount}. أنت تقرّ بتنفيذ الخدمة فعليًا بواسطة هذا المدرب؛ الحضور وحده ليس الإثبات. تحفظ نسخة الاتفاق ولا ينشأ صرف.` });
  }
  if (hidden) return null;
  const currency = data?.currency ?? data?.items[0]?.currency ?? "EGP";
  return <section className="detail-card coach-private-services" aria-label="تنفيذ الخاص وعمولاته"><div className="section-heading"><div><h2>تنفيذ الخاص وعمولاته</h2><p className="muted">خدمة معتمدة + تحصيل مخصص لنفس المطالبة، لا إجمالي مدفوعات الأسرة.</p></div><button className="secondary-button" disabled={busy} onClick={() => void load().catch(e => setError(e.message))}>تحديث التحصيل</button></div>
    {error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}{!data && !error && <LoadingState />}
    {data && <>{data.items.some(r => r.requiresReview) && <p role="alert">التقييم غير مكتمل: توجد خدمة معتمدة تتعارض مع حضور أو تعويض لاحق؛ استُبعدت من الإجماليات حتى مراجعة مصدرها.</p>}<div className="summary-grid"><div><small>عمولة مستوفية للشروط — غير مرحّلة</small><strong>{formatMoneyAr(data.items.reduce((s, r) => s + (r.earned ?? 0), 0), currency)}</strong></div><div><small>معلقة على التحصيل — ليست مستحقًا</small><strong>{formatMoneyAr(data.items.reduce((s, r) => s + (r.contingent ?? 0), 0), currency)}</strong></div></div><p className="muted">هذه مراجعة مصادر الخدمة؛ لم تُضف هذه المبالغ بعد إلى كشف الرواتب أو الدفتر، ولا تدخل الربحية النهائية. لا تعدل الإيصال أو الراتب السابق.</p>
      {data.canReview && options && <details><summary>مراجعة تنفيذ حصة خاصة</summary><form className="form-shell" aria-label="ربط مطالبة الخاص" onSubmit={bind}>
        <label>المطالبة الموجودة<select name="document" required defaultValue=""><option value="">اختر المطالبة</option>{options.documents.map(d => <option key={d.id} value={d.id}>{d.player} · {d.plan} · {d.sessionCount} حصص · مرجع {d.id.slice(0, 8)}</option>)}</select></label><div className="form-actions"><button className="secondary-button" disabled={busy || !options.documents.length}>تثبيت توزيع الحصص</button><Link href="/dashboard/finance/receivables">الفواتير والتحصيل</Link></div>
      </form><form className="form-shell" aria-label="اعتماد تنفيذ الخاص" onSubmit={deliver}>
        <label>باقة الخدمة<select name="package" required defaultValue=""><option value="">اختر اللاعب والمطالبة</option>{options.packages.map(p => <option key={p.id} value={p.id}>{p.player} · {p.plan} · مرجع {p.documentId.slice(0, 8)} · نُفذ {p.delivered}/{p.sessionCount}</option>)}</select></label>
        <label>الحصة المنفذة<select name="session" required defaultValue=""><option value="">اختر الحصة المسندة للمدرب</option>{options.sessions.map(s => <option key={s.id} value={s.id}>{s.group} · {formatDateAr(s.sessionDate)} · {s.startTime.slice(0, 5)}</option>)}</select></label>
        <label>رقم الحصة في الباقة<input name="ordinal" type="number" min="1" max="1000" required placeholder="مثلًا 1 من 8" /></label><div className="form-actions"><button className="primary-button" disabled={busy || !options.packages.length || !options.sessions.length}>مراجعة واعتماد التنفيذ</button></div>
      </form></details>}
      {!data.items.length ? <p>لا خدمات خاصة معتمدة لهذا المدرب. الحجز أو الحضور لا يولد عمولة تلقائيًا.</p> : <div className="linked-list">{data.items.map(d => <article key={d.id}><div><strong>{d.player} · {d.branch} · {d.sport}</strong><p>{formatDateAr(d.serviceDate)} · الحصة {d.ordinal}/{d.sessionCount} · {d.variableMethod === "Percent" ? `${d.variableValue}%` : `${formatMoneyAr(d.variableValue, d.currency)} للحصة`}</p><small className="muted">مرجع المطالبة: {d.documentId.slice(0, 8)}</small>{d.requiresReview ? <p>تعارض يتطلب مراجعة — العمولة غير محسوبة</p> : <p>مستوفية: {formatMoneyAr(d.earned ?? 0, d.currency)} · معلقة: {formatMoneyAr(d.contingent ?? 0, d.currency)}</p>}<Link href={`/dashboard/finance/receivables/${d.documentId}`}>مصدر التحصيل</Link></div></article>)}</div>}
    </>}
    <ConfirmationDialog open={confirm !== null} title="مراجعة مصدر الخدمة" message={confirm?.message ?? ""} confirmLabel="تأكيد مراجعة الخدمة" busy={busy} onConfirm={() => void save()} onCancel={() => setConfirm(null)} />
  </section>;
}
