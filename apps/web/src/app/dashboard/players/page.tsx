"use client";

import { useEffect, useMemo, useState } from "react";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge } from "../../../components/dashboard-shell";
import { csrfRequest } from "../../../features/dashboard-api";
import { IdentityMark } from "../../../components/sports-ui";
import { formatDateAr } from "../../../lib/formatters";

type Player = { id: string; playerCode: string; arabicName: string; dateOfBirth: string; enrollments: number; isActive: boolean; photoReference?: string; enrollmentSummaries?: Enrollment[] };
type Enrollment = { id: string; sportName: string; branchName: string; groupName: string; status: string; subscription?: { plan: string; status: string } };
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
  const [page, setPage] = useState(0);
  const pageSize = 12;
  const pageCount = Math.max(1, Math.ceil(players.length / pageSize));
  const currentPage = Math.min(page, pageCount - 1);
  const visiblePlayers = useMemo(() => players.slice(currentPage * pageSize, (currentPage + 1) * pageSize), [players, currentPage]);


  useEffect(() => { fetch("/api/v1/manage/structure/options").then(response => response.json()).then(setOptions).catch(() => setError("تعذر تحميل الفلاتر.")); }, []);
  useEffect(() => { const params = new URLSearchParams({ search }); Object.entries(filters).forEach(([key, value]) => { if (value) params.set(key, value); }); fetch(`/api/v1/people/players?${params}`).then(async response => { if (!response.ok) throw new Error(); setPlayers(await response.json() as Player[]); }).catch(() => setError("تعذر تحميل اللاعبين.")).finally(() => setLoading(false)); }, [search, filters]);
  const pick = (key: keyof typeof filters, value: string) => { setPage(0); setFilters(current => ({ ...current, [key]: value, ...(key !== "groupId" ? { groupId: "" } : {}) })); };
  async function changeStatus() { if (!target) return; try { await csrfRequest(`/api/v1/people/players/${target.id}/status`, "PUT", { isActive: !target.isActive }); setPlayers(current => current.map(player => player.id === target.id ? { ...player, isActive: !player.isActive } : player)); setSuccess(target.isActive ? `تم إيقاف «${target.arabicName}».` : `تم تفعيل «${target.arabicName}».`); setTarget(null); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تغيير حالة اللاعب."); } }

  function resetFilters() { setSearch(""); setFilters({ branchId: "", sportId: "", ageCategoryId: "", groupId: "" }); }

  return <>
    <PageHeader title="قائمة اللاعبين" context="اللاعبون / إدارة اللاعبين" description="ابحث في ملفات اللاعبين وتسجيلاتهم الرياضية وحالتهم الحالية." action={{ label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }} />
    <FilterToolbar resultCount={players.length} onReset={search || Object.values(filters).some(Boolean) ? resetFilters : undefined}>
      <SearchInput label="بحث اللاعبين" value={search} onChange={value => { setPage(0); setSearch(value); }} placeholder="الاسم أو الكود أو هاتف ولي الأمر" />
      <select aria-label="فلتر الفرع" value={filters.branchId} onChange={event => pick("branchId", event.target.value)}><option value="">كل الفروع</option>{options.branches.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select>
      <select aria-label="فلتر الرياضة" value={filters.sportId} onChange={event => pick("sportId", event.target.value)}><option value="">كل الرياضات</option>{options.sports.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select>
      <select aria-label="فلتر الفئة" value={filters.ageCategoryId} onChange={event => pick("ageCategoryId", event.target.value)}><option value="">كل الفئات</option>{options.categories.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select>
      <select aria-label="فلتر المجموعة" value={filters.groupId} onChange={event => pick("groupId", event.target.value)}><option value="">كل المجموعات</option>{options.groups.filter(option => (!filters.branchId || option.branchId === filters.branchId) && (!filters.sportId || option.sportId === filters.sportId) && (!filters.ageCategoryId || option.ageCategoryId === filters.ageCategoryId)).map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select>
    </FilterToolbar>
    {success && <p className="success-message" role="status">{success}</p>}{error && <ErrorState message={error} />}
    <section className="list-card">{loading ? <LoadingState /> : players.length === 0 ? <EmptyState title="لا يوجد لاعبون مطابقون" message="جرّب تعديل البحث أو إعادة ضبط الفلاتر." /> : <AdminDataTable label="قائمة اللاعبين" rows={visiblePlayers} rowKey={player => player.id} rowClassName={player => !player.isActive ? "inactive-record" : ""} columns={[
      { key: "player", header: "اللاعب", primary: true, width: "minmax(11rem, 1.5fr)", render: player => <IdentityMark photo={player.photoReference} name={player.arabicName} detail={`تاريخ الميلاد ${formatDateAr(player.dateOfBirth)}`} /> },
      { key: "code", header: "الكود", width: "minmax(5rem, .65fr)", render: player => <span dir="ltr">{player.playerCode}</span> },
      { key: "enrollments", header: "الفرع والمجموعة", width: "minmax(8rem, 1.1fr)", render: player => !player.enrollmentSummaries ? <small>بيانات التسجيلات غير متاحة</small> : player.enrollmentSummaries?.length === 0 ? "لا توجد تسجيلات" : player.enrollmentSummaries?.map(enrollment => <div className="player-enrollment" key={enrollment.id}><strong>{enrollment.branchName}</strong><small>{enrollment.sportName} · {enrollment.groupName}</small></div>) },
      { key: "subscription", header: "الاشتراك", width: "minmax(7rem, 1fr)", render: player => player.enrollmentSummaries?.map(enrollment => <div className="player-enrollment" key={enrollment.id}><small>{enrollment.sportName} · {enrollment.subscription?.plan ?? "بلا اشتراك"}</small>{enrollment.subscription && <StatusBadge status={enrollment.subscription.status} />}</div>) ?? "—" },
      { key: "status", header: "الحالة", width: "minmax(5rem, .6fr)", render: player => <StatusBadge status={player.isActive ? "Active" : "Inactive"} /> },
      { key: "actions", header: "الإجراءات", width: "minmax(6rem, .65fr)", render: player => <RowActions actions={[{ label: "عرض", href: `/dashboard/players/${player.id}` }, { label: "تعديل", href: `/dashboard/players/${player.id}/edit` }, { label: player.isActive ? "إيقاف" : "تفعيل", onClick: () => setTarget(player), tone: player.isActive ? "danger" : "normal" }]} /> },
    ]} />}</section>
    {pageCount > 1 && <nav className="list-pagination" aria-label="صفحات اللاعبين"><button disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>السابق</button><span>صفحة {currentPage + 1} من {pageCount}</span><button disabled={currentPage + 1 >= pageCount} onClick={() => setPage(currentPage + 1)}>التالي</button></nav>}
    <ConfirmationDialog open={target !== null} title="تأكيد حالة اللاعب" message={`هل تريد ${target?.isActive ? "إيقاف" : "تفعيل"} «${target?.arabicName ?? ""}»؟ ستظل التسجيلات التاريخية محفوظة.`} confirmLabel={target?.isActive ? "إيقاف" : "تفعيل"} tone={target?.isActive ? "danger" : "primary"} onConfirm={() => void changeStatus()} onCancel={() => setTarget(null)} />
  </>;
}
