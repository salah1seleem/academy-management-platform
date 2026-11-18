"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { ArrowUpLeft, CalendarDays, ListChecks } from "lucide-react";
import { WeekdaysField } from "../components/weekdays-field";
import { TimeField } from "../components/time-field";
import { formatDateAr, formatTimeAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Choice = { groupId: string; group: string; rank: number; currentPlayers: number; capacity: number; nextDate: string; startTime: string; reasons: string[] };
type Suggestions = { asOfDate: string; waitingDays: number; choices: Choice[]; noAlternativeMessage?: string };
type Coach = { id: string; name: string };
const loadMessage = "تعذر تحميل تفضيلات التسكين. أعد المحاولة.";
function record(value: unknown): value is Record<string, unknown> { return !!value && typeof value === "object" && !Array.isArray(value); }
function date(value: unknown): value is string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = Date.parse(`${value}T12:00:00Z`);
  return Number.isFinite(parsed) && new Date(parsed).toISOString().slice(0, 10) === value;
}
function validCoaches(value: unknown): value is { coaches: Coach[] } {
  return record(value) && Array.isArray(value.coaches) && value.coaches.every(c => record(c) && typeof c.id === "string" && typeof c.name === "string");
}
function validSuggestions(value: unknown): value is Suggestions {
  if (!record(value) || !date(value.asOfDate) || !Number.isSafeInteger(value.waitingDays) || (value.waitingDays as number) < 0 || !Array.isArray(value.choices) || value.choices.length > 3) return false;
  const asOf = value.asOfDate;
  return (value.choices.length > 0 || typeof value.noAlternativeMessage === "string" && value.noAlternativeMessage.trim().length > 0)
    && (value.noAlternativeMessage == null || typeof value.noAlternativeMessage === "string")
    && value.choices.every((c, i) => record(c) && typeof c.groupId === "string" && typeof c.group === "string" && c.rank === i + 1
      && typeof c.capacity === "number" && Number.isSafeInteger(c.capacity) && c.capacity > 0
      && typeof c.currentPlayers === "number" && Number.isSafeInteger(c.currentPlayers) && c.currentPlayers >= 0 && c.currentPlayers <= c.capacity
      && date(c.nextDate) && c.nextDate >= asOf && typeof c.startTime === "string" && /^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$/.test(c.startTime)
      && Array.isArray(c.reasons) && c.reasons.length > 0 && c.reasons.every(reason => typeof reason === "string"));
}
export function PlacementRecommendations({ enrollmentId }: { enrollmentId: string }) {
  const [options, setOptions] = useState<{ id: string; coaches: Coach[] }>(); const [savedResult, setResult] = useState<{ id: string; value: Suggestions }>(); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [attempt, setAttempt] = useState(0), [loadFailure, setLoadFailure] = useState<{ id: string; message: string }>();
  const coaches = options?.id === enrollmentId ? options.coaches : undefined;
  const result = savedResult?.id === enrollmentId ? savedResult.value : undefined;
  const loadError = loadFailure?.id === enrollmentId ? loadFailure.message : "";
  useEffect(() => { let alive = true; fetch(`/api/v1/people/enrollments/${enrollmentId}/placement-preferences`).then(async r => {
    if (!r.ok) throw new Error(); const data: unknown = await r.json(); if (!validCoaches(data)) throw new Error();
    if (alive) { setOptions({ id: enrollmentId, coaches: data.coaches }); setLoadFailure(undefined); setError(""); setResult(undefined); }
  }).catch(() => { if (alive) setLoadFailure({ id: enrollmentId, message: loadMessage }); }); return () => { alive = false; }; }, [enrollmentId, attempt]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const data = new FormData(event.currentTarget); setBusy(true); setError(""); setResult(undefined);
    try { const r = await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/placement-recommendations`, "POST", { days: data.getAll("days").map(Number), startTime: data.get("preferredTime") || null, preferredCoachId: data.get("coach") || null }); const value: unknown = await r.json(); if (!validSuggestions(value)) throw new Error(); setResult({ id: enrollmentId, value }); }
    catch (caught) { setError(caught instanceof DashboardRequestError ? caught.message : "تعذر عرض بدائل التسكين. أعد المحاولة."); } finally { setBusy(false); }
  }
  return <details className="placement-level placement-recommendations"><summary><ListChecks size={18} />اقتراح مجموعات مناسبة</summary><p className="muted">أفضل ثلاثة بدائل مستوفية للشروط. التفضيلات لا تتجاوز المستوى أو السعة أو الاستحقاق، والاختيار لا يحفظ التسكين.</p>{!coaches ? loadError ? <div><p role="alert">{loadError}</p><button type="button" className="secondary-button" onClick={() => { setLoadFailure(undefined); setAttempt(value => value + 1); }}>إعادة تحميل تفضيلات التسكين</button></div> : <p role="status">جارٍ تحميل تفضيلات التسكين…</p> : <form className="form-shell" onSubmit={event => void submit(event)} onChange={() => setResult(undefined)} onClick={event => { if ((event.target as HTMLElement).closest(".time-field-clear")) setResult(undefined); }}>
    <WeekdaysField /><TimeField name="preferredTime" label="وقت مفضل اختياري" /><label>مدرب مفضل اختياري<select name="coach"><option value="">دون تفضيل</option>{coaches.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><div className="form-actions"><button className="secondary-button" disabled={busy}>{busy ? "جارٍ فحص المجموعات…" : "عرض البدائل المؤهلة"}</button></div></form>}
    {coaches && error && <p role="alert">{error}</p>}{result && <section className="placement-suggestions" aria-label="بدائل التسكين" aria-live="polite"><p>بتاريخ {formatDateAr(result.asOfDate)} · في الانتظار {result.waitingDays} يوم</p>{result.noAlternativeMessage && <p className="placement-conflicts">{result.noAlternativeMessage}</p>}<div>{result.choices.map(choice => <article key={choice.groupId}><header><span>{choice.rank}</span><h4>{choice.group}</h4></header><p><CalendarDays size={16} />{formatDateAr(choice.nextDate)} · {formatTimeAr(choice.startTime)}</p><p>{choice.currentPlayers} لاعب من {choice.capacity}</p><ul>{choice.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul><Link href={`/dashboard/academy/groups/${choice.groupId}?tab=players&candidateId=${enrollmentId}`}>فتح المجموعة ومراجعة التسكين<ArrowUpLeft size={16} /></Link></article>)}</div></section>}
  </details>;
}
