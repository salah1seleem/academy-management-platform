"use client";
import { ContextHelp } from "../components/context-help";

import { useCallback, useEffect, useState } from "react";
import { AdminDataTable, Button, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge } from "../components/dashboard-shell";
import { IdentityMark } from "../components/sports-ui";
import { PlayerCardLookup } from "./player-card";
import { PastAttendanceRequest } from "./attendance-corrections";
import { DashboardRequestError } from "./dashboard-api";
import { formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { AttendanceSnapshot, AttendanceSyncNotice, useAttendanceSync } from "./attendance-sync";
import { apiProblemMessage } from "../lib/api-problem";
import styles from "./attendance-index.module.css";

type Session = { id: string; trainingGroupId: string; sessionDate: string; startTime: string; endTime: string; status: string; group: string; sport: string; branch: string; playerCount: number; recorded: number; present?: number; absent?: number; excused?: number; coaches?: string[]; staff?: { id: string; name: string; attendanceStatus: string }[] };
type SessionSummary = Pick<Session, "id" | "sessionDate" | "startTime" | "endTime" | "status" | "group" | "sport" | "branch">;
function AttendanceSessionSummary({ session }: { session: SessionSummary }) {
  return <section className="session-summary">
    <strong>{formatSessionDateAr(session.sessionDate)} · <bdi>{formatTimeAr(session.startTime)} – {formatTimeAr(session.endTime)}</bdi></strong>
    <span>{session.group} · {session.sport} · {session.branch}</span>
  </section>;
}
const attendanceLabels: Record<string, string> = { Present: "حاضر", Late: "متأخر", Excused: "معتذر", Absent: "غائب", NotRecorded: "لم يُسجل" };
const sessionListPath = "/dashboard/attendance/sessions";
type SessionFilters = { from: string; to: string; groupId: string; search: string; status: string };
type SessionListResult = { request: string; sessions?: Session[]; error?: string; denied?: boolean };

function sessionListHref(filters: SessionFilters, playerId: string) {
  const query = new URLSearchParams({ from: filters.from, to: filters.to });
  if (filters.groupId) query.set("groupId", filters.groupId);
  if (filters.search) query.set("search", filters.search);
  if (filters.status) query.set("status", filters.status);
  if (playerId) query.set("playerId", playerId);
  return `${sessionListPath}?${query}`;
}

function sessionListBack(returnTo?: string) {
  if (typeof returnTo !== "string" || !returnTo.startsWith(`${sessionListPath}?`)) return { href: sessionListPath, label: "العودة إلى الحصص" };
  const supplied = new URLSearchParams(returnTo.slice(sessionListPath.length + 1));
  const safe = new URLSearchParams();
  for (const key of ["from", "to", "groupId", "search", "status", "playerId"]) {
    const value = supplied.get(key);
    if (value !== null) safe.set(key, value);
  }
  return { href: safe.size ? `${sessionListPath}?${safe}` : sessionListPath, label: "العودة إلى الحصص" };
}

export function TrainingSessionsList({ staffOnly = false, initial = {}, playerId = "" }: {
  staffOnly?: boolean; initial?: Partial<SessionFilters>; playerId?: string;
}) {
  const defaults = useDateFilterDefaults();
  const hasPeriod = typeof initial.from === "string" || typeof initial.to === "string";
  const [filters, setFilters] = useState<SessionFilters>(() => ({
    from: typeof initial.from === "string" ? initial.from : hasPeriod ? "" : defaults.to,
    to: typeof initial.to === "string" ? initial.to : hasPeriod ? "" : defaults.to,
    groupId: typeof initial.groupId === "string" ? initial.groupId : "",
    search: typeof initial.search === "string" ? initial.search : "",
    status: staffOnly && typeof initial.status === "string" && initial.status in attendanceLabels ? initial.status : "",
  }));
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<SessionListResult>();
  const { from, to, groupId, search, status } = filters;
  const invalidRange = !!from && !!to && to < from;
  const apiQuery = from || to
    ? new URLSearchParams({ from: from || to, to: to || from })
    : new URLSearchParams({ allDates: "true" });
  const path = `/api/v1/attendance/sessions?${apiQuery}`;
  const request = `${path}:${attempt}`;
  const current = result?.request === request ? result : undefined;
  const error = invalidRange ? "تاريخ النهاية يجب ألا يسبق تاريخ البداية." : current?.error;
  const loading = !invalidRange && !current;
  const sessions = !error ? current?.sessions ?? [] : [];
  const groups = new Map(sessions.map(session => [session.trainingGroupId, session.group]));
  const term = search.trim().toLowerCase();
  const visible = sessions.filter(session => session.status !== "Cancelled" &&
    (!groupId || session.trainingGroupId === groupId) &&
    `${session.group} ${session.branch} ${session.sport} ${session.coaches?.join(" ") ?? ""}`.toLowerCase().includes(term)
  ).flatMap(session => staffOnly
    ? (session.staff ?? []).map(coach => ({ ...session, rowId: `${session.id}-${coach.id}`, coach: coach.name, attendanceStatus: coach.attendanceStatus }))
    : [{ ...session, rowId: session.id, coach: session.coaches?.join("، ") || "—", attendanceStatus: "" }]
  ).filter(row => !staffOnly || !status || row.attendanceStatus === status);

  useEffect(() => {
    if (invalidRange) return;
    const controller = new AbortController();
    const { signal } = controller;
    async function load() {
      try {
        const response = await fetch(path, { signal, cache: "no-store" });
        if (response.status === 401 || response.status === 403) {
          if (!signal.aborted) setResult({ request, error: "الحصص غير متاحة لهذا الحساب.", denied: true });
          return;
        }
        if (!response.ok) throw new DashboardRequestError(
          await apiProblemMessage(response, "تعذر تحميل الحصص. أعد المحاولة."), response.status);
        const sessions: unknown = await response.json();
        if (!Array.isArray(sessions)) throw new Error();
        if (!signal.aborted) setResult({ request, sessions });
      } catch (failure) {
        if (!signal.aborted) setResult({
          request,
          error: failure instanceof DashboardRequestError ? failure.message : "تعذر تحميل الحصص. أعد المحاولة.",
        });
      }
    }
    void load();
    return () => controller.abort();
  }, [invalidRange, path, request]);

  function change(next: Partial<SessionFilters>) {
    const value = { ...filters, ...next };
    setFilters(value);
    if (value.from !== from || value.to !== to) setAttempt(attempt => attempt + 1);
    window.history.replaceState(null, "", sessionListHref(value, playerId));
  }

  function rosterHref(id: string) {
    const query = new URLSearchParams({ returnTo: sessionListHref(filters, playerId) });
    if (playerId && !staffOnly) query.set("playerId", playerId);
    return `${sessionListPath}/${encodeURIComponent(id)}/${staffOnly ? "staff" : "players"}?${query}`;
  }

  return <>
    <PageHeader title={staffOnly ? "حضور المدربين" : "حضور اللاعبين"} context="الحضور" />
    {!staffOnly && <ContextHelp lesson="player-attendance" />}
    <section className="list-card">
      <div className={styles.filters} aria-label="فلاتر الحصص" role="group">
        <SearchInput value={search} onChange={value => change({ search: value })} label="البحث بالاسم" placeholder="المجموعة أو المدرب أو الفرع" />
        <div className={styles.dates}>
          <label>من تاريخ<input type="date" value={from} onChange={event => change({ from: event.target.value })} /></label>
          <label>إلى تاريخ<input type="date" value={to} onChange={event => change({ to: event.target.value })} /></label>
        </div>
        <label>المجموعة<select aria-label="فلتر المجموعة" value={groupId} onChange={event => change({ groupId: event.target.value })}>
          <option value="">كل المجموعات</option>
          {groupId && !groups.has(groupId) && <option value={groupId}>المجموعة المحددة</option>}
          {Array.from(groups, ([id, name]) => <option key={id} value={id}>{name}</option>)}
        </select></label>
        {staffOnly && <label>حالة الحضور<select aria-label="حالة الحضور" value={status} onChange={event => change({ status: event.target.value })}>
          <option value="">كل الحالات</option>
          {Object.entries(attendanceLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>}
        <div className={styles.actions}>
          <Button variant="secondary" onClick={() => change({ search: "", from: "", to: "", groupId: "", status: "" })}>مسح</Button>
          <Button variant="secondary" disabled={loading || invalidRange || current?.denied} onClick={() => setAttempt(value => value + 1)}>تحديث الحصص</Button>
        </div>
      </div>
      {error ? <>
        <ErrorState message={error} />
        {!invalidRange && !current?.denied && <Button variant="secondary" onClick={() => setAttempt(value => value + 1)}>إعادة المحاولة</Button>}
      </> : loading ? <LoadingState message="جارٍ تحميل الحصص…" /> : !visible.length
        ? <EmptyState message={search || groupId || status ? "لا توجد حصص مطابقة للفلاتر." : "لا توجد حصص متاحة خلال الفترة المختارة."} />
        : <AdminDataTable label={staffOnly ? "حضور المدربين" : "حضور اللاعبين"} rows={visible} rowKey={row => row.rowId} columns={[
          { key: "group", header: "المجموعة", primary: true, render: row => row.group },
          { key: "coach", header: "المدرب", render: row => row.coach },
          { key: "branch", header: "الفرع", render: row => row.branch },
          { key: "sport", header: "الرياضة", render: row => row.sport },
          { key: "date", header: "اليوم والتاريخ والتوقيت", render: row => <>{formatSessionDateAr(row.sessionDate)}<small>من {formatTimeAr(row.startTime)} إلى {formatTimeAr(row.endTime)}</small></> },
          { key: "state", header: "الحالة", render: row => staffOnly ? <StatusBadge status={row.attendanceStatus} /> : <small>{row.present ?? 0} حاضر · {row.absent ?? 0} غائب{(row.excused ?? 0) > 0 && ` · ${row.excused} معتذر`} · {Math.max(0, row.playerCount - row.recorded)} لم يُسجل</small> },
          { key: "actions", header: "الإجراء", render: row => <RowActions actions={[{ label: staffOnly ? "تسجيل حضور المدرب" : "تسجيل الحضور", href: rosterHref(row.id) }]} /> },
        ]} />}
    </section>
  </>;
}

type Roster = { canManageAttendance: boolean; canRequestCorrection?: boolean; isPast?: boolean; draftLease?: string; draftScope?: string; draftExpiresAtUtc?: string; session: SessionSummary; players: PlayerRow[] };
function AttendanceStateSelect({name,value,disabled,onChange}:{name:string;value:string;disabled:boolean;onChange:(value:string)=>void}){
 return <select className="attendance-state-select" aria-label={`حضور ${name}`} value={value} disabled={disabled} onChange={event=>onChange(event.target.value)}>{Object.entries(attendanceLabels).map(([key,label])=><option value={key} key={key}>{label}</option>)}</select>;
}

type PlayerRow = { sportEnrollmentId: string; playerId: string; playerCode: string; arabicName: string; photoReference?: string; attendanceStatus: string; version?: number; subscriptionPlan?: string; subscriptionType?: string; remainingSessions?: number; sessionConsumed: boolean; warning?: string };
async function readPlayerRoster(path: string, signal?: AbortSignal): Promise<Roster> {
  const response = await fetch(path, { signal, cache: "no-store" });
  if (!response.ok) throw new DashboardRequestError("تعذر تحديث الكشف. حاول مرة أخرى.", response.status);
  return response.json();
}

export function PlayerAttendance({ sessionId, playerId = "", returnTo }: { sessionId: string; playerId?: string; returnTo?: string }) {
  return <PlayerAttendancePage key={sessionId} sessionId={sessionId} playerId={playerId} returnTo={returnTo} />;
}
function PlayerAttendancePage({ sessionId, playerId, returnTo }: { sessionId: string; playerId: string; returnTo?: string }) {
  const [data, setData] = useState<Roster | null>(null); const [states, setStates] = useState<Record<string, string>>({}); const [search, setSearch] = useState(""); const [loadError, setLoadError] = useState("");
  const path = `/api/v1/attendance/sessions/${sessionId}/players`;
  const snapshot = (roster: Roster): AttendanceSnapshot => ({ ...roster, canManage: roster.canManageAttendance, rows: roster.players.map(row => ({ id: row.sportEnrollmentId, name: row.arabicName, status: row.attendanceStatus, version: row.version ?? 0 })) });
  const applyRoster = useCallback((roster: Roster) => {
    setData(roster);
    setStates(Object.fromEntries(roster.players.map(row => [row.sportEnrollmentId, row.attendanceStatus])));
    setLoadError("");
  }, []);
  const sync = useAttendanceSync(path, "sportEnrollmentId", () => readPlayerRoster(path), snapshot, applyRoster);
  const error = sync.error || loadError;
  useEffect(() => {
    const request = new AbortController();
    void readPlayerRoster(path, request.signal).then(roster => {
      if (!request.signal.aborted) applyRoster(roster);
    }).catch(() => { if (!request.signal.aborted) setLoadError("تعذر تحميل كشف اللاعبين."); });
    return () => request.abort();
  }, [path, applyRoster]);
  const visible = data?.players.filter(row => (!playerId || row.playerId === playerId) && `${row.playerCode} ${row.arabicName}`.toLowerCase().includes(search.trim().toLowerCase())) ?? [];
  const toMarkPresent = visible.filter(row => row.attendanceStatus === "NotRecorded" && states[row.sportEnrollmentId] === "NotRecorded");
  const pending = data?.players.filter(row => states[row.sportEnrollmentId] !== row.attendanceStatus).length ?? 0;
  function markVisiblePresent() {
    setStates(current => ({ ...current, ...Object.fromEntries(toMarkPresent.map(row => [row.sportEnrollmentId, "Present"])) }));
  }
  async function save() { if (data?.canManageAttendance) await sync.save(snapshot(data), states); }
  if (sync.blocked) return <><PageHeader title="حضور اللاعبين" context="الحضور" back={sessionListBack(returnTo)} />{error && <ErrorState message={error} />}<AttendanceSyncNotice sync={sync} /></>;
  if (!data && !error) return <LoadingState />;
  return <><PageHeader title="حضور اللاعبين" context="الحضور / جلسات التدريب" back={sessionListBack(returnTo)} />{error && <ErrorState message={error} />}{data ? <>
    <AttendanceSessionSummary session={data.session} />
    <section className="list-card attendance-editor"><AttendanceSyncNotice sync={sync} />{data.canManageAttendance && <PlayerCardLookup sessionId={sessionId} onResolved={setSearch} />}
    <div className="attendance-actions">
      <ListSearch value={search} onChange={setSearch} label="بحث بالاسم أو كود اللاعب" placeholder="اسم اللاعب أو الكود" />
      {data.canManageAttendance && !playerId && <button className="secondary-button" type="button" disabled={sync.locked || !toMarkPresent.length} onClick={markVisiblePresent}>اعتبار غير المسجلين في النتائج حاضرين ({toMarkPresent.length})</button>}
    </div>
    {!data.canManageAttendance && <p role="status">{
      data.session.status === "Cancelled" ? "أُلغيت الحصة." : data.isPast ? "كشف حضور يوم سابق." : "كشف الحضور للعرض فقط."
    }</p>}
    <AdminDataTable label="كشف حضور اللاعبين" rows={visible} rowKey={row=>row.sportEnrollmentId} columns={[
 {key:"player",header:"اللاعب",primary:true,render:row=><IdentityMark name={row.arabicName} detail={row.playerCode}/>},
 {key:"plan",header:"الاشتراك",render:row=><>{row.subscriptionPlan??"لا يوجد اشتراك مؤهل"}{row.warning&&row.warning!==(row.subscriptionPlan??"لا يوجد اشتراك مؤهل")&&<small className="attendance-row-warning">{row.warning}</small>}</>},
 {key:"balance",header:"رصيد الحصص",render:row=>row.remainingSessions??"غير منطبق"},
 {key:"status",header:"الحضور",render:row=>data.canManageAttendance?<AttendanceStateSelect name={row.arabicName} value={states[row.sportEnrollmentId]??row.attendanceStatus} disabled={sync.locked} onChange={value=>setStates({...states,[row.sportEnrollmentId]:value})}/>:<StatusBadge status={row.attendanceStatus}/>},
 ]}/>
    {data.canManageAttendance && <div className="attendance-submit">
      {sync.draft ? <p role="status">{sync.reviewed ? "تغييرات غير مسجلة" : "الحفظ غير مؤكد"}</p> : pending > 0 ? <p role="status">تغييرات قيد المراجعة ({pending})</p> : sync.message && <p className="success-message" role="status">{sync.message}</p>}
      <button className="primary-button" disabled={sync.busy || sync.conflicts.length > 0 || (!sync.draft && (!pending || data.session.status === "Cancelled"))} onClick={() => void save()}>{sync.actionLabel ?? "حفظ الحضور"}</button>
    </div>}
    {data.canRequestCorrection && <PastAttendanceRequest sessionId={sessionId} kind="Players" rows={data.players.map(row=>({id:row.sportEnrollmentId,name:row.arabicName,status:row.attendanceStatus,version:row.version}))}/>}
    </section></> : <button className="secondary-button" disabled={sync.busy} onClick={() => void sync.discard()}>إعادة المحاولة</button>}</>;
}

type StaffRow = { academyMembershipId: string; name: string; role: string; attendanceStatus: string; version?: number };
type StaffRoster = {
  session: SessionSummary;
  rows: StaffRow[];
  canManage: boolean;
  canRequestCorrection: boolean;
  draftLease?: string;
  draftScope?: string;
  draftExpiresAtUtc?: string;
};

async function readStaffRoster(sessionPath: string, signal?: AbortSignal): Promise<StaffRoster> {
  const options = { signal, cache: "no-store" as const };
  const [roster, grants, context, sessionResponse] = await Promise.all([
    fetch(`${sessionPath}/staff`, options), fetch("/api/v1/my-permissions", options),
    fetch(`${sessionPath}/correction-context`, options), fetch(sessionPath, options),
  ]);
  for (const response of [roster, grants, context, sessionResponse]) {
    if (!response.ok) throw new DashboardRequestError("تعذر تحديث كشف المدربين. حاول مرة أخرى.", response.status);
  }
  const [rows, permissions, data, session] = await Promise.all([roster.json(), grants.json(), context.json(), sessionResponse.json()]);
  return { ...data, rows, session, canManage: permissions.includes("attendance.manage") && data.canManageAttendance === true };
}

function staffSnapshot(data: StaffRoster): AttendanceSnapshot {
  return { ...data, rows: data.rows.map(row => ({ id: row.academyMembershipId, name: row.name, status: row.attendanceStatus, version: row.version ?? 0 })) };
}

export function StaffAttendance({ sessionId, returnTo }: { sessionId: string; returnTo?: string }) {
  return <StaffAttendancePage key={sessionId} sessionId={sessionId} returnTo={returnTo} />;
}

function StaffAttendancePage({ sessionId, returnTo }: { sessionId: string; returnTo?: string }) {
  const [data, setData] = useState<StaffRoster | null>(null);
  const [states, setStates] = useState<Record<string, string>>({});
  const [loadError, setLoadError] = useState("");
  const sessionPath = `/api/v1/attendance/sessions/${sessionId}`;
  const applyRoster = useCallback((value: StaffRoster) => {
    setData(value);
    setStates(Object.fromEntries(value.rows.map(row => [row.academyMembershipId, row.attendanceStatus])));
    setLoadError("");
  }, []);
  const sync = useAttendanceSync(`${sessionPath}/staff`, "academyMembershipId", () => readStaffRoster(sessionPath), staffSnapshot, applyRoster);
  const error = sync.error || loadError;
  useEffect(() => {
    const request = new AbortController();
    void readStaffRoster(sessionPath, request.signal).then(roster => {
      if (!request.signal.aborted) applyRoster(roster);
    }).catch(() => { if (!request.signal.aborted) setLoadError("تعذر تحميل كشف المدربين."); });
    return () => request.abort();
  }, [sessionPath, applyRoster]);
  const pending = data?.rows.filter(row => states[row.academyMembershipId] !== row.attendanceStatus).length ?? 0;
  if (sync.blocked) return <><PageHeader title="حضور المدربين" context="الحضور" back={sessionListBack(returnTo)} />{error && <ErrorState message={error} />}<AttendanceSyncNotice sync={sync} /></>;
  if (!data && !error) return <LoadingState message="جارٍ تحميل كشف المدربين…" />;
  return <><PageHeader title="حضور المدربين" context="الحضور" back={sessionListBack(returnTo)} />{error && <ErrorState message={error} />}
    {data ? <><AttendanceSessionSummary session={data.session} /><section className="list-card attendance-editor">
      <AttendanceSyncNotice sync={sync} />
      {!data.canManage && <p role="status">الحضور للعرض فقط؛ يتولى الإداري المصرح له التسجيل.</p>}
      {!data.rows.length ? <EmptyState message="لا يوجد جهاز فني مكلف بهذه المجموعة." /> : <AdminDataTable label="كشف حضور المدربين" rows={data.rows} rowKey={row => row.academyMembershipId} columns={[
        {key:"name",header:"الاسم",primary:true,render:row=><strong>{row.name}</strong>},
        {key:"role",header:"الدور",render:row=>row.role==="Coach"?"مدرب":"جهاز فني"},
        {key:"status",header:"الحضور",render:row=>data.canManage?<AttendanceStateSelect name={row.name} value={states[row.academyMembershipId]??row.attendanceStatus} disabled={sync.locked} onChange={value=>setStates(current=>({...current,[row.academyMembershipId]:value}))}/>:<StatusBadge status={row.attendanceStatus}/>},
      ]}/>}
      {data.canManage && data.rows.length > 0 && <div className="attendance-submit">
        {sync.draft ? <p role="status">{sync.reviewed ? "تغييرات غير مسجلة" : "الحفظ غير مؤكد"}</p> : pending > 0 ? <p role="status">تغييرات قيد المراجعة ({pending})</p> : sync.message && <p className="success-message" role="status">{sync.message}</p>}
        <button className="primary-button" disabled={sync.busy || sync.conflicts.length > 0 || (!sync.draft && !pending)} onClick={() => void sync.save(staffSnapshot(data), states)}>{sync.actionLabel ?? "حفظ حضور المدربين"}</button>
      </div>}
      {data.canRequestCorrection && <PastAttendanceRequest sessionId={sessionId} kind="Staff" rows={data.rows.map(row=>({id:row.academyMembershipId,name:row.name,status:row.attendanceStatus,version:row.version}))}/>}
    </section></> : <button className="secondary-button" disabled={sync.busy} onClick={() => void sync.discard()}>إعادة المحاولة</button>}
  </>;
}

type HistoryRow = { id: string; playerCode: string; player: string; group: string; sport: string; branch: string; sessionDate: string; startTime: string; status: string; sessionConsumed: boolean };
export function AttendanceHistory({ playerId = "" }: { playerId?: string }) { const defaults=useDateFilterDefaults(); const [from,setFrom]=useState(defaults.from),[to,setTo]=useState(defaults.to); const [rows, setRows] = useState<HistoryRow[]>([]); const [search, setSearch] = useState(""); const [status, setStatus] = useState(""); const [error, setError] = useState(""); useEffect(() => { const query = new URLSearchParams(); if(from)query.set("from",from);if(to)query.set("to",to); if (playerId) query.set("playerId", playerId); if (search) query.set("search", search); if (status) query.set("status", status); const timer = setTimeout(() => fetch(`/api/v1/attendance/history?${query}`).then(async response => { if (!response.ok) throw new Error(); setRows(await response.json() as HistoryRow[]); }).catch(() => setError("تعذر تحميل سجل الحضور.")), 200); return () => clearTimeout(timer); }, [playerId, search, status,from,to]); return <><PageHeader title="سجل الحضور" context="الحضور / سجل الحضور" description={playerId ? "سجل الحضور الخاص باللاعب المحدد فقط." : "راجع الحالات المسجلة فعليًا لكل لاعب وجلسة تدريب."} />{error && <ErrorState message={error} />}<section className="list-card"><div className="player-filters"><label>من تاريخ<input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></label><label>إلى تاريخ<input type="date" value={to} onChange={e=>setTo(e.target.value)}/></label><button onClick={()=>{setFrom("");setTo("");setSearch("");setStatus("");}}>مسح</button><ListSearch value={search} onChange={setSearch} label="بحث سجل الحضور" placeholder="الاسم أو كود اللاعب" /><select aria-label="حالة الحضور" value={status} onChange={event => setStatus(event.target.value)}><option value="">كل الحالات</option><option value="Present">حاضر</option><option value="Late">متأخر</option><option value="Absent">غائب</option><option value="Excused">معتذر</option><option value="NotRecorded">لم يُسجل</option></select></div>{!rows.length ? <EmptyState title="لا توجد سجلات حضور" message="لا توجد سجلات مطابقة للبحث والحالة المختارة." /> : <AdminDataTable label="سجل الحضور" rows={rows} rowKey={row => row.id} columns={[
    { key: "player", header: "اللاعب", primary: true, width: "minmax(12rem, 1.5fr)", render: row => <><strong>{row.player}</strong><small dir="ltr">{row.playerCode}</small></> },
    { key: "session", header: "التاريخ والوقت", width: "minmax(8rem, 1fr)", render: row => <>{row.sessionDate}<small>{row.startTime.slice(0, 5)}</small></> },
    { key: "group", header: "المجموعة", width: "minmax(11rem, 1.35fr)", render: row => row.group },
    { key: "location", header: "الرياضة والفرع", width: "minmax(10rem, 1.2fr)", render: row => <>{row.sport}<small>{row.branch}</small></> },
    { key: "status", header: "الحالة", width: "minmax(7rem, .7fr)", render: row => <><StatusBadge status={row.status} />{row.sessionConsumed && <small>تم خصم حصة</small>}</> },
  ]} />}</section></>; }

export function GuardianAttendanceList({ playerId }: { playerId: string }) { const [rows, setRows] = useState<{ id: string; sessionDate: string; startTime: string; group: string; sport: string; status: string }[]>([]); useEffect(() => { fetch(`/api/v1/guardian/children/${playerId}/attendance`).then(response => response.ok ? response.json() : []).then(setRows); }, [playerId]); return <section className="guardian-attendance"><h3>آخر الحضور</h3>{rows.length === 0 ? <small>لا يوجد حضور مسجل بعد.</small> : rows.slice(0, 5).map(row => <p key={row.id}><span>{row.sessionDate} · {row.sport} · {row.group}</span><b>{attendanceLabels[row.status]}</b></p>)}</section>; }
