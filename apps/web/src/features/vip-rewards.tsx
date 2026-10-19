"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";
import styles from "./vip-rewards.module.css";

type Recipient={id:string;name:string;monthlyValue:number};
type Terms={items:Recipient[];currency:string;today:string;conditions:string};
type Reward={id:string;guardian:string;percentage:number;message:string;expiresOn:string;issuedOn:string;state:string;read:boolean;delivery:string;history:{sourceKind?:string;sourceId?:string;createdAtUtc:string;usedAtUtc?:string;releasedAtUtc?:string}[]};
type Ledger={items:Reward[];count:number;page:number;conditions:string};
type Preview={quoteHash:string;recipients:Recipient[];percentage:number;message:string;expiresOn:string;conditions:string;delivery:string};
const states:Record<string,string>={Available:"متاحة",Reserved:"محجوزة لطلب",Used:"استخدمت",Expired:"انتهت",Cancelled:"ألغيت"};
async function get<T>(url:string):Promise<T>{const r=await fetch(url,{cache:"no-store"});if(!r.ok)throw new Error("تعذر تحميل المكافآت أو ليست لديك صلاحية عرضها.");return r.json();}

export function VipRewards({owner=false}:{owner?:boolean}){
  const root=owner?"/api/v1/manage/vip-rewards":"/api/v1/guardian/vip-rewards";
  const [data,setData]=useState<Ledger|null>(null),[page,setPage]=useState(1),[error,setError]=useState(""),[create,setCreate]=useState(false),[busy,setBusy]=useState(false);
  const load=useCallback(()=>get<Ledger>(`${root}?page=${page}`).then(d=>{setData(d);setError("");}).catch(e=>setError(e.message)),[root,page]);
  useEffect(()=>{void load();},[load]);
  async function cancel(id:string){if(!window.confirm("إلغاء هذه المكافأة؟ لا يمس الإلغاء أي اشتراك مدفوع."))return;setBusy(true);try{await csrfRequest(`${root}/${id}/cancel`,"POST",{});await load();}catch(e){setError(e instanceof Error?e.message:"تعذر الإلغاء.");}finally{setBusy(false);}}
  return <div className={styles.page}><PageHeader title="مكافآت الأعضاء المميزين" context={owner?"الأعضاء والاشتراكات":"مكافآت الأسرة"}/><Link className="back-link" href={owner?"/dashboard":"/guardian"}>← العودة</Link>
    {error&&<ErrorState message={error}/>}<p>استحقاق محفوظ يظهر في سعر الباقة إذا كان أفضل من العرض الحالي، وليس مبلغًا نقديًا.</p>
    {owner&&!create&&<button className="primary-button" onClick={()=>setCreate(true)}>إرسال مكافأة</button>}
    {create&&<VipRewardIssue onClose={()=>{setCreate(false);void load();}}/>}
    {data===null&&!error?<LoadingState/>:data&&<SectionCard title={owner?"الإصدار والتسليم والاستخدام":"استحقاقات أسرتك"}><p className="muted">{data.conditions}</p>{data.items.length===0?<EmptyState message="لا توجد مكافآت مسجلة بعد."/>:data.items.map(r=><article className={styles.rewardCard} key={r.id}><div><strong>{owner?r.guardian:"مكافأة خاصة لأسرتك"} · <bdi>{r.percentage}%</bdi></strong><p>{r.message}</p><small>{states[r.state]??"غير متاحة"} · تنتهي {formatDateAr(r.expiresOn)} · صدرت {formatDateAr(r.issuedOn)}</small><small>إشعار داخلي محفوظ · {r.read?"قرأته الأسرة":"لم يقرأ بعد"}</small>{r.history.map((h,i)=><small key={i}>حجز {formatDateAr(h.createdAtUtc)}{h.usedAtUtc?` · استخدمت ${formatDateAr(h.usedAtUtc)}`:h.releasedAtUtc?" · حرر الحجز بعد إغلاق الطلب غير المدفوع":""}{owner&&h.sourceId&&<Link href={h.sourceKind==="Family"?`/dashboard/family-renewals/${h.sourceId}`:h.sourceKind==="Enrollment"?`/dashboard/enrollment-requests/${h.sourceId}`:`/dashboard/subscriptions/renewals/${h.sourceId}`}>فتح الطلب المرتبط</Link>}</small>)}</div>{owner&&["Available","Expired"].includes(r.state)&&<button className="secondary-button" disabled={busy} onClick={()=>void cancel(r.id)}>إلغاء المكافأة</button>}{!owner&&r.state==="Available"&&<Link className="primary-button" href="/guardian/renew-family">اختيار باقة التجديد</Link>}</article>)}<nav aria-label="صفحات المكافآت" className="row-actions"><button disabled={page===1} onClick={()=>setPage(p=>p-1)}>السابق</button><span>{page}</span><button disabled={page*25>=data.count} onClick={()=>setPage(p=>p+1)}>التالي</button></nav></SectionCard>}
  </div>;
}

