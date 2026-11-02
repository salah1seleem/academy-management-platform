"use client";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { PageHeader, ErrorState, LoadingState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr } from "../lib/formatters";

type Version = { id: string; revision: number; effectiveFrom: string; contractStart: string; contractEnd: string | null; note?: string };
type Data = { name: string; currentMonth: string; contractStart: string; contractEnd: string | null; hasVerifiedHistory: boolean; revision: number; canChange: boolean; items: Version[] };
type Change = { effectiveFrom: string; contractStart: string; contractEnd: string | null; expectedRevision: number; note: string | null };
export function ContractHistory({ id }: { id: string }) {
  const [data, setData] = useState<Data | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState(""), [pending, setPending] = useState<Change | null>(null);
  const [month, setMonth] = useState(""), [start, setStart] = useState(""), [end, setEnd] = useState(""), [fixed, setFixed] = useState(false), [note, setNote] = useState("");
  const keys = useRef(new Map<string, string>());
  const path = `/api/v1/manage/employees/${id}/contract-versions`;
  const load = useCallback(async (signal?: AbortSignal) => {
    const response = await fetch(path, { cache: "no-store", signal }); if (!response.ok) throw new Error("تعذر تحميل سجل العقد أو لا توجد صلاحية.");
    const value: Data = await response.json(); const latest = value.items[0]; const draft = latest && latest.effectiveFrom > value.currentMonth ? latest : value;
    setData(value); setStart(draft.contractStart); setEnd(draft.contractEnd ?? ""); setFixed(Boolean(draft.contractEnd)); setMonth((latest && latest.effectiveFrom > value.currentMonth ? latest.effectiveFrom : value.currentMonth).slice(0, 7));
  }, [path]);
  useEffect(() => { const abort = new AbortController(); void Promise.resolve().then(() => load(abort.signal)).catch(e => { if (!abort.signal.aborted) setError(e.message); }); return () => abort.abort(); }, [load]);
  function review(e: FormEvent<HTMLFormElement>) { e.preventDefault(); if (!data || busy) return; setMessage(""); setError(""); setPending({ effectiveFrom: `${month}-01`, contractStart: start, contractEnd: fixed ? end : null, expectedRevision: data.revision, note: note.trim() || null }); }
  async function confirm() {
    if (!pending || busy) return; setBusy(true); setError("");
    const signature = JSON.stringify(pending); if (!keys.current.has(signature)) keys.current.set(signature, crypto.randomUUID());
    try { await csrfRequest(path, "POST", pending, { "Idempotency-Key": keys.current.get(signature)! }); await load(); setPending(null); setMessage("تم اعتماد نسخة العقد؛ الكشوف السابقة محفوظة ولم يسجل صرف جديد."); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر اعتماد العقد؛ أعد المحاولة بنفس التأكيد."); }
    finally { setBusy(false); }
  }
  return <><PageHeader title="سجل العقد وتغييراته" context={data?.name ?? "فريق الأكاديمية"} secondaryActions={[{ label: "ملف الموظف", href: `/dashboard/staff/employees/${id}` }, { label: "الرواتب", href: `/dashboard/payroll?employeeId=${id}` }]} />
    {error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}{!data && !error && <LoadingState />}
    {data && <><section className="detail-card"><h2>مدة العقد الحالية</h2><p>{formatDateAr(data.contractStart)} — {data.contractEnd ? formatDateAr(data.contractEnd) : "دائم"}</p><p>الراتب الجزئي يحسب بأيام العقد وأيام الشهر الفعلية. اعتماد العقد لا يعني صرفًا، ولا يغير كشفًا معتمدًا.</p>{!data.hasVerifiedHistory && <p>مرجع قديم غير مؤرخ؛ لا نفترض تاريخًا سابقًا موثقًا.</p>}</section>
      {data.canChange && <form className="form-shell detail-card contract-history-form" onSubmit={review}>
        <h2>تغيير مؤرخ للعقد</h2><label>شهر سريان العقد<input type="month" required value={month} disabled={busy || Boolean(pending)} onChange={e => setMonth(e.target.value)} /></label>
        <label>تاريخ بداية العقد<input type="date" required value={start} disabled={busy || Boolean(pending)} onChange={e => setStart(e.target.value)} /></label>
        <label className="check-inline"><input type="checkbox" checked={fixed} disabled={busy || Boolean(pending)} onChange={e => setFixed(e.target.checked)} />عقد محدد المدة</label>
        {fixed && <label>تاريخ نهاية العقد<input type="date" required min={start} value={end} disabled={busy || Boolean(pending)} onChange={e => setEnd(e.target.value)} /></label>}
        <label className="contract-note">ملاحظة العقد — اختياري<textarea maxLength={500} value={note} disabled={busy || Boolean(pending)} onChange={e => setNote(e.target.value)} /></label>
        {!pending ? <div className="form-actions"><button className="primary-button" disabled={busy}>مراجعة تغيير العقد</button></div> : <section role="region" aria-label="تأكيد العقد المؤرخ" className="detail-card"><h3>تأكيد نسخة العقد</h3><p>شهر السريان: {formatDateAr(pending.effectiveFrom)} · من {formatDateAr(pending.contractStart)} إلى {pending.contractEnd ? formatDateAr(pending.contractEnd) : "دائم"}.</p><p>الشهر المعتمد أو المسدد لا يمكن تغييره. النسخة السابقة لا تُحذف.</p><div className="form-actions"><button className="primary-button" type="button" disabled={busy} onClick={() => void confirm()}>تأكيد اعتماد العقد</button><button className="secondary-button" type="button" disabled={busy} onClick={() => setPending(null)}>عودة للتعديل</button></div></section>}
      </form>}
      <section className="detail-card"><h2>سجل النسخ</h2>{!data.items.length ? <p>لا توجد نسخ مؤرخة بعد.</p> : <ul>{data.items.map(v => <li key={v.id}>نسخة {v.revision} · تسري من {formatDateAr(v.effectiveFrom)} · {formatDateAr(v.contractStart)} — {v.contractEnd ? formatDateAr(v.contractEnd) : "دائم"}{v.note && <p>{v.note}</p>}</li>)}</ul>}</section>
    </>}
  </>;
}
