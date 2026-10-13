"use client";
import { useCallback, useEffect, useState } from "react";
import { csrfRequest } from "./dashboard-api";
import { ErrorState, LoadingState } from "../components/dashboard-shell";

type Row = { id: string; player: string; planName: string; amount: string; currency: string; proofAssetId: string; createdAtUtc: string };
export function DayPassReviewPanel({ canReview }: { canReview: boolean }) {
  const [rows, setRows] = useState<Row[] | null>(null), [error, setError] = useState(""), [notice, setNotice] = useState(""), [busy, setBusy] = useState(false), [reason, setReason] = useState<Record<string,string>>({});
  const load = useCallback(async () => { const response = await fetch("/api/v1/facilities/day-pass/review", { cache: "no-store" }); if (!response.ok) throw new Error("تعذر تحميل طلبات الزيارة اليومية."); const result: unknown = await response.json(); if (!Array.isArray(result)) throw new Error("تعذر قراءة قائمة طلبات الزيارة."); setRows(result as Row[]); }, []);
  useEffect(() => { let disposed = false; void Promise.resolve().then(() => { if (!disposed) return load(); }).catch(e => { if (!disposed) setError(e.message); }); return () => { disposed = true; }; }, [load]);
  async function decide(row: Row, accept: boolean) {
    if (busy || (!accept && !reason[row.id]?.trim())) return;
    if (accept && !window.confirm(`هل راجعت وصول مبلغ ${row.amount} ${row.currency} بالفعل لحساب الأكاديمية؟ صورة الإثبات وحدها لا تكفي.`)) return;
    setBusy(true); setError(""); setNotice("");
    try { await csrfRequest(`/api/v1/facilities/day-pass/${row.id}/review`, "POST", { accept, reason: reason[row.id]?.trim() || null }); await load(); setNotice(accept ? "اعتمدت الزيارة بعد مراجعة التحويل. يظهر القرار لولي الأمر." : "رُفض الإثبات مع السبب، ووصل القرار لولي الأمر."); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حسم الطلب."); }
    finally { setBusy(false); }
  }
  return <section className="list-card"><header className="section-heading"><div><h2>مراجعة زيارات اليوم المدفوعة</h2><small>تذكرة الزيارة لا تُفعّل اشتراك تدريب ولا تُسجّل حضور حصة.</small></div><button className="secondary-button" type="button" onClick={() => void load().catch(e => setError(e.message))}>تحديث</button></header>{error && <ErrorState message={error} />}{notice && <p role="status" className="notice">{notice}</p>}{!rows && !error && <LoadingState />}{rows?.length === 0 && <p>لا توجد إثباتات زيارة بانتظار المراجعة.</p>}{rows?.map(row => <article key={row.id} className="profile-row"><div><strong>{row.player}</strong><small>{row.planName} · {row.amount} {row.currency}</small><small>{new Date(row.createdAtUtc).toLocaleString("ar-EG-u-nu-latn")}</small><a href={`/api/v1/assets/${row.proofAssetId}`} target="_blank" rel="noreferrer">عرض صورة الإثبات</a></div>{canReview && <div className="row-actions"><label>سبب الرفض<input value={reason[row.id] ?? ""} maxLength={500} onChange={e => setReason(value => ({ ...value, [row.id]: e.target.value }))} /></label><button className="primary-button" disabled={busy} onClick={() => void decide(row, true)}>اعتماد بعد التحقق</button><button className="secondary-button" disabled={busy || !reason[row.id]?.trim()} onClick={() => void decide(row, false)}>رفض مع السبب</button></div>}</article>)}</section>;
}