function VipRewardIssue({onClose}:{onClose:()=>void}){
  const [options,setOptions]=useState<Terms|null>(null),[selected,setSelected]=useState<string[]>([]),[percentage,setPercentage]=useState(""),[message,setMessage]=useState("شكرًا لثقتكم بأكاديميتنا. يسعدنا تقديم مكافأة خاصة لدعم رحلة أبنائكم الرياضية."),[expires,setExpires]=useState(""),[preview,setPreview]=useState<Preview|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false);
  const [requestId]=useState(()=>crypto.randomUUID());
  useEffect(()=>{get<Terms>("/api/v1/manage/vip-rewards/options").then(d=>{setOptions(d);setExpires(d.today);}).catch(e=>setError(e.message));},[]);
  function changed(){setPreview(null);setError("");}
  const body={requestId,guardianIds:selected,percentage:Number(percentage),message,expiresOn:expires};
  async function submit(e:FormEvent){e.preventDefault();setBusy(true);setError("");try{const r=await csrfRequest("/api/v1/manage/vip-rewards/preview","POST",body);setPreview(await r.json());}catch(e){setError(e instanceof Error?e.message:"تعذرت المعاينة.");}finally{setBusy(false);}}
  async function issue(){if(!preview)return;setBusy(true);setError("");try{await csrfRequest("/api/v1/manage/vip-rewards/","POST",{...body,quoteHash:preview.quoteHash});onClose();}catch(e){setError(e instanceof Error?e.message:"تعذر الإصدار. أعد المحاولة بنفس البيانات دون تكرار الاستحقاق.");}finally{setBusy(false);}}
  return <SectionCard title="إصدار استحقاق خصم"><button className="back-link" disabled={busy} onClick={onClose}>← العودة للسجل</button>{error&&<ErrorState message={error}/>}<p>اختر من أعلى10أسر المؤهلة اليوم. المعاينة لا تصدر مكافأة ولا ترسل إشعارًا.</p>{!options?<LoadingState/>:<form onSubmit={submit} className="form-grid"><fieldset><legend>أولياء الأمور المستحقون</legend>{options.items.map(g=><label className="placement-choice" key={g.id}><input type="checkbox" checked={selected.includes(g.id)} onChange={e=>{changed();setSelected(s=>e.target.checked?[...s,g.id]:s.filter(x=>x!==g.id));}}/><span>{g.name} · {formatMoneyAr(g.monthlyValue,options.currency)} شهريًا</span></label>)}{!options.items.length&&<p>لا توجد أسر ذات قيمة اشتراك مكتملة وسارية.</p>}</fieldset><label>نسبة الخصم %<input required type="number" min="0.01" max="99.99" step="0.01" value={percentage} onChange={e=>{changed();setPercentage(e.target.value);}}/></label><label>صالحة حتى<input required type="date" min={options.today} value={expires} onChange={e=>{changed();setExpires(e.target.value);}}/></label><label>رسالة الأسرة<textarea required maxLength={500} value={message} onChange={e=>{changed();setMessage(e.target.value);}}/></label><p>{options.conditions}</p><button className="secondary-button" disabled={busy||!selected.length}>معاينة المكافأة</button></form>}
    {preview&&<section role="region" aria-label="تأكيد المكافأة"><h3>تأكيد الإصدار</h3><p>{preview.recipients.map(g=>g.name).join("، ")} · {preview.percentage}% · حتى {formatDateAr(preview.expiresOn)}</p><p>{preview.message}</p><p>{preview.conditions}</p><p>{preview.delivery}</p><button className="primary-button" disabled={busy} onClick={()=>void issue()}>تأكيد الإصدار وحفظ الإشعار الداخلي</button></section>}
  </SectionCard>;
}
