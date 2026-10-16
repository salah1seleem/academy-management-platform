"use client";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { browserPasskey } from "./staff-passkeys";

const root="/api/v1/staff/passkeys/attendance";
type Receipt={attendanceRecorded:boolean;attendanceId:string;workPeriodId:string;operation:string;workDate:string;confirmedAtUtc:string};
type State={configured:boolean;profileMissing?:boolean;hasCredential:boolean;hasOpenAttendance:boolean;requiresSourceReview:boolean;branches:{id:string;name:string;configured:boolean}[];recent:Receipt[]};
type Proof={challengeId:string;intent:{branchId:string;operation:string;latitude:number;longitude:number;accuracyMeters:number;capturedAtUtc:string};credential:unknown};
export function StaffVerifiedAttendance(){
  const [state,setState]=useState<State|null>(null),[branch,setBranch]=useState(""),[busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState(""),[pending,setPending]=useState<Proof|null>(null);
  const load=useCallback(async()=>{const r=await fetch(root,{cache:"no-store"});if(!r.ok)throw new Error();setState(await r.json());},[]);
  useEffect(()=>{const controller=new AbortController();fetch(root,{signal:controller.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error();const data=await r.json();if(!controller.signal.aborted)setState(data);}).catch(()=>{if(!controller.signal.aborted)setError("تعذر قراءة حالة حضورك؛ أعد المحاولة.");});return()=>controller.abort();},[]);
  async function submit(operation:"CheckIn"|"CheckOut"){
    if(busy||!branch&&!pending)return;setBusy(true);setError("");setMessage("");let command=pending;
    try{
      if(!command){
        if(!window.isSecureContext||!navigator.geolocation)throw new Error("لا يدعم الاتصال الموقع الآمن.");
        const position=await new Promise<GeolocationPosition>((resolve,reject)=>navigator.geolocation.getCurrentPosition(resolve,reject,{enableHighAccuracy:true,maximumAge:0,timeout:20000}));
        const intent={branchId:branch,operation,latitude:position.coords.latitude,longitude:position.coords.longitude,accuracyMeters:position.coords.accuracy,capturedAtUtc:new Date(position.timestamp).toISOString()};
        const options=await(await csrfRequest(root+"/options","POST",intent,{},{announce:false})).json();
        if(options.purpose!==operation||!options.challengeId)throw new Error();
        const credential=await browserPasskey("verify",options.options);command={challengeId:options.challengeId,intent,credential};setPending(command);
      }
      const result=await(await csrfRequest(root+"/complete","POST",command,{},{announce:false})).json() as Receipt;
      if(result.attendanceRecorded!==true||!result.attendanceId||!result.workPeriodId||result.operation!==command.intent.operation)throw new Error();
      setPending(null);setMessage(result.operation==="CheckIn"?"تم تسجيل حضورك بوقت الخادم المؤكد.":"تم تسجيل انصرافك للفترة نفسها بوقت الخادم المؤكد.");await load();
    }catch(caught){if(caught instanceof DashboardRequestError&&caught.status>=400&&caught.status<500){setPending(null);setError(caught.status===409?caught.message:"لم تقبل العملية؛ حدّث حالة الحضور أو اطلب مراجعة الإدارة.");}else setError(command?"لم نتأكد من نتيجة الإرسال. أعد إرسال الطلب نفسه أو حدّث الحالة؛ لا تغيّر بياناته ولا تفترض نجاحه.":"تعذر تأكيد التوقيع أو الموقع. أعد التقاط موقع دقيق وحديث داخل نطاق الفرع، أو اطلب إثبات حضور للمراجعة من التطبيق.");}
    finally{setBusy(false);}
  }
  async function refresh(){if(busy)return;setBusy(true);setError("");try{await load();setPending(null);}catch{setError("تعذر تحديث الحالة؛ احتفظ بالطلب وأعد المحاولة.");}finally{setBusy(false);}}
  const ready=state?.configured&&state.hasCredential&&!state.profileMissing;
  return <><PageHeader title="حضوري وانصرافي" context="حسابي / الحضور الوظيفي" action={{label:"وسائل التحقق",href:"/dashboard/account/verification"}}/>
    <section className="detail-card staff-verified-attendance"><h2>خطوة واضحة، ووقت مؤكد</h2><p>التقط موقعك عند التسجيل فقط ثم أكد وسيلة التحقق. النطاق 50 مترًا، والدقة المطلوبة حتى30 مترًا والقراءة خلال30 ثانية. التوقيع لا يضمن سلامة GPS أو أن الوسيلة هاتف مادي وحيد.</p><p>هذا حضورك الوظيفي فقط؛ لا يسجل حضور اللاعبين ولا خصمًا أو راتبًا تلقائيًا.</p>
      {error&&<ErrorState message={error}/>} {message&&<p role="status">{message}</p>} {!state&&!error&&<LoadingState/>}
      {state?.profileMissing&&<p>لا يوجد ملف موظف مرتبط بحسابك؛ تواصل مع الإدارة.</p>}
      {state&&!state.configured&&<p>يلزم إعداد التحقق الآمن من المسؤول؛ مسار المراجعة الإداري متاح.</p>}
      {state?.configured&&!state.hasCredential&&<p>أضف وسيلة تحقق فعالة من <Link href="/dashboard/account/verification">وسائل التحقق الخاصة بي</Link> قبل التسجيل.</p>}
      {ready&&<><div className="form-shell"><label>فرع الحضور<select aria-label="فرع الحضور" value={branch} disabled={busy||!!pending} onChange={e=>setBranch(e.target.value)}><option value="">اختر الفرع</option>{state.branches.map(b=><option key={b.id} value={b.id} disabled={!b.configured}>{b.name}{!b.configured?" — يلزم إعداد الموقع":""}</option>)}</select></label></div>{!state.branches.some(b=>b.configured)&&<p>يلزم إعداد موقع الفرع؛ لن نستعمل نقطة تجريبية بدلًا منه.</p>}
        {state.requiresSourceReview&&<p>الفترة المفتوحة تحتاج مراجعة المصدر؛ لا نسجل انصرافًا تخمينيًا.</p>}
        <div className="form-actions">{pending?<button className="primary-button" disabled={busy} onClick={()=>void submit(pending.intent.operation as "CheckIn"|"CheckOut")}>إعادة إرسال الطلب نفسه</button>:<><button className="primary-button" disabled={busy||!branch||state.hasOpenAttendance} onClick={()=>void submit("CheckIn")}>تسجيل حضوري</button><button disabled={busy||!branch||!state.hasOpenAttendance||state.requiresSourceReview} onClick={()=>void submit("CheckOut")}>تسجيل انصرافي</button></>}</div></>}
      <button disabled={busy} onClick={()=>void refresh()}>تحديث حالة الحضور</button>
      <p className="muted">إذا تعذر الموقع أو التحقق، اطلب إثبات حضور للمراجعة من تطبيق الموظف الحالي. لا يوجد اعتماد ذاتي دون اتصال.</p>
      {!!state?.recent?.length&&<section aria-label="عملياتي المؤكدة"><h3>آخر العمليات المؤكدة</h3><ul className="staff-passkey-list">{state.recent.map((r,i)=><li key={`${r.workPeriodId}:${r.operation}:${i}`}><strong>{r.operation==="CheckIn"?"حضور مسجل":"انصراف مسجل"}</strong><span>{new Date(r.confirmedAtUtc).toLocaleString("ar-EG-u-nu-latn")}</span><small>يوم العمل {r.workDate} · مصدر متحقق خادميًا</small></li>)}</ul></section>}
    </section></>;
}
