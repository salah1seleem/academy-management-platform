"use client";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { dayLabel } from "../components/weekdays-field";
import { GroupTimetableForm, type GroupSchedule, type ResourcePlan } from "./group-timetable-form";
import { RecordTabs } from "../components/record-tabs";
type Data = { group?: { branchId: string; sportId: string }; resourcePlan?: ResourcePlan | null; coaches?: { id: string; name: string }[]; players: { id: string; playerId: string; arabicName: string }[]; schedules: GroupSchedule[]; sessions: { id: string; sessionDate: string; startTime: string; endTime: string }[] };
export function GroupWorkspace({ id }: { id: string }) {
  const [data, setData] = useState<Data | null>(null); const [error, setError] = useState("");
  const load = useCallback(() => fetch(`/api/v1/manage/groups/${id}/details`).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => setError("تعذر تحميل تفاصيل المجموعة.")), [id]);
  useEffect(() => { void load(); }, [load]);
  if (!data) return error ? <ErrorState message={error} /> : <LoadingState />;
  return <RecordTabs label="أقسام ملف المجموعة" tabs={[{ id: "overview", label: "نظرة عامة", content: <section className="detail-card"><h2>ملخص المجموعة</h2><p>{data.players.length} لاعب · {data.coaches?.length ?? 0} مدرب · {data.schedules.length} موعد أسبوعي</p><nav className="profile-record-links"><Link href={`?tab=players`}>لاعبو المجموعة</Link><Link href={`?tab=schedule`}>جدول المجموعة</Link></nav></section> }, { id: "players", label: "اللاعبون", content: <section className="detail-card"><h2>لاعبو المجموعة</h2><div className="linked-list">{data.players.map(p => <article key={p.id}><strong>{p.arabicName}</strong><Link href={`/dashboard/players/${p.playerId}`}>ملف اللاعب</Link></article>)}{!data.players.length && <p>لا يوجد لاعبون مرتبطون بالمجموعة.</p>}</div></section> }, { id: "schedule", label: "الجدول", content:
    <section className="detail-card"><div className="group-sessions-heading"><h2>جدول المجموعة والحصص</h2><Link className="secondary-button" href={`/dashboard/academy/groups/sessions?groupId=${id}`}>عرض حصص المجموعة</Link></div>{data?.schedules.map((s, i) => <p key={i}>{dayLabel(s.dayOfWeek)} · {formatTimeAr(s.startTime)} — {formatTimeAr(s.endTime)}</p>)}
      <details><summary>إنشاء الحصص / تعديل الجدول</summary><GroupTimetableForm key={JSON.stringify(data.schedules)} id={id} schedules={data.schedules} group={data.group} resourcePlan={data.resourcePlan} coaches={data.coaches} players={data.players.length} /></details>
      <div className="linked-list">{data?.sessions.slice(0, 20).map(s => <article key={s.id}><span>{formatSessionDateAr(s.sessionDate)} · {formatTimeAr(s.startTime)} — {formatTimeAr(s.endTime)}</span><Link href={`/dashboard/attendance/sessions/${s.id}/players`}>حضور اللاعبين</Link></article>)}</div>
    </section> }, { id: "coaches", label: "المدربون", content: <section className="detail-card"><h2>مدربو المجموعة</h2><div className="linked-list">{data.coaches?.map(c => <article key={c.id}><strong>{c.name}</strong><Link href={`/dashboard/academy/coaches/${c.id}`}>ملف المدرب</Link></article>)}{!data.coaches?.length && <p>لم يُسند مدرب بعد.</p>}</div></section> }, { id: "performance", label: "الأداء", content: <section className="detail-card"><h2>أداء المجموعة</h2><nav className="profile-record-links"><Link href={`/dashboard/evaluations?groupId=${id}`}>تقييمات لاعبي المجموعة</Link><Link href={`/dashboard/attendance/sessions?groupId=${id}`}>حضور المجموعة</Link></nav></section> }]} />;
}
