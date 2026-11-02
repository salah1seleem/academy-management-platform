import Link from "next/link";

export type RecordedAbsenceData = { playerCount: number; enrollmentCount: number; sessionCount: number; fromDate: string | null; toDate: string | null };
export type AbsenceSourceRecord = { SessionId: string; SessionDate: string; StartTime: string };
export function RecordedAbsenceReview({ data }: { data?: RecordedAbsenceData | null }) {
  return <div aria-label="ملخص الغياب المسجل">
    <p>{data ? `${data.playerCount} لاعب فريد · ${data.enrollmentCount} تسجيل له غياب في آخر 3 حصص مؤهلة مسجلة داخل المجموعة.` : "بيانات الغياب غير متاحة حاليًا."}</p>
    {data?.fromDate && <p>فترة السجلات: <bdi>{data.fromDate} — {data.toDate}</bdi></p>}
    <small>الملغاة وغير المسجلة والمستقبلية لا تُحسب. مراجعة بشرية دون رسالة أو عقوبة تلقائية.</small>
  </div>;
}
export function RecordedAbsenceLinks({ records }: { records?: AbsenceSourceRecord[] | null }) {
  const sessions = [...new Map(records?.map(r => [r.SessionId, r])).values()];
  return <>{sessions.slice(0, 3).map(s => <p key={s.SessionId}><Link href={`/dashboard/attendance/sessions/${s.SessionId}/players`}>مراجعة كشف <bdi>{s.SessionDate} · {s.StartTime.slice(0, 5)}</bdi></Link></p>)}
    {sessions.length > 3 && <small>تعرض أول 3 كشوف من دليل الإنشاء؛ بقية الحصص في المجموعة.</small>}
    <small>دليل الإنشاء ثابت؛ إغلاق المتابعة لا يغير حضور أي لاعب.</small></>;
}
