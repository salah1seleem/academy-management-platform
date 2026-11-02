"use client";
import {FormEvent,useEffect,useState} from "react";
import Link from "next/link";
import {AdminDataTable,ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {formatDateAr,formatMoneyAr} from "../lib/formatters";
import {PayrollFinalPayment} from "./payroll-final-payment";

type Item={id:string;month:string;approvedAtUtc:string;netDue:number;paid:number;remaining:number;payments:{id:string;amount:number;createdAtUtc:string;kind?:string}[]};
type Account={employeeName:string;currency:string;asOf:string;cutoffUtc:string;timeZone:string;totalDue:number;totalPaid:number;remaining:number;priorRemaining:number;currentRemaining:number;currentMonthApproved:boolean;legacyPaidMonthsExcluded:number;items:Item[];directPaid?:number;advanceApplied?:number;recordedAdvanceTotal?:number;unallocatedAdvances?:number;cashOutRecorded?:number;opening?:{asOf:string;amount:number;originalAmount?:number;corrections?:{id:string;delta:number;targetAmount:number}[];paid:number;remaining:number;payments:Item["payments"]}|null;finalSettlements?:{id:string;approvedAtUtc:string;amount:number;paid:number;remaining:number;canPay:boolean;payments:Item["payments"]}[]};
export function PayrollAccount({id}:{id:string}){
  const [date,setDate]=useState<string|null>(null),[query,setQuery]=useState(""),[data,setData]=useState<Account|null>(null),[error,setError]=useState("");
  const [revision,setRevision]=useState(0);
  const path=`/api/v1/manage/payroll/${id}/account`,suffix=query?`?asOf=${encodeURIComponent(query)}`:"";
  useEffect(()=>{const c=new AbortController();void Promise.resolve().then(async()=>{
    setData(null);setError("");const r=await fetch(path+suffix,{cache:"no-store",signal:c.signal});if(!r.ok)throw new Error();const value:Account=await r.json();if(!c.signal.aborted)setData(value);
  }).catch(()=>{if(!c.signal.aborted)setError("تعذر تحميل كشف الحساب أو التاريخ غير متاح أو لا توجد صلاحية.");});return()=>c.abort();},[path,suffix,revision]);
  function filter(e:FormEvent){e.preventDefault();setQuery(date??data?.asOf??"");setRevision(v=>v+1);}
  return <><PageHeader title="حساب مستحقات الموظف" context={data?.employeeName??"الرواتب"} secondaryActions={[{label:"عودة للرواتب",href:`/dashboard/payroll?employeeId=${id}`}]}/>
    <form className="report-filters" aria-label="تاريخ كشف الحساب" onSubmit={filter}><label>حتى تاريخ<input type="date" value={date??data?.asOf??""} onChange={e=>setDate(e.target.value)}/></label><button className="primary-button">عرض</button><button className="secondary-button" type="button" onClick={()=>{setDate(null);setQuery("");setRevision(v=>v+1);}}>حتى اليوم</button></form>
    {error?<ErrorState message={error}/>:!data?<LoadingState/>:<>
      <p>المصادر المسجلة حتى {formatDateAr(data.asOf)} · توقيت {data.timeZone}</p>
      <section className="statement-metrics" aria-label="مجاميع الحساب">{[["المستحق المعتمد",data.totalDue],["المسدد من المستحق",data.totalPaid],["المتبقي المعروف",data.remaining]].map(([label,value])=><div key={String(label)}><small>{label}</small><strong>{formatMoneyAr(Number(value),data.currency)}</strong></div>)}</section>
      <section className="detail-card statement-details"><h2>الرصيد الافتتاحي</h2>{data.opening?<><p>موثق قبل {formatDateAr(data.opening.asOf)} · {formatMoneyAr(data.opening.amount,data.currency)} — داخل الإجمالي بالفعل، وليس راتب الشهر.</p><p>المسدد {formatMoneyAr(data.opening.paid,data.currency)} · المتبقي {formatMoneyAr(data.opening.remaining,data.currency)}</p>{data.opening.originalAmount!=null&&<p>أصل الافتتاح {formatMoneyAr(data.opening.originalAmount,data.currency)}</p>}{data.opening.corrections?.map(c=><p key={c.id}>تصحيح موثق · {formatMoneyAr(c.delta,data.currency)} — داخل الإجمالي بالفعل، وليس صرفًا</p>)}{data.opening.payments.map(p=><p key={p.id}>{p.kind==="AdvanceApplication"?"تخصيص سلفة سابقة":"صرف افتتاح"} · {formatMoneyAr(p.amount,data.currency)}</p>)}</>:<p>لم يوثق الافتتاح؛ لا نفترض أنه صفر.</p>}<Link href={`/dashboard/payroll/${id}/opening`}>مراجعة الافتتاح والصرف</Link></section>
      <section className="detail-card statement-details"><h2>السلف وخروج المال المسجل</h2>{data.recordedAdvanceTotal==null?<p>تفاصيل السلف المسجلة غير متاحة في هذا الكشف؛ لا نفترض أنها صفر.</p>:<><p>سلف سبق صرفها · {formatMoneyAr(data.recordedAdvanceTotal,data.currency)}</p><p>غير المخصص · {formatMoneyAr(data.unallocatedAdvances??0,data.currency)} — لا يخصم تلقائيًا من المتبقي</p><p>من السداد تخصيص سلف · {formatMoneyAr(data.advanceApplied??0,data.currency)}، وصرف مباشر · {formatMoneyAr(data.directPaid??data.totalPaid,data.currency)}</p><p>خروج المال المسجل · {formatMoneyAr(data.cashOutRecorded??data.totalPaid,data.currency)} — صرف مباشر وسلف، دون تكرار التخصيص</p></>}<Link href={`/dashboard/payroll/${id}/advances`}>تفاصيل السلف وتخصيصها</Link></section>
      <section className="detail-card statement-details"><h2>من أين جاء المتبقي؟</h2><p>الأشهر السابقة · {formatMoneyAr(data.priorRemaining,data.currency)}</p><p>الشهر المختار · {formatMoneyAr(data.currentRemaining,data.currency)}</p>
        {!!data.finalSettlements?.length&&<p>عمولات بعد نهاية العقد · {formatMoneyAr(data.finalSettlements.reduce((sum,s)=>sum+s.remaining,0),data.currency)} — مستقلة عن راتب الشهر</p>}
        {!data.currentMonthApproved&&<p>لا كشف معتمد لهذا الشهر حتى التاريخ المختار؛ غيابه لا يعني عدم وجود راتب مستحق.</p>}
        <p className="muted">{data.opening?"الافتتاح الموثق يخص الفترة قبل حد البداية فقط. ":"هذه مصادر معتمدة معروفة، وليست رصيدًا افتتاحيًا موثقًا. "}السلف التاريخية غير المستوردة والأشهر غير المعتمدة ليست صفرًا. لا ننسخ المتبقي إلى راتب شهر آخر ولا نخصم دين التعامل الخارجي.</p>
        {data.legacyPaidMonthsExcluded>0&&<p>{data.legacyPaidMonthsExcluded} شهر مسدد بالمسار السابق مستبعد من التجميع؛ لم نُعِد ترحيله.</p>}
        <a href={`${path}/export?asOf=${data.asOf}&cutoffUtc=${encodeURIComponent(data.cutoffUtc)}`}>تنزيل نفس الكشف CSV</a>
      </section>
      {!!data.finalSettlements?.length&&<section className="list-card statement-details"><h2>عمولات بعد نهاية العقد</h2><p>داخل المجاميع أعلاه بالفعل، وليست راتب شهر جديد. صرفها لا يعيد إثبات المصروف.</p>{data.finalSettlements.map(s=><article className="detail-card statement-details" key={s.id}><p>اعتمدت {formatDateAr(s.approvedAtUtc)} · المستحق {formatMoneyAr(s.amount,data.currency)} · المسدد {formatMoneyAr(s.paid,data.currency)} · المتبقي {formatMoneyAr(s.remaining,data.currency)}</p>{s.payments.map(p=><p key={p.id}>صرف مسجل · {formatMoneyAr(p.amount,data.currency)}</p>)}{s.canPay&&s.remaining>0&&<PayrollFinalPayment employeeId={id} sourceId={s.id} paid={s.paid} remaining={s.remaining} currency={data.currency} onChanged={()=>setRevision(v=>v+1)}/>}</article>)}</section>}
      <section className="list-card statement-details"><h2>الأشهر المعتمدة</h2>{!data.items.length?<p>{data.finalSettlements?.length?"لا أشهر راتب معتمدة حتى هذا التاريخ؛ راجع عمولات نهاية العقد أعلاه.":"لا مصادر معتمدة حتى هذا التاريخ؛ لا يعني ذلك أن المستحقات صفر."}</p>:<AdminDataTable label="أشهر حساب الموظف" rows={data.items} rowKey={s=>s.id} columns={[
        {key:"month",header:"شهر الخدمة",primary:true,render:s=>formatDateAr(s.month)},
        {key:"due",header:"المستحق",render:s=>formatMoneyAr(s.netDue,data.currency)},
        {key:"paid",header:"المسدد",render:s=>formatMoneyAr(s.paid,data.currency)},
        {key:"remaining",header:"المتبقي",render:s=>formatMoneyAr(s.remaining,data.currency)},
        {key:"action",header:"المصدر والإجراء",render:s=><Link href={`/dashboard/payroll/${id}/statement?month=${s.month}`}>كشف الشهر والصرف</Link>},
      ]}/>}</section>
      <section className="list-card"><h2>تسويات الكشوف المسجلة</h2>{data.items.every(s=>!s.payments.length)?<p>لا دفعات مسجلة على هذه المصادر حتى التاريخ المختار.</p>:data.items.flatMap(s=>s.payments.map(p=><article className="statement-payment" key={p.id}><strong>{formatMoneyAr(p.amount,data.currency)}</strong><span>{p.kind==="AdvanceApplication"?"تخصيص سلفة سابقة — ليس صرفًا جديدًا":"صرف مباشر"} · عن {formatDateAr(s.month)} · {formatDateAr(p.createdAtUtc)}</span></article>))}</section>
    </>}
  </>;
}
