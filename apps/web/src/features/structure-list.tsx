"use client";

import { useEffect, useMemo, useState } from "react";
import { ConfirmationDialog, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { IdentityMark } from "../components/sports-ui";
import { StatusBadge } from "../components/dashboard-shell";

type Kind = "branches" | "sports" | "categories" | "groups" | "coaches";
type Item = { id: string; arabicName: string; englishName?: string; isActive: boolean; branchName?: string; sportName?: string; categoryName?: string; assignedGroups?: number; playerCount?: number; coaches?: string[]; groups?: { id: string; name: string; branch: string }[]; schedules?: { dayOfWeek: string; startTime: string; endTime: string }[] };
const dayNames: Record<string, string> = { Sunday: "الأحد", Monday: "الاثنين", Tuesday: "الثلاثاء", Wednesday: "الأربعاء", Thursday: "الخميس", Friday: "الجمعة", Saturday: "السبت" };
const config: Record<Kind, { title: string; create: string }> = {
  branches: { title: "الفروع", create: "إضافة فرع" }, sports: { title: "الرياضات", create: "إضافة رياضة" },
  categories: { title: "الفئات العمرية", create: "إضافة فئة عمرية" }, groups: { title: "المجموعات", create: "إنشاء مجموعة" },
  coaches: { title: "المدربون", create: "إسناد مجموعة" },
};

export function StructureList({ kind }: { kind: Kind }) {
  const [items, setItems] = useState<Item[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [target, setTarget] = useState<Item | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => { fetch(`/api/v1/manage/structure/${kind}`).then(async response => { if (!response.ok) throw new Error(); setItems(await response.json() as Item[]); }).catch(() => setError("تعذر تحميل البيانات.")).finally(() => setLoading(false)); }, [kind]);
  const filtered = useMemo(() => { const term = search.trim().toLocaleLowerCase("ar"); return term ? items.filter(item => [item.arabicName, item.englishName, item.branchName, item.sportName, item.categoryName].some(value => value?.toLocaleLowerCase("ar").includes(term))) : items; }, [items, search]);

  async function changeStatus() { if (!target) return; setSaving(true); setError(""); try { await csrfRequest(`/api/v1/manage/structure/${kind}/${target.id}/status`, "PUT", { isActive: !target.isActive }); const response = await fetch(`/api/v1/manage/structure/${kind}`); if (!response.ok) throw new Error("تعذر تحديث القائمة."); setItems(await response.json() as Item[]); setSuccess(target.isActive ? `تم إيقاف «${target.arabicName}».` : `تم تفعيل «${target.arabicName}».`); setTarget(null); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تغيير الحالة."); } finally { setSaving(false); } }

  return <>
    <PageHeader title={config[kind].title} context={`الأكاديمية / ${config[kind].title}`} action={{ label: config[kind].create, href: `/dashboard/academy/${kind}/new` }} />
    <section className="list-card">
      <ListSearch value={search} onChange={setSearch} label={`بحث ${config[kind].title}`} />
      {success && <p className="success-message" role="status">{success}</p>}
      {error && <ErrorState message={error} />}
      {loading ? <LoadingState /> : filtered.length === 0 ? <EmptyState /> : <div className={kind === "groups" || kind === "coaches" ? "sports-structure-list" : "data-list"}>{filtered.map(item => <article className={`${kind === "groups" || kind === "coaches" ? "sports-structure-card" : ""} ${!item.isActive ? "inactive-record" : ""}`} key={item.id}><div><IdentityMark name={item.arabicName} detail={kind === "groups" ? item.categoryName : kind === "coaches" ? `${item.assignedGroups ?? 0} مجموعة مسندة` : undefined} /><div className="team-chips">{item.branchName && <span className="team-chip">{item.branchName}</span>}{item.sportName && <span className="team-chip">{item.sportName}</span>}<StatusBadge status={item.isActive ? "Active" : "Inactive"} /></div>{kind === "groups" && <div className="structure-context"><span>المدرب: {item.coaches?.join("، ") || "لا يوجد إسناد فعال"}</span>{item.playerCount != null && <span>{item.playerCount} لاعب مسجل</span>}{item.schedules?.length ? item.schedules.map((schedule, i) => <small key={i}>{dayNames[schedule.dayOfWeek] ?? schedule.dayOfWeek} · {schedule.startTime.slice(0, 5)}–{schedule.endTime.slice(0, 5)}</small>) : <small>لا توجد مواعيد دورية مسجلة</small>}</div>}{kind === "coaches" && item.groups?.map(group => <p className="structure-group" key={group.id}>{group.name}<small>{group.branch}</small></p>)}</div><RowActions actions={kind === "coaches" ? [
          { label: "عرض المجموعات", href: `/dashboard/academy/coaches/${item.id}` }, { label: "إسناد مجموعة", href: "/dashboard/academy/coaches/new" },
        ] : [
          { label: "عرض", href: `/dashboard/academy/${kind}/${item.id}` }, { label: "تعديل", href: `/dashboard/academy/${kind}/${item.id}/edit` }, { label: item.isActive ? "إيقاف" : "تفعيل", onClick: () => setTarget(item), tone: item.isActive ? "danger" : "normal" },
        ]} /></article>)}</div>}
    </section>
    <ConfirmationDialog open={target !== null} title={target?.isActive ? "تأكيد الإيقاف" : "تأكيد التفعيل"} message={`هل تريد ${target?.isActive ? "إيقاف" : "تفعيل"} «${target?.arabicName ?? ""}»؟ لن تُحذف العلاقات التاريخية.`} confirmLabel={target?.isActive ? "إيقاف" : "تفعيل"} busy={saving} onConfirm={() => void changeStatus()} onCancel={() => setTarget(null)} />
  </>;
}
