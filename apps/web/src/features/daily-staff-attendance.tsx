"use client";
import { WeekdaysField } from "../components/weekdays-field";
import { TimeField } from "../components/time-field";
import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions, StatusBadge } from "../components/dashboard-shell";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatDateAr, formatMoneyAr, formatTimeAr, formatStaffCheckout } from "../lib/formatters";
import { StaffWorkPeriodsPanel, PeriodSummary, WorkPeriod } from "./staff-work-periods";

type Coach = { id: string; name: string; workDate: string; today?: string; version?: number; periodCount?: number; periods?: WorkPeriod[]; changeReason?: string; status: string; checkIn?: string; checkOut?: string; checkOutDate?: string; lateMinutes: number; suggestedDeduction: number; approvedDeduction: number; deductionReason?: string };
type AttendanceChange = { id: string; createdAtUtc: string; actor: string; before: Coach | null; after: Coach };

export function DailyCoaches() {
  const defaults = useDateFilterDefaults();
  const [date, setDate] = useState(defaults.to), [search, setSearch] = useState("");
  const [rows, setRows] = useState<Coach[] | null>(null), [error, setError] = useState("");
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/staff-attendance/?${date ? `date=${date}` : "allDates=true"}`, { signal: abort.signal })
      .then(async r => { if (!r.ok) throw new Error(); const x = await r.json(); setRows(x.items); setError(""); })
      .catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل حضور المدربين."); });
    return () => abort.abort();
  }, [date]);
  const visible = rows?.filter(x => x.name.includes(search)) ?? [];
  return <><PageHeader title="حضور المدربين" context="الحضور اليومي" />
    <div className="report-filters"><label>التاريخ<input type="date" value={date} onChange={e => { setDate(e.target.value); setRows(null); }} /></label>
      <ListSearch label="البحث بالاسم" placeholder="اسم المدرب" value={search} onChange={setSearch} />
      <button onClick={() => { setDate(""); setSearch(""); setError(""); if (date) setRows(null); }}>مسح</button>
      <Link href="/dashboard/reports/attendance?subject=staff">تقارير المدربين والخصومات</Link>
      <Link href="/dashboard/attendance/staff/exceptions">طلبات إثبات الحضور — للمخولين بإدارة الموظفين</Link>
    </div>{error && <ErrorState message={error} />}{!rows && !error ? <LoadingState /> :
      !visible.length ? <EmptyState message="لا يوجد مدربون مطابقون." /> :
      <section className="list-card"><AdminDataTable label="حضور المدربين اليومي" rows={visible} rowKey={x => `${x.id}-${x.workDate}`} columns={[
        { key: "coach", header: "المدرب", primary: true, render: x => x.name },
        { key: "date", header: "التاريخ", render: x => formatDateAr(x.workDate) },
        { key: "time", header: "حضور / انصراف", render: x => `${x.checkIn ? formatTimeAr(x.checkIn) : "—"} / ${formatStaffCheckout(x.checkOut, x.checkOutDate, x.workDate)}` },
        { key: "state", header: "الحالة", render: x => <StatusBadge status={x.status} /> },
        { key: "late", header: "التأخير / الخصم", render: x => <>{x.lateMinutes} دقيقة<small>{formatMoneyAr(x.approvedDeduction, "EGP")}</small></> },
        { key: "actions", header: "الإجراء", render: x => <RowActions actions={[{ label: "تسجيل / تعديل", href: `/dashboard/attendance/staff/${x.id}?date=${x.workDate}` }]} /> },
      ]} /></section>}</>;
}

export function DailyCoachRecord({ id, initialDate }: { id: string; initialDate?: string }) {
  const defaults = useDateFilterDefaults(), router = useRouter();
  const [date, setDate] = useState(initialDate ?? defaults.to), [coach, setCoach] = useState<Coach | null>(null);
  const [error, setError] = useState(""), [busy, setBusy] = useState(false), [status, setStatus] = useState("NotRecorded"), [deduction, setDeduction] = useState(0);
  const [revision, setRevision] = useState(0), [stale, setStale] = useState(false);
  const [pending, setPending] = useState<{ key: string; body: object } | null>(null);
  const [history, setHistory] = useState<{ total: number; timeZone: string; items: AttendanceChange[] } | null>(null), [historyError, setHistoryError] = useState("");
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/staff-attendance/${id}?date=${date}`, { signal: abort.signal }).then(async r => {
      if (!r.ok) throw new Error(); const x = await r.json() as Coach; setCoach(x); setStatus(x.status); setDeduction(x.approvedDeduction); setError(""); setStale(false);
    }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل سجل المدرب."); });
    return () => abort.abort();
  }, [id, date, revision]);
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/staff-attendance/${id}/history?date=${date}`, { signal: abort.signal }).then(async r => {
      if (!r.ok) throw new Error(); setHistory(await r.json()); setHistoryError("");
    }).catch(e => { if (e.name !== "AbortError") setHistoryError("تعذر تحميل سجل التعديلات."); });
    return () => abort.abort();
  }, [id, date, revision]);
  const future = date > (coach?.today ?? defaults.to);
  const managed = (coach?.periodCount ?? 0) > 0;
  function reload() { setCoach(null); setHistory(null); setRevision(x => x + 1); }
  async function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (!coach || busy || future || stale || coach.workDate !== date) return;
    const f = new FormData(e.currentTarget); setBusy(true); setError("");
    const command = pending ?? { key: crypto.randomUUID(), body: { date, version: coach.version ?? 0, status: managed ? coach.status : status, checkIn: managed ? coach.checkIn : status === "Present" && f.get("in") ? `${f.get("in")}:00` : null, checkOut: managed ? coach.checkOut ?? null : status === "Present" && f.get("out") ? `${f.get("out")}:00` : null, ...(managed ? { checkOutDate: coach.checkOutDate ?? null } : status === "Present" && f.get("out") ? { checkOutDate: f.get("outDate") || date } : {}), approvedDeduction: deduction, reason: f.get("reason") } };
    setPending(command);
    try {
      await csrfRequest(`/api/v1/manage/staff-attendance/${id}`, "PUT", command.body, { "Idempotency-Key": command.key }, { announce: false });
      setPending(null);
      window.dispatchEvent(new CustomEvent("academy-save-success", { detail: "تم حفظ حضور المدرب بنجاح." }));
      router.push("/dashboard/attendance/staff");
    } catch (e) {
      if (e instanceof DashboardRequestError) { setPending(null); if (e.status === 409) setStale(true); }
      setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد حفظ الحضور. أعد إرسال نفس الطلب للتحقق دون تكراره.");
    } finally { setBusy(false); }
  }
  return <><PageHeader title={coach ? `حضور ${coach.name}` : "تسجيل حضور المدرب"} context="الحضور اليومي" action={{ label: "عودة للقائمة", href: "/dashboard/attendance/staff" }} />
    {error && <ErrorState message={error} />}{future && <ErrorState message="لا يمكن تسجيل الحضور بتاريخ مستقبلي." />}
    {stale && <button type="button" onClick={() => { setCoach(null); setHistory(null); setRevision(x => x + 1); }}>إعادة تحميل السجل</button>}
    {pending && !busy && <p role="status">تعذر تأكيد نتيجة الحفظ. أعد إرسال نفس الطلب؛ لن تتكرر العملية.</p>}
    {!coach && !error ? <LoadingState /> : coach && <section className="detail-card"><form onSubmit={e => void save(e)}>
      <fieldset key={`${id}-${coach.workDate}-${revision}`} className="form-shell staff-daily-edit-fields" disabled={busy || !!pending || stale}>
      <label>التاريخ<input type="date" required max={coach.today ?? defaults.to} value={date} onChange={e => { if (e.target.value) { setCoach(null); setHistory(null); setDate(e.target.value); } }} /></label>
      {!managed && <label>الحالة<select name="status" aria-label="الحالة" value={status} onChange={e => setStatus(e.target.value)}><option value="NotRecorded">لم يُسجل</option><option value="Present">حاضر</option><option value="Absent">غائب</option></select></label>}
      {managed ? <p>نطاق اليوم: {coach.checkIn ? formatTimeAr(coach.checkIn) : "—"} — {formatStaffCheckout(coach.checkOut, coach.checkOutDate, coach.workDate)}. هذا النطاق ليس مدة عمل؛ صحح الفترة المحددة أدناه.</p> : status === "Present" && <><TimeField label="وقت الحضور" name="in" required defaultValue={coach.checkIn} /><TimeField label="وقت الانصراف" name="out" defaultValue={coach.checkOut} /><label>تاريخ الانصراف<input type="date" name="outDate" aria-label="تاريخ الانصراف" min={date} max={coach.today ?? defaults.to} defaultValue={coach.checkOutDate ?? date} /><small>اختر اليوم التالي إذا استمر العمل بعد منتصف الليل. بدون وقت انصراف يبقى الحضور مفتوحًا.</small></label></>}
      <p>خصم التأخير المقترح: {formatMoneyAr(coach.suggestedDeduction, "EGP")}</p>
      <label>الخصم المعتمد<input name="deduction" type="number" min="0" step="0.01" value={deduction} onChange={e => setDeduction(Number(e.target.value))} /></label>
      <label>سبب الخصم{deduction > 0 ? " (مطلوب)" : " (اختياري)"}<textarea name="reason" required={deduction > 0} maxLength={500} defaultValue={coach.deductionReason} /></label>
      </fieldset><button className="primary-button" disabled={busy || future || stale}>{busy ? "جارٍ الحفظ…" : pending ? "إعادة إرسال نفس الحفظ" : "حفظ الحضور"}</button>
    </form><StaffWorkPeriodsPanel key={`${id}-${date}-${revision}`} id={id} date={date} onSaved={reload} /><section aria-label="سجل تعديلات الحضور"><h2>سجل تعديلات الحضور اليدوي</h2><p>يعرض الحفظ اليدوي لهذا اليوم فقط. طلبات إثبات الحضور وقراراتها في سجل مستقل.</p>
      {historyError ? <ErrorState message={historyError} /> : !history ? <LoadingState /> : !history.items.length ? <p>لا توجد تعديلات يدوية محفوظة لهذا اليوم.</p> : <><p>آخر {history.items.length} من {history.total} تعديل</p><ol className="staff-daily-history">{history.items.map(change => <li key={change.id}>
        <strong>{change.actor}</strong><time dateTime={change.createdAtUtc}>{new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "medium", timeStyle: "short", timeZone: history.timeZone }).format(new Date(change.createdAtUtc))} — توقيت الأكاديمية</time>
        <div><span>قبل التعديل</span><AttendanceSnapshot value={change.before} /></div><div><span>بعد التعديل</span><AttendanceSnapshot value={change.after} /></div>
      </li>)}</ol></>}
    </section><CoachWorkSchedule id={id} /></section>}</>;
}
function AttendanceSnapshot({ value }: { value: Coach | null }) {
  if (!value) return <p>لا يوجد سجل سابق</p>;
  return <><p>{value.status === "Present" ? "حاضر" : value.status === "Absent" ? "غائب" : "لم يُسجل"} · {value.checkIn ? formatTimeAr(value.checkIn) : "—"} / {formatStaffCheckout(value.checkOut, value.checkOutDate, value.workDate)} · الخصم: {formatMoneyAr(value.approvedDeduction, "EGP")}{value.deductionReason && <small>{value.deductionReason}</small>}{value.changeReason && <small>سبب التصحيح: {value.changeReason}</small>}</p>{!!value.periods?.length && <ol>{value.periods.map(p => <li key={p.id}><PeriodSummary value={p} /></li>)}</ol>}</>;
}
function CoachWorkSchedule({ id }: { id: string }) {
  const [message, setMessage] = useState("");
  const [schedule, setSchedule] = useState<{ dayOfWeek: string | number; startTime: string; endTime: string; lateThresholdMinutes: number; lateDeduction: number }[]>([]);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => { fetch(`/api/v1/manage/staff-attendance/${id}/schedule`).then(async r => { if (!r.ok) throw new Error(); setSchedule(await r.json()); setLoaded(true); }).catch(() => setMessage("تعذر تحميل جدول الدوام الحالي.")); }, [id]);
  async function save(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const f = new FormData(e.currentTarget); try { await csrfRequest(`/api/v1/manage/staff-attendance/${id}/schedule`, "PUT", { days: f.getAll("day").map(Number), start: `${f.get("start")}:00`, end: `${f.get("end")}:00`, thresholdMinutes: Number(f.get("threshold")), deduction: Number(f.get("deduction")) }); setMessage("تم حفظ جدول الدوام للأيام المختارة. السجلات السابقة لا تتغير."); } catch (e) { setMessage(e instanceof Error ? e.message : "تعذر حفظ جدول الدوام."); } }
  return <details><summary>جدول دوام المدرب وقاعدة التأخير</summary>{!loaded ? <p>{message || "جارٍ تحميل الدوام…"}</p> : <form key={id} className="form-shell" onSubmit={e => void save(e)}><WeekdaysField name="day" label="أيام الدوام" initialDays={schedule.map(s => s.dayOfWeek)} /><TimeField label="من الساعة" name="start" required defaultValue={schedule[0]?.startTime} /><TimeField label="إلى الساعة" name="end" required defaultValue={schedule[0]?.endTime} /><label>حد التأخير بالدقائق<input type="number" min="0" max="480" name="threshold" defaultValue={schedule[0]?.lateThresholdMinutes ?? 30} required /></label><label>الخصم المقترح عند بلوغ الحد<input type="number" name="deduction" min="0" step="0.01" defaultValue={schedule[0]?.lateDeduction ?? 100} required /></label><button>حفظ الدوام</button>{message && <p role="status">{message}</p>}</form>}</details>;
}
