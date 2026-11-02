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
export function useAttendanceSync(path: string, idField: string, read: () => Promise<AttendanceSnapshot>) {
  const [draft, setDraft] = useState<Draft | null>(null);
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

  async function save(snapshot: AttendanceSnapshot, states: Record<string, string>, accept: () => Promise<void>) {
    if (busyRef.current || blocked || conflicts.length) return;
    const captured = generation.current;
    const current = draft ?? {
      snapshot,
      changes: snapshot.rows.filter(row => states[row.id] !== row.status).map(row => ({ id: row.id, status: states[row.id], expectedVersion: row.version })),
      expires: Math.min(Date.now() + 15 * 60_000, snapshot.draftExpiresAtUtc ? Date.parse(snapshot.draftExpiresAtUtc) : Infinity),
    };
    if (!current.changes.length) { if (draft) await discard(accept); setMessage("لا توجد تغييرات جديدة للحفظ."); return; }
    busyRef.current = true; setBusy(true); setMessage(""); setError("");
    try {
      if (current.expires <= Date.now()) throw new DashboardRequestError("انتهت صلاحية المسودة؛ افتح كشفًا جديدًا وراجع اليوم.", 410);
      // Refresh on every retry, preserving the original versions until review.
      if (draft) {
        const latest = await read();
        if (captured !== generation.current) return;
        if (latest.draftScope !== current.snapshot.draftScope) throw new DashboardRequestError("تغير حساب الدخول أو الأكاديمية؛ حُذفت المسودة من هذه الصفحة.", 401);
        if (!latest.canManage) throw new DashboardRequestError("لم يعد تعديل هذه الحصة متاحًا؛ راجع الصلاحية أو طلب تصحيح يوم سابق.", 403);
        const mismatches = current.changes.flatMap(change => {
          const row = latest.rows.find(row => row.id === change.id);
          return !row || row.status !== change.status && row.version !== change.expectedVersion ? [{ id: change.id, name: row?.name ?? "عضو لم يعد في الكشف", local: change.status, server: row }] : [];
        });
        if (mismatches.length) { setConflicts(mismatches); setError("تغير الكشف على جهاز آخر؛ راجع كل تعارض قبل إعادة الإرسال."); return; }
        if (current.changes.every(change => latest.rows.some(row => row.id === change.id && row.status === change.status))) {
          await accept(); if (captured !== generation.current) return;
          setDraft(null); setMessage("تأكدت المزامنة من الخادم؛ الحضور محفوظ بالفعل دون إعادة الإرسال."); return;
        }
      }
      await csrfRequest(path, "PUT", { draftLease: current.snapshot.draftLease, items: current.changes.map(change => ({ [idField]: change.id, status: values[change.status], expectedVersion: change.expectedVersion })) }, {}, { announce: false });
      await accept(); if (captured !== generation.current) return;
      setDraft(null); setMessage(idField === "sportEnrollmentId" ? "تم حفظ الحضور بنجاح." : "تم حفظ حضور الجهاز الفني.");
    } catch (failure) {
      if (captured !== generation.current) return;
      const unbound = !current.snapshot.draftLease || !current.snapshot.draftScope;
      const denied = unbound || failure instanceof DashboardRequestError && [401, 403, 404, 410].includes(failure.status);
      setBlocked(denied);
      setDraft(denied ? null : current);
      setError(unbound ? "تعذر تأكيد الحفظ ولا توجد جلسة مسودة آمنة؛ سجّل الدخول مجددًا وافتح كشفًا محدثًا." : failure instanceof TypeError ? "تعذر الاتصال؛ لم تتأكد المزامنة بعد." : failure instanceof Error ? failure.message : "تعذر الحفظ.");
    } finally { if (captured === generation.current) { busyRef.current = false; setBusy(false); } }
  }
  function resolve(id: string, keepLocal: boolean) {
    const conflict = conflicts.find(row => row.id === id);
    if (!conflict || keepLocal && !conflict.server) return;
    setDraft(current => current && ({ ...current, changes: current.changes.flatMap(change => change.id !== id ? [change] : keepLocal ? [{ ...change, expectedVersion: conflict.server!.version }] : []) }));
    setConflicts(current => current.filter(row => row.id !== id));
  }
  async function discard(accept: () => Promise<void>) {
    if (busyRef.current) return;
    setDraft(null); setConflicts([]); setBlocked(false); setError(""); setMessage("");
    try { await accept(); } catch { setBlocked(true); setError("تعذر فتح كشف محدث؛ أعد فتح الصفحة عند توفر الاتصال."); }
  }
  return { save, discard, resolve, busy, message, error, blocked, draft, conflicts, locked: busy || !!draft || blocked };
}

export function AttendanceSyncNotice({ sync, discard }: { sync: ReturnType<typeof useAttendanceSync>; discard: () => void }) {
  return <>
    {sync.draft && <aside className="list-card" role="status"><strong>بانتظار المزامنة</strong><p>المسودة في هذه الصفحة فقط لمدة 15 دقيقة كحد أقصى. أبقِ الصفحة مفتوحة، ثم راجع الاتصال وأعد المحاولة.</p><button type="button" className="secondary-button" disabled={sync.busy} onClick={discard}>تجاهل المسودة وفتح كشف محدث</button></aside>}
    {sync.blocked && <p role="alert">لا يمكن إرسال هذه المسودة. أعد فتح كشف الحضور بعد تسجيل الدخول والتحقق من الصلاحية.</p>}
    {sync.conflicts.map(row => <section className="list-card" aria-label={`تعارض حضور ${row.name}`} key={row.id}><strong>{row.name}</strong><p>اختيارك: {labels[row.local]} · الخادم: {row.server ? labels[row.server.status] : "لم يعد ضمن الحصة"}</p><div className="row-actions"><button className="secondary-button" type="button" onClick={() => sync.resolve(row.id, false)}>اعتماد حالة الخادم</button>{row.server && <button className="primary-button" type="button" onClick={() => sync.resolve(row.id, true)}>الاحتفاظ باختياري بعد المراجعة</button>}</div></section>)}
  </>;
}
