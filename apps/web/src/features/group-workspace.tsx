"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { announceSaveSuccess, csrfRequest } from "./dashboard-api";
import { formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { TimeField } from "../components/time-field";
import { dayLabel, WeekdaysField } from "../components/weekdays-field";
type Data = { players: { id: string; playerId: string; arabicName: string }[]; schedules: { dayOfWeek: number | string; startTime: string; endTime: string; validFrom?: string; validTo?: string }[]; sessions: { id: string; sessionDate: string; startTime: string; endTime: string }[] };
export function GroupWorkspace({ id }: { id: string }) {
  const router = useRouter();
  const [data, setData] = useState<Data | null>(null); const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(() => fetch(`/api/v1/manage/groups/${id}/details`).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => setError("تعذر تحميل تفاصيل المجموعة.")), [id]);
  useEffect(() => { void load(); }, [load]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setBusy(true); const form = new FormData(event.currentTarget);
    if (!form.getAll("days").length) { setError("اختر يوم تدريب واحدًا على الأقل."); setBusy(false); return; }
    try { const result = await csrfRequest(`/api/v1/manage/groups/${id}/timetable`, "POST", { from: form.get("from"), to: form.get("to"), days: form.getAll("days").map(Number), startTime: `${form.get("start")}:00`, endTime: `${form.get("end")}:00` }).then(r => r.json()); setMessage(`تم حفظ الجدول وإنشاء ${result.created} حصة بنجاح.`); announceSaveSuccess(`تم حفظ الجدول وإنشاء ${result.created} حصة بنجاح.`); router.push("/dashboard/academy/groups"); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ الجدول."); } finally { setBusy(false); }
  }
  return <><section className="detail-card"><h2>لاعبو المجموعة</h2>{!data ? <LoadingState /> : <div className="linked-list">{data.players.map(p => <article key={p.id}><strong>{p.arabicName}</strong><Link href={`/dashboard/players/${p.playerId}`}>ملف اللاعب</Link></article>)}{!data.players.length && <p>لا يوجد لاعبون مرتبطون بالمجموعة.</p>}</div>}</section>
    <section className="detail-card"><div className="group-sessions-heading"><h2>جدول المجموعة والحصص</h2><Link className="secondary-button" href={`/dashboard/academy/groups/sessions?groupId=${id}`}>عرض حصص المجموعة</Link></div>{data?.schedules.map((s, i) => <p key={i}>{dayLabel(s.dayOfWeek)} · {formatTimeAr(s.startTime)} — {formatTimeAr(s.endTime)}</p>)}
      {data && <details><summary>إنشاء الحصص / تعديل الجدول</summary><form key={JSON.stringify(data.schedules)} className="form-shell timetable-form" onSubmit={event => void submit(event)}><WeekdaysField initialDays={data.schedules.map(s => s.dayOfWeek)} />
        <label>من تاريخ<input type="date" name="from" required defaultValue={data?.schedules[0]?.validFrom} /></label><label>إلى تاريخ<input type="date" name="to" required defaultValue={data?.schedules[0]?.validTo} /></label><label className="timetable-duration">مدة سريعة<select defaultValue="" onChange={e => { const form = e.currentTarget.form; const from = form?.elements.namedItem("from") as HTMLInputElement | null; const to = form?.elements.namedItem("to") as HTMLInputElement | null; if (from?.value && to && e.target.value) { const date = new Date(`${from.value}T12:00:00Z`); const day = date.getUTCDate(); date.setUTCDate(1); date.setUTCMonth(date.getUTCMonth() + Number(e.target.value)); const lastDay = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate(); date.setUTCDate(Math.min(day, lastDay)); date.setUTCDate(date.getUTCDate() - 1); to.value = date.toISOString().slice(0, 10); } }}><option value="">اختر المدة بعد تاريخ البداية</option><option value="1">شهر</option><option value="6">ستة أشهر</option><option value="12">سنة</option></select></label><TimeField label="وقت البداية" name="start" required defaultValue={data.schedules[0]?.startTime} /><TimeField label="وقت النهاية" name="end" required defaultValue={data.schedules[0]?.endTime} />
        <div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : "حفظ وإنشاء الحصص"}</button></div>
      </form></details>}{error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}
      <div className="linked-list">{data?.sessions.slice(0, 20).map(s => <article key={s.id}><span>{formatSessionDateAr(s.sessionDate)} · {formatTimeAr(s.startTime)} — {formatTimeAr(s.endTime)}</span><Link href={`/dashboard/attendance/sessions/${s.id}/players`}>حضور اللاعبين</Link></article>)}</div>
    </section></>;
}
