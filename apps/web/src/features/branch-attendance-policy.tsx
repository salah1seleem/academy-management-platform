"use client";
import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { ErrorState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";

type Policy={enabled:boolean;radiusMeters:number;maxAccuracyMeters:number;maxAgeSeconds:number;challengeSeconds:number;coachRequired:boolean;adminRequired:boolean;ownerRequired:boolean;reviewerMembershipId:string|null};
type Site={version:number;checkInLatitude:number|null;checkInLongitude:number|null;policy:Policy;reviewers:{id:string;name:string}[];history:{id:string;createdAtUtc:string;afterLatitude:number|null;afterLongitude:number|null;afterPolicy:string|null}[]};
export function BranchCheckInSite({id}:{id:string}){
  return <BranchCheckInSiteForm key={id} id={id}/>;
}
function BranchCheckInSiteForm({id}:{id:string}){
  const [site,setSite]=useState<Site|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState("");
  useEffect(()=>{const c=new AbortController();fetch(`/api/v1/manage/branches/${id}/check-in-site`,{signal:c.signal,cache:"no-store"}).then(async r=>{if(r.status===403)return null;if(!r.ok)throw new Error();return await r.json() as Site;}).then(s=>{if(!c.signal.aborted)setSite(s);}).catch(()=>{if(!c.signal.aborted)setError("تعذر قراءة سياسة حضور الفرع؛ أعد تحميل الصفحة.");});return()=>c.abort();},[id]);
  async function save(e:FormEvent<HTMLFormElement>){
    e.preventDefault();if(busy||!site)return;const f=new FormData(e.currentTarget);const lat=String(f.get("latitude")??"").trim(),lon=String(f.get("longitude")??"").trim();
    const policy:Policy={enabled:f.get("enabled")==="on",radiusMeters:Number(f.get("radiusMeters")),maxAccuracyMeters:Number(f.get("maxAccuracyMeters")),maxAgeSeconds:Number(f.get("maxAgeSeconds")),challengeSeconds:Number(f.get("challengeSeconds")),coachRequired:f.get("coachRequired")==="on",adminRequired:f.get("adminRequired")==="on",ownerRequired:f.get("ownerRequired")==="on",reviewerMembershipId:String(f.get("reviewerMembershipId")??"")||null};
    setBusy(true);setError("");setMessage("");
    try{await csrfRequest(`/api/v1/manage/branches/${id}/check-in-site`,"PUT",{latitude:lat?Number(lat):null,longitude:lon?Number(lon):null,version:site.version,policy},{},{announce:false});const r=await fetch(`/api/v1/manage/branches/${id}/check-in-site`,{cache:"no-store"});if(!r.ok)throw new Error("تم الحفظ، لكن تعذر تأكيد القراءة؛ أعد تحميل الصفحة.");setSite(await r.json());setMessage("تم حفظ سياسة حضور الفرع وتأكيد قراءتها.");}
    catch(caught){setError(caught instanceof Error?caught.message:"تعذر حفظ سياسة الحضور.");}finally{setBusy(false);}
  }
  if(!site)return error?<ErrorState message={error}/>:null;
  const p=site.policy;
  return <section className="detail-card branch-attendance-policy"><h2>سياسة حضور الموظفين</h2><p>إعدادات هذا الفرع فقط. راجع الموقع ميدانيًا قبل اعتماده؛ لا نستعمل إحداثيات تجريبية. تغيير الإعدادات يلغي المحاولات غير المكتملة، ولا يعدّل الحضور السابق أو يغلق وردية مفتوحة.</p>
    <form key={site.version} className="form-shell contract-history-form" onSubmit={e=>void save(e)}>
      <label className="check-inline"><input name="enabled" type="checkbox" defaultChecked={p.enabled} disabled={busy}/>تفعيل التسجيل الذاتي الآمن لهذا الفرع</label>
      <label>مسؤول الاستثناءات<select name="reviewerMembershipId" defaultValue={p.reviewerMembershipId??""} disabled={busy}><option value="">كل المخولين الحاليين</option>{p.reviewerMembershipId&&!site.reviewers.some(r=>r.id===p.reviewerMembershipId)&&<option value={p.reviewerMembershipId}>المسؤول السابق غير مخول حاليًا — اختر بديلًا</option>}{site.reviewers.map(r=><option key={r.id} value={r.id}>{r.name}</option>)}</select></label>
      <label>خط العرض<input name="latitude" type="number" step="any" min="-90" max="90" defaultValue={site.checkInLatitude??""} disabled={busy} dir="ltr"/></label>
      <label>خط الطول<input name="longitude" type="number" step="any" min="-180" max="180" defaultValue={site.checkInLongitude??""} disabled={busy} dir="ltr"/></label>
      <label>نصف القطر بالمتر<input name="radiusMeters" type="number" min="10" max="500" required defaultValue={p.radiusMeters} disabled={busy}/></label>
      <label>أقصى تقدير دقة بالمتر<input name="maxAccuracyMeters" type="number" min="1" max="100" required defaultValue={p.maxAccuracyMeters} disabled={busy}/></label>
      <label>حداثة قراءة الموقع بالثواني<input name="maxAgeSeconds" type="number" min="5" max="60" required defaultValue={p.maxAgeSeconds} disabled={busy}/></label>
      <label>صلاحية محاولة التوقيع بالثواني<input name="challengeSeconds" type="number" min="30" max="120" required defaultValue={p.challengeSeconds} disabled={busy}/></label>
      <label className="check-inline"><input name="coachRequired" type="checkbox" defaultChecked={p.coachRequired} disabled={busy}/>المدربون ملزمون بتسجيل الحضور</label>
      <label className="check-inline"><input name="adminRequired" type="checkbox" defaultChecked={p.adminRequired} disabled={busy}/>الإداريون ملزمون بتسجيل الحضور</label>
      <label className="check-inline"><input name="ownerRequired" type="checkbox" defaultChecked={p.ownerRequired} disabled={busy}/>المالكون ذوو ملف موظف ملزمون بتسجيل الحضور</label>
      <p className="muted">الإلزام توجيه للحساب المرتبط بملف موظف، لا صلاحية جديدة ولا خصم تلقائي. جدول العمل الاختياري يبقى في إعداد الموظف الحالي. المالك بديل لمسؤول الاستثناءات، ولا أحد يعتمد طلبه بنفسه.</p>
      <div className="form-actions"><button className="primary-button" disabled={busy}>حفظ سياسة الحضور</button><Link href="/dashboard/academy/branches">العودة للفروع</Link></div>
    </form>{error&&<ErrorState message={error}/>} {message&&<p role="status">{message}</p>}
    {!!site.history.length&&<section aria-label="سجل سياسة الحضور"><h3>آخر تغييرات السياسة والموقع</h3><ul className="staff-passkey-list">{site.history.slice(0,3).map(h=><li key={h.id}><span>{new Date(h.createdAtUtc).toLocaleString("ar-EG-u-nu-latn")}</span><strong>{h.afterLatitude===null?"الموقع غير مهيأ":"نقطة الفرع محفوظة"}</strong><small>{h.afterPolicy?"حُفظت السياسة مع دليل قبل وبعد":"تغيير موقع سابق — السياسة التاريخية غير معلومة"}</small></li>)}</ul></section>}
  </section>;
}
