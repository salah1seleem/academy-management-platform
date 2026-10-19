"use client";

import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, RowActions, SearchInput } from "../../../components/dashboard-shell";
import { formatMoneyAr } from "../../../lib/formatters";
type Guardian = { id: string; displayName: string; contactPhone: string; linkedChildren?: number; monthlySubscriptionValue?: number; unpricedSubscriptions?: number; currency?: string };
type Options = {branches:{id:string;arabicName:string}[];sports:{id:string;arabicName:string}[]};

export default function GuardiansPage() {
  const incoming = useSearchParams();
  const [items, setItems] = useState<Guardian[]>([]), [search, setSearch] = useState(""), [loading, setLoading] = useState(true), [error, setError] = useState("");
  const [sort, setSort] = useState(incoming?.get("sort") ?? "name"), [page, setPage] = useState(0), [pageSize, setPageSize] = useState(25), [total, setTotal] = useState(0);
  const [branch, setBranch] = useState(incoming?.get("branchId") ?? ""), [sport, setSport] = useState(incoming?.get("sportId") ?? ""), [asOf, setAsOf] = useState(incoming?.get("asOf") ?? "");
  const [options, setOptions] = useState<Options>({branches:[],sports:[]});
  useEffect(() => { const c = new AbortController(); fetch("/api/v1/manage/structure/options", {signal:c.signal,cache:"no-store"}).then(async r=>r.ok?await r.json() as Partial<Options>:{} as Partial<Options>).then(x=>{if(!c.signal.aborted)setOptions({branches:x.branches??[],sports:x.sports??[]});}).catch(()=>{});return()=>c.abort(); },[]);
  useEffect(() => {
    const c = new AbortController(); const timer=window.setTimeout(()=>{
      setLoading(true); const q=new URLSearchParams({search,sort,page:String(page+1),pageSize:String(pageSize)});if(branch)q.set("branchId",branch);if(sport)q.set("sportId",sport);if(asOf)q.set("asOf",asOf);
      fetch(`/api/v1/people/guardians?${q}`,{signal:c.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error();return r.json() as Promise<Guardian[]|{items:Guardian[];total:number}>;}).then(d=>{if(!c.signal.aborted){setItems(Array.isArray(d)?d:d.items);setTotal(Array.isArray(d)?d.length:d.total);setError("");}}).catch(()=>{if(!c.signal.aborted)setError("تعذر تحميل أولياء الأمور.");}).finally(()=>{if(!c.signal.aborted)setLoading(false);});
    },150);return()=>{window.clearTimeout(timer);c.abort();};
  },[search,sort,page,pageSize,branch,sport,asOf]);
  function reset(){setSearch("");setBranch("");setSport("");setAsOf("");setSort("name");setPage(0);}
  return <><PageHeader title="أولياء الأمور" context="الأعضاء والاشتراكات"/><FilterToolbar resultCount={total} onReset={reset}>
    <SearchInput label="بحث أولياء الأمور" placeholder="الاسم أو رقم الهاتف" value={search} onChange={v=>{setSearch(v);setPage(0);}}/>
    <select aria-label="ترتيب أولياء الأمور" value={sort} onChange={e=>{setSort(e.target.value);setPage(0);}}><option value="name">بالاسم</option><option value="value_desc">إجمالي الاشتراكات: الأعلى أولًا</option><option value="value_asc">إجمالي الاشتراكات: الأقل أولًا</option></select>
    {!!options.branches.length&&<select aria-label="فلتر الفرع" value={branch} onChange={e=>{setBranch(e.target.value);setPage(0);}}><option value="">كل الفروع</option>{options.branches.map(o=><option key={o.id} value={o.id}>{o.arabicName}</option>)}</select>}
    {!!options.sports.length&&<select aria-label="فلتر الرياضة" value={sport} onChange={e=>{setSport(e.target.value);setPage(0);}}><option value="">كل الرياضات</option>{options.sports.map(o=><option key={o.id} value={o.id}>{o.arabicName}</option>)}</select>}
    <label>قيمة الاشتراك في<input type="date" value={asOf} onChange={e=>{setAsOf(e.target.value);setPage(0);}}/></label>
  </FilterToolbar><p className="muted">القيمة الشهرية لاشتراكات الأبناء المرتبطين، وليست سجل مدفوعات ولي الأمر.</p>{error&&<ErrorState message={error}/>}
    <section className="list-card">{loading?<LoadingState/>:items.length===0?<EmptyState/>:<AdminDataTable label="قائمة أولياء الأمور" rows={items} rowKey={g=>g.id} serverPagination={{page,pageSize,total,onPageChange:setPage,onPageSizeChange:size=>{setPageSize(size);setPage(0);}}} columns={[
      {key:"name",header:"ولي الأمر",primary:true,render:g=><strong>{g.displayName}</strong>},
      {key:"phone",header:"رقم الهاتف",render:g=><bdi>{g.contactPhone}</bdi>},
      {key:"children",header:"عدد الأبناء",render:g=>g.linkedChildren??"—"},
      {key:"value",header:"إجمالي الاشتراكات",sortDirection:sort==="value_desc"?"descending":sort==="value_asc"?"ascending":"none",onSort:()=>{setSort(sort==="value_desc"?"value_asc":"value_desc");setPage(0);},render:g=><div><strong>{g.monthlySubscriptionValue==null?"لم تحسب بعد":formatMoneyAr(g.monthlySubscriptionValue,g.currency)}</strong><small>شهريًا{g.unpricedSubscriptions?" · توجد باقات بلا مكافئ شهري":""}</small></div>},
      {key:"actions",header:"الإجراءات",render:g=><RowActions actions={[{label:"عرض الأبناء",href:`/dashboard/guardians/${g.id}`},{label:"تعديل البيانات",href:`/dashboard/guardians/${g.id}/edit`}]}/>}
    ]}/>}</section></>;
}
