"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./vip-rewards.module.css";

type Recipient={id:string;name:string;monthlyValue:number};
type Terms={items:Recipient[];currency:string;today:string;conditions:string};
type Reward={id:string;guardianId:string;requestId?:string;currency?:string;guardian:string;percentage:number;message:string;expiresOn:string;issuedOn:string;state:string;read:boolean;delivery:string;history:{sourceKind?:string;sourceId?:string;createdAtUtc:string;usedAtUtc?:string;releasedAtUtc?:string}[]};
type Ledger={items:Reward[];count:number;page:number;conditions:string};
type Preview={currency?:string;quoteHash:string;recipients:Recipient[];percentage:number;message:string;expiresOn:string;conditions:string;delivery:string};
const states:Record<string,string>={Available:"متاحة",Reserved:"محجوزة لطلب",Used:"استخدمت",Expired:"انتهت",Cancelled:"ألغيت"};
const unavailable="تعذر تحميل المكافآت أو ليست لديك صلاحية عرضها.";
function failure(e:unknown,fallback:string){return e instanceof Error&&/[\u0600-\u06ff]/.test(e.message)?e.message:fallback;}
function denied(e:unknown){return e instanceof DashboardRequestError&&[401,403].includes(e.status);}
async function get<T>(url:string):Promise<T>{const r=await fetch(url,{cache:"no-store"});if(!r.ok)throw new DashboardRequestError(unavailable,r.status);return r.json();}
type Scope={check:()=>Promise<void>;read:<T>(url:string)=>Promise<T>;current:()=>boolean;reject:(e:unknown)=>void};
type Identity={userId:string;academyId:string;membershipId:string;role:string};
function identity(me:Identity){return [me.userId,me.academyId,me.membershipId,me.role].join(":");}

