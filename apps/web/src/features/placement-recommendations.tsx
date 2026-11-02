"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { ArrowUpLeft, CalendarDays, ListChecks } from "lucide-react";
import { WeekdaysField } from "../components/weekdays-field";
import { TimeField } from "../components/time-field";
import { formatDateAr, formatTimeAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";

type Choice = { groupId: string; group: string; rank: number; currentPlayers: number; capacity: number; nextDate: string; startTime: string; reasons: string[] };
type Suggestions = { asOfDate: string; waitingDays: number; choices: Choice[]; noAlternativeMessage?: string };
export function PlacementRecommendations({ enrollmentId }: { enrollmentId: string }) {
  const [coaches, setCoaches] = useState<{ id: string; name: string }[]>([]); const [result, setResult] = useState<Suggestions>(); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { let alive = true; fetch(`/api/v1/people/enrollments/${enrollmentId}/placement-preferences`).then(async r => { if (!r.ok) throw new Error("عرض البدائل يحتاج إذن قراءة المجموعات واللاعبين."); const data = await r.json(); if (alive) setCoaches(data.coaches); }).catch(c => { if (alive) setError(c.message); }); return () => { alive = false; }; }, [enrollmentId]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const data = new FormData(event.currentTarget); setBusy(true); setError(""); setResult(undefined);
    try { const r = await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/placement-recommendations`, "POST", { days: data.getAll("days").map(Number), startTime: data.get("preferredTime") || null, preferredCoachId: data.get("coach") || null }); setResult(await r.json()); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر عرض البدائل."); } finally { setBusy(false); }
  }
  return <details className="placement-level placement-recommendations"><summary><ListChecks size={18} />اقتراح مجموعات مناسبة</summary><p className="muted">أفضل ثلاثة بدائل مستوفية للشروط. التفضيلات لا تتجاوز المستوى أو السعة أو الاستحقاق، والاختيار لا يحفظ التسكين.</p><form className="form-shell" onSubmit={event => void submit(event)} onChange={() => setResult(undefined)} onClick={event => { if ((event.target as HTMLElement).closest(".time-field-clear")) setResult(undefined); }}>
    <WeekdaysField /><TimeField name="preferredTime" label="وقت مفضل اختياري" /><label>مدرب مفضل اختياري<select name="coach"><option value="">دون تفضيل</option>{coaches.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><div className="form-actions"><button className="secondary-button" disabled={busy}>{busy ? "جارٍ فحص المجموعات…" : "عرض البدائل المؤهلة"}</button></div></form>
    {error && <p role="alert">{error}</p>}{result && <section className="placement-suggestions" aria-label="بدائل التسكين" aria-live="polite"><p>بتاريخ {formatDateAr(result.asOfDate)} · في الانتظار {result.waitingDays} يوم</p>{result.noAlternativeMessage && <p className="placement-conflicts">{result.noAlternativeMessage}</p>}<div>{result.choices.map(choice => <article key={choice.groupId}><header><span>{choice.rank}</span><h4>{choice.group}</h4></header><p><CalendarDays size={16} />{formatDateAr(choice.nextDate)} · {formatTimeAr(choice.startTime)}</p><p>{choice.currentPlayers} لاعب من {choice.capacity}</p><ul>{choice.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul><Link href={`/dashboard/academy/groups/${choice.groupId}?tab=players&candidateId=${enrollmentId}`}>فتح المجموعة ومراجعة التسكين<ArrowUpLeft size={16} /></Link></article>)}</div></section>}
  </details>;
}
