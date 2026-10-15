"use client";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ConfirmationDialog, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";

type Version={id:string;kind:string;revision:number;effectiveFrom:string;monthlySalary:number;approvedBy:string;note?:string};
type Data={id:string;name:string;currency:string;currentSalary:number;hasVerifiedHistory:boolean;revision:number;canChange:boolean;currentMonth:string;currentMonthPaid:boolean;items:Version[]};
const kind:Record<string,string>={Initial:"الراتب الأول المعتمد",LegacyBaseline:"مرجع قائم من شهر المراجعة",Change:"تغيير راتب معتمد"};
export function SalaryHistory({id}:{id:string}){
  const path=`/api/v1/manage/employees/${id}/salary-versions`;
  const [data,setData]=useState<Data|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false),[message,setMessage]=useState("");
  const [pending,setPending]=useState<{effectiveFrom:string;monthlySalary:number;expectedRevision:number;expectedCurrentSalary:number;note:string}|null>(null);const keys=useRef(new Map<string,string>());
  const load=useCallback(async(signal?:AbortSignal)=>{const r=await fetch(path,{cache:"no-store",signal});if(!r.ok)throw new Error();const value=await r.json();if(!signal?.aborted)setData(value);},[path]);
  useEffect(()=>{const controller=new AbortController();void Promise.resolve().then(()=>load(controller.signal)).catch(()=>{if(!controller.signal.aborted)setError("تعذر تحميل سجل الراتب أو لا توجد صلاحية.");});return()=>controller.abort();},[load]);
  function review(e:FormEvent<HTMLFormElement>){e.preventDefault();if(!data)return;const f=new FormData(e.currentTarget);setPending({effectiveFrom:`${f.get("month")}-01`,monthlySalary:Number(f.get("amount")),expectedRevision:data.revision,expectedCurrentSalary:data.currentSalary,note:String(f.get("note")??"")});setMessage("");setError("");}
  async function save(){if(!pending||busy)return;setBusy(true);const signature=JSON.stringify(pending);if(!keys.current.has(signature))keys.current.set(signature,crypto.randomUUID());
    try{await csrfRequest(path,"POST",pending,{"Idempotency-Key":keys.current.get(signature)!});await load();setPending(null);setMessage("تم اعتماد التغيير المؤرخ دون تعديل راتب مسدد أو إنشاء صرف.");}
    catch(e){setPending(null);setError(e instanceof DashboardRequestError?e.message:"تعذر تأكيد الحفظ. أعد المراجعة؛ لا تكرر تغييرًا مختلفًا قبل التحقق.");}finally{setBusy(false);}}
  const first=data?.currentMonth??"";const date=first?new Date(`${first}T12:00:00Z`):null;if(date&&data?.currentMonthPaid)date.setUTCMonth(date.getUTCMonth()+1);
  const openMonth=date?date.toISOString().slice(0,7):"";const latestMonth=data?.items[0]?.effectiveFrom.slice(0,7)??"";const next=latestMonth>openMonth?latestMonth:openMonth;
  return <><PageHeader title="سجل الراتب وتغييراته" context={data?.name??"فريق الأكاديمية"} secondaryActions={[{label:"الموظفون",href:"/dashboard/staff/employees"},{label:"كشف الرواتب",href:`/dashboard/payroll?employeeId=${id}`}]} />{error&&<ErrorState message={error}/>} {!data&&!error?<LoadingState/>:data&&<>
    <section className="detail-card salary-history-summary"><div><small>الراتب الشهري الحالي — ليس سدادًا</small><h2>{formatMoneyAr(data.currentSalary,data.currency)}</h2></div><div><p>كل تغيير له شهر سريان ومصدر اعتماد محفوظ. لا تغيير لشهر سابق أو مسدد، ولا تعديل تلقائي لنسبة الخاص.</p>{!data.hasVerifiedHistory&&<p role="alert">الراتب القائم مرجع غير مؤرخ للماضي. أول تغيير يثبت مرجعه من الشهر الحالي فقط، ولا يخترع تاريخ رواتب سابقًا.</p>}{data.currentMonthPaid&&<p>الشهر الحالي مسدد؛ اختر شهرًا لاحقًا.</p>}</div></section>
    {message&&<p role="status">{message}</p>}{data.canChange&&<section className="detail-card"><h2>تغيير مؤرخ</h2><form className="form-shell" aria-label="تغيير الراتب" onSubmit={review} key={data.revision}><label>شهر السريان<input name="month" type="month" required min={next} defaultValue={next}/></label><label>الراتب الشهري الجديد<input name="amount" type="number" required min="0" max="100000000" step="0.01" defaultValue={data.currentSalary}/></label><label>ملاحظة (اختياري)<textarea name="note" maxLength={500}/></label><div className="form-actions"><button className="primary-button" disabled={busy}>مراجعة واعتماد التغيير</button></div></form></section>}
    <section className="list-card"><h2>تاريخ الاعتمادات</h2>{!data.items.length?<p>لا نسخ مؤرخة معتمدة بعد. القراءة وحدها لا تنشئ سجلًا.</p>:<div className="linked-list">{data.items.map(v=><article key={v.id}><div><strong>{formatMoneyAr(v.monthlySalary,data.currency)}</strong><p>{kind[v.kind]} · من {formatDateAr(v.effectiveFrom)} · النسخة {v.revision}</p><small>{v.approvedBy}</small>{v.note&&<p>{v.note}</p>}</div></article>)}</div>}<Link href={`/dashboard/payroll?employeeId=${id}`}>فتح المستحقات</Link></section>
    </>}
    <ConfirmationDialog open={pending!==null} title="اعتماد تغيير الراتب" message={pending?`الراتب ${formatMoneyAr(pending.monthlySalary,data?.currency??"EGP")} من ${formatDateAr(pending.effectiveFrom)}. لن يعاد حساب شهر سابق أو مسدد، ولن ينشأ صرف. تأكد من شهر السريان.`:""} confirmLabel="تأكيد اعتماد الراتب" busy={busy} onConfirm={()=>void save()} onCancel={()=>setPending(null)}/>
  </>;
}
