"use client";
import Link from "next/link";
import {useEffect,useState} from "react";
import {SectionCard} from "../components/dashboard-shell";
type Summary={open:number;high:number;overdue:number;unassigned:number};
export function FollowUpSummary({branchId=""}:{branchId?:string}){
 const [data,setData]=useState<Summary|null>(null),[error,setError]=useState(false);
 useEffect(()=>{const c=new AbortController();fetch(`/api/v1/manage/owner-followups/summary?${new URLSearchParams(branchId?{branchId}:{})}`,{signal:c.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error();const v=await r.json();if(["open","high","overdue","unassigned"].some(k=>typeof v?.[k]!=="number"))throw new Error();return v;}).then(d=>{if(!c.signal.aborted){setData(d);setError(false);}}).catch(()=>{if(!c.signal.aborted){setData(null);setError(true);}});return()=>c.abort();},[branchId]);
 const base=new URLSearchParams({state:"Open",allDates:"true",...branchId?{branchId}:{}});
 return <SectionCard title="المتابعات واتخاذ القرار" action={<Link href={`/dashboard/operations/follow-ups?${base}`}>عرض الكل</Link>}>{error?<p>تعذر تحميل ملخص المتابعات.</p>:!data?<p>جارٍ التحميل…</p>:<div className="followup-summary">{[{label:"مفتوحة",value:data.open,filter:""},{label:"عالية الأولوية",value:data.high,filter:"priority=2"},{label:"متأخرة",value:data.overdue,filter:"overdue=true"},{label:"تحتاج إسنادًا",value:data.unassigned,filter:"unassigned=true"}].map(x=><Link key={x.label} href={`/dashboard/operations/follow-ups?${base}${x.filter?"&"+x.filter:""}`}><strong>{x.value.toLocaleString("ar-EG-u-nu-latn")}</strong><span>{x.label}</span></Link>)}</div>}</SectionCard>;
}
