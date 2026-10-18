"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { SessionCoachOverrideDialog } from "./session-coach-override";
import { useStaffAction } from "./use-staff-action";

type Option = { id: string; name: string };
type Options = Record<"groups" | "coaches" | "players" | "guardians" | "branches" | "sports", Option[]>;
type Filters = { groupId: string; coachId: string; playerId: string; guardianId: string; branchId: string; sportId: string; from: string; to: string };
type Session = { id: string; group: string; branch: string; sport: string; coaches: string[]; trainingGroupId: string; sessionDate: string; startTime: string; endTime: string; playerCount: number };
const emptyFilters: Filters = { groupId: "", coachId: "", playerId: "", guardianId: "", branchId: "", sportId: "", from: "", to: "" };
const filterFields = [
  ["groupId", "المجموعة", "groups"], ["coachId", "المدرب", "coaches"], ["playerId", "اللاعب", "players"],
  ["guardianId", "ولي الأمر", "guardians"], ["branchId", "الفرع", "branches"], ["sportId", "الرياضة", "sports"],
] as const;

export function GroupSessions({ initialGroupId = "", initialDate }: { initialGroupId?: string; initialDate: string }) {
  const canEditCoach = useStaffAction("academy", "edit");
  const [overriding, setOverriding] = useState<string | null>(null), [revision, setRevision] = useState(0);
  const defaultFilters = { ...emptyFilters, from: `${initialDate.slice(0, 8)}01`, to: initialDate };
  const [filters, setFilters] = useState<Filters>({ ...defaultFilters, groupId: initialGroupId });
  const [options, setOptions] = useState<Options>({ groups: [], coaches: [], players: [], guardians: [], branches: [], sports: [] });
  const [sessions, setSessions] = useState<Session[]>([]);
  const [error, setError] = useState(""); const [loading, setLoading] = useState(true); const [page, setPage] = useState(0);
  const query = useMemo(() => new URLSearchParams(Object.entries(filters).filter(([, value]) => value)).toString(), [filters]);
  useEffect(() => {
    const abort = new AbortController();
    fetch("/api/v1/manage/sessions/options", { signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); setOptions(await r.json()); }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل خيارات الحصص. تحقق من صلاحياتك."); });
    return () => abort.abort();
  }, []);
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/sessions${query ? `?${query}` : ""}`, { signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); setSessions(await r.json()); }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل الحصص. تحقق من نطاق التاريخ والصلاحيات."); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [query, revision]);
  function change(key: keyof Filters, value: string) { if (filters[key] === value) return; setLoading(true); setError(""); setPage(0); setFilters(previous => ({ ...previous, [key]: value })); }
  return <>{overriding && <SessionCoachOverrideDialog sessionId={overriding} onClose={() => setOverriding(null)} onSaved={async () => { setError(""); setLoading(true); setRevision(v => v + 1); }} />}<PageHeader title="الحصص" context="الأكاديمية / المجموعات / الحصص" action={{ label: "المجموعات", href: "/dashboard/academy/groups" }} />
    <section className="list-card"><div className="report-filters">
      {filterFields.map(([key, label, collection]) => <label key={key}>{label}<select aria-label={label} value={filters[key]} onChange={e => change(key, e.target.value)}><option value="">الكل</option>{options[collection].map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>)}
      <label>من تاريخ<input type="date" value={filters.from} onChange={e => change("from", e.target.value)} /></label><label>إلى تاريخ<input type="date" value={filters.to} onChange={e => change("to", e.target.value)} /></label>
      <button type="button" onClick={() => { if (!query) return; setLoading(true); setError(""); setPage(0); setFilters({ ...emptyFilters }); }}>مسح الفلاتر</button>
    </div>{error && <ErrorState message={error} />}{loading ? <LoadingState /> : error ? null : !sessions.length ? <EmptyState message="لا توجد حصص مطابقة للفلاتر." /> : <>
      <div className="sessions-toolbar"><span>{sessions.length} حصة</span><span>صفحة {page + 1} من {Math.ceil(sessions.length / 50)}</span></div>
      <AdminDataTable label="حصص المجموعات" rows={sessions.slice(page * 50, (page + 1) * 50)} rowKey={s => s.id} columns={[
        { key: "date", header: "اليوم والتاريخ والوقت", primary: true, render: s => <>{formatSessionDateAr(s.sessionDate)}<small>{formatTimeAr(s.startTime)} — {formatTimeAr(s.endTime)}</small></> },
        { key: "group", header: "المجموعة", render: s => <Link href={`/dashboard/academy/groups/${s.trainingGroupId}`}>{s.group}</Link> },
        { key: "coach", header: "المدرب", render: s => s.coaches.join("، ") || "—" },
        { key: "location", header: "الفرع والرياضة", render: s => <>{s.branch}<small>{s.sport}</small></> },
        { key: "players", header: "اللاعبون", render: s => s.playerCount },
        { key: "actions", header: "الحضور وإدارة الحصة", render: s => <div className="row-actions"><Link className="secondary-button" href={`/dashboard/attendance/sessions/${s.id}/players${filters.playerId ? `?playerId=${filters.playerId}` : ""}`}>حضور اللاعبين</Link><Link className="secondary-button" href={`/dashboard/attendance/sessions/${s.id}/lesson`}>خطة الحصة</Link>{canEditCoach && <button className="secondary-button" onClick={() => setOverriding(s.id)}>بديل لهذه الحصة</button>}</div> },
      ]} /><div className="sessions-toolbar"><button type="button" disabled={page === 0} onClick={() => setPage(p => p - 1)}>السابق</button><button type="button" disabled={(page + 1) * 50 >= sessions.length} onClick={() => setPage(p => p + 1)}>التالي</button></div>
    </> }</section></>;
}
