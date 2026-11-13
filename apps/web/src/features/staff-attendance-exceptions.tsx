"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateAr, formatStaffCheckout } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./staff-attendance-exceptions.module.css";

type Row = { id: string; name: string; branch: string; operation: string; status: string; workDate: string; claimedAtUtc: string; receivedAtUtc: string };
type Period = { id: string; version: number; startDate: string; startTime: string; endDate?: string; endTime?: string; timeZoneId?: string };
type Detail = { name: string; branch: string; timeZone: string; canReview: boolean; currentPeriod?: Period | null; currentAttendance?: { status: string; checkIn?: string; checkOut?: string; checkOutDate?: string } | null; request: Row & { reason: string; reviewNote?: string; reviewedAtUtc?: string; version: number; attendanceId?: string; targetPeriod?: Period | null } };
type Decision = { approve: boolean; note: string; version: number; key: string };
type InboxData = { total: number; items: Row[] };
type InboxResult = { status: string; revision: number } & ({ data: InboxData } | { error: string });

const inboxPath = "/dashboard/attendance/staff/exceptions";
const endpoint = "/api/v1/manage/staff-attendance-exceptions";
const stateLabels: Record<string, string> = {
  Pending: "يحتاج مراجعة",
  Approved: "مقبول إداريًا",
  Rejected: "مرفوض",
  all: "كل الطلبات",
};
const state = (value: string) => stateLabels[value] ?? "غير معروف";
const operation = (value: string) => value === "CheckIn" ? "حضور" : value === "CheckOut" ? "انصراف" : "غير محدد";

function inboxStatus(value: string | null) {
  return value === "Approved" || value === "Rejected" || value === "all" ? value : "Pending";
}

function statusQuery(status: string) {
  return status === "Pending" ? "" : `?status=${status}`;
}

function unavailable(status: number) {
  return [401, 403, 404].includes(status);
}

function sameRequest(actual: string | undefined, expected: string) {
  return actual?.toLowerCase() === expected.toLowerCase();
}

function StatusBadge({ status }: { status: string }) {
  const tone = status === "Approved" ? "success" : status === "Rejected" ? "danger" : "info";
  return <span className={`status-badge status-${tone}`}>{state(status)}</span>;
}

function academyTime(value: string, timeZone: string) {
  try {
    return new Intl.DateTimeFormat("ar-EG-u-nu-latn", {
      timeZone,
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(value));
  } catch {
    return "وقت غير متاح";
  }
}

function periodZone(timeZone: string | undefined, academyZone: string, instant: string) {
  if (!timeZone) return "توقيت غير مؤكد";
  if (timeZone === academyZone) return "بتوقيت الأكاديمية";
  try {
    return new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone, timeZoneName: "longGeneric" })
      .formatToParts(new Date(instant)).find(part => part.type === "timeZoneName")?.value ?? "توقيت غير مؤكد";
  } catch {
    return "توقيت غير مؤكد";
  }
}

