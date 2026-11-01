"use client";
import Link from "next/link";
import {useEffect,useRef,useState} from "react";
import {ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {csrfRequest} from "./dashboard-api";
import type {Correction} from "./attendance-corrections";

const statusLabels:Record<string,string>={Pending:"بانتظار الاعتماد",Applying:"قيد التطبيق",Approved:"اعتمد وطُبق",Rejected:"لم يُعتمد"};
const attendanceLabels:Record<string,string>={Present:"حاضر",Absent:"غائب",Late:"متأخر",Excused:"معتذر",NotRecorded:"لم يُسجل"};
type Intent={approve:boolean;body:{expectedVersion:number;reason:string|null};accepted:boolean};
async function read(id:string,signal?:AbortSignal):Promise<Correction>{
  const response=await fetch(`/api/v1/attendance/corrections/${encodeURIComponent(id)}`,{cache:"no-store",signal});
  if(!response.ok)throw Error("تعذر تحميل طلب التصحيح أو لا توجد صلاحية لعرضه.");
  const row=await response.json() as Correction;
  if(row.id!==id)throw Error("تعذر التحقق من مصدر طلب التصحيح.");
  return row;
}

export function AttendanceCorrectionDetail({id}:{id:string}){
  const [row,setRow]=useState<Correction|null>(null),[error,setError]=useState(""),[reason,setReason]=useState(""),[busy,setBusy]=useState(false),[pending,setPending]=useState(false),[accepted,setAccepted]=useState(false),[message,setMessage]=useState("");
  const intent=useRef<Intent|null>(null),current=useRef(id);
  useEffect(()=>{const c=new AbortController();current.current=id;read(id,c.signal).then(d=>{if(!c.signal.aborted)setRow(d);}).catch(()=>{if(!c.signal.aborted)setError("تعذر تحميل طلب التصحيح أو لا توجد صلاحية لعرضه.");});return()=>{c.abort();current.current="";};},[id]);
  const visible=row?.id===id?row:null;
  async function refresh(){
    if(busy)return;setBusy(true);setError("");
    try{const saved=await read(id);if(current.current===id){setRow(saved);intent.current=null;setPending(false);setAccepted(false);setMessage("");}}catch{if(current.current===id)setError("تعذر تحميل طلب التصحيح أو لا توجد صلاحية لعرضه.");}finally{if(current.current===id)setBusy(false);}
  }
  async function decide(approve?:boolean){
    if(busy||!visible)return;
    if(!intent.current){if(approve===undefined||!visible.canApprove)return;intent.current={approve,body:{expectedVersion:visible.version,reason:reason.trim()||null},accepted:false};setAccepted(false);}
    const frozen=intent.current;setBusy(true);setPending(true);setError("");setMessage("");
    try{
      if(!frozen.accepted){await csrfRequest(`/api/v1/attendance/corrections/${encodeURIComponent(id)}/${frozen.approve?"approve":"reject"}`,"POST",frozen.body,{},{announce:false});frozen.accepted=true;if(current.current===id)setAccepted(true);}
      const saved=await read(id);
      if(saved.state!==(frozen.approve?"Approved":"Rejected")||(saved.decisionReason??null)!==frozen.body.reason||frozen.approve&&!saved.appliedAtUtc)throw Error("لم تتأكد مطابقة القرار المحفوظ؛ أعد التحقق من الطلب.");
      if(current.current!==id)return;
      setRow(saved);setPending(false);intent.current=null;setMessage(frozen.approve?"تأكد اعتماد التصحيح وتطبيقه من السجل المحفوظ.":"تأكد عدم اعتماد الطلب من السجل المحفوظ.");
    }catch{if(current.current===id)setError(frozen.accepted?"تعذر التحقق من القرار المحفوظ. أعد التحقق دون إرسال القرار مرة أخرى.":"لم تتأكد نتيجة إرسال القرار. أعد محاولة القرار نفسه للتحقق بأمان.");}
    finally{if(current.current===id)setBusy(false);}
  }
  return <><PageHeader title="طلب تصحيح الحضور" context="الحضور / مراجعة الطلب" description="القرار مرتبط بهذا الطلب وكشف الحصة المحفوظ؛ يراجع الخادم الصلاحية والنسخة قبل التطبيق."/>
    <Link href="/dashboard/attendance/corrections">العودة لطلبات التصحيح</Link>
    {error&&<ErrorState message={error}/>} {message&&<p role="status" className="success-message">{message}</p>}
    {!visible&&!error?<LoadingState/>:visible&&<section className="detail-card" aria-label="تفاصيل طلب تصحيح الحضور">
      <header className="section-heading"><h2>{visible.group}</h2><span>{statusLabels[visible.state]??"غير متاح"}</span></header>
      <p><time dir="ltr">{visible.sessionDate}</time> · {visible.kind==="Players"?"اللاعبون":"الجهاز الفني"}</p>
      <p>صاحب الطلب: {visible.requester??"غير متاح"}</p><p>سبب التصحيح: {visible.reason}</p>
      <div className="attendance-roster">{visible.items.map(item=><article key={item.subjectId}><strong>{item.name}</strong><span>المسجل: {attendanceLabels[item.before]??"غير متاح"} · المقترح: {attendanceLabels[item.after]??"غير متاح"}</span></article>)}</div>
      {visible.reviewer&&<p>المراجع: {visible.reviewer}</p>}{visible.decisionReason&&<p>ملاحظة القرار: {visible.decisionReason}</p>}
      {visible.decidedAtUtc&&<p>وقت القرار: {new Date(visible.decidedAtUtc).toLocaleString("ar-EG-u-nu-latn")}</p>}
      <Link className="secondary-button" href={`/dashboard/attendance/sessions/${visible.trainingSessionId}/${visible.kind==="Players"?"players":"staff"}`}>كشف الحصة</Link>
      {visible.canApprove&&!pending&&<div className="form-shell"><label>ملاحظة القرار (اختيارية)<textarea maxLength={500} disabled={busy} value={reason} onChange={e=>setReason(e.target.value)}/></label><div className="form-actions"><button className="primary-button" disabled={busy} onClick={()=>void decide(true)}>اعتماد وتطبيق التصحيح</button><button className="secondary-button" disabled={busy} onClick={()=>void decide(false)}>عدم الاعتماد</button></div></div>}
    </section>}
    <div className="form-actions">{pending?<button className="secondary-button" disabled={busy} onClick={()=>void decide()}>{accepted?"إعادة التحقق من القرار":"إعادة محاولة القرار نفسه"}</button>:null}<button className="secondary-button" disabled={busy} onClick={()=>void refresh()}>إعادة تحميل الطلب</button></div>
  </>;
}
