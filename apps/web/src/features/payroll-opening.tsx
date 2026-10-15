"use client";
import {FormEvent,useCallback,useEffect,useRef,useState} from "react";
import Link from "next/link";
import {ConfirmationDialog,ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {csrfRequest,DashboardRequestError} from "./dashboard-api";
import {formatDateAr,formatMoneyAr} from "../lib/formatters";

type Data={employeeName:string;currency:string;latestBoundary:string;quoteHash:string;canApprove:boolean;canPay:boolean;canCorrect?:boolean;correctionQuoteHash?:string;corrections?:{id:string;delta:number;targetAmount:number;reference:string}[];unallocatedAdvances:number;opening:null|{asOf:string;amount:number;originalAmount?:number;paid:number;remaining:number;openingReference:string;note?:string};payments:{id:string;amount:number;kind:string}[]};
export function PayrollOpening({id}:{id:string}){
  const path=`/api/v1/manage/payroll/${id}/opening`;
  const [data,setData]=useState<Data|null>(null),[error,setError]=useState(""),[notice,setNotice]=useState(""),[busy,setBusy]=useState(false);
  const [pending,setPending]=useState<{pay:boolean;correct?:boolean;body:unknown;message:string}|null>(null),keys=useRef(new Map<string,string>());
  const load=useCallback(async(signal?:AbortSignal)=>{const r=await fetch(path,{cache:"no-store",signal});if(!r.ok)throw new Error();const v:Data=await r.json();if(!signal?.aborted)setData(v);},[path]);
  useEffect(()=>{const c=new AbortController();void Promise.resolve().then(()=>load(c.signal)).catch(()=>{if(!c.signal.aborted)setError("تعذر تحميل الافتتاح أو لا توجد صلاحية.");});return()=>c.abort();},[load]);
  function review(e:FormEvent<HTMLFormElement>,pay:boolean){e.preventDefault();if(!data)return;const f=new FormData(e.currentTarget),amount=Number(f.get("amount"));setError("");setNotice("");
    setPending({pay,body:pay?{amount,expectedPaid:data.opening!.paid,expectedRemaining:data.opening!.remaining,reference:String(f.get("reference")??"")}:{asOf:`${f.get("month")}-01`,amount,reference:String(f.get("reference")),note:String(f.get("note")??""),quoteHash:data.quoteHash},
      message:pay?`تسجيل صرف فعلي ${formatMoneyAr(amount,data.currency)} من الرصيد الافتتاحي. لا ينفذ التطبيق تحويلًا بنكيًا ولا يسجل مصروف راتب جديدًا.`:`اعتماد ${formatMoneyAr(amount,data.currency)} كرصيد مستحق قديم غير مسجل قبل شهر البداية المختار. راجع السند واستبعد المدفوعات والسلف والرواتب الموجودة. سيمنع اعتماد راتب سابق لهذا الحد؛ الاعتماد لا يصرف مالًا ولا يمكن تعديله مباشرة.`});
  }
  function reviewCorrection(e:FormEvent<HTMLFormElement>){e.preventDefault();if(!data?.opening)return;const f=new FormData(e.currentTarget),targetAmount=Number(f.get("targetAmount"));setError("");setNotice("");setPending({pay:false,correct:true,body:{targetAmount,reference:String(f.get("reference")),note:String(f.get("note")??""),quoteHash:data.correctionQuoteHash},message:`تصحيح إجمالي الافتتاح من ${formatMoneyAr(data.opening.amount,data.currency)} إلى ${formatMoneyAr(targetAmount,data.currency)}. الفرق ${formatMoneyAr(targetAmount-data.opening.amount,data.currency)}. نحفظ الأصل والدفعات السابقة؛ لا يصرف هذا الإجراء مالًا ولا ينشئ راتبًا أو دينًا خارجيًا.`});}
  async function confirm(){if(!pending||busy)return;setBusy(true);const signature=JSON.stringify(pending);if(!keys.current.has(signature))keys.current.set(signature,crypto.randomUUID());try{await csrfRequest(path+(pending.correct?"/corrections":pending.pay?"/disbursements":""),"POST",pending.body,{"Idempotency-Key":keys.current.get(signature)!});await load();setNotice(pending.correct?"تم اعتماد تصحيح الافتتاح مع حفظ الأصل والدفعات.":pending.pay?"تم تسجيل صرف الافتتاح وتحديث الحساب.":"تم اعتماد الافتتاح الموثق دون صرف.");setPending(null);}catch(e){setPending(null);setError(e instanceof DashboardRequestError?e.message:"تعذر تأكيد الحفظ؛ حدّث المراجعة قبل المحاولة.");try{await load();}catch{/* Keep the honest failure, not a success message. */}}finally{setBusy(false);}}
  return <><PageHeader title="افتتاح مستحقات الموظف" context={data?.employeeName??"الرواتب"} secondaryActions={[{label:"حساب الموظف",href:`/dashboard/payroll/${id}/account`}]}/>
    {error&&<ErrorState message={error}/>} {!data&&!error?<LoadingState/>:data&&<>
      <section className="detail-card statement-details"><h2>{data.opening?"افتتاح معتمد — ليس راتب شهر":"رصيد قديم يحتاج مراجعة المالك"}</h2><p>سجّل المتبقي غير المسجل قبل بداية النظام فقط، وليس إجمالي رواتب قديمة أو أموال سبق صرفها. الحساب الخارجي للموظف مستقل ولا يخصم هنا تلقائيًا.</p><Link href={`/dashboard/payroll/${id}/account`}>راجع المصادر المعتمدة والمدفوعات أولًا</Link>
        {data.opening?<><p>قبل {formatDateAr(data.opening.asOf)} · المرجع {data.opening.openingReference}</p><section className="statement-metrics payroll-opening-metrics" aria-label="مبالغ الافتتاح">{[["الأصل",data.opening.originalAmount??data.opening.amount],["المعتمد بعد التصحيح",data.opening.amount],["المسدد",data.opening.paid],["المتبقي",data.opening.remaining]].map(([label,value])=><div key={String(label)}><small>{label}</small><strong>{formatMoneyAr(Number(value),data.currency)}</strong></div>)}</section>{data.opening.note&&<p>{data.opening.note}</p>}</>:<p>لم يوثق الافتتاح، ولا نفترض أنه صفر. أحدث حد متاح: {formatDateAr(data.latestBoundary)}؛ لا يتداخل مع أشهر سبق تسجيلها.</p>}
      </section>
      {notice&&<p role="status">{notice}</p>}
      {data.canApprove&&<section className="detail-card"><form className="form-shell payroll-opening-form" aria-label="اعتماد الافتتاح" onSubmit={e=>review(e,false)}>
        <label>شهر بداية النظام<input name="month" type="month" min="2001-01" max={data.latestBoundary.slice(0,7)} defaultValue={data.latestBoundary.slice(0,7)} required/></label>
        <label>الرصيد القديم غير المسجل<input name="amount" type="number" min="0" max="100000000" step="0.01" required/></label>
        <label>مرجع مستند المراجعة<input name="reference" minLength={3} maxLength={120} required/></label>
        <label>ملاحظة (اختياري)<textarea name="note" maxLength={500}/></label>
        <div className="form-actions"><button className="primary-button" disabled={busy}>مراجعة الافتتاح</button></div>
      </form></section>}
      {data.canCorrect&&data.opening&&<section className="detail-card"><h2>تصحيح افتتاح معتمد</h2><p>سند مستقل للمالك فقط. أدخل الإجمالي الصحيح، وليس الفرق. لا يمكن تخفيضه عن المسدد أو تغيير أصل الاعتماد.</p><form className="form-shell payroll-opening-form" aria-label="تصحيح الافتتاح" key={`${data.opening.amount}-${data.opening.paid}`} onSubmit={reviewCorrection}>
        <label>إجمالي الافتتاح الصحيح<input name="targetAmount" type="number" min={data.opening.paid} max="100000000" step="0.01" required/></label>
        <label>مرجع مستند التصحيح<input name="reference" minLength={3} maxLength={120} required/></label><label>ملاحظة التصحيح (اختياري)<textarea name="note" maxLength={500}/></label><div className="form-actions"><button className="secondary-button" disabled={busy}>مراجعة تصحيح الافتتاح</button></div>
      </form></section>}
      {!!data.corrections?.length&&<section className="list-card"><h2>سجل التصحيحات — داخل الإجمالي بالفعل</h2>{data.corrections.map(c=><article className="statement-payment" key={c.id}><strong>{formatMoneyAr(c.delta,data.currency)}</strong><span>الإجمالي الصحيح {formatMoneyAr(c.targetAmount,data.currency)} · {c.reference}</span></article>)}</section>}
      {data.unallocatedAdvances>0&&<p role="note">توجد سلف غير مخصصة {formatMoneyAr(data.unallocatedAdvances,data.currency)}. <Link href={`/dashboard/payroll/${id}/advances`}>راجعها وخصصها قبل صرف مبلغ جديد</Link>؛ لا تخصم تلقائيًا.</p>}
      {data.canPay&&<section className="detail-card"><h2>صرف الرصيد الافتتاحي</h2><form className="form-shell payroll-opening-form" aria-label="صرف الافتتاح" key={`${data.opening!.paid}-${data.opening!.amount}`} onSubmit={e=>review(e,true)}>
        <label>مبلغ الصرف<input name="amount" type="number" min="0.01" max={data.opening!.remaining} step="0.01" required/></label><label>مرجع الصرف (اختياري)<input name="reference" maxLength={120}/></label><div className="form-actions"><button className="primary-button" disabled={busy}>مراجعة صرف الافتتاح</button></div>
      </form></section>}
      {data.payments.length>0&&<section className="list-card"><h2>تسويات الافتتاح</h2>{data.payments.map(p=><article className="statement-payment" key={p.id}><strong>{formatMoneyAr(p.amount,data.currency)}</strong><span>{p.kind==="AdvanceApplication"?"تخصيص سلفة سابقة — ليس صرفًا جديدًا":"صرف مباشر"}</span></article>)}</section>}
    </>}
    <ConfirmationDialog open={pending!==null} title="مراجعة الرصيد الافتتاحي" message={pending?.message??""} confirmLabel="تأكيد الحفظ" busy={busy} onConfirm={()=>void confirm()} onCancel={()=>setPending(null)}/>
  </>;
}
