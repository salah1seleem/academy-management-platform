"use client";

import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, type DataColumn, ConfirmationDialog, EmptyState, ErrorState, FilterToolbar, SearchInput, LoadingState, PageHeader, RowActions } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { IdentityMark } from "../components/sports-ui";
import { StatusBadge } from "../components/dashboard-shell";

type Kind = "branches" | "sports" | "categories" | "groups" | "coaches";
type Item = { parentRating?:number|null;parentRatingCount?:number;photoReference?:string;id: string; arabicName: string; englishName?: string; isActive: boolean; branchName?: string; sportName?: string; categoryName?: string; assignedGroups?: number; playerCount?: number; coaches?: string[]; groups?: { id: string; name: string; branch: string }[]; schedules?: { dayOfWeek: string; startTime: string; endTime: string }[] };
const dayNames: Record<string, string> = { Sunday: "الأحد", Monday: "الاثنين", Tuesday: "الثلاثاء", Wednesday: "الأربعاء", Thursday: "الخميس", Friday: "الجمعة", Saturday: "السبت" };
const config: Record<Kind, { title: string; create: string }> = {
  branches: { title: "الفروع", create: "إضافة فرع" }, sports: { title: "الرياضات", create: "إضافة رياضة" },
  categories: { title: "الفئات العمرية", create: "إضافة فئة عمرية" }, groups: { title: "المجموعات", create: "إنشاء مجموعة" },
  coaches: { title: "المدربون", create: "إضافة مدرب" },
};

