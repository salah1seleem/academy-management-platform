"use client";

import { useEffect, useState } from "react";
import { ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader, RowActions } from "../../../components/dashboard-shell";
import { csrfRequest } from "../../../features/dashboard-api";

type Player = { id: string; playerCode: string; arabicName: string; dateOfBirth: string; enrollments: number; isActive: boolean };
type Option = { id: string; arabicName: string; branchId?: string; sportId?: string; ageCategoryId?: string };
type Options = { branches: Option[]; sports: Option[]; categories: Option[]; groups: Option[] };

export default function PlayersPage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [search, setSearch] = useState("");
  const [filters, setFilters] = useState({ branchId: "", sportId: "", ageCategoryId: "", groupId: "" });
  const [options, setOptions] = useState<Options>({ branches: [], sports: [], categories: [], groups: [] });
  const [target, setTarget] = useState<Player | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  useEffect(() => { fetch("/api/v1/manage/structure/options").then(response => response.json()).then(setOptions).catch(() => setError("تعذر تحميل الفلاتر.")); }, []);
  useEffect(() => { const params = new URLSearchParams({ search }); Object.entries(filters).forEach(([key, value]) => { if (value) params.set(key, value); }); fetch(`/api/v1/people/players?${params}`).then(async response => { if (!response.ok) throw new Error(); setPlayers(await response.json() as Player[]); }).catch(() => setError("تعذر تحميل اللاعبين.")).finally(() => setLoading(false)); }, [search, filters]);
  const pick = (key: keyof typeof filters, value: string) => setFilters(current => ({ ...current, [key]: value, ...(key !== "groupId" ? { groupId: "" } : {}) }));
  async function changeStatus() { if (!target) return; try { await csrfRequest(`/api/v1/people/players/${target.id}/status`, "PUT", { isActive: !target.isActive }); setPlayers(current => current.map(player => player.id === target.id ? { ...player, isActive: !player.isActive } : player)); setSuccess(target.isActive ? `تم إيقاف «${target.arabicName}».` : `تم تفعيل «${target.arabicName}».`); setTarget(null); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تغيير حالة اللاعب."); } }

  return <><PageHeader title="قائمة اللاعبين" context="اللاعبون / قائمة اللاعبين" action={{ label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }} /><section className="list-card"><div className="filter-bar player-filters"><input aria-label="بحث اللاعبين" value={search} onChange={event => setSearch(event.target.value)} placeholder="الاسم أو الكود أو هاتف ولي الأمر" /><select aria-label="فلتر الفرع" value={filters.branchId} onChange={event => pick("branchId", event.target.value)}><option value="">كل الفروع</option>{options.branches.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select><select aria-label="فلتر الرياضة" value={filters.sportId} onChange={event => pick("sportId", event.target.value)}><option value="">كل الرياضات</option>{options.sports.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select><select aria-label="فلتر الفئة" value={filters.ageCategoryId} onChange={event => pick("ageCategoryId", event.target.value)}><option value="">كل الفئات</option>{options.categories.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select><select aria-label="فلتر المجموعة" value={filters.groupId} onChange={event => pick("groupId", event.target.value)}><option value="">كل المجموعات</option>{options.groups.filter(option => (!filters.branchId || option.branchId === filters.branchId) && (!filters.sportId || option.sportId === filters.sportId) && (!filters.ageCategoryId || option.ageCategoryId === filters.ageCategoryId)).map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select></div>{success && <p className="success-message" role="status">{success}</p>}{error && <ErrorState message={error} />}{loading ? <LoadingState /> : players.length === 0 ? <EmptyState /> : <div className="data-list">{players.map(player => <article className={!player.isActive ? "inactive-record" : ""} key={player.id}><div><strong>{player.arabicName}</strong><small>{player.playerCode} · {player.enrollments} تسجيل رياضي · {player.isActive ? "فعال" : "متوقف"}</small></div><RowActions actions={[{ label: "عرض", href: `/dashboard/players/${player.id}` }, { label: "تعديل", href: `/dashboard/players/${player.id}/edit` }, { label: player.isActive ? "إيقاف" : "تفعيل", onClick: () => setTarget(player), tone: player.isActive ? "danger" : "normal" }]} /></article>)}</div>}</section><ConfirmationDialog open={target !== null} title="تأكيد حالة اللاعب" message={`هل تريد ${target?.isActive ? "إيقاف" : "تفعيل"} «${target?.arabicName ?? ""}»؟ ستظل التسجيلات التاريخية محفوظة.`} confirmLabel={target?.isActive ? "إيقاف" : "تفعيل"} onConfirm={() => void changeStatus()} onCancel={() => setTarget(null)} /></>;
}
