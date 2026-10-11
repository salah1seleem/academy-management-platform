"use client";
import { FormEvent, useEffect, useState } from "react";
import { csrfRequest } from "./dashboard-api";

type Option = { id: string; arabicName: string };
export function UnassignedEnrollment({ playerId, onCreated }: { playerId: string; onCreated: () => void }) {
  const [options, setOptions] = useState<{ branches: Option[]; sports: Option[] }>();
  const [branch, setBranch] = useState(""); const [sport, setSport] = useState("");
  const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  useEffect(() => { let alive = true; fetch("/api/v1/people/unassigned/options").then(async r => { if (!r.ok) throw new Error(); const data = await r.json(); if (alive) setOptions(data); }).catch(() => { if (alive) setError("تعذر تحميل الرياضات والفروع المصرح بها."); }); return () => { alive = false; }; }, []);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError("");
    try { const result = await csrfRequest(`/api/v1/people/players/${playerId}/unassigned-enrollments`, "POST", { sportId: sport, branchId: branch }); await result.json(); setSport(""); onCreated(); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر إضافة التسجيل."); }
    finally { setBusy(false); }
  }
  return <section className="detail-card"><details><summary>إضافة رياضة في انتظار التسكين</summary><p>يُحفظ التسجيل في ملف اللاعب نفسه دون تحصيل أو تفعيل اشتراك أو إسناد مجموعة.</p><form onSubmit={submit} className="form-shell">
    <label>رياضة التسجيل<select required value={sport} onChange={e => setSport(e.target.value)}><option value="">اختر الرياضة</option>{options?.sports.map(o => <option key={o.id} value={o.id}>{o.arabicName}</option>)}</select></label>
    <label>فرع التسجيل<select required value={branch} onChange={e => setBranch(e.target.value)}><option value="">اختر الفرع</option>{options?.branches.map(o => <option key={o.id} value={o.id}>{o.arabicName}</option>)}</select></label>
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy || !options}>{busy ? "جارٍ الحفظ…" : "حفظ في انتظار التسكين"}</button></div>
    {error && <p role="alert">{error}</p>}
  </form></details></section>;
}