export function StaffAttendanceExceptionInbox() {
  const status = inboxStatus(useSearchParams().get("status"));
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<InboxResult | null>(null);
  const current = result?.status === status && result.revision === revision ? result : null;
  const data = current && "data" in current ? current.data : null;
  const error = current && "error" in current ? current.error : "";
  useEffect(() => {
    const abort = new AbortController();
    const query = new URLSearchParams({ status: status === "all" ? "" : status });
    void fetch(`${endpoint}/?${query}`, { signal: abort.signal, cache: "no-store" }).then(async response => {
      if (!response.ok) throw new DashboardRequestError("تعذر تحميل الطلبات. أعد المحاولة.", response.status);
      const next = await response.json() as InboxData;
      if (!abort.signal.aborted) setResult({ status, revision, data: next });
    }).catch((failure: unknown) => {
      if (abort.signal.aborted) return;
      const error = failure instanceof DashboardRequestError && unavailable(failure.status)
        ? "طلبات إثبات الحضور غير متاحة لحسابك."
        : "تعذر تحميل الطلبات. أعد المحاولة.";
      setResult({ status, revision, error });
    });
    return () => abort.abort();
  }, [status, revision]);

  return <>
    <PageHeader title="طلبات إثبات حضور الموظفين" context="الحضور الوظيفي"
      back={{ label: "العودة إلى الحضور الوظيفي", href: "/dashboard/attendance/staff" }}
      description="القبول يسجل مراجعة إدارية للحضور، دون إثبات بصمة أو موقع أو تغيير المستحقات." />
    <div className={styles.filters}>
      <label>حالة الطلب<select value={status} onChange={event => window.history.pushState(null, "", inboxPath + statusQuery(event.target.value))}>
        {Object.entries(stateLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}
      </select></label>
      <button type="button" className="button button-secondary" disabled={!current} onClick={() => setRevision(value => value + 1)}>
        {error ? "إعادة المحاولة" : "تحديث الطلبات"}
      </button>
    </div>
    {error ? <ErrorState message={error} /> : !data ? <LoadingState message="جارٍ تحميل طلبات إثبات الحضور…" />
      : !data.items.length ? <EmptyState
        title={status === "Pending" ? "لا توجد طلبات بانتظار المراجعة" : "لا توجد طلبات بهذه الحالة"}
        action={status !== "all" ? <Link href={inboxPath + "?status=all"} className="button button-secondary">عرض كل الطلبات</Link> : undefined} />
      : <section className="list-card">
        {data.total > data.items.length && <p>تظهر أحدث {data.items.length} من {data.total} طلبًا.</p>}
        <AdminDataTable label="طلبات إثبات الحضور" rows={data.items} rowKey={row => row.id} columns={[
          { key: "name", header: "الموظف", primary: true, render: row => <><b>{row.name}</b><small>{row.branch}</small></> },
          { key: "date", header: "يوم العمل", render: row => formatDateAr(row.workDate) },
          { key: "operation", header: "نوع الطلب", render: row => operation(row.operation) },
          { key: "status", header: "الحالة", render: row => <StatusBadge status={row.status} /> },
          { key: "action", header: "الإجراء", render: row => <Link href={`${inboxPath}/${row.id}${statusQuery(status)}`}>عرض الطلب</Link> },
        ]} />
      </section>}
  </>;
}

export function StaffAttendanceExceptionDetail({ id }: { id: string }) {
  const back = inboxPath + statusQuery(inboxStatus(useSearchParams().get("status")));
  const [data, setData] = useState<Detail | null>(null);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");
  const [pending, setPending] = useState<Decision | null>(null);
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const busyRef = useRef(false);

  const load = useCallback(async (signal?: AbortSignal) => {
    const response = await fetch(`${endpoint}/${id}`, { signal, cache: "no-store" });
    if (!response.ok) throw new DashboardRequestError("تعذر تحميل تفاصيل الطلب.", response.status);
    const detail = await response.json() as Detail;
    if (!sameRequest(detail.request?.id, id)) throw new DashboardRequestError("تعذر التحقق من تفاصيل الطلب.", 0);
    return detail;
  }, [id]);

  useEffect(() => {
    const abort = new AbortController();
    void load(abort.signal).then(detail => {
      if (!abort.signal.aborted) setData(detail);
    }).catch((failure: unknown) => {
      if (!abort.signal.aborted) setError(failure instanceof DashboardRequestError && unavailable(failure.status)
        ? "هذا الطلب غير متاح لحسابك."
        : "تعذر تحميل تفاصيل الطلب. أعد المحاولة.");
    });
    return () => abort.abort();
  }, [load]);

  function readFailure(failure: unknown) {
    if (failure instanceof DashboardRequestError && unavailable(failure.status)) {
      setData(null);
      setPending(null);
      setNeedsRefresh(false);
      setError("هذا الطلب غير متاح لحسابك.");
      return;
    }
    setError("تعذر التحقق من حالة الطلب. أعد المحاولة.");
  }

  async function refresh() {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const current = await load();
      setData(current);
      if (pending && (needsRefresh || current.request.status !== "Pending" || current.request.version !== pending.version || !current.canReview)) {
        setPending(null);
        setNeedsRefresh(false);
        setMessage("تم تحديث حالة الطلب.");
      } else if (pending) {
        setMessage("لا يزال الطلب بانتظار المراجعة. يمكنك إعادة إرسال القرار نفسه.");
      }
    } catch (failure) {
      readFailure(failure);
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  async function decide(approve: boolean) {
    if (!data?.canReview || busyRef.current || needsRefresh) return;
    const decisionNote = note.trim();
    if (!pending && !approve && decisionNote.length < 5) {
      setError("اكتب سبب الرفض من خمسة أحرف على الأقل.");
      return;
    }
    const command = pending ?? { approve, note: decisionNote, version: data.request.version, key: crypto.randomUUID() };
    const expected = command.approve ? "Approved" : "Rejected";
    busyRef.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    setPending(command);
    try {
      const response = await csrfRequest(`${endpoint}/${id}/review`, "POST",
        { approve: command.approve, note: command.note || null, version: command.version },
        { "Idempotency-Key": command.key }, { announce: false });
      const saved = await response.json() as { id?: string; status?: string };
      if (!sameRequest(saved.id, id) || saved.status !== expected) throw new Error();
      const current = await load();
      setData(current);
      if (current.request.status !== expected) throw new Error();
      setPending(null);
      setMessage(command.approve ? "تم قبول الطلب وتسجيل الحضور إداريًا." : "تم رفض الطلب؛ لم يتغير الحضور.");
    } catch (failure) {
      if (failure instanceof DashboardRequestError && unavailable(failure.status)) {
        readFailure(failure);
      } else if (failure instanceof DashboardRequestError && failure.status === 409) {
        setNeedsRefresh(true);
        setError(failure.message);
      } else if (failure instanceof DashboardRequestError && [400, 422].includes(failure.status)) {
        setPending(null);
        setError(failure.message);
      } else {
        setError("لم يتأكد القرار. تحقّق من حالة الطلب أو أعد إرسال القرار نفسه.");
      }
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  const request = data?.request;
  return <>
    <PageHeader title="مراجعة إثبات الحضور" context="الحضور الوظيفي"
      back={{ label: "العودة إلى الطلبات", href: back }} />
    {error && <ErrorState message={error} />}
    {message && <p role="status">{message}</p>}
    {!data || !request ? error
      ? <button className="button button-secondary" type="button" disabled={busy} onClick={() => void refresh()}>إعادة المحاولة</button>
      : <LoadingState message="جارٍ تحميل تفاصيل الطلب…" />
      : <section className={`detail-card ${styles.detail}`}>
        <header className={styles.claimHeader}>
          <div><h2>{data.name}</h2><p>{data.branch} · {formatDateAr(request.workDate)} · {operation(request.operation)}</p></div>
          <StatusBadge status={request.status} />
        </header>
        <dl className={styles.facts}>
          <div><dt>الوقت الذي ذكره الموظف</dt><dd>{academyTime(request.claimedAtUtc, data.timeZone)}</dd></div>
          <div><dt>وقت استلام الطلب</dt><dd>{academyTime(request.receivedAtUtc, data.timeZone)}</dd></div>
          <div><dt>سبب الطلب</dt><dd>{request.reason}</dd></div>
        </dl>
        <p className={styles.secondary}>أوقات الطلب بتوقيت الأكاديمية.</p>
        {request.operation === "CheckOut" && <p>يرتبط الانصراف بيوم بدء الحضور حتى إذا وقع في اليوم التالي.</p>}
        {request.targetPeriod && <aside className="staff-request-target" aria-label="الفترة المحددة">
          <h3>الفترة المحددة عند إرسال الطلب</h3>
          <p>{formatDateAr(request.targetPeriod.startDate)} · بداية {request.targetPeriod.startTime.slice(0, 5)} · {periodZone(request.targetPeriod.timeZoneId, data.timeZone, request.claimedAtUtc)}</p>
          {data.currentPeriod && <p>الفترة الحالية: {data.currentPeriod.startTime.slice(0, 5)} / {formatStaffCheckout(data.currentPeriod.endTime, data.currentPeriod.endDate, data.currentPeriod.startDate)}</p>}
          <p>القرار يخص هذه الفترة فقط؛ تغيير المصدر يتطلب طلبًا جديدًا.</p>
        </aside>}
        {"currentAttendance" in data && <section>
          <h3>الحضور المسجل لهذا اليوم</h3>
          {data.currentAttendance
            ? <p>{data.currentAttendance.status === "Present" ? "حاضر" : data.currentAttendance.status === "Absent" ? "غائب" : "لم يُسجل"} · حضور {data.currentAttendance.checkIn?.slice(0, 5) ?? "—"} · انصراف {formatStaffCheckout(data.currentAttendance.checkOut, data.currentAttendance.checkOutDate, request.workDate)}</p>
            : <p>لا يوجد سجل حضور لهذا اليوم.</p>}
        </section>}
        {request.reviewNote && <p>ملاحظة القرار: {request.reviewNote}</p>}
        {(data.canReview || pending) ? <section className="form-shell staff-exception-review-form" aria-label="قرار المراجعة" aria-busy={busy || undefined}>
          <p>القبول يسجل الحضور بمراجعة إدارية دون إثبات بصمة أو موقع. لا يمكن اعتماد طلبك، أو استبدال سجل قائم، أو تعديل شهر معتمد.</p>
          <label>ملاحظة القرار — مطلوبة عند الرفض<textarea maxLength={500} value={note} disabled={busy || pending !== null} onChange={event => setNote(event.target.value)} /></label>
          {pending && <p>{needsRefresh ? "تحقّق من حالة الطلب قبل اتخاذ قرار جديد." : `القرار المعلّق: ${pending.approve ? "قبول الطلب" : "رفض الطلب"}.`}</p>}
          <div className="form-actions">
            {pending ? <>
              <button type="button" className="button button-secondary" disabled={busy} onClick={() => void refresh()}>تحقق من حالة الطلب</button>
              {!needsRefresh && data.canReview && <button type="button" className="primary-button" disabled={busy} onClick={() => void decide(pending.approve)}>إعادة إرسال القرار نفسه</button>}
            </> : <>
              <button type="button" className="primary-button" disabled={busy} onClick={() => void decide(true)}>قبول وتسجيل إداري</button>
              <button type="button" className="button button-secondary" disabled={busy} onClick={() => void decide(false)}>رفض الطلب</button>
            </>}
          </div>
        </section> : request.status === "Pending" && <p>لا يمكنك مراجعة هذا الطلب بنفسك أو ليس لديك إذن التعديل؛ يعتمده مسؤول آخر مخول.</p>}
      </section>}
  </>;
}