export function VipRewards({owner=false,rewardId}:{owner?:boolean;rewardId?:string}){
  return <VipScope key={`${owner}:${rewardId??"list"}`} owner={owner} rewardId={rewardId}/>;
}
function VipScope({owner,rewardId}:{owner:boolean;rewardId?:string}){
  const [ready,setReady]=useState(false),[error,setError]=useState("");
  const live=useRef(true),revoked=useRef(false),signature=useRef<string|null>(null);
  const reject=useCallback((e:unknown)=>{if(live.current&&denied(e)){revoked.current=true;setError(unavailable);}},[]);
  const check=useCallback(async()=>{
    if(!live.current||revoked.current)throw new Error(unavailable);
    try{
      const me=await get<Identity>("/api/v1/me");
      if(!live.current||revoked.current)throw new Error(unavailable);
      if(!me.userId||!me.academyId||!me.membershipId||me.role!==(owner?"AcademyOwner":"Guardian")||(signature.current!==null&&signature.current!==identity(me)))throw new DashboardRequestError(unavailable,403);
      signature.current=identity(me);
    }catch(e){reject(e);throw e;}
  },[owner,reject]);
  const scope=useMemo<Scope>(()=>({check,reject,current:()=>live.current&&!revoked.current,
    read:async<T,>(url:string)=>{try{const data=await get<T>(url);await check();return data;}catch(e){reject(e);throw e;}}
  }),[check,reject]);
  useEffect(()=>{
    live.current=true;
    void check().then(()=>{if(scope.current())setReady(true);}).catch(e=>{if(live.current)setError(failure(e,unavailable));});
    const focus=()=>{void check().catch(()=>{/* A transient focus check must preserve a pending request. Revocation is handled by reject. */});};
    window.addEventListener("focus",focus);
    return()=>{live.current=false;window.removeEventListener("focus",focus);};
  },[check,scope]);
  if(error)return <ErrorState message={error}/>;
  return ready?<VipLedger owner={owner} rewardId={rewardId} scope={scope}/>:<LoadingState/>;
}
function exact(data:Ledger,id:string){if(data.items.length!==1||data.items[0].id!==id)throw new Error("تعذر التحقق من المكافأة المطلوبة.");return data.items[0];}
function VipLedger({owner,rewardId,scope}:{owner:boolean;rewardId?:string;scope:Scope}){
  const root=owner?"/api/v1/manage/vip-rewards":"/api/v1/guardian/vip-rewards";
  const [data,setData]=useState<Ledger|null>(null),[page,setPage]=useState(1),[error,setError]=useState(""),[create,setCreate]=useState(false),[busy,setBusy]=useState(false),[pendingCancel,setPendingCancel]=useState<string|null>(null),[version,setVersion]=useState(0);
  const mutation=useRef(false);
  useEffect(()=>{let active=true;void scope.read<Ledger>(rewardId?`${root}/${encodeURIComponent(rewardId)}`:`${root}?page=${page}`).then(d=>{if(rewardId)exact(d,rewardId);if(active&&scope.current()){setData(d);}}).catch(e=>{if(active&&scope.current())setError(failure(e,unavailable));});return()=>{active=false;};},[scope,root,page,rewardId,version]);
  async function verifyCancel(id:string){const d=await scope.read<Ledger>(`${root}/${encodeURIComponent(id)}`);if(exact(d,id).state!=="Cancelled")throw new Error("لم يتأكد الإلغاء بعد. أعد التحقق من السجل.");if(scope.current()){setPendingCancel(null);setData(null);setVersion(v=>v+1);announceSaveSuccess("تم التحقق من إلغاء المكافأة.");}}
  async function cancel(id:string,retry=false){
    if(mutation.current||(!retry&&!window.confirm("إلغاء هذه المكافأة؟ لا يمس الإلغاء أي اشتراك مدفوع.")))return;
    mutation.current=true;setBusy(true);setError("");let accepted=false;
    try{await scope.check();if(!retry){setPendingCancel(id);await csrfRequest(`${root}/${encodeURIComponent(id)}/cancel`,"POST",{},{},{announce:false});accepted=true;}await verifyCancel(id);}
    catch(e){scope.reject(e);if(scope.current()){if(!retry&&!accepted&&e instanceof DashboardRequestError&&e.status>=400&&e.status<500){setPendingCancel(null);setData(null);setVersion(v=>v+1);}setError(failure(e,"تعذر التحقق من الإلغاء. أعد التحقق دون إرسال طلب جديد."));}}
    finally{mutation.current=false;if(scope.current())setBusy(false);}
  }
  return <div className={styles.page}><PageHeader title="مكافآت الأعضاء المميزين" context={owner?"الأعضاء والاشتراكات":"مكافآت الأسرة"}/>{!owner&&<Link className="back-link" href="/guardian">← العودة</Link>}
    {error&&<ErrorState message={error}/>}<p>استحقاق محفوظ يظهر في سعر الباقة إذا كان أفضل من العرض الحالي، وليس مبلغًا نقديًا.</p>
    {pendingCancel&&<button className="secondary-button" disabled={busy} onClick={()=>void cancel(pendingCancel,true)}>إعادة التحقق من الإلغاء</button>}{owner&&!rewardId&&!create&&<button className="primary-button" disabled={busy||!!pendingCancel} onClick={()=>setCreate(true)}>إرسال مكافأة</button>}
    {create&&<VipRewardIssue scope={scope} onClose={()=>{if(scope.current()){setCreate(false);setData(null);setVersion(v=>v+1);}}}/>}
    {data===null&&!error?<LoadingState/>:data&&<SectionCard title={owner?"الإصدار والتسليم والاستخدام":"استحقاقات أسرتك"}><p className="muted">{data.conditions}</p>{data.items.length===0?<EmptyState message="لا توجد مكافآت مسجلة بعد."/>:data.items.map(r=><article className={styles.rewardCard} key={r.id}><div><strong>{owner?r.guardian:"مكافأة خاصة لأسرتك"} · <bdi>{r.percentage}%</bdi></strong><p>{r.message}</p><small>{states[r.state]??"غير متاحة"} · تنتهي {formatDateAr(r.expiresOn)} · صدرت {formatDateAr(r.issuedOn)}</small><small>{r.delivery==="Disabled"?"التنبيه معطل؛ المكافأة محفوظة حسب شروطها.":`إشعار داخلي محفوظ · ${r.read?"قرأته الأسرة":"لم يقرأ بعد"}`}</small>{r.history.map((h,i)=><small key={i}>حجز {formatDateAr(h.createdAtUtc)}{h.usedAtUtc?` · استخدمت ${formatDateAr(h.usedAtUtc)}`:h.releasedAtUtc?" · حرر الحجز بعد إغلاق الطلب غير المدفوع":""}{owner&&h.sourceId&&<Link href={h.sourceKind==="Family"?`/dashboard/family-renewals/${h.sourceId}`:h.sourceKind==="Enrollment"?`/dashboard/enrollment-requests/${h.sourceId}`:`/dashboard/subscriptions/renewals/${h.sourceId}`}>فتح الطلب المرتبط</Link>}</small>)}</div>{owner&&["Available","Expired"].includes(r.state)&&<button className="secondary-button" disabled={busy||!!pendingCancel} onClick={()=>void cancel(r.id)}>إلغاء المكافأة</button>}{!owner&&r.state==="Available"&&<Link className="primary-button" href="/guardian/renew-family">اختيار باقة التجديد</Link>}</article>)}{!rewardId&&<nav aria-label="صفحات المكافآت" className="row-actions"><button disabled={page===1} onClick={()=>{setData(null);setError("");setPage(p=>p-1);}}>السابق</button><span>{page}</span><button disabled={page*25>=data.count} onClick={()=>{setData(null);setError("");setPage(p=>p+1);}}>التالي</button></nav>}</SectionCard>}
  </div>;
}

