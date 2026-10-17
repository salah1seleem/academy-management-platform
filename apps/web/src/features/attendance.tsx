"use client";
import { ContextHelp } from "../components/context-help";

import { useEffect, useMemo, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions, StatusBadge } from "../components/dashboard-shell";
import { AttendanceChoice, IdentityMark } from "../components/sports-ui";
import { PlayerCardLookup } from "./player-card";
import { PastAttendanceRequest } from "./attendance-corrections";
import { DashboardRequestError } from "./dashboard-api";
import { formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { AttendanceSnapshot, AttendanceSyncNotice, useAttendanceSync } from "./attendance-sync";

type Session = { id: string; trainingGroupId: string; sessionDate: string; startTime: string; endTime: string; status: string; group: string; sport: string; branch: string; playerCount: number; recorded: number; present?: number; absent?: number; coaches?: string[]; staff?: { id: string; name: string; attendanceStatus: string }[] };
const attendanceLabels: Record<string, string> = { Present: "حاضر", Late: "متأخر", Excused: "معتذر", Absent: "غائب", NotRecorded: "لم يُسجل" };
export function TrainingSessionsList({ staffOnly = false, initialGroupId = "", playerId = "" }: { staffOnly?: boolean; initialGroupId?: string; playerId?: string }) {
  const defaults = useDateFilterDefaults();
  const [sessions, setSessions] = useState<Session[]>([]); const [search, setSearch] = useState(""); const [from, setFrom] = useState(defaults.to); const [to, setTo] = useState(defaults.to); const [group, setGroup] = useState(initialGroupId); const [status, setStatus] = useState(""); const [error, setError] = useState(""); const [loading, setLoading] = useState(true);
  useEffect(() => { const abort = new AbortController(); const params = new URLSearchParams(); if (from) params.set("from", from); if (to) params.set("to", to); if (!from && !to) params.set("allDates", "true"); if (group) params.set("groupId", group); fetch(`/api/v1/attendance/sessions${params.size ? `?${params}` : ""}`, { signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); setSessions(await r.json()); }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل الحضور. راجع نطاق التاريخ."); }).finally(() => { if (!abort.signal.aborted) setLoading(false); }); return () => abort.abort(); }, [from, to, group]);
  const visible = useMemo(() => sessions.filter(s => s.status !== "Cancelled" && `${s.group} ${s.branch} ${s.sport} ${s.coaches?.join(" ")}`.includes(search)).flatMap(s => staffOnly ? (s.staff ?? []).map(coach => ({ ...s, rowId: `${s.id}-${coach.id}`, coach: coach.name, attendanceStatus: coach.attendanceStatus })) : [{ ...s, rowId: s.id, coach: s.coaches?.join("، ") ?? "—", attendanceStatus: "" }]).filter(s => !status || !staffOnly || s.attendanceStatus === status), [sessions, search, staffOnly, status]);
  return <><PageHeader title={staffOnly ? "حضور المدربين" : "حضور اللاعبين"} context="الحضور" description="حالات الحضور من السجلات المحفوظة. إنشاء وتعديل الحصص من ملف المجموعة." />{!staffOnly && <ContextHelp lesson="player-attendance" />}
    <section className="list-card"><div className="session-filters"><ListSearch value={search} onChange={setSearch} label="البحث بالاسم" placeholder="المجموعة أو المدرب أو الفرع" /><label>من تاريخ<input type="date" value={from} onChange={e => { setFrom(e.target.value); setLoading(true); setError(""); }} /></label><label>إلى تاريخ<input type="date" value={to} onChange={e => { setTo(e.target.value); setLoading(true); setError(""); }} /></label><select aria-label="فلتر المجموعة" value={group} onChange={e => { setGroup(e.target.value); setLoading(true); setError(""); }}><option value="">كل المجموعات</option>{Array.from(new Map(sessions.map(s => [s.trainingGroupId, s.group]))).map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select>{staffOnly && <select aria-label="حالة الحضور" value={status} onChange={e => setStatus(e.target.value)}><option value="">كل الحالات</option>{Object.entries(attendanceLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select>}<button onClick={() => { setSearch(""); setFrom(""); setTo(""); setGroup(""); setStatus(""); if (from || to || group) setLoading(true); setError(""); }}>مسح</button></div>
      {error && <ErrorState message={error} />}{loading ? <LoadingState /> : !visible.length ? <EmptyState message="لا توجد حصص مطابقة." /> : <AdminDataTable label={staffOnly ? "حضور المدربين" : "حضور اللاعبين"} rows={visible} rowKey={s => s.rowId} columns={[
        { key: "group", header: "المجموعة", primary: true, render: s => s.group },
        { key: "coach", header: "المدرب", render: s => s.coach },
        { key: "branch", header: "الفرع", render: s => s.branch },
        { key: "sport", header: "الرياضة", render: s => s.sport },
        { key: "date", header: "اليوم والتاريخ والتوقيت", render: s => <>{formatSessionDateAr(s.sessionDate)}<small>{formatTimeAr(s.startTime)} — {formatTimeAr(s.endTime)}</small></> },
        { key: "state", header: "الحالة", render: s => staffOnly ? <StatusBadge status={s.attendanceStatus} /> : <small>{s.present ?? 0} حاضر · {s.absent ?? 0} غائب · {Math.max(0, s.playerCount - s.recorded)} لم يُسجل</small> },
        { key: "actions", header: "الإجراء", render: s => <RowActions actions={[{ label: staffOnly ? "تسجيل حضور المدرب" : "تسجيل الحضور", href: staffOnly ? `/dashboard/attendance/sessions/${s.id}/staff` : `/dashboard/attendance/sessions/${s.id}/players${playerId ? `?playerId=${playerId}` : ""}` }]} /> },
      ]} />}
    </section></>;
}

type Roster = { canManageAttendance: boolean; canRequestCorrection?: boolean; isPast?: boolean; draftLease?: string; draftScope?: string; draftExpiresAtUtc?: string; lastSynchronizedAtUtc?: string; session: { id: string; sessionDate: string; startTime: string; endTime: string; status: string; group: string; sport: string; branch: string }; players: PlayerRow[] };
type PlayerRow = { sportEnrollmentId: string; playerId: string; playerCode: string; arabicName: string; photoReference?: string; attendanceStatus: string; version?: number; subscriptionPlan?: string; subscriptionType?: string; remainingSessions?: number; sessionConsumed: boolean; warning?: string };
export function PlayerAttendance({ sessionId, playerId = "" }: { sessionId: string; playerId?: string }) {
  return <PlayerAttendancePage key={sessionId} sessionId={sessionId} playerId={playerId} />;
}
function PlayerAttendancePage({ sessionId, playerId }: { sessionId: string; playerId: string }) {
  const [data, setData] = useState<Roster | null>(null); const [states, setStates] = useState<Record<string, string>>({}); const [search, setSearch] = useState(""); const [loadError, setLoadError] = useState("");
  const path = `/api/v1/attendance/sessions/${sessionId}/players`;
  const snapshot = (roster: Roster): AttendanceSnapshot => ({ ...roster, canManage: roster.canManageAttendance, rows: roster.players.map(row => ({ id: row.sportEnrollmentId, name: row.arabicName, status: row.attendanceStatus, version: row.version ?? 0 })) });
  const sync = useAttendanceSync(path, "sportEnrollmentId", async () => { const response = await fetch(path); if (!response.ok) throw new DashboardRequestError("تعذر تحديث الكشف؛ أعد فتحه قبل تصحيح آخر.", response.status); return snapshot(await response.json() as Roster); });
  const { error: syncError, message, busy: saving } = sync;
  const error = syncError || loadError;
  const load = async () => { const response = await fetch(`/api/v1/attendance/sessions/${sessionId}/players`); if (!response.ok) throw new Error("تعذر تحديث الكشف؛ أعد فتحه قبل تصحيح آخر."); const roster = await response.json() as Roster; setData(roster); setStates(Object.fromEntries(roster.players.map(row => [row.sportEnrollmentId, row.attendanceStatus]))); };
  useEffect(() => { let active = true; fetch(`/api/v1/attendance/sessions/${sessionId}/players`).then(async response => { if (!response.ok) throw new Error(); const roster = await response.json() as Roster; if (!active) return; setData(roster); setStates(Object.fromEntries(roster.players.map(row => [row.sportEnrollmentId, row.attendanceStatus]))); }).catch(() => { if (active) setLoadError("تعذر تحميل كشف اللاعبين."); }); return () => { active = false; }; }, [sessionId]);
  const visible = data?.players.filter(row => (!playerId || row.playerId === playerId) && `${row.playerCode} ${row.arabicName}`.includes(search)) ?? [];
  async function save() { if (!data?.canManageAttendance) return; await sync.save(snapshot(data), states, load); }
  if (sync.blocked) return <><PageHeader title="حضور اللاعبين" context="الحضور" />{error && <ErrorState message={error} />}<AttendanceSyncNotice sync={sync} discard={() => void sync.discard(load)} /></>;
  if (!data && !error) return <LoadingState />;
  return <><PageHeader title="حضور اللاعبين" context="الحضور / جلسات التدريب" />{error && <ErrorState message={error} />}{data && <><section className="session-summary"><strong>{data.session.sessionDate} · {data.session.startTime.slice(0, 5)}–{data.session.endTime.slice(0, 5)}</strong><span>{data.session.group} · {data.session.sport} · {data.session.branch}</span></section><section className="list-card">{data.lastSynchronizedAtUtc && <p className="muted">آخر مزامنة مؤكدة: {new Date(data.lastSynchronizedAtUtc).toLocaleTimeString("ar-EG-u-nu-latn")}</p>}<AttendanceSyncNotice sync={sync} discard={() => void sync.discard(load)} />{data.canManageAttendance && <PlayerCardLookup sessionId={sessionId} onResolved={setSearch} />}<div className="attendance-actions"><ListSearch value={search} onChange={setSearch} label="بحث بالاسم أو كود اللاعب" placeholder="اسم اللاعب أو الكود" />{data.canManageAttendance && <button className="secondary-button" type="button" disabled={sync.locked} onClick={() => setStates(Object.fromEntries(data.players.map(row => [row.sportEnrollmentId, "Present"]))) }>تحديد الحاضرين</button>}</div>{!data.canManageAttendance && <p role="status">الحضور للعرض فقط؛ يتولى الإداري المصرح له التسجيل.</p>}<div className="attendance-roster">{visible.map(row => <article key={row.sportEnrollmentId}><div className="player-identity"><div><IdentityMark name={row.arabicName} /><small>{row.playerCode} · {row.subscriptionPlan ?? "لا يوجد اشتراك مؤهل"}{row.remainingSessions != null ? ` · الرصيد: ${row.remainingSessions}` : ""}</small>{row.warning && <em>{row.warning}</em>}</div></div>{data.canManageAttendance ? <fieldset disabled={sync.locked} className="status-picker" aria-label={`حضور ${row.arabicName}`}>{["Present", "Late", "Absent", "Excused", "NotRecorded"].map(value => <AttendanceChoice value={value} selected={states[row.sportEnrollmentId] === value} onClick={() => setStates({ ...states, [row.sportEnrollmentId]: value })} key={value} />)}</fieldset> : <StatusBadge status={row.attendanceStatus} />}</article>)}</div>{message && <p className="success-message" role="status">{message}</p>}{data.canManageAttendance && <button className="primary-button attendance-save" disabled={saving || sync.conflicts.length > 0 || data.session.status === "Cancelled"} onClick={() => void save()}>{saving ? "جارٍ الحفظ…" : sync.draft ? "مراجعة وإعادة المزامنة" : "حفظ الحضور"}</button>}{data.canRequestCorrection && <PastAttendanceRequest sessionId={sessionId} kind="Players" rows={data.players.map(row=>({id:row.sportEnrollmentId,name:row.arabicName,status:row.attendanceStatus,version:row.version}))}/>}</section></>}</>;
}

type StaffRow = { academyMembershipId: string; name: string; role: string; attendanceStatus: string; version?: number };
export function StaffAttendance({ sessionId }: { sessionId: string }) {
  return <StaffAttendancePage key={sessionId} sessionId={sessionId} />;
}
function StaffAttendancePage({ sessionId }: { sessionId: string }) {
  const [canRequestCorrection,setCanRequestCorrection]=useState(false);
  const [rows, setRows] = useState<StaffRow[]>([]), [states, setStates] = useState<Record<string, string>>({}), [canManage, setCanManage] = useState(false), [synced, setSynced] = useState(""), [loadError, setLoadError] = useState("");
  const [draftContext, setDraftContext] = useState<Partial<AttendanceSnapshot>>({});
  const path = `/api/v1/attendance/sessions/${sessionId}/staff`;
  async function read() {
    const [r, p, c] = await Promise.all([fetch(path), fetch("/api/v1/my-permissions"), fetch(`/api/v1/attendance/sessions/${sessionId}/correction-context`)]);
    for (const response of [r, p, c]) if (!response.ok) throw new DashboardRequestError("تعذر تحميل حضور الجهاز الفني والصلاحيات.", response.status);
    const result = await r.json() as StaffRow[], permissions = await p.json() as string[];
    const context = await c.json() as { canManageAttendance: boolean; canRequestCorrection: boolean; draftLease?: string; draftScope?: string; draftExpiresAtUtc?: string };
    return { result, context, canManage: permissions.includes("attendance.manage") && context.canManageAttendance === true };
  }
  const toSnapshot = (value: Awaited<ReturnType<typeof read>>): AttendanceSnapshot => ({ ...value.context, canManage: value.canManage, rows: value.result.map(row => ({ id: row.academyMembershipId, name: row.name, status: row.attendanceStatus, version: row.version ?? 0 })) });
  const sync = useAttendanceSync(path, "academyMembershipId", async () => toSnapshot(await read()));
  const { busy, message } = sync, error = sync.error || loadError;
  async function load() {
    const value = await read(); setRows(value.result); setStates(Object.fromEntries(value.result.map(row => [row.academyMembershipId, row.attendanceStatus]))); setCanManage(value.canManage); setCanRequestCorrection(value.context.canRequestCorrection); setDraftContext(value.context); setSynced(new Date().toISOString());
  }
  useEffect(() => { const abort = new AbortController(); void (async () => {
    const [r, p, c] = await Promise.all([fetch(`/api/v1/attendance/sessions/${sessionId}/staff`, { signal: abort.signal }), fetch("/api/v1/my-permissions", { signal: abort.signal }), fetch(`/api/v1/attendance/sessions/${sessionId}/correction-context`, {signal:abort.signal})]);
    if (!r.ok || !p.ok || !c.ok) throw new Error("تعذر تحميل الحضور والصلاحيات.");
    const result = await r.json() as StaffRow[], permissions = await p.json() as string[];
    const context=await c.json() as {canManageAttendance:boolean;canRequestCorrection:boolean;draftLease?:string;draftScope?:string;draftExpiresAtUtc?:string};setCanRequestCorrection(context.canRequestCorrection===true);setDraftContext(context);
    setRows(result); setStates(Object.fromEntries(result.map(row => [row.academyMembershipId, row.attendanceStatus]))); setCanManage(permissions.includes("attendance.manage")&&context.canManageAttendance===true); setSynced(new Date().toISOString());
  })().catch(e => { if (e.name !== "AbortError") setLoadError("تعذر تحميل حضور الجهاز الفني."); }); return () => abort.abort(); }, [sessionId]);
  async function save() {
    if (!canManage) return;
    await sync.save({ ...draftContext, canManage, rows: rows.map(row => ({ id: row.academyMembershipId, name: row.name, status: row.attendanceStatus, version: row.version ?? 0 })) }, states, load);
  }
  if (sync.blocked) return <><PageHeader title="حضور المدربين" context="الحضور" />{error && <ErrorState message={error} />}<AttendanceSyncNotice sync={sync} discard={() => void sync.discard(load)} /></>;
  return <><PageHeader title="حضور المدربين" context="الحضور / حضور المدربين" />{error && <ErrorState message={error} />}<section className="list-card">
    <AttendanceSyncNotice sync={sync} discard={() => void sync.discard(load)} />
    {synced && <p className="muted">آخر مزامنة مؤكدة: {new Date(synced).toLocaleTimeString("ar-EG-u-nu-latn")}</p>}
    {!canManage && <p role="status">الحضور للعرض فقط؛ يتولى الإداري المصرح له التسجيل.</p>}
    {!rows.length ? <EmptyState message="لا يوجد جهاز فني مكلف بهذه المجموعة." /> : <div className="attendance-roster">{rows.map(row => <article key={row.academyMembershipId}><div><strong>{row.name}</strong><small>{row.role === "Coach" ? "مدرب" : "جهاز فني"}</small></div>
      {canManage ? <fieldset disabled={sync.locked} className="status-picker" aria-label={`حضور ${row.name}`}>{Object.keys(attendanceLabels).map(value => <AttendanceChoice value={value} selected={states[row.academyMembershipId] === value} onClick={() => { if (!sync.locked) setStates({ ...states, [row.academyMembershipId]: value }); }} key={value} />)}</fieldset> : <StatusBadge status={row.attendanceStatus} />}
    </article>)}</div>}
    {message && <p className="success-message" role="status">{message}</p>}{canManage && rows.length > 0 && <button className="primary-button attendance-save" disabled={busy || sync.conflicts.length > 0} onClick={() => void save()}>{busy ? "جارٍ الحفظ…" : sync.draft ? "مراجعة وإعادة المزامنة" : "حفظ حضور المدربين"}</button>}
    {canRequestCorrection&&<PastAttendanceRequest sessionId={sessionId} kind="Staff" rows={rows.map(row=>({id:row.academyMembershipId,name:row.name,status:row.attendanceStatus,version:row.version}))}/>}
  </section></>;
}

type HistoryRow = { id: string; playerCode: string; player: string; group: string; sport: string; branch: string; sessionDate: string; startTime: string; status: string; sessionConsumed: boolean };
export function AttendanceHistory({ playerId = "" }: { playerId?: string }) { const [rows, setRows] = useState<HistoryRow[]>([]); const [search, setSearch] = useState(""); const [status, setStatus] = useState(""); const [error, setError] = useState(""); useEffect(() => { const query = new URLSearchParams(); if (playerId) query.set("playerId", playerId); if (search) query.set("search", search); if (status) query.set("status", status); const timer = setTimeout(() => fetch(`/api/v1/attendance/history?${query}`).then(async response => { if (!response.ok) throw new Error(); setRows(await response.json() as HistoryRow[]); }).catch(() => setError("تعذر تحميل سجل الحضور.")), 200); return () => clearTimeout(timer); }, [playerId, search, status]); return <><PageHeader title="سجل الحضور" context="الحضور / سجل الحضور" description={playerId ? "سجل الحضور الخاص باللاعب المحدد فقط." : "راجع الحالات المسجلة فعليًا لكل لاعب وجلسة تدريب."} />{error && <ErrorState message={error} />}<section className="list-card"><div className="player-filters"><ListSearch value={search} onChange={setSearch} label="بحث سجل الحضور" placeholder="الاسم أو كود اللاعب" /><select aria-label="حالة الحضور" value={status} onChange={event => setStatus(event.target.value)}><option value="">كل الحالات</option><option value="Present">حاضر</option><option value="Late">متأخر</option><option value="Absent">غائب</option><option value="Excused">معتذر</option><option value="NotRecorded">لم يُسجل</option></select></div>{!rows.length ? <EmptyState title="لا توجد سجلات حضور" message="لا توجد سجلات مطابقة للبحث والحالة المختارة." /> : <AdminDataTable label="سجل الحضور" rows={rows} rowKey={row => row.id} columns={[
    { key: "player", header: "اللاعب", primary: true, width: "minmax(12rem, 1.5fr)", render: row => <><strong>{row.player}</strong><small dir="ltr">{row.playerCode}</small></> },
    { key: "session", header: "التاريخ والوقت", width: "minmax(8rem, 1fr)", render: row => <>{row.sessionDate}<small>{row.startTime.slice(0, 5)}</small></> },
    { key: "group", header: "المجموعة", width: "minmax(11rem, 1.35fr)", render: row => row.group },
    { key: "location", header: "الرياضة والفرع", width: "minmax(10rem, 1.2fr)", render: row => <>{row.sport}<small>{row.branch}</small></> },
    { key: "status", header: "الحالة", width: "minmax(7rem, .7fr)", render: row => <><StatusBadge status={row.status} />{row.sessionConsumed && <small>تم خصم حصة</small>}</> },
  ]} />}</section></>; }

export function GuardianAttendanceList({ playerId }: { playerId: string }) { const [rows, setRows] = useState<{ id: string; sessionDate: string; startTime: string; group: string; sport: string; status: string }[]>([]); useEffect(() => { fetch(`/api/v1/guardian/children/${playerId}/attendance`).then(response => response.ok ? response.json() : []).then(setRows); }, [playerId]); return <section className="guardian-attendance"><h3>آخر الحضور</h3>{rows.length === 0 ? <small>لا يوجد حضور مسجل بعد.</small> : rows.slice(0, 5).map(row => <p key={row.id}><span>{row.sessionDate} · {row.sport} · {row.group}</span><b>{attendanceLabels[row.status]}</b></p>)}</section>; }
