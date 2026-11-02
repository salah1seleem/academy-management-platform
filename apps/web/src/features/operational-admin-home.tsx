"use client";
import Link from "next/link";
import {UserPlus,RefreshCcw,ClipboardCheck,Users,CalendarDays} from "lucide-react";
import { useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { SubscriptionHome } from "./subscription-home";
import { formatSessionDateAr, formatTimeAr } from "../lib/formatters";
type Access={useActionGrants:boolean;actions?:{module:string;read:boolean;add:boolean;edit:boolean}[];workflowActions?:{key:string;allowed:boolean}[]};
export function OperationalAdminHome({logoUrl,primaryColor}:{logoUrl?:string|null;primaryColor?:string}={}){
 const [meta,setMeta]=useState<{permissions:string[];access:Access}>();const [error,setError]=useState(false);const [revision,setRevision]=useState(0);
 useEffect(()=>{const abort=new AbortController();const get=async(path:string)=>{const r=await fetch(path,{signal:abort.signal,cache:"no-store"});if(!r.ok)throw Error();return r.json();};Promise.all([get("/api/v1/my-permissions"),get("/api/v1/my-access")]).then(([permissions,access])=>{if(!abort.signal.aborted){setMeta({permissions,access});setError(false);}}).catch(()=>{if(!abort.signal.aborted)setError(true);});return()=>abort.abort();},[revision]);
 if(error)return <><ErrorState message="تعذر تحميل صلاحيات يوم الأكاديمية."/><button onClick={()=>setRevision(v=>v+1)}>إعادة المحاولة</button></>;if(!meta)return <LoadingState/>;
 const allowed=(permission:string,module:string,operation:"read"|"add"|"edit"|"write"="read",workflow?:string)=>{if(!meta.permissions.includes(permission))return false;if(!meta.access.useActionGrants)return true;const g=meta.access.actions?.find(g=>g.module===module);return g?.read===true&&(operation==="read"||operation==="write"?operation==="read"||g.add&&g.edit:g[operation])&&(!workflow||meta.access.workflowActions?.some(w=>w.key===workflow&&w.allowed)===true);};
 const actions=[
  {icon:UserPlus,label:"تسجيل لاعب",href:"/dashboard/players/new",show:allowed("people.manage","players","add","workflow.players.create")&&allowed("payment.read","payments","write","workflow.payments.collect")},
  {icon:RefreshCcw,label:"تجديد اشتراك",href:"/dashboard/subscriptions/renew/new",show:allowed("subscription.renew.manage","subscriptions","write","workflow.subscriptions.renew")},
  {icon:ClipboardCheck,label:"تسجيل الحضور",href:"/dashboard/attendance/sessions",show:allowed("attendance.read","attendance")},
  {icon:Users,label:"تسكين اللاعبين",href:"/dashboard/academy/groups",show:allowed("people.manage","players","edit","workflow.players.place")&&allowed("structure.manage","academy")},
  {icon:CalendarDays,label:"الحصص",href:"/dashboard/attendance/sessions",show:allowed("attendance.read","attendance")},
 ];
 return <main className="page-shell" dir="rtl"><h1>يوم الأكاديمية</h1><nav className="sports-quick-actions" aria-label="إجراءات نطاقك">{actions.filter(a=>a.show).map(({icon:Icon,...a})=><Link key={a.label} href={a.href}><span className="quick-action-icon"><Icon aria-hidden="true"/></span><span><b>{a.label}</b></span></Link>)}</nav>
 {allowed("subscription.read","subscriptions")&&<SubscriptionHome owner={false} logoUrl={logoUrl} primaryColor={primaryColor}/>}
 {allowed("attendance.read","attendance")&&<AdminSessions/>}
 {allowed("payment.read","payments")&&<AdminPendingPayments/>}
 {allowed("followups.read","followups")&&<AdminFollowups/>}
 </main>;
}
function useSource<T>(path:string){const [data,setData]=useState<T>();const[error,setError]=useState(false);const[revision,setRevision]=useState(0);useEffect(()=>{const c=new AbortController();fetch(path,{signal:c.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw Error();return r.json() as Promise<T>;}).then(v=>{if(!c.signal.aborted){setData(v);setError(false);}}).catch(()=>{if(!c.signal.aborted)setError(true);});return()=>c.abort();},[path,revision]);return{data,error,retry:()=>setRevision(v=>v+1)};}
function AdminSessions(){const s=useSource<{id:string;group:string;sessionDate:string;startTime:string;endTime:string;status:string}[]>("/api/v1/attendance/sessions");return <section className="detail-card"><h2>حصص اليوم</h2>{s.error?<><ErrorState message="تعذر تحميل حصص اليوم."/><button onClick={s.retry}>إعادة تحميل الحصص</button></>:!s.data?<LoadingState/>:s.data.filter(x=>x.status!=="Cancelled").length?s.data.filter(x=>x.status!=="Cancelled").map(x=><article className="detail-card" key={x.id}><h3>{x.group}</h3><p>{formatSessionDateAr(x.sessionDate)} · {formatTimeAr(x.startTime)}–{formatTimeAr(x.endTime)}</p><Link href={`/dashboard/attendance/sessions/${x.id}/players`}>عرض الحصة</Link></article>):<p>لا توجد حصص في اليوم.</p>}</section>;}
function AdminPendingPayments(){const s=useSource<{pending:number}>("/api/v1/manage/payment-requests?status=Pending&allDates=true");return <section className="detail-card"><h2>طلبات الدفع المعلقة</h2>{s.error?<><ErrorState message="تعذر تحميل طلبات الدفع."/><button onClick={s.retry}>إعادة تحميل الطلبات</button></>:!s.data?<LoadingState/>:<><p>{s.data.pending} طلب معلق، بما فيه الأقدم من اليوم</p><Link href="/dashboard/subscriptions/payments?status=Pending&allDates=true">مراجعة الطلبات</Link></>}</section>;}
function AdminFollowups(){const s=useSource<{items:{id:string;title?:string;sourceLabel?:string}[]}>("/api/v1/my-followups?state=Open&allDates=true");return <section className="detail-card"><h2>متابعات مسندة إليّ</h2>{s.error?<><ErrorState message="تعذر تحميل المتابعات."/><button onClick={s.retry}>إعادة تحميل المتابعات</button></>:!s.data?<LoadingState/>:<><p>{s.data.items.length} متابعة معروضة</p><Link href="/dashboard/my-followups">عرض المتابعات المسندة</Link></>}</section>;}
