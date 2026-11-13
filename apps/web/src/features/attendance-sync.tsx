"use client";

import { useEffect, useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

export type SyncRow = { id: string; name: string; status: string; version: number };
export type AttendanceSnapshot = { rows: SyncRow[]; canManage: boolean; draftLease?: string; draftScope?: string; draftExpiresAtUtc?: string };
type Change = { id: string; status: string; expectedVersion: number };
type Draft = { snapshot: AttendanceSnapshot; changes: Change[]; expires: number };
type Conflict = { id: string; name: string; local: string; server?: SyncRow };
const values: Record<string, number> = { NotRecorded: 1, Present: 2, Absent: 3, Late: 4, Excused: 5 };
const labels: Record<string, string> = { NotRecorded: "لم يُسجل", Present: "حاضر", Absent: "غائب", Late: "متأخر", Excused: "معتذر" };

// Volatile page memory only: no names, attendance or leases in browser storage.
// Resuming always reads the server; reconnecting never sends writes by itself.
export function useAttendanceSync<T>(path: string, idField: string, read: () => Promise<T>, snapshotOf: (value: T) => AttendanceSnapshot, accept: (value: T) => void) {
  const [draft, setDraft] = useState<Draft | null>(null);
  const [reviewed, setReviewed] = useState(false);
  const [conflicts, setConflicts] = useState<Conflict[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [blocked, setBlocked] = useState(false);
  const busyRef = useRef(false), generation = useRef(0);
  useEffect(() => {
    const cycle = ++generation.current;
    // Also scrub a page retained in the browser back/forward cache on logout/navigation.
    const leave = () => { generation.current = cycle + 1; setDraft(null); setConflicts([]); setBlocked(true); };
    window.addEventListener("pagehide", leave);
    return () => { generation.current = cycle + 1; window.removeEventListener("pagehide", leave); };
  }, [path]);
  useEffect(() => {
    if (!draft) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [draft]);

  async function save(snapshot: AttendanceSnapshot, states: Record<string, string>) {
    if (busyRef.current || blocked || conflicts.length) return;
    const captured = generation.current;
    const current = draft ?? {
      snapshot,
      changes: snapshot.rows.filter(row => states[row.id] && states[row.id] !== row.status).map(row => ({ id: row.id, status: states[row.id], expectedVersion: row.version })),
      expires: Math.min(Date.now() + 15 * 60_000, snapshot.draftExpiresAtUtc ? Date.parse(snapshot.draftExpiresAtUtc) : Infinity),
    };
    if (!draft && !current.changes.length) return;
    busyRef.current = true; setBusy(true); setMessage(""); setError("");
    try {
      if (current.expires <= Date.now()) throw new DashboardRequestError("انتهت صلاحية المسودة؛ افتح كشفًا جديدًا وراجع اليوم.", 410);
      let remaining = current.changes;
      if (draft) {
        const result = await read();
        if (captured !== generation.current) return;
        const latest = snapshotOf(result);
        if (latest.draftScope !== current.snapshot.draftScope) throw new DashboardRequestError("تغير حساب الدخول أو الأكاديمية؛ حُذفت المسودة من هذه الصفحة.", 401);
        if (!latest.canManage) throw new DashboardRequestError("لم يعد تعديل هذه الحصة متاحًا؛ راجع الصلاحية أو طلب تصحيح يوم سابق.", 403);
        const remote = new Map(latest.rows.map(row => [row.id, row]));
        remaining = current.changes.filter(change => remote.get(change.id)?.status !== change.status);
        const mismatches = remaining.flatMap(change => {
          const row = remote.get(change.id);
          return !row || row.version !== change.expectedVersion ? [{ id: change.id, name: row?.name ?? "عضو لم يعد في الكشف", local: change.status, server: row }] : [];
        });
        if (mismatches.length) {
          setReviewed(false); setConflicts(mismatches); setError("تغير كشف الحضور. راجع الحالات المتعارضة."); return;
        }
        if (!remaining.length) {
          accept(result);
          setDraft(null); setReviewed(false);
          setMessage(current.changes.length ? "الحضور محفوظ بالفعل دون إعادة الإرسال." : "تم تحديث الكشف بحالات الخادم.");
          return;
        }
        if (!reviewed) {
          setReviewed(true);
          return;
        }
      }
      await csrfRequest(path, "PUT", { draftLease: current.snapshot.draftLease, items: remaining.map(change => ({ [idField]: change.id, status: values[change.status], expectedVersion: change.expectedVersion })) }, {}, { announce: false });
      if (captured !== generation.current) return;
      const result = await read();
      if (captured !== generation.current) return;
      const latest = snapshotOf(result);
      if (current.snapshot.draftScope && latest.draftScope !== current.snapshot.draftScope)
        throw new DashboardRequestError("تغير حساب الدخول أو الأكاديمية؛ حُذفت المسودة من هذه الصفحة.", 401);
      if (!latest.canManage) throw new DashboardRequestError("لم يعد تعديل هذه الحصة متاحًا.", 403);
      const remote = new Map(latest.rows.map(row => [row.id, row.status]));
      if (!current.changes.every(change => remote.get(change.id) === change.status))
        throw new DashboardRequestError("لم تظهر كل التغييرات في الكشف المحدث.", 409);
      accept(result);
      setDraft(null); setReviewed(false); setMessage(idField === "sportEnrollmentId" ? "تم حفظ الحضور بنجاح." : "تم حفظ حضور الجهاز الفني.");
    } catch (failure) {
      if (captured !== generation.current) return;
      const unbound = !current.snapshot.draftLease || !current.snapshot.draftScope;
      const denied = unbound || failure instanceof DashboardRequestError && [401, 403, 404, 410].includes(failure.status);
      setBlocked(denied);
      setDraft(denied ? null : current);
      setReviewed(false);
      setError(unbound ? "تعذر تأكيد الحفظ ولا توجد جلسة مسودة آمنة؛ سجّل الدخول مجددًا وافتح كشفًا محدثًا." : failure instanceof TypeError ? "تعذر الاتصال. تحقّق من الحفظ عند عودة الاتصال." : failure instanceof DashboardRequestError ? failure.message : "تعذر تأكيد الحفظ. تحقّق من الكشف.");
    } finally { if (captured === generation.current) { busyRef.current = false; setBusy(false); } }
  }
  function resolve(id: string, keepLocal: boolean) {
    const conflict = conflicts.find(row => row.id === id);
    if (!conflict || keepLocal && !conflict.server) return;
    setDraft(current => current && ({ ...current, changes: current.changes.flatMap(change => change.id !== id ? [change] : keepLocal ? [{ ...change, expectedVersion: conflict.server!.version }] : []) }));
    setConflicts(current => current.filter(row => row.id !== id));
  }
  async function discard() {
    if (busyRef.current) return;
    const captured = generation.current;
    busyRef.current = true; setBusy(true);
    try {
      const result = await read();
      if (captured !== generation.current) return;
      accept(result);
      setDraft(null); setReviewed(false); setConflicts([]); setBlocked(false); setError(""); setMessage("");
    } catch {
      if (captured !== generation.current) return;
      setError("تعذر تحديث الكشف. حاول مرة أخرى.");
    } finally {
      if (captured === generation.current) { busyRef.current = false; setBusy(false); }
    }
  }
  const actionLabel = busy ? draft && !reviewed ? "جارٍ التحقق…" : "جارٍ الحفظ…" : draft ? reviewed ? "إعادة إرسال التغييرات" : "تحقق من الحفظ" : null;
  return { save, discard, resolve, busy, message, error, blocked, draft, reviewed, actionLabel, conflicts, locked: busy || !!draft || blocked };
}

export function AttendanceSyncNotice({ sync }: { sync: Pick<ReturnType<typeof useAttendanceSync>, "draft" | "busy" | "discard" | "blocked" | "conflicts" | "resolve"> }) {
  return <>
    {sync.draft && <aside className="list-card"><p>تبقى التغييرات في هذه الصفحة لمدة 15 دقيقة كحد أقصى.</p><button type="button" className="secondary-button" disabled={sync.busy} onClick={() => void sync.discard()}>تجاهل التغييرات وتحديث الكشف</button></aside>}
    {sync.blocked && <p role="alert">لا يمكن إرسال هذه المسودة. أعد فتح كشف الحضور بعد تسجيل الدخول والتحقق من الصلاحية.</p>}
    {sync.conflicts.map(row => <section className="list-card" aria-label={`تعارض حضور ${row.name}`} key={row.id}><strong>{row.name}</strong><p>اختيارك: {labels[row.local]} · الخادم: {row.server ? labels[row.server.status] : "لم يعد ضمن الحصة"}</p><div className="row-actions"><button className="secondary-button" type="button" disabled={sync.busy} onClick={() => sync.resolve(row.id, false)}>اعتماد حالة الخادم</button>{row.server && <button className="primary-button" type="button" disabled={sync.busy} onClick={() => sync.resolve(row.id, true)}>الاحتفاظ باختياري بعد المراجعة</button>}</div></section>)}
  </>;
}
