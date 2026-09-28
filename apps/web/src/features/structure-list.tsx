"use client";

import { useEffect, useMemo, useState } from "react";
import { ConfirmationDialog, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";

type Kind = "branches" | "sports" | "categories" | "groups" | "coaches";
type Item = { id: string; arabicName: string; englishName?: string; isActive: boolean; branchName?: string; sportName?: string; categoryName?: string; assignedGroups?: number };
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
      {loading ? <LoadingState /> : filtered.length === 0 ? <EmptyState /> : <div className="data-list">{filtered.map(item => <article className={!item.isActive ? "inactive-record" : ""} key={item.id}><div><strong>{item.arabicName}</strong><small>{kind === "groups" ? `${item.branchName} · ${item.sportName} · ${item.categoryName}` : kind === "coaches" ? `${item.assignedGroups ?? 0} مجموعة مسندة` : item.isActive ? "فعال" : "متوقف"}</small></div><RowActions actions={kind === "coaches" ? [
          { label: "عرض المجموعات", href: `/dashboard/academy/coaches/${item.id}` }, { label: "إسناد مجموعة", href: "/dashboard/academy/coaches/new" },
        ] : [
          { label: "عرض", href: `/dashboard/academy/${kind}/${item.id}` }, { label: "تعديل", href: `/dashboard/academy/${kind}/${item.id}/edit` }, { label: item.isActive ? "إيقاف" : "تفعيل", onClick: () => setTarget(item), tone: item.isActive ? "danger" : "normal" },
        ]} /></article>)}</div>}
    </section>
    <ConfirmationDialog open={target !== null} title={target?.isActive ? "تأكيد الإيقاف" : "تأكيد التفعيل"} message={`هل تريد ${target?.isActive ? "إيقاف" : "تفعيل"} «${target?.arabicName ?? ""}»؟ لن تُحذف العلاقات التاريخية.`} confirmLabel={target?.isActive ? "إيقاف" : "تفعيل"} busy={saving} onConfirm={() => void changeStatus()} onCancel={() => setTarget(null)} />
  </>;
}
