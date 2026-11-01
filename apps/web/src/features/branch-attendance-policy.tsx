"use client";
import { FormEvent, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ErrorState } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Policy={enabled:boolean;radiusMeters:number;maxAccuracyMeters:number;maxAgeSeconds:number;challengeSeconds:number;coachRequired:boolean;adminRequired:boolean;ownerRequired:boolean;reviewerMembershipId:string|null};
type Site={version:number;checkInLatitude:number|null;checkInLongitude:number|null;policy:Policy;reviewers:{id:string;name:string}[];history:{id:string;createdAtUtc:string;afterLatitude:number|null;afterLongitude:number|null;afterPolicy:string|null}[]};
export function BranchCheckInSite({id}:{id:string}){
  return <BranchCheckInSiteForm key={id} id={id}/>;
}
const policyFields: (keyof Policy)[] = ["enabled","radiusMeters","maxAccuracyMeters","maxAgeSeconds","challengeSeconds","coachRequired","adminRequired","ownerRequired","reviewerMembershipId"];
type Intent = {body:{latitude:number|null;longitude:number|null;version:number;policy:Policy};confirmedVersion?:number};
async function readSite(id:string,signal?:AbortSignal):Promise<Site> {
 const r=await fetch(`/api/v1/manage/branches/${id}/check-in-site`,{cache:"no-store",signal});
 if(!r.ok)throw new DashboardRequestError("تعذر قراءة سياسة حضور الفرع. راجع الصلاحية وأعد المحاولة.",r.status);
 return r.json();
}
function BranchCheckInSiteForm({id}:{id:string}){
  const [site,setSite]=useState<Site|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState(""),[pending,setPending]=useState<Intent|null>(null),[formRevision,setFormRevision]=useState(0);
  const mounted=useRef(true),writing=useRef(false),locked=busy||pending!==null;
  useEffect(()=>{mounted.current=true;return()=>{mounted.current=false;};},[]);
  useEffect(()=>{const c=new AbortController();readSite(id,c.signal).then(s=>{if(!c.signal.aborted)setSite(s);}).catch(()=>{if(!c.signal.aborted)setError("تعذر قراءة سياسة حضور الفرع؛ راجع الصلاحية وأعد تحميل الصفحة.");});return()=>c.abort();},[id]);
  function reject(caught:unknown){
   if(!mounted.current)return;
   if(caught instanceof DashboardRequestError&&[401,403].includes(caught.status)){setSite(null);setPending(null);}
   setError(caught instanceof DashboardRequestError&&caught.status<500&&/[\u0600-\u06ff]/.test(caught.message)?caught.message:"تعذر تأكيد حفظ سياسة الحضور. أعد التحقق من القيم المحفوظة.");
  }
  async function verify(intent:Intent){
   const read=await readSite(id);if(!mounted.current)return;
   if(read.checkInLatitude!==intent.body.latitude||read.checkInLongitude!==intent.body.longitude||
    intent.confirmedVersion!==undefined&&read.version!==intent.confirmedVersion||
    !policyFields.every(k=>read.policy?.[k]===intent.body.policy[k]))
     throw new DashboardRequestError("لم تتطابق سياسة الفرع المقروءة مع القيم المرسلة. أعد التحقق قبل إرسال تغيير آخر.",0);
   setSite(read);setPending(null);setMessage("تم حفظ سياسة حضور الفرع وتأكيد قراءتها.");
  }
  async function save(e:FormEvent<HTMLFormElement>){
    e.preventDefault();if(writing.current||pending||!site)return;const f=new FormData(e.currentTarget);const lat=String(f.get("latitude")??"").trim(),lon=String(f.get("longitude")??"").trim();
    const policy:Policy={enabled:f.get("enabled")==="on",radiusMeters:Number(f.get("radiusMeters")),maxAccuracyMeters:Number(f.get("maxAccuracyMeters")),maxAgeSeconds:Number(f.get("maxAgeSeconds")),challengeSeconds:Number(f.get("challengeSeconds")),coachRequired:f.get("coachRequired")==="on",adminRequired:f.get("adminRequired")==="on",ownerRequired:f.get("ownerRequired")==="on",reviewerMembershipId:String(f.get("reviewerMembershipId")??"")||null};
    let intent:Intent={body:{latitude:lat?Number(lat):null,longitude:lon?Number(lon):null,version:site.version,policy}},stored=false;
    writing.current=true;setBusy(true);setPending(intent);setError("");setMessage("");
    try{
     const response=await csrfRequest(`/api/v1/manage/branches/${id}/check-in-site`,"PUT",intent.body,{},{announce:false});stored=true;
     const saved=await response.json().catch(()=>null) as {version?:number}|null;
     if(!mounted.current)return;
     intent={...intent,confirmedVersion:typeof saved?.version==="number"?saved.version:undefined};setPending(intent);
     await verify(intent);
    }catch(caught){if(!stored&&caught instanceof DashboardRequestError&&caught.status>=400&&caught.status<500&&mounted.current)setPending(null);reject(caught);}
    finally{writing.current=false;if(mounted.current)setBusy(false);}
  }
  async function recheck(){if(writing.current||!pending)return;writing.current=true;setBusy(true);setError("");try{await verify(pending);}catch(caught){reject(caught);}finally{writing.current=false;if(mounted.current)setBusy(false);}}
  async function reload(){if(writing.current)return;writing.current=true;setBusy(true);setError("");setMessage("");try{const read=await readSite(id);if(mounted.current){setSite(read);setPending(null);setFormRevision(v=>v+1);}}catch(caught){reject(caught);}finally{writing.current=false;if(mounted.current)setBusy(false);}}
  if(!site)return error?<ErrorState message={error}/>:null;
  const p=site.policy;
  return <section className="detail-card branch-attendance-policy"><h2>سياسة حضور الموظفين</h2><p>إعدادات هذا الفرع فقط. راجع الموقع ميدانيًا قبل اعتماده؛ لا نستعمل إحداثيات تجريبية. تغيير الإعدادات يلغي المحاولات غير المكتملة، ولا يعدّل الحضور السابق أو يغلق وردية مفتوحة.</p>
    <form key={`${site.version}-${formRevision}`} className="form-shell contract-history-form" onSubmit={e=>void save(e)}>
      <label className="check-inline"><input name="enabled" type="checkbox" defaultChecked={p.enabled} disabled={locked}/>تفعيل التسجيل الذاتي الآمن لهذا الفرع</label>
      <label>مسؤول الاستثناءات<select name="reviewerMembershipId" defaultValue={p.reviewerMembershipId??""} disabled={locked}><option value="">كل المخولين الحاليين</option>{p.reviewerMembershipId&&!site.reviewers.some(r=>r.id===p.reviewerMembershipId)&&<option value={p.reviewerMembershipId}>المسؤول السابق غير مخول حاليًا — اختر بديلًا</option>}{site.reviewers.map(r=><option key={r.id} value={r.id}>{r.name}</option>)}</select></label>
      <label>خط العرض<input name="latitude" type="number" step="any" min="-90" max="90" defaultValue={site.checkInLatitude??""} disabled={locked} dir="ltr"/></label>
      <label>خط الطول<input name="longitude" type="number" step="any" min="-180" max="180" defaultValue={site.checkInLongitude??""} disabled={locked} dir="ltr"/></label>
      <label>نصف القطر بالمتر<input name="radiusMeters" type="number" min="10" max="500" required defaultValue={p.radiusMeters} disabled={locked}/></label>
      <label>أقصى تقدير دقة بالمتر<input name="maxAccuracyMeters" type="number" min="1" max="100" required defaultValue={p.maxAccuracyMeters} disabled={locked}/></label>
      <label>حداثة قراءة الموقع بالثواني<input name="maxAgeSeconds" type="number" min="5" max="60" required defaultValue={p.maxAgeSeconds} disabled={locked}/></label>
      <label>صلاحية محاولة التوقيع بالثواني<input name="challengeSeconds" type="number" min="30" max="120" required defaultValue={p.challengeSeconds} disabled={locked}/></label>
      <label className="check-inline"><input name="coachRequired" type="checkbox" defaultChecked={p.coachRequired} disabled={locked}/>المدربون ملزمون بتسجيل الحضور</label>
      <label className="check-inline"><input name="adminRequired" type="checkbox" defaultChecked={p.adminRequired} disabled={locked}/>الإداريون ملزمون بتسجيل الحضور</label>
      <label className="check-inline"><input name="ownerRequired" type="checkbox" defaultChecked={p.ownerRequired} disabled={locked}/>المالكون ذوو ملف موظف ملزمون بتسجيل الحضور</label>
      <p className="muted">الإلزام توجيه للحساب المرتبط بملف موظف، لا صلاحية جديدة ولا خصم تلقائي. جدول العمل الاختياري يبقى في إعداد الموظف الحالي. المالك بديل لمسؤول الاستثناءات، ولا أحد يعتمد طلبه بنفسه.</p>
      <div className="form-actions"><button className="primary-button" disabled={locked}>حفظ سياسة الحضور</button><Link href="/dashboard/academy/branches">العودة للفروع</Link></div>
    </form>{(pending||error)&&<div className="form-actions">{pending&&<><p>توقف إرسال التغييرات حتى التحقق من العملية السابقة. إعادة التحقق تقرأ القيم فقط.</p><button disabled={busy} onClick={()=>void recheck()}>إعادة التحقق من الحفظ</button></>}<button disabled={busy} onClick={()=>void reload()}>إعادة تحميل سياسة الفرع</button></div>}{error&&<ErrorState message={error}/>} {message&&<p role="status">{message}</p>}
    {!!site.history.length&&<section aria-label="سجل سياسة الحضور"><h3>آخر تغييرات السياسة والموقع</h3><ul className="staff-passkey-list">{site.history.slice(0,3).map(h=><li key={h.id}><span>{new Date(h.createdAtUtc).toLocaleString("ar-EG-u-nu-latn")}</span><strong>{h.afterLatitude===null?"الموقع غير مهيأ":"نقطة الفرع محفوظة"}</strong><small>{h.afterPolicy?"حُفظت السياسة مع دليل قبل وبعد":"تغيير موقع سابق — السياسة التاريخية غير معلومة"}</small></li>)}</ul></section>}
  </section>;
}
