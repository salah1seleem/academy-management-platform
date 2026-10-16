"use client";
import { FormEvent, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr } from "../lib/formatters";

type Row = { id: string; name: string; branch: string; operation: string; status: string; workDate: string; claimedAtUtc: string; receivedAtUtc: string };
type Detail = { name: string; branch: string; timeZone: string; canReview: boolean; currentAttendance?: { status: string; checkIn?: string; checkOut?: string } | null; request: Row & { reason: string; reviewNote?: string; reviewedAtUtc?: string; version: number; attendanceId?: string } };
const state = (v: string) => ({ Pending: "يحتاج مراجعة", Approved: "مقبول إداريًا", Rejected: "مرفوض" }[v] ?? "غير معروف");
const operation = (v: string) => v === "CheckIn" ? "حضور" : "انصراف";

export function StaffAttendanceExceptionInbox() {
  const [status, setStatus] = useState("Pending"), [revision, setRevision] = useState(0);
  const [data, setData] = useState<{ total: number; items: Row[] } | null>(null), [error, setError] = useState("");
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/staff-attendance-exceptions/?${new URLSearchParams({ status })}`, { signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); const next = await r.json(); if (!abort.signal.aborted) { setData(next); setError(""); } }).catch(() => { if (!abort.signal.aborted) setError("تعذر عرض الطلبات أو لا تملك صلاحية الموظفين."); });
    return () => abort.abort();
  }, [status, revision]);
  return <><PageHeader title="طلبات إثبات حضور الموظفين" context="الحضور الوظيفي" action={{ label: "عودة للحضور", href: "/dashboard/attendance/staff" }} />
    <p>الطلب ليس حضورًا مؤكدًا. القبول يسجل مراجعة إدارية، وليس إثبات بصمة أو موقع، ولا ينشئ خصمًا أو زيادة مالية.</p>
    <div className="report-filters"><label>حالة الطلب<select value={status} onChange={e => { setStatus(e.target.value); setData(null); }}><option value="Pending">يحتاج مراجعة</option><option value="Approved">مقبول إداريًا</option><option value="Rejected">مرفوض</option><option value="">الكل</option></select></label><button onClick={() => { setStatus(""); setData(null); setRevision(x => x + 1); }}>مسح الفلتر</button><button onClick={() => { setData(null); setRevision(x => x + 1); }}>تحديث</button></div>
    {error && <ErrorState message={error} />}{!data && !error && <LoadingState />}{data && <><p>{data.total} طلب — أحدث 200 سجل ضمن صلاحياتك</p>{data.items.length === 0 ? <EmptyState message="لا توجد طلبات بهذه الحالة." /> : <section className="list-card"><AdminDataTable label="طلبات إثبات الحضور" rows={data.items} rowKey={x => x.id} columns={[
      { key: "name", header: "الموظف", primary: true, render: x => <>{x.name}<small>{x.branch}</small></> },
      { key: "date", header: "اليوم", render: x => formatDateAr(x.workDate) },
      { key: "operation", header: "الطلب", render: x => operation(x.operation) },
      { key: "status", header: "الحالة", render: x => state(x.status) },
      { key: "action", header: "الإجراء", render: x => <Link href={`/dashboard/attendance/staff/exceptions/${x.id}`}>عرض الطلب</Link> },
    ]} /></section>}</>}</>;
}

export function StaffAttendanceExceptionDetail({ id }: { id: string }) {
  const [data, setData] = useState<Detail | null>(null), [error, setError] = useState(""), [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false), [note, setNote] = useState("");
  const [pending, setPending] = useState<{ approve: boolean; note: string; version: number; key: string } | null>(null);
  const load = useCallback(async () => { const r = await fetch(`/api/v1/manage/staff-attendance-exceptions/${id}`, { cache: "no-store" }); if (!r.ok) { if ([401,403,404].includes(r.status)) setData(null); throw new Error("تعذر عرض تفاصيل الطلب أو لم يعد ضمن صلاحياتك."); } setData(await r.json()); }, [id]);
  useEffect(() => { let active = true; fetch(`/api/v1/manage/staff-attendance-exceptions/${id}`).then(async r => { if (!r.ok) throw new Error(); const d = await r.json(); if (active) setData(d); }).catch(() => { if (active) setError("تعذر عرض تفاصيل الطلب أو لم يعد ضمن صلاحياتك."); }); return () => { active = false; }; }, [id]);
  async function decide(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (!data || busy || !data.canReview) return;
    const submitter = (e.nativeEvent as SubmitEvent).submitter as HTMLButtonElement;
    const command = pending ?? { approve: submitter.value === "approve", note: note.trim(), version: data.request.version, key: crypto.randomUUID() };
    if (!command.approve && command.note.length < 5) { setError("اكتب سبب الرفض من خمسة أحرف على الأقل."); return; }
    setBusy(true); setError(""); setPending(command);
    try {
      const response = await csrfRequest(`/api/v1/manage/staff-attendance-exceptions/${id}/review`, "POST", { approve: command.approve, note: command.note || null, version: command.version }, { "Idempotency-Key": command.key }, { announce: false });
      const saved = await response.json() as { status?: string };
      if (saved.status !== (command.approve ? "Approved" : "Rejected")) throw new Error("لم يتأكد القرار المتوقع؛ أعد تحميل الطلب.");
      await load(); setPending(null); setMessage(command.approve ? "تم قبول الطلب وتسجيل الحضور إداريًا." : "تم رفض الطلب؛ لم يتغير الحضور.");
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر تأكيد القرار. أعد إرسال القرار نفسه أو حدّث التفاصيل."); }
    finally { setBusy(false); }
  }
  return <><PageHeader title="مراجعة إثبات الحضور" context="الحضور الوظيفي" action={{ label: "عودة للطلبات", href: "/dashboard/attendance/staff/exceptions" }} />
    {error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}{!data && !error && <LoadingState />}{data && <section className="detail-card">
      <h2>{data.name}</h2><p>{data.branch} · {formatDateAr(data.request.workDate)} · {operation(data.request.operation)}</p><p>{state(data.request.status)}</p>
      <dl><dt>الوقت المدعى — بتوقيت الأكاديمية</dt><dd>{new Date(data.request.claimedAtUtc).toLocaleString("ar-EG-u-nu-latn", { timeZone: data.timeZone })}</dd><dt>استلام الخادم — بتوقيت الأكاديمية</dt><dd>{new Date(data.request.receivedAtUtc).toLocaleString("ar-EG-u-nu-latn", { timeZone: data.timeZone })}</dd><dt>سبب الطلب</dt><dd>{data.request.reason}</dd></dl>
      <p>القبول مراجعة إدارية مستقلة، لا إثبات جهاز. لا يمكن اعتماد طلبك أنت، أو استبدال سجل قائم، أو تعديل شهر معتمد.</p>
      {"currentAttendance" in data && <aside><h3>الحضور المسجل لهذا اليوم</h3>{data.currentAttendance ? <p>{data.currentAttendance.status === "Present" ? "حاضر" : data.currentAttendance.status === "Absent" ? "غائب" : "لم يُسجل"} · حضور {data.currentAttendance.checkIn?.slice(0,5) ?? "—"} · انصراف {data.currentAttendance.checkOut?.slice(0,5) ?? "—"}</p> : <p>لا يوجد سجل حضور لهذا اليوم.</p>}</aside>}
      {data.request.reviewNote && <p>ملاحظة القرار: {data.request.reviewNote}</p>}
      {data.canReview ? <form className="form-shell" onSubmit={e => void decide(e)}><label>ملاحظة القرار — مطلوبة عند الرفض<textarea maxLength={500} value={note} disabled={busy || pending !== null} onChange={e => setNote(e.target.value)} /></label>
        {pending ? <><p>يوجد قرار لم يُؤكد بعد؛ إعادة الإرسال تستخدم القرار والمفتاح نفسيهما.</p><button className="primary-button" value={pending.approve ? "approve" : "reject"} disabled={busy}>إعادة تأكيد القرار</button><button type="button" disabled={busy} onClick={() => { void load().then(() => { setPending(null); setError(""); }).catch(() => setError("تعذر إعادة قراءة الطلب.")); }}>إعادة تحميل التفاصيل</button></> : <><button className="primary-button" value="approve" disabled={busy}>قبول وتسجيل إداري</button><button value="reject" disabled={busy}>رفض الطلب</button></>}
      </form> : data.request.status === "Pending" && <p>لا يمكنك مراجعة هذا الطلب بنفسك أو ليس لديك إذن التعديل؛ يعتمده مسؤول آخر مخول.</p>}
    </section>}</>;
}
