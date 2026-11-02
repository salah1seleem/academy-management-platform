"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { SectionCard } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
type Vip={id:string;displayName:string;monthlySubscriptionValue:number;currency:string};
export function VipMembers({branchId="",asOf=""}:{branchId?:string;asOf?:string}){
  const [items,setItems]=useState<Vip[]|null>(null),[error,setError]=useState(false);
  useEffect(()=>{let c:AbortController;const load=()=>{c?.abort();c=new AbortController();setItems(null);if(document.hidden)return;const signal=c.signal;const q=new URLSearchParams();if(branchId)q.set("branchId",branchId);if(asOf)q.set("asOf",asOf);fetch(`/api/v1/reports/vip-members?${q}`,{cache:"no-store",signal}).then(async r=>{if(!r.ok)throw new Error();return r.json();}).then(d=>{if(!signal.aborted){setItems(d.items??[]);setError(false);}}).catch(()=>{if(!signal.aborted)setError(true);});};load();window.addEventListener("focus",load);document.addEventListener("visibilitychange",load);return()=>{c?.abort();window.removeEventListener("focus",load);document.removeEventListener("visibilitychange",load);};},[branchId,asOf]);
  const q=new URLSearchParams({sort:"value_desc"});if(branchId)q.set("branchId",branchId);if(asOf)q.set("asOf",asOf);
return <SectionCard title="أعضاء مميزون" action={<Link href={`/dashboard/guardians?${q}`}>عرض الكل</Link>}><small>أعلى قيمة شهرية مرتبطة بالأبناء، لا ترتيب المدفوعات.</small><Link className="secondary-button" href="/dashboard/guardians/rewards">إرسال مكافأة</Link>{error?<p role="status">تعذر تحميل الأعضاء المميزين.</p>:items===null?<p>جارٍ التحميل…</p>:items.length===0?<p>لا توجد اشتراكات سارية ذات قيمة شهرية مكتملة.</p>:<ol className="vip-member-list">{items.map(g=><li key={g.id}><Link href={`/dashboard/guardians/${g.id}`}>{g.displayName}</Link><strong>{formatMoneyAr(g.monthlySubscriptionValue,g.currency)}</strong></li>)}</ol>}</SectionCard>;
}