export function StructureList({ kind }: { kind: Kind }) {
  const incoming=useSearchParams(),branch=incoming.get("branchId"),from=incoming.get("from"),to=incoming.get("to");
  const listUrl = `/api/v1/manage/structure/${kind}${kind === "coaches" ? `?${new URLSearchParams({...branch?{branchId:branch}:{},...from?{from}:{},...to?{to}:{}})}` : ""}`;
  const [items, setItems] = useState<Item[]>([]);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [coachByGroups,setCoachByGroups]=useState(incoming.get("sort")==="groups");
  const [attempt, setAttempt] = useState(0);
  const [loaded, setLoaded] = useState<{ url: string; attempt: number; failed: boolean }>();
  const loading = loaded?.url !== listUrl || loaded.attempt !== attempt;
  const loadFailed = !loading && loaded.failed;
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [target, setTarget] = useState<Item | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    fetch(listUrl, { signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error();
      const rows = await response.json() as Item[];
      if (controller.signal.aborted) return;
      setItems(rows);
      setLoaded({ url: listUrl, attempt, failed: false });
    }).catch(() => {
      if (!controller.signal.aborted) setLoaded({ url: listUrl, attempt, failed: true });
    });
    return () => controller.abort();
  }, [listUrl, attempt]);
  const filtered = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("ar");
    const result = items.filter(item => (!status || String(item.isActive) === status) && (!term || [
      item.arabicName, item.englishName, item.branchName, item.sportName, item.categoryName,
      ...(item.coaches ?? []), ...(item.groups ?? []).flatMap(group => [group.name, group.branch]),
    ].some(value => value?.toLocaleLowerCase("ar").includes(term))));
    return kind === "coaches" ? result.sort((a, b) => (
      coachByGroups ? (b.assignedGroups ?? 0) - (a.assignedGroups ?? 0) : (b.parentRating ?? -1) - (a.parentRating ?? -1)
    ) || a.arabicName.localeCompare(b.arabicName, "ar")) : result;
  }, [items, search, kind, coachByGroups, status]);

  async function changeStatus() { if (!target) return; setSaving(true); setError(""); try { await csrfRequest(`/api/v1/manage/structure/${kind}/${target.id}/status`, "PUT", { isActive: !target.isActive }); const response = await fetch(listUrl); if (!response.ok) throw new Error("تعذر تحديث القائمة."); setItems(await response.json() as Item[]); setSuccess(target.isActive ? `تم إيقاف «${target.arabicName}».` : `تم تفعيل «${target.arabicName}».`); setTarget(null); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تغيير الحالة."); } finally { setSaving(false); } }

  const columns: DataColumn<Item>[] = [
    {key:"name",header:kind==="coaches"?"المدرب":"الاسم",primary:true,width:"minmax(0,1.5fr)",render:item=><IdentityMark name={item.arabicName} detail={item.categoryName??item.englishName}/>},
    ...(kind==="groups"||kind==="categories"?[{key:"branch",header:"الفرع",render:(item:Item)=>item.branchName??"—"},{key:"sport",header:"الرياضة",render:(item:Item)=>item.sportName??"—"}]:[]),
    ...(kind==="groups"?[
      {key:"coaches",header:"المدربون",render:(item:Item)=><span>{item.coaches?.join("، ")||"لا يوجد إسناد فعال"}</span>},
      {key:"sessions",header:"مواعيد التدريب",render:(item:Item)=>item.schedules?.length?<div className="cell-stack">{item.schedules.map((s,i)=><span key={i}>{dayNames[s.dayOfWeek]??s.dayOfWeek}<small dir="ltr">{s.startTime.slice(0,5)} — {s.endTime.slice(0,5)}</small></span>)}</div>:"لم تحدد مواعيد"},
      {key:"players",header:"اللاعبون",render:(item:Item)=>item.playerCount??"—"},
    ]:[]),
    ...(kind==="coaches"?[
      {key:"groups",header:"المجموعات المسندة",render:(item:Item)=><div className="cell-stack"><strong>{item.assignedGroups ?? "—"}</strong>{Boolean(item.groups?.length)&&<details><summary>عرض المجموعات والفروع</summary>{item.groups?.map(g=><span className="cell-stack" key={g.id}><span>{g.name}</span><small>{g.branch}</small></span>)}</details>}</div>},
      {key:"rating",header:"تقييم أولياء الأمور",render:(item:Item)=>item.parentRating==null?"لم يُقيّم بعد":<div className="cell-stack"><span>{item.parentRating} ★</span>{item.parentRatingCount != null && <small>عدد التقييمات {item.parentRatingCount}</small>}</div>},
    ]:[]),
    {key:"status",header:"الحالة",render:item=><StatusBadge status={item.isActive?"Active":"Inactive"}/>},
    {key:"actions",header:"الإجراءات",width:kind==="coaches"?"160px":"110px",render:item=><RowActions actions={kind==="coaches"?[
      {label:"عرض المجموعات",href:`/dashboard/academy/coaches/${item.id}`},{label:"إسناد مجموعة",href:`/dashboard/academy/coaches/new?coachId=${item.id}`},
      {label:item.isActive?"إيقاف":"تفعيل",onClick:()=>setTarget(item),tone:item.isActive?"danger":"normal"},
    ]:[{label:"عرض",href:`/dashboard/academy/${kind}/${item.id}`},{label:"تعديل",href:`/dashboard/academy/${kind}/${item.id}/edit`},{label:item.isActive?"إيقاف":"تفعيل",onClick:()=>setTarget(item),tone:item.isActive?"danger":"normal"}]}/>},
  ];
  return <>
    <PageHeader title={config[kind].title} context={`الأكاديمية / ${config[kind].title}`} action={{ label: config[kind].create, href: `/dashboard/academy/${kind}/${kind === "coaches" ? "create" : "new"}` }} />
    <FilterToolbar resultCount={loading || loadFailed ? undefined : filtered.length} onReset={()=>{setSearch("");setStatus("");setCoachByGroups(incoming.get("sort") === "groups");}}>
      <SearchInput value={search} onChange={setSearch} label={kind === "coaches" ? "بحث المدربين" : `بحث ${config[kind].title}`}
        placeholder={kind === "coaches" ? "ابحث بالاسم أو المجموعة أو الفرع" : "ابحث بالاسم أو الفرع أو الرياضة"} />
      <label>الحالة<select value={status} onChange={e=>setStatus(e.target.value)}><option value="">كل الحالات</option><option value="true">فعال</option><option value="false">متوقف</option></select></label>
      {kind==='coaches'&&<label>ترتيب المدربين<select value={coachByGroups?"groups":"rating"} onChange={e=>setCoachByGroups(e.target.value==="groups")}><option value="rating">حسب التقييم</option><option value="groups">حسب المجموعات المسندة</option></select></label>}
    </FilterToolbar>
    <section className="list-card">
      {success && <p className="success-message" role="status">{success}</p>}
      {error && <ErrorState message={error} />}
      {loading ? <LoadingState /> : loadFailed ? <>
        <ErrorState message="تعذر تحميل البيانات." />
        <button className="secondary-button" type="button" onClick={() => setAttempt(attempt + 1)}>إعادة المحاولة</button>
      </> : filtered.length === 0 ? <EmptyState /> : <AdminDataTable label={kind==="coaches"?"قائمة المدربين":`قائمة ${config[kind].title}`} columns={columns} rows={filtered} rowKey={item=>item.id} rowClassName={item=>item.isActive?"":"inactive-record"}/>}
    </section>
    <ConfirmationDialog open={target !== null} title={target?.isActive ? "تأكيد الإيقاف" : "تأكيد التفعيل"} message={`هل تريد ${target?.isActive ? "إيقاف" : "تفعيل"} «${target?.arabicName ?? ""}»؟ لن تُحذف العلاقات التاريخية.`} confirmLabel={target?.isActive ? "إيقاف" : "تفعيل"} busy={saving} onConfirm={() => void changeStatus()} onCancel={() => setTarget(null)} />
  </>;
}
