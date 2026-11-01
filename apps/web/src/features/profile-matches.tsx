"use client";

import Link from "next/link";
import { MatchResultCard } from "../components/match-result-card";
import { useEffect, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { ErrorState, LoadingState } from "../components/dashboard-shell";

type Match = { id: string; team: string; opponent: string; tournament: string; matchDate: string; startTime: string; venue: string; status: string; ourScore: number | null; opponentScore: number | null; coachAssignmentSource?:string;coachName?:string };

/** The record ID cannot be cleared with filters. Permissions remain server-side. */
export function ProfileMatches({ playerId, coachMembershipId }: { playerId: string; coachMembershipId?: never } | { playerId?: never; coachMembershipId: string }) {
  const defaults = useDateFilterDefaults();
  const search = useSearchParams(), router = useRouter(), path = usePathname();
  const [filters, writeFilters] = useState(() => search.has("matchDates") ? { from: search.get("matchFrom") ?? "", to: search.get("matchTo") ?? "", status: search.get("matchStatus") ?? "" } : { ...defaults, status: "" });
  function setFilters(next: typeof filters) {
    writeFilters(next);
    const params = new URLSearchParams(search.toString());
    params.set("matchDates", "custom");
    for (const [key, value] of Object.entries({ matchFrom: next.from, matchTo: next.to, matchStatus: next.status })) { if (value) params.set(key, value); else params.delete(key); }
    router.replace(`${path}?${params}`, { scroll: false });
  }
  const [revision, setRevision] = useState(0);
  const query = new URLSearchParams({ ...(playerId ? { playerId } : {}), ...(coachMembershipId ? { coachMembershipId } : {}), ...Object.fromEntries(Object.entries(filters).filter(([key, value]) => key !== "status" && value)) }).toString();
  const key = `${query}:${revision}`;
  const [result, setResult] = useState<{ key: string; items?: Match[]; error?: string }>();
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/matches/?${query}`, { cache: "no-store", signal: abort.signal }).then(async r => {
      if (!r.ok) throw new Error(r.status === 403 ? "لا تملك صلاحية عرض المباريات في هذا النطاق." : "تعذر عرض مباريات هذا الملف؛ أعد المحاولة.");
      if (playerId && r.headers.get("X-Match-Player-Id")?.toLowerCase() !== playerId.toLowerCase() || coachMembershipId && r.headers.get("X-Match-Coach-Id")?.toLowerCase() !== coachMembershipId.toLowerCase()) throw new Error("الخدمة الحالية لا تؤكد نطاق الملف؛ يلزم تحديثها قبل عرض المباريات.");
      const items = await r.json() as Match[];
      if (!abort.signal.aborted) setResult({ key, items });
    }).catch(e => { if (!abort.signal.aborted) setResult({ key, error: e instanceof Error ? e.message : "تعذر عرض المباريات." }); });
    return () => abort.abort();
  }, [key, query, playerId, coachMembershipId]);
  const items = result?.key === key ? result.items?.filter(m => !filters.status || m.status === filters.status) : undefined;
  return <section className="detail-card" aria-label="مباريات الملف">
    <h2>المباريات والمنافسات</h2>
    <p className="muted">{playerId ? "مشاركة اللاعب من القائمة المحفوظة عند النشر؛ تغيير مجموعته لا يغير هذه القائمة." : "الإسناد المحفوظ للمباراة مستقل عن مدرب الفريق. أي مباراة قديمة غير موثقة تظهر بوضوح لحين مراجعتها."}</p>
    <div className="report-filters">
      <label>من تاريخ<input type="date" value={filters.from} onChange={e => setFilters({ ...filters, from: e.target.value })} /></label>
      <label>إلى تاريخ<input type="date" value={filters.to} onChange={e => setFilters({ ...filters, to: e.target.value })} /></label>
      <label>الحالة<select value={filters.status} onChange={e => setFilters({ ...filters, status: e.target.value })}><option value="">كل الحالات</option><option value="Published">منشورة</option><option value="Completed">منتهية بنتيجة</option><option value="Cancelled">ملغاة</option>{coachMembershipId && <option value="Draft">مسودة</option>}</select></label>
      <button type="button" onClick={() => setFilters({ from: "", to: "", status: "" })}>مسح الفلاتر · كل المواعيد</button>
    </div>
    {result?.key === key && result.error ? <><ErrorState message={result.error} /><button type="button" onClick={() => setRevision(v => v + 1)}>إعادة المحاولة</button></> : !items ? <LoadingState /> : items.length ? items.map(m => <MatchResultCard key={m.id} match={m}>{m.coachAssignmentSource==="Confirmed"?<span>{m.coachName}</span>:<span>{"\u0625\u0633\u0646\u0627\u062f \u0642\u062f\u064a\u0645 \u063a\u064a\u0631 \u0645\u0648\u062b\u0642 \u2014 \u0645\u0646 \u0627\u0644\u0641\u0631\u064a\u0642 \u0627\u0644\u062d\u0627\u0644\u064a \u0644\u062d\u064a\u0646 \u0627\u0644\u0645\u0631\u0627\u062c\u0639\u0629"}</span>}<Link href={`/dashboard/matches?matchId=${encodeURIComponent(m.id)}`}>{"\u0639\u0631\u0636 \u0627\u0644\u0645\u0628\u0627\u0631\u0627\u0629"}</Link></MatchResultCard>) : <p>لا توجد مباريات تخص هذا الملف ضمن الفلاتر.</p>}
  </section>;
}