type Intent={requestId:string;guardianIds:readonly string[];percentage:number;message:string;expiresOn:string;quoteHash:string};
function validIds(value:unknown,count:number):value is string[]{return Array.isArray(value)&&value.length===count&&value.every(x=>typeof x==="string"&&x.length>0)&&new Set(value).size===count;}
function VipRewardIssue({onClose,scope}:{onClose:()=>void;scope:Scope}){
  const [options,setOptions]=useState<Terms|null>(null),[selected,setSelected]=useState<string[]>([]),[percentage,setPercentage]=useState(""),[message,setMessage]=useState("شكرًا لثقتكم بأكاديميتنا. يسعدنا تقديم مكافأة خاصة لدعم رحلة أبنائكم الرياضية."),[expires,setExpires]=useState(""),[preview,setPreview]=useState<Preview|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false),[intent,setIntent]=useState<Intent|null>(null),[missing,setMissing]=useState(false);
  const [requestId]=useState(()=>crypto.randomUUID());
  const mutation=useRef(false),issuedIds=useRef<string[]|null>(null),live=useRef(true);
  const current=()=>live.current&&scope.current();
  useEffect(()=>{live.current=true;void scope.read<Terms>("/api/v1/manage/vip-rewards/options").then(d=>{if(live.current&&scope.current()){setOptions(d);setExpires(d.today);}}).catch(e=>{if(live.current&&scope.current())setError(failure(e,unavailable));});return()=>{live.current=false;};},[scope]);
  function changed(){setPreview(null);setError("");}
  const body={requestId,guardianIds:[...selected].sort(),percentage:Number(percentage),message:message.trim(),expiresOn:expires};
  async function submit(e:FormEvent){e.preventDefault();if(mutation.current||intent)return;mutation.current=true;setBusy(true);setError("");try{await scope.check();const r=await csrfRequest("/api/v1/manage/vip-rewards/preview","POST",body,{},{announce:false});const p:Preview=await r.json();if(p.percentage!==body.percentage||p.message!==body.message||p.expiresOn!==body.expiresOn||p.recipients.map(g=>g.id).sort().join(":")!==body.guardianIds.join(":"))throw new Error("تغيرت بيانات المعاينة. أعد المعاينة قبل الإصدار.");await scope.check();if(current())setPreview(p);}catch(e){scope.reject(e);if(current())setError(failure(e,"تعذرت المعاينة."));}finally{mutation.current=false;if(current())setBusy(false);}}
  async function verify(saved:Intent,ids:string[]){
    const records=await Promise.all(ids.map(async id=>exact(await scope.read<Ledger>(`/api/v1/manage/vip-rewards/${encodeURIComponent(id)}`),id)));
    if(records.some(r=>r.requestId!==saved.requestId||r.percentage!==saved.percentage||r.message!==saved.message||r.expiresOn!==saved.expiresOn||(preview?.currency&&r.currency!==preview.currency))||records.map(r=>r.guardianId).sort().join(":")!==[...saved.guardianIds].sort().join(":"))throw new Error("تعذر مطابقة المكافأة المحفوظة مع الطلب. أعد التحقق دون إصدار جديد.");
    if(current()){announceSaveSuccess("تم التحقق من حفظ المكافأة.");onClose();}
  }
  async function issue(recover=false){
    if(mutation.current||!preview)return;
    const prior=intent,saved=prior??Object.freeze({...body,guardianIds:Object.freeze([...body.guardianIds]),quoteHash:preview.quoteHash});
    mutation.current=true;setIntent(saved);setBusy(true);setError("");setMissing(false);let accepted=false;
    try{
      await scope.check();let ids=issuedIds.current;
      if(!recover){const r=await csrfRequest("/api/v1/manage/vip-rewards/","POST",saved,{},{announce:false});accepted=true;const result=await r.json();ids=validIds(result.ids,saved.guardianIds.length)?result.ids:null;issuedIds.current=ids;}
      if(!ids){const result=await scope.read<{ids:unknown}>(`/api/v1/manage/vip-rewards/requests/${encodeURIComponent(saved.requestId)}`);if(!validIds(result.ids,saved.guardianIds.length))throw new Error("تعذر التحقق من كل المكافآت المحفوظة.");ids=result.ids;issuedIds.current=ids;}
      await verify(saved,ids);
    }catch(e){scope.reject(e);if(current()){
      if(recover&&!issuedIds.current&&e instanceof DashboardRequestError&&e.status===404)setMissing(true);
      if(!recover&&!prior&&!accepted&&e instanceof DashboardRequestError&&e.status>=400&&e.status<500){setIntent(null);setPreview(null);}
      setError(failure(e,"تعذر التحقق من الإصدار. أعد التحقق من الطلب المحفوظ دون إصدار جديد."));
    }}finally{mutation.current=false;if(current())setBusy(false);}
  }
  const locked=busy||!!intent;
  return <SectionCard title="إصدار استحقاق خصم"><button className="back-link" disabled={locked} onClick={onClose}>← العودة للسجل</button>{error&&<ErrorState message={error}/>}<p>اختر من أعلى10أسر المؤهلة اليوم. المعاينة لا تصدر مكافأة ولا ترسل إشعارًا.</p>{!options?<LoadingState/>:<form onSubmit={submit} className="form-grid"><fieldset disabled={locked}><legend>أولياء الأمور المستحقون</legend>{options.items.map(g=><label className="placement-choice" key={g.id}><input type="checkbox" checked={selected.includes(g.id)} onChange={e=>{changed();setSelected(s=>e.target.checked?[...s,g.id]:s.filter(x=>x!==g.id));}}/><span>{g.name} · {formatMoneyAr(g.monthlyValue,options.currency)} شهريًا</span></label>)}{!options.items.length&&<p>لا توجد أسر ذات قيمة اشتراك مكتملة وسارية.</p>}</fieldset><label>نسبة الخصم %<input disabled={locked} required type="number" min="0.01" max="99.99" step="0.01" value={percentage} onChange={e=>{changed();setPercentage(e.target.value);}}/></label><label>صالحة حتى<input disabled={locked} required type="date" min={options.today} value={expires} onChange={e=>{changed();setExpires(e.target.value);}}/></label><label>رسالة الأسرة<textarea disabled={locked} required maxLength={500} value={message} onChange={e=>{changed();setMessage(e.target.value);}}/></label><p>{options.conditions}</p><button className="secondary-button" disabled={locked||!selected.length}>معاينة المكافأة</button></form>}
    {preview&&<section role="region" aria-label="تأكيد المكافأة"><h3>تأكيد الإصدار</h3><p>{preview.recipients.map(g=>g.name).join("، ")} · {preview.percentage}% · حتى {formatDateAr(preview.expiresOn)}</p><p>{preview.message}</p><p>{preview.conditions}</p><p>{preview.delivery}</p>{!intent&&<button className="primary-button" disabled={busy} onClick={()=>void issue()}>تأكيد الإصدار وحفظ الإشعار الداخلي</button>}{intent&&<button className="secondary-button" disabled={busy} onClick={()=>void issue(true)}>إعادة التحقق من الإصدار</button>}{intent&&missing&&<button className="primary-button" disabled={busy} onClick={()=>void issue()}>إعادة تأكيد الطلب نفسه</button>}</section>}
  </SectionCard>;
}
